using System.IO.Compression;
using System.Net;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using MalumMenuEnhanced.Setup.Core;

var tests = new List<(string Name, Func<Task> Run)>();
void Test(string name, Func<Task> run) => tests.Add((name, run));
void Check(bool value, string message = "Assertion failed") { if (!value) throw new Exception(message); }
async Task Reject(Func<Task> run, string? contains = null)
{
    try { await run(); }
    catch (Exception ex) when (ex is InvalidOperationException or InvalidDataException or IOException or OperationCanceledException or ArgumentException)
    { if (contains != null) Check(ex.Message.Contains(contains, StringComparison.OrdinalIgnoreCase), ex.Message); return; }
    throw new Exception("Expected the operation to stop safely");
}

Test("Fresh install downloads pinned files and installs loader plus mod", async () =>
{
    using var f = new Fixture();
    var progress = new ProgressRecorder();
    var result = await f.Service().InstallAsync(f.Game, progress);
    Check(result.LoaderInstalled && result.GameDirectory == f.Game);
    Check(result.BackupDirectory == "");
    Check(f.Http.Requests.SequenceEqual(new[] { "https://example.test/plugin.zip", "https://example.test/loader.zip" }));
    Check(File.ReadAllBytes(f.PluginPath).SequenceEqual(f.PluginDll));
    Check(InstallerService.CheckCompatibleLoader(f.Game));
    Check(progress.Values.Last().Percent == 100);
    Check(!Directory.EnumerateFiles(f.Game, "*.tmp", SearchOption.AllDirectories).Any());
});

Test("Verified IL2CPP loader is reused after full inventory download check", async () =>
{
    using var f = new Fixture();
    f.ExistingLoader();
    var before = f.Snapshot();
    var result = await f.Service().InstallAsync(f.Game);
    Check(!result.LoaderInstalled);
    Check(f.Http.Requests.Count == 2);
    foreach (var item in before) Check(File.ReadAllBytes(Path.Combine(f.Game, item.Key)).SequenceEqual(item.Value));
});

Test("Menu updates back up old DLLs outside plugins and preserve configuration", async () =>
{
    using var f = new Fixture();
    f.ExistingLoader();
    f.Write("BepInEx/config/user.cfg", "settings stay");
    f.Write("BepInEx/plugins/Other.dll", "unrelated");
    f.Write("BepInEx/plugins/MalumMenu.dll", "old original");
    f.Write("BepInEx/plugins/HaddadMenu.dll", "old renamed");
    f.Write("BepInEx/plugins/MalumMenuEnhanced.dll", "old enhanced");
    var result = await f.Service().InstallAsync(f.Game);
    Check(result.BackupDirectory.StartsWith(Path.Combine(f.Game, "MalumMenuEnhanced-backups")));
    Check(!result.BackupDirectory.Contains("plugins", StringComparison.OrdinalIgnoreCase));
    Check(File.ReadAllText(Path.Combine(result.BackupDirectory, "MalumMenu.dll")) == "old original");
    Check(File.ReadAllText(Path.Combine(result.BackupDirectory, "HaddadMenu.dll")) == "old renamed");
    Check(File.ReadAllText(Path.Combine(result.BackupDirectory, "MalumMenuEnhanced.dll")) == "old enhanced");
    Check(!File.Exists(Path.Combine(f.Game, "BepInEx/plugins/MalumMenu.dll")));
    Check(!File.Exists(Path.Combine(f.Game, "BepInEx/plugins/HaddadMenu.dll")));
    Check(File.ReadAllText(Path.Combine(f.Game, "BepInEx/config/user.cfg")) == "settings stay");
    Check(File.ReadAllText(Path.Combine(f.Game, "BepInEx/plugins/Other.dll")) == "unrelated");
});

Test("Fresh loader installation preserves existing config and unrelated plugin folder", async () =>
{
    using var f = new Fixture();
    f.Write("BepInEx/config/a.cfg", "config"); f.Write("BepInEx/plugins/Other.dll", "other");
    await f.Service().InstallAsync(f.Game);
    Check(File.ReadAllText(Path.Combine(f.Game, "BepInEx/config/a.cfg")) == "config");
    Check(File.ReadAllText(Path.Combine(f.Game, "BepInEx/plugins/Other.dll")) == "other");
});

Test("Running game stops before HTTP or filesystem changes", async () =>
{
    using var f = new Fixture(); var before = f.Snapshot();
    await Reject(() => f.Service(() => true).InstallAsync(f.Game), "Close Among Us");
    f.AssertSnapshot(before); Check(f.Http.Requests.Count == 0);
});

Test("Game launched during download stops before changes", async () =>
{
    using var f = new Fixture(); var before = f.Snapshot();
    await Reject(() => f.Service(() => f.Http.Requests.Count > 1).InstallAsync(f.Game), "Close Among Us");
    f.AssertSnapshot(before);
});

Test("Game launched during commit rolls back every earlier change", async () =>
{
    using var f = new Fixture(); f.Write("BepInEx/plugins/MalumMenu.dll", "prior"); var before = f.Snapshot();
    var running = false; var steps = 0;
    await Reject(() => f.Service(() => running, _ => { if (++steps == 6) running = true; }).InstallAsync(f.Game));
    f.AssertSnapshot(before);
});

foreach (var signal in new[] { "winhttp.dll", "doorstop_config.ini", "BepInEx/core/not-a-loader.txt", "dotnet/unknown.txt" })
    Test("Unknown loader is preserved: " + signal, async () =>
    {
        using var f = new Fixture(); f.Write(signal, "unknown"); var before = f.Snapshot();
        await Reject(() => f.Service().InstallAsync(f.Game), "unknown");
        f.AssertSnapshot(before); Check(f.Http.Requests.Count == 0);
    });

Test("BepInEx 5 or Mono is rejected without overwriting it", async () =>
{
    using var f = new Fixture(); f.ExistingLoader();
    File.WriteAllBytes(Path.Combine(f.Game, "BepInEx/core/BepInEx.Core.dll"), Fixture.AssemblyBytes("BepInEx.Core", 5));
    var before = f.Snapshot(); await Reject(() => f.Service().InstallAsync(f.Game)); f.AssertSnapshot(before);
});

Test("Incomplete runtime is rejected without downloading or changing files", async () =>
{
    using var f = new Fixture(); f.ExistingLoader(); File.Delete(Path.Combine(f.Game, "dotnet/coreclr.dll"));
    var before = f.Snapshot(); await Reject(() => f.Service().InstallAsync(f.Game)); f.AssertSnapshot(before);
});

foreach (var name in new[] { "Among Us.exe", "GameAssembly.dll", "UnityPlayer.dll", "Among Us_Data" })
    Test("Incorrect game folder rejected when missing " + name, () =>
    {
        using var f = new Fixture(); var path = Path.Combine(f.Game, name);
        if (Directory.Exists(path)) Directory.Delete(path, true); else File.Delete(path);
        Check(InstallerService.ValidateGameDirectory(f.Game) != null); return Task.CompletedTask;
    });

Test("Xbox executable alias need only exist while native architecture is checked", () =>
{
    using var f = new Fixture(); File.WriteAllBytes(Path.Combine(f.Game, "Among Us.exe"), []);
    Check(InstallerService.ValidateGameDirectory(f.Game) == null); return Task.CompletedTask;
});

Test("32-bit game is rejected", () =>
{
    using var f = new Fixture(); var bytes = Fixture.NativeBytes(); bytes[132] = 0x4c; bytes[133] = 0x01;
    File.WriteAllBytes(Path.Combine(f.Game, "GameAssembly.dll"), bytes);
    Check(InstallerService.ValidateGameDirectory(f.Game)!.Contains("64-bit")); return Task.CompletedTask;
});

Test("Steam build is explicitly rejected", () =>
{
    using var f = new Fixture(); f.Write("steam_api64.dll", "steam");
    Check(InstallerService.ValidateGameDirectory(f.Game)!.Contains("Steam")); return Task.CompletedTask;
});

Test("WindowsApps location is explicitly rejected", () =>
{
    using var f = new Fixture(); var folder = Path.Combine(f.Root, "WindowsApps"); Directory.CreateDirectory(folder);
    Check(InstallerService.ValidateGameDirectory(folder)!.Contains("protected")); return Task.CompletedTask;
});

Test("Non-HTTPS or malformed catalog hash is rejected", () =>
{
    using var f = new Fixture();
    foreach (var artifact in new[] { new DownloadArtifact(new("http://example.test/a"), new string('0', 64), "a"),
        new DownloadArtifact(new("https://example.test/a"), "bad", "a") })
    {
        try { _ = new InstallerService(f.Catalog with { Plugin = artifact }, f.Client); }
        catch (ArgumentException) { continue; }
        throw new Exception("Unsafe catalog accepted");
    }
    return Task.CompletedTask;
});

Test("Plugin hash mismatch causes no game changes", async () =>
{
    using var f = new Fixture(); var before = f.Snapshot();
    await Reject(() => f.Service(catalog: f.Catalog with { Plugin = f.Catalog.Plugin with { Sha256 = new string('0', 64) } }).InstallAsync(f.Game), "safety check");
    f.AssertSnapshot(before);
});

Test("Loader hash mismatch causes no game changes", async () =>
{
    using var f = new Fixture(); var before = f.Snapshot();
    await Reject(() => f.Service(catalog: f.Catalog with { Loader = f.Catalog.Loader with { Sha256 = new string('0', 64) } }).InstallAsync(f.Game));
    f.AssertSnapshot(before);
});

Test("HTTP failure causes no game changes", async () =>
{
    using var f = new Fixture(); f.Http.Status = HttpStatusCode.NotFound; var before = f.Snapshot();
    await Reject(() => f.Service().InstallAsync(f.Game), "connection"); f.AssertSnapshot(before);
});

Test("Malformed archive is rejected before game changes", async () =>
{
    using var f = new Fixture(); f.ReplacePlugin(Encoding.UTF8.GetBytes("not zip")); var before = f.Snapshot();
    await Reject(() => f.Service().InstallAsync(f.Game)); f.AssertSnapshot(before);
});

foreach (var unsafeName in new[] { "../escape.txt", "C:/escape.txt", "/escape.txt", "x/../escape.txt", "x\\..\\escape.txt", "x:stream", "CON.txt", "folder./file", "space /file", "x//file", "NUL", "LPT1.txt" })
    Test("Unsafe archive path rejected: " + unsafeName, async () =>
    {
        using var f = new Fixture(); f.ReplacePlugin(Fixture.Zip([(unsafeName, new byte[] { 1 }, 0)]));
        var before = f.Snapshot(); await Reject(() => f.Service().InstallAsync(f.Game)); f.AssertSnapshot(before);
    });

Test("Symbolic link ZIP entries are rejected", async () =>
{
    using var f = new Fixture(); f.ReplacePlugin(Fixture.Zip([("link.txt", new byte[] { 1 }, unchecked((int)0xA1FF0000))]));
    var before = f.Snapshot(); await Reject(() => f.Service().InstallAsync(f.Game), "linked"); f.AssertSnapshot(before);
});

Test("Windows reparse ZIP entries are rejected", async () =>
{
    using var f = new Fixture(); f.ReplacePlugin(Fixture.Zip([("link.txt", new byte[] { 1 }, (int)FileAttributes.ReparsePoint)]));
    var before = f.Snapshot(); await Reject(() => f.Service().InstallAsync(f.Game)); f.AssertSnapshot(before);
});

Test("Case-insensitive duplicate destinations are rejected", async () =>
{
    using var f = new Fixture(); f.ReplacePlugin(Fixture.Zip([("a.txt", [1], 0), ("A.txt", [2], 0)]));
    var before = f.Snapshot(); await Reject(() => f.Service().InstallAsync(f.Game), "duplicate"); f.AssertSnapshot(before);
});

Test("File and directory collision is rejected", async () =>
{
    using var f = new Fixture(); f.ReplacePlugin(Fixture.Zip([("a", [1], 0), ("a/b", [2], 0)]));
    var before = f.Snapshot(); await Reject(() => f.Service().InstallAsync(f.Game), "conflicting"); f.AssertSnapshot(before);
});

Test("Unexpected additional plugin DLL is rejected", async () =>
{
    using var f = new Fixture(); f.ReplacePlugin(Fixture.Zip([("MalumMenuEnhanced.dll", f.PluginDll, 0), ("Other.dll", [1], 0)]));
    var before = f.Snapshot(); await Reject(() => f.Service().InstallAsync(f.Game), "expected files"); f.AssertSnapshot(before);
});

Test("Wrong assembly disguised as mod DLL is rejected", async () =>
{
    using var f = new Fixture(); f.ReplacePlugin(Fixture.Zip([("BepInEx/plugins/MalumMenuEnhanced.dll", Fixture.AssemblyBytes("Unexpected", 1), 0)]));
    var before = f.Snapshot(); await Reject(() => f.Service().InstallAsync(f.Game), "incompatible"); f.AssertSnapshot(before);
});

Test("Loader archive cannot modify config or unrelated plugins", async () =>
{
    using var f = new Fixture(); f.ReplaceLoader(Fixture.Zip([("BepInEx/config/user.cfg", [1], 0)]));
    var before = f.Snapshot(); await Reject(() => f.Service().InstallAsync(f.Game), "unexpected"); f.AssertSnapshot(before);
});

Test("Too many ZIP entries are rejected", async () =>
{
    using var f = new Fixture(); f.ReplacePlugin(Fixture.Zip(Enumerable.Range(0, 4097).Select(i => ($"{i}.txt", Array.Empty<byte>(), 0))));
    var before = f.Snapshot(); await Reject(() => f.Service().InstallAsync(f.Game), "too many"); f.AssertSnapshot(before);
});

Test("Cancellation before download causes no writes or requests", async () =>
{
    using var f = new Fixture(); using var cts = new CancellationTokenSource(); cts.Cancel(); var before = f.Snapshot();
    await Reject(() => f.Service().InstallAsync(f.Game, ct: cts.Token)); f.AssertSnapshot(before); Check(f.Http.Requests.Count == 0);
});

Test("Cancellation after loader writes restores complete original tree", async () =>
{
    using var f = new Fixture(); f.Write("BepInEx/plugins/MalumMenu.dll", "old"); f.Write("BepInEx/config/a.cfg", "keep");
    using var cts = new CancellationTokenSource(); var before = f.Snapshot(); var steps = 0;
    await Reject(() => f.Service(before: _ => { if (++steps == 8) cts.Cancel(); }).InstallAsync(f.Game, ct: cts.Token));
    Check(steps == 8); f.AssertSnapshot(before);
});

Test("Commit failure restores loader originals and old menus", async () =>
{
    using var f = new Fixture(); f.Write("changelog.txt", "prior text"); f.Write("BepInEx/plugins/MalumMenu.dll", "old");
    var before = f.Snapshot(); var steps = 0;
    await Reject(() => f.Service(before: _ => { if (++steps == 14) throw new IOException("Test disk failure"); }).InstallAsync(f.Game));
    Check(steps == 14); f.AssertSnapshot(before);
});

Test("Failure updating compatible existing menu preserves exact old DLL", async () =>
{
    using var f = new Fixture(); f.ExistingLoader(); f.Write("BepInEx/plugins/MalumMenuEnhanced.dll", "prior version");
    var before = f.Snapshot(); var steps = 0;
    await Reject(() => f.Service(before: _ => { if (++steps == 3) throw new IOException("Test replace failure"); }).InstallAsync(f.Game));
    f.AssertSnapshot(before);
});

Test("Existing linked plugin folder is rejected before game mutation", async () =>
{
    using var f = new Fixture(); var external = Path.Combine(f.Root, "external"); Directory.CreateDirectory(external);
    Directory.CreateDirectory(Path.Combine(f.Game, "BepInEx"));
    var link = Path.Combine(f.Game, "BepInEx", "plugins");
    Fixture.MakeLink(link, external);
    try { await Reject(() => f.Service().InstallAsync(f.Game)); Check(!Directory.EnumerateFileSystemEntries(external).Any()); }
    finally { Directory.Delete(link); }
});

Test("Linked game folder is rejected", () =>
{
    using var f = new Fixture(); var link = Path.Combine(f.Root, "linked-game"); Fixture.MakeLink(link, f.Game);
    try { Check(InstallerService.ValidateGameDirectory(link) != null); }
    finally { Directory.Delete(link); }
    return Task.CompletedTask;
});

foreach (var change in new[] { "app", "magic", "metadata-version", "xbox-version", "publisher", "manifest-version", "architecture", "xml-dtd" })
    Test("Wrong or unsupported game identity is rejected: " + change, () =>
    {
        using var f = new Fixture();
        if (change == "app") f.Write("Among Us_Data/app.info", "Other company\nOther game");
        else if (change is "magic" or "metadata-version")
        {
            var path = Path.Combine(f.Game, "Among Us_Data/il2cpp_data/Metadata/global-metadata.dat");
            var bytes = File.ReadAllBytes(path); bytes[change == "magic" ? 0 : 4] = 0; File.WriteAllBytes(path, bytes);
        }
        else
        {
            var path = Path.Combine(f.Game, change == "manifest-version" ? "AppxManifest.xml" : "MicrosoftGame.config");
            var content = File.ReadAllText(path);
            content = change switch
            {
                "xbox-version" or "manifest-version" => content.Replace("2026.9.293.0", "2025.1.1.0"),
                "publisher" => content.Replace("CN=5A57224C-EF56-4C83-83C1-11C78B125F60", "Fake"),
                "architecture" => content.Replace("x64", "x86"),
                _ => "<!DOCTYPE Game [<!ENTITY x SYSTEM 'file:///missing'>]><Game>&x;</Game>"
            };
            File.WriteAllText(path, content);
        }
        Check(InstallerService.ValidateGameDirectory(f.Game) != null); return Task.CompletedTask;
    });

Test("Unverified store version is rejected with manual guide instruction", () =>
{
    using var f = new Fixture(); File.Delete(Path.Combine(f.Game, "MicrosoftGame.config")); File.Delete(Path.Combine(f.Game, "AppxManifest.xml"));
    Check(InstallerService.ValidateGameDirectory(f.Game)!.Contains("manual")); return Task.CompletedTask;
});

Test("Declared oversized ZIP extraction is rejected before writes", async () =>
{
    using var f = new Fixture(); var zip = Fixture.Zip([("BepInEx/plugins/MalumMenuEnhanced.dll", [1], 0)]);
    for (var i = 0; i < zip.Length - 28; i++)
        if (zip[i] == 0x50 && zip[i + 1] == 0x4b && zip[i + 2] == 1 && zip[i + 3] == 2)
        { BitConverter.GetBytes(129 * 1024 * 1024).CopyTo(zip, i + 24); break; }
    f.ReplacePlugin(zip); var before = f.Snapshot(); await Reject(() => f.Service().InstallAsync(f.Game), "size"); f.AssertSnapshot(before);
});

Test("Cancellation during download causes no game changes", async () =>
{
    using var f = new Fixture(); using var cts = new CancellationTokenSource(); f.Http.OnResponse = () => cts.Cancel();
    var before = f.Snapshot(); await Reject(() => f.Service().InstallAsync(f.Game, ct: cts.Token)); f.AssertSnapshot(before);
});

Test("Rollback failure keeps originals and reports recovery location", async () =>
{
    using var f = new Fixture(); f.ExistingLoader(); f.Write("BepInEx/plugins/MalumMenuEnhanced.dll", "original menu");
    FileStream? locked = null; var count = 0; string? recovery = null;
    try
    {
        await Reject(async () =>
        {
            try
            {
                await f.Service(before: path =>
                {
                    if (++count == 3)
                    {
                        locked = new FileStream(f.PluginPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
                        throw new IOException("Test file lock");
                    }
                }).InstallAsync(f.Game);
            }
            catch (IOException ex)
            {
                var marker = "preserved in: "; var start = ex.Message.IndexOf(marker, StringComparison.Ordinal);
                if (start >= 0) recovery = ex.Message[(start + marker.Length)..].Split(". Close programs")[0];
                throw;
            }
        }, "preserved in");
        Check(recovery != null && Directory.Exists(recovery));
        Check(Directory.EnumerateFiles(Path.Combine(recovery!, "rollback"))
            .Any(p => File.ReadAllText(p) == "original menu"));
    }
    finally { locked?.Dispose(); if (recovery != null && Directory.Exists(recovery)) Directory.Delete(recovery, true); }
});

Test("Real pinned archives install successfully in an isolated game fixture", async () =>
{
    using var f = new Fixture();
    var repo = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
    var plugin = File.ReadAllBytes(Path.Combine(repo, "downloads/v1.0/MalumMenuEnhanced-1.0-Plugin.zip"));
    var loader = File.ReadAllBytes(Path.Combine(repo, "artifacts/verification/downloader-inputs/BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.755+3fab71a.zip"));
    Check(Convert.ToHexString(SHA256.HashData(plugin)) == "227F8D82300F5F89D30C49BEEB4E83C152C072D07BAC5400B194D0D6FBB6B8C1");
    Check(Convert.ToHexString(SHA256.HashData(loader)) == "3616D6A67F5F595973EC4AA7BD7EDAF7F799D5BB9926F7146A6DCC7B4ABF478F");
    f.ReplacePlugin(plugin); f.ReplaceLoader(loader);
    var result = await f.Service().InstallAsync(f.Game);
    Check(result.LoaderInstalled && InstallerService.CheckCompatibleLoader(f.Game));
    Check(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f.PluginPath))) == "D3FCC31C69C83F0381F25B5AC33FF052162D9BDCD6D74CDBDEFBFFEDD6E22559");
    var second = await f.Service().InstallAsync(f.Game);
    Check(!second.LoaderInstalled && File.Exists(Path.Combine(second.BackupDirectory, "MalumMenuEnhanced.dll")));
});

foreach (var dependency in new[] { "BepInEx/core/BepInEx.Preloader.Core.dll", "BepInEx/core/BepInEx.Unity.Common.dll", "dotnet/System.Private.CoreLib.dll", "dotnet/coreclr.dll" })
    Test("Incomplete existing loader dependency is refused: " + dependency, async () =>
    {
        using var f = new Fixture(); f.ExistingLoader(); File.Delete(Path.Combine(f.Game, dependency)); var before = f.Snapshot();
        await Reject(() => f.Service().InstallAsync(f.Game)); f.AssertSnapshot(before); Check(f.Http.Requests.Count == 0);
    });

Test("Existing loader with wrong corlib directory is refused", async () =>
{
    using var f = new Fixture(); f.ExistingLoader(); var config = Path.Combine(f.Game, "doorstop_config.ini");
    File.WriteAllText(config, File.ReadAllText(config).Replace("corlib_dir = dotnet", "corlib_dir = elsewhere"));
    var before = f.Snapshot(); await Reject(() => f.Service().InstallAsync(f.Game)); f.AssertSnapshot(before);
});

Test("Different major-6 loader binary is refused after verified inventory comparison", async () =>
{
    using var f = new Fixture(); f.ExistingLoader();
    File.WriteAllBytes(Path.Combine(f.Game, "BepInEx/core/BepInEx.Core.dll"), Fixture.AssemblyBytes("BepInEx.Core", 6));
    var before = f.Snapshot(); await Reject(() => f.Service().InstallAsync(f.Game), "verified loader release");
    f.AssertSnapshot(before); Check(f.Http.Requests.Count == 2);
});

Test("Additional unknown core binary is refused and preserved", async () =>
{
    using var f = new Fixture(); f.ExistingLoader(); f.Write("BepInEx/core/Unknown.dll", "unknown"); var before = f.Snapshot();
    await Reject(() => f.Service().InstallAsync(f.Game), "verified loader release"); f.AssertSnapshot(before);
});

Test("Existing valid user Doorstop settings are preserved", async () =>
{
    using var f = new Fixture(); f.ExistingLoader(); var config = Path.Combine(f.Game, "doorstop_config.ini");
    File.AppendAllText(config, "redirect_output_log = true\n"); var before = File.ReadAllBytes(config);
    await f.Service().InstallAsync(f.Game); Check(File.ReadAllBytes(config).SequenceEqual(before));
});

Test("Progress consumer failure before success rolls back all writes", async () =>
{
    using var f = new Fixture(); f.Write("BepInEx/plugins/MalumMenuEnhanced.dll", "old"); var before = f.Snapshot();
    await Reject(() => f.Service().InstallAsync(f.Game, new ThrowingProgress()), "Test progress failure"); f.AssertSnapshot(before);
});

Test("Linked dependency inside existing loader is refused", async () =>
{
    using var f = new Fixture(); f.ExistingLoader(); var external = Path.Combine(f.Root, "external-core"); Directory.CreateDirectory(external);
    var core = Path.Combine(f.Game, "BepInEx/core"); Directory.Move(core, Path.Combine(f.Root, "original-core")); Fixture.MakeLink(core, external);
    try { await Reject(() => f.Service().InstallAsync(f.Game)); Check(!Directory.EnumerateFileSystemEntries(external).Any()); }
    finally { Directory.Delete(core); }
});

var failed = 0;
foreach (var test in tests)
{
    try { await test.Run(); Console.WriteLine("PASS " + test.Name); }
    catch (Exception ex) { failed++; Console.WriteLine("FAIL " + test.Name + ": " + ex); }
}
Console.WriteLine($"{tests.Count - failed}/{tests.Count} passed. Fixtures only; no installed game was changed.");
return failed == 0 ? 0 : 1;

sealed class ProgressRecorder : IProgress<InstallProgress>
{
    public List<InstallProgress> Values { get; } = [];
    public void Report(InstallProgress value) => Values.Add(value);
}

sealed class ThrowingProgress : IProgress<InstallProgress>
{
    public void Report(InstallProgress value) { if (value.Percent == 100) throw new IOException("Test progress failure"); }
}

sealed class FakeHttp : HttpMessageHandler
{
    public Dictionary<string, byte[]> Responses { get; } = [];
    public List<string> Requests { get; } = [];
    public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
    public Action? OnResponse { get; set; }
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var uri = request.RequestUri!.AbsoluteUri; Requests.Add(uri);
        OnResponse?.Invoke();
        return Task.FromResult(new HttpResponseMessage(Status) { Content = new ByteArrayContent(Responses[uri]) });
    }
}

sealed class Fixture : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "MenuSetupTests", Guid.NewGuid().ToString("N"));
    public string Game { get; }
    public string PluginPath => Path.Combine(Game, "BepInEx", "plugins", "MalumMenuEnhanced.dll");
    public byte[] PluginDll { get; } = AssemblyBytes("MalumMenuEnhanced", 1);
    public FakeHttp Http { get; } = new();
    public HttpClient Client { get; }
    public SetupCatalog Catalog { get; private set; }
    private readonly List<(string, byte[], int)> _loader;
    public Fixture()
    {
        Game = Path.Combine(Root, "Among Us"); Directory.CreateDirectory(Path.Combine(Game, "Among Us_Data"));
        foreach (var file in new[] { "Among Us.exe", "GameAssembly.dll", "UnityPlayer.dll" }) File.WriteAllBytes(Path.Combine(Game, file), NativeBytes());
        Write("Among Us_Data/app.info", "Innersloth\nAmong Us");
        var metadata = Path.Combine(Game, "Among Us_Data/il2cpp_data/Metadata/global-metadata.dat");
        Directory.CreateDirectory(Path.GetDirectoryName(metadata)!);
        File.WriteAllBytes(metadata, BitConverter.GetBytes(0xFAB11BAFu).Concat(BitConverter.GetBytes(31u)).ToArray());
        Write("MicrosoftGame.config", "<Game><Identity Name=\"Innersloth.AmongUs\" Publisher=\"CN=5A57224C-EF56-4C83-83C1-11C78B125F60\" Version=\"2026.9.293.0\"/><DesktopRegistration><ProcessorArchitecture>x64</ProcessorArchitecture></DesktopRegistration><ExecutableList><Executable Name=\"Among Us.exe\"/></ExecutableList></Game>");
        Write("AppxManifest.xml", "<Package xmlns=\"http://schemas.microsoft.com/appx/manifest/foundation/windows10\"><Identity Name=\"Innersloth.AmongUs\" Publisher=\"CN=5A57224C-EF56-4C83-83C1-11C78B125F60\" Version=\"2026.9.293.0\" ProcessorArchitecture=\"x64\"/></Package>");
        _loader = [
            ("BepInEx/core/BepInEx.Core.dll", AssemblyBytes("BepInEx.Core", 6), 0),
            ("BepInEx/core/BepInEx.Unity.IL2CPP.dll", AssemblyBytes("BepInEx.Unity.IL2CPP", 6), 0),
            ("BepInEx/core/BepInEx.Preloader.Core.dll", AssemblyBytes("BepInEx.Preloader.Core", 6), 0),
            ("BepInEx/core/BepInEx.Unity.Common.dll", AssemblyBytes("BepInEx.Unity.Common", 6), 0),
            ("BepInEx/core/Il2CppInterop.Runtime.dll", AssemblyBytes("Il2CppInterop.Runtime", 1), 0),
            ("dotnet/System.Private.CoreLib.dll", AssemblyBytes("System.Private.CoreLib", 10), 0),
            ("winhttp.dll", NativeBytes(), 0), ("dotnet/coreclr.dll", NativeBytes(), 0),
            ("doorstop_config.ini", Encoding.UTF8.GetBytes("[General]\nenabled = true\ntarget_assembly = BepInEx\\core\\BepInEx.Unity.IL2CPP.dll\n[Il2Cpp]\ncoreclr_path = dotnet\\coreclr.dll\ncorlib_dir = dotnet\n"), 0),
            ("changelog.txt", Encoding.UTF8.GetBytes("loader release"), 0), (".doorstop_version", Encoding.UTF8.GetBytes("4.4"), 0)
        ];
        var loader = Zip(_loader); var plugin = Zip([("BepInEx/plugins/MalumMenuEnhanced.dll", PluginDll, 0),
            ("README.md", [1], 0), ("FORK.md", [1], 0), ("CREDITS.md", [1], 0), ("FEATURES.md", [1], 0), ("LICENSE", [1], 0)]);
        Http.Responses["https://example.test/loader.zip"] = loader; Http.Responses["https://example.test/plugin.zip"] = plugin;
        Client = new HttpClient(Http);
        Catalog = new("1.0", new(new("https://example.test/loader.zip"), Hash(loader), "Loader"), new(new("https://example.test/plugin.zip"), Hash(plugin), "Plugin"));
    }
    public InstallerService Service(Func<bool>? running = null, Action<string>? before = null, SetupCatalog? catalog = null)
        => new(catalog ?? Catalog, Client, running ?? (() => false), before);
    public void ReplacePlugin(byte[] bytes)
    { Http.Responses["https://example.test/plugin.zip"] = bytes; Catalog = Catalog with { Plugin = Catalog.Plugin with { Sha256 = Hash(bytes) } }; }
    public void ReplaceLoader(byte[] bytes)
    { Http.Responses["https://example.test/loader.zip"] = bytes; Catalog = Catalog with { Loader = Catalog.Loader with { Sha256 = Hash(bytes) } }; }
    public void Write(string relative, string contents)
    { var target = Path.Combine(Game, relative.Replace('/', Path.DirectorySeparatorChar)); Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.WriteAllText(target, contents); }
    public void ExistingLoader()
    { foreach (var (name, bytes, _) in _loader) { var target = Path.Combine(Game, name); Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.WriteAllBytes(target, bytes); } }
    public Dictionary<string, byte[]> Snapshot() => Directory.EnumerateFiles(Game, "*", SearchOption.AllDirectories)
        .ToDictionary(p => Path.GetRelativePath(Game, p), File.ReadAllBytes, StringComparer.OrdinalIgnoreCase);
    public void AssertSnapshot(Dictionary<string, byte[]> before)
    {
        var after = Snapshot();
        if (after.Count != before.Count || !after.Keys.Order().SequenceEqual(before.Keys.Order())) throw new Exception("File tree changed after failure");
        foreach (var (key, bytes) in before) if (!bytes.SequenceEqual(after[key])) throw new Exception("Original bytes changed: " + key);
        if (Directory.Exists(Path.Combine(Game, "MalumMenuEnhanced-backups"))) throw new Exception("Backup directories remained after rollback");
    }
    public void Dispose() { Client.Dispose(); Directory.Delete(Root, true); }
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    public static byte[] NativeBytes()
    {
        var bytes = new byte[256]; bytes[0] = 0x4D; bytes[1] = 0x5A; BitConverter.GetBytes(128).CopyTo(bytes, 60);
        bytes[128] = 0x50; bytes[129] = 0x45; bytes[132] = 0x64; bytes[133] = 0x86; return bytes;
    }
    public static byte[] AssemblyBytes(string name, int major)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(0, metadata.GetOrAddString(name + ".dll"), metadata.GetOrAddGuid(Guid.NewGuid()), default, default);
        metadata.AddAssembly(metadata.GetOrAddString(name), new Version(major, 0, 0, 0), default, default, 0, AssemblyHashAlgorithm.None);
        metadata.AddTypeDefinition(TypeAttributes.NotPublic, default, metadata.GetOrAddString("<Module>"), default,
            MetadataTokens.FieldDefinitionHandle(1), MetadataTokens.MethodDefinitionHandle(1));
        var pe = new ManagedPEBuilder(new PEHeaderBuilder(imageCharacteristics: Characteristics.ExecutableImage | Characteristics.Dll),
            new MetadataRootBuilder(metadata), new BlobBuilder());
        var blob = new BlobBuilder(); pe.Serialize(blob); return blob.ToArray();
    }
    public static byte[] Zip(IEnumerable<(string Name, byte[] Bytes, int Attributes)> entries)
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
            foreach (var item in entries)
            { var entry = zip.CreateEntry(item.Name); entry.ExternalAttributes = item.Attributes; using var output = entry.Open(); output.Write(item.Bytes); }
        return stream.ToArray();
    }
    public static void MakeLink(string link, string target)
    {
        if (!OperatingSystem.IsWindows()) { Directory.CreateSymbolicLink(link, target); return; }
        using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "cmd.exe", Arguments = $"/c mklink /J \"{link}\" \"{target}\"",
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
        })!;
        process.WaitForExit();
        if (process.ExitCode != 0) throw new IOException("Could not create temporary test junction: " + process.StandardError.ReadToEnd());
    }
}
