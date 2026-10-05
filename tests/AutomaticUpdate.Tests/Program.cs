using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MalumMenuEnhanced.Updates;

var tests = new List<(string Name, Func<Task> Run)>();
void Test(string name, Func<Task> run) => tests.Add((name, run));
void Check(bool condition, string message = "Check failed") { if (!condition) throw new Exception(message); }

Test("Disabled automatic updates make no request, cache or helper", async () =>
{
    using var f = new Fixture(); var service = f.Service();
    Check(await service.CheckOnceAsync(f.Snapshot, false) == AutomaticUpdateOutcome.Disabled);
    Check(f.Http.Requests.Count == 0 && f.Launches.Count == 0 && !Directory.Exists(f.Cache));
});

Test("Manual Epic-style installation requires a ZIP without any network request or helper", async () =>
{
    using var f = new Fixture(); File.Delete(Path.Combine(f.Game, "MicrosoftGame.config")); File.Delete(Path.Combine(f.Game, "AppxManifest.xml"));
    var service = f.Service();
    Check(await service.CheckOnceAsync(f.Snapshot, true) == AutomaticUpdateOutcome.ManualUpdateRequired);
    Check(service.Status == "This game installation uses manual updates. Download the latest ZIP.");
    Check(f.Http.Requests.Count == 0 && f.Launches.Count == 0 && !Directory.Exists(f.Cache));
    Check(File.ReadAllText(Path.Combine(f.Game, "game-marker.txt")) == "untouched" && Directory.EnumerateFileSystemEntries(f.Game).Count() == 1);
});

Test("Canonical Steam installation with its app manifest is eligible for automatic updates", async () =>
{
    using var f = new Fixture(); var steamapps = Path.Combine(f.Root, "Steam", "steamapps");
    var game = Path.Combine(steamapps, "common", "Among Us"); Directory.CreateDirectory(game);
    File.WriteAllText(Path.Combine(steamapps, "appmanifest_945360.acf"), "Steam manifest availability marker");
    Check(await f.Service().CheckOnceAsync(f.Snapshot with { GameDirectory = game }, true) == AutomaticUpdateOutcome.Prepared);
    Check(f.Http.Requests.Count == 2 && f.Launches.Single().ArgumentList.Contains(game));
});

foreach (var version in new[] { "1.0", "0.9", "1.0.0" })
    Test("Current or older release is skipped: " + version, async () =>
    {
        using var f = new Fixture(); f.Http.Manifest = f.Envelope(version);
        Check(await f.Service().CheckOnceAsync(f.Snapshot, true) == AutomaticUpdateOutcome.UpToDate);
        Check(f.Http.Requests.Count == 1 && f.Launches.Count == 0 && !Directory.Exists(f.Cache));
    });

Test("New release must explicitly support the captured game version", async () =>
{
    using var f = new Fixture(); f.Http.Manifest = f.Envelope("2.0", "2027.1.1");
    Check(await f.Service().CheckOnceAsync(f.Snapshot, true) == AutomaticUpdateOutcome.UnsupportedGameVersion);
    Check(f.Http.Requests.Count == 1 && f.Launches.Count == 0 && !Directory.Exists(f.Cache));
});

Test("Updated unsupported game is not reported up to date for a current manifest", async () =>
{
    using var f = new Fixture(); f.Http.Manifest = f.Envelope("1.0"); var service = f.Service();
    Check(await service.CheckOnceAsync(f.Snapshot with { GameVersion = "2027.1.1" }, true) == AutomaticUpdateOutcome.UnsupportedGameVersion);
    Check(service.Status.Contains("No newer mod update supports") && f.Http.Requests.Count == 1 && f.Launches.Count == 0);
});

Test("A matching newer game release can update an older unsupported mod", async () =>
{
    using var f = new Fixture(); f.Http.Manifest = f.Envelope("2.0", "2027.1.1");
    Check(await f.Service().CheckOnceAsync(f.Snapshot with { GameVersion = "2027.1.1" }, true) == AutomaticUpdateOutcome.Prepared);
    Check(f.Launches.Single().ArgumentList.Contains("2027.1.1"));
});

Test("Experimental current versions do not silently update into a normal release", async () =>
{
    using var f = new Fixture();
    Check(await f.Service().CheckOnceAsync(f.Snapshot with { CurrentVersion = "1.0-judge-test" }, true) == AutomaticUpdateOutcome.Skipped);
    Check(f.Http.Requests.Count == 0 && f.Launches.Count == 0);
});

Test("Offline check leaves game and cache untouched", async () =>
{
    using var f = new Fixture(); f.Http.Failure = new HttpRequestException("Offline test");
    Check(await f.Service().CheckOnceAsync(f.Snapshot, true) == AutomaticUpdateOutcome.Skipped);
    Check(f.Launches.Count == 0 && !Directory.Exists(f.Cache)); f.AssertGameUntouched();
});

Test("Invalid signature cannot download or execute an installer", async () =>
{
    using var f = new Fixture(); var text = Encoding.UTF8.GetString(f.Http.Manifest);
    f.Http.Manifest = Encoding.UTF8.GetBytes(text.Replace("\"signature\":\"", "\"signature\":\"AA"));
    Check(await f.Service().CheckOnceAsync(f.Snapshot, true) == AutomaticUpdateOutcome.Skipped);
    Check(f.Http.Requests.Count == 1 && f.Launches.Count == 0 && !Directory.Exists(f.Cache));
});

Test("Manifest responses have a bound even without Content-Length", async () =>
{
    using var f = new Fixture(); f.Http.Manifest = new byte[UpdateManifestVerifier.MaximumEnvelopeBytes + 1]; f.Http.OmitLength = true;
    Check(await f.Service().CheckOnceAsync(f.Snapshot, true) == AutomaticUpdateOutcome.Skipped);
    Check(f.Http.Requests.Count == 1 && f.Launches.Count == 0 && !Directory.Exists(f.Cache));
});

Test("Valid release stages exact authenticated bytes and launches hidden helper with separate arguments", async () =>
{
    using var f = new Fixture(); var service = f.Service();
    Check(await service.CheckOnceAsync(f.Snapshot, true) == AutomaticUpdateOutcome.Prepared);
    Check(service.PendingVersion == "2.0" && service.Status.Contains("Close Among Us normally"));
    var launch = f.Launches.Single();
    Check(!launch.UseShellExecute && launch.CreateNoWindow && launch.WindowStyle == ProcessWindowStyle.Hidden && launch.WorkingDirectory == f.Slot);
    Check(launch.ArgumentList.SequenceEqual(new[] { "--apply-update", "--game-directory", f.Game, "--game-pid", "100", "--manifest",
        Path.Combine(f.Slot, "latest.json"), "--game-version", "2026.9.29", "--current-version", "1.0" }));
    Check(File.ReadAllBytes(launch.FileName).SequenceEqual(f.SetupBytes));
    Check(File.ReadAllBytes(Path.Combine(f.Slot, "latest.json")).SequenceEqual(f.Http.Manifest));
    Check(!Directory.EnumerateFiles(f.Slot, "*.tmp").Any()); f.AssertGameUntouched();
});

Test("A concurrent startup check is only run once", async () =>
{
    using var f = new Fixture(); var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    f.Http.BeforeResponse = async (_, ct) => { started.TrySetResult(); await release.Task.WaitAsync(ct); };
    var service = f.Service(); var first = service.CheckOnceAsync(f.Snapshot, true);
    await started.Task;
    Check(await service.CheckOnceAsync(f.Snapshot, true) == AutomaticUpdateOutcome.AlreadyChecked);
    release.SetResult(); Check(await first == AutomaticUpdateOutcome.Prepared);
    Check(f.Http.Requests.Count == 2 && f.Launches.Count == 1);
});

Test("An existing live helper is preserved and never launched twice", async () =>
{
    using var f = new Fixture(); Check(await f.Service().CheckOnceAsync(f.Snapshot, true) == AutomaticUpdateOutcome.Prepared);
    f.HelperAlive = true; var before = f.CacheSnapshot(); f.Http.Requests.Clear();
    var service = f.Service(); Check(await service.CheckOnceAsync(f.Snapshot, true) == AutomaticUpdateOutcome.AlreadyPending);
    Check(service.PendingVersion == "2.0" && f.Launches.Count == 1 && f.Http.Requests.Count == 1);
    Check(before.SequenceEqual(f.CacheSnapshot()));
});

Test("An inactive helper uses the exact valid cached installer without another asset download", async () =>
{
    using var f = new Fixture(); Check(await f.Service().CheckOnceAsync(f.Snapshot, true) == AutomaticUpdateOutcome.Prepared);
    f.Http.Requests.Clear();
    Check(await f.Service().CheckOnceAsync(f.Snapshot, true) == AutomaticUpdateOutcome.Prepared);
    Check(f.Http.Requests.Count == 1 && f.Launches.Count == 2 && !Directory.EnumerateFiles(f.Slot, "*.tmp").Any());
});

Test("Successful update startup cleans completed cached binaries", async () =>
{
    using var f = new Fixture(); Check(await f.Service().CheckOnceAsync(f.Snapshot, true) == AutomaticUpdateOutcome.Prepared);
    Check(await f.Service().CheckOnceAsync(f.Snapshot with { CurrentVersion = "2.0" }, true) == AutomaticUpdateOutcome.UpToDate);
    Check(!File.Exists(Path.Combine(f.Slot, "setup.exe")) && !File.Exists(Path.Combine(f.Slot, "latest.json")) && !File.Exists(Path.Combine(f.Slot, "helper.json")));
    Check(f.Launches.Count == 1);
});

Test("A forged cache binary is replaced with the authenticated installer", async () =>
{
    using var f = new Fixture(); Check(await f.Service().CheckOnceAsync(f.Snapshot, true) == AutomaticUpdateOutcome.Prepared);
    File.WriteAllBytes(Path.Combine(f.Slot, "setup.exe"), Enumerable.Repeat((byte)'X', f.SetupBytes.Length).ToArray()); f.Http.Requests.Clear();
    Check(await f.Service().CheckOnceAsync(f.Snapshot, true) == AutomaticUpdateOutcome.Prepared);
    Check(f.Http.Requests.Count == 2 && File.ReadAllBytes(f.Launches.Last().FileName).SequenceEqual(f.SetupBytes));
});

foreach (var damage in new[] { "hash", "short", "long", "header" })
    Test("Damaged installer is never launched and partial files are removed: " + damage, async () =>
    {
        using var f = new Fixture();
        f.Http.Setup = damage switch
        {
            "hash" => Enumerable.Repeat((byte)'X', f.SetupBytes.Length).ToArray(),
            "short" => f.SetupBytes[..^1], "long" => f.SetupBytes.Concat(new byte[] { 1 }).ToArray(), _ => f.SetupBytes
        };
        f.Http.OmitLength = damage != "header"; if (damage == "header") f.Http.SetupLength = f.SetupBytes.Length + 1;
        Check(await f.Service().CheckOnceAsync(f.Snapshot, true) == AutomaticUpdateOutcome.Skipped);
        Check(f.Launches.Count == 0 && !File.Exists(Path.Combine(f.Slot, "setup.exe")) && !Directory.EnumerateFiles(f.Slot, "*.tmp").Any());
        f.AssertGameUntouched();
    });

Test("Cancellation during a streamed installer does not start a helper or leave a partial file", async () =>
{
    using var f = new Fixture(); using var cancellation = new CancellationTokenSource();
    f.Http.SetupStream = () => new CancelAfterReadStream(f.SetupBytes, cancellation); f.Http.OmitLength = true;
    Check(await f.Service().CheckOnceAsync(f.Snapshot, true, cancellation.Token) == AutomaticUpdateOutcome.Skipped);
    Check(f.Launches.Count == 0 && !File.Exists(Path.Combine(f.Slot, "setup.exe")) && !Directory.EnumerateFiles(f.Slot, "*.tmp").Any());
});

Test("A busy cache slot prevents a second helper", async () =>
{
    using var f = new Fixture(); Directory.CreateDirectory(f.Slot);
    using var held = new FileStream(Path.Combine(f.Slot, "check.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    Check(await f.Service().CheckOnceAsync(f.Snapshot, true) == AutomaticUpdateOutcome.Skipped);
    Check(f.Launches.Count == 0 && f.Http.Requests.Count == 1);
});

Test("Cache growth is bounded and an inactive old slot can be pruned", async () =>
{
    using var f = new Fixture(); Directory.CreateDirectory(f.Cache);
    for (var i = 0; i < 8; i++) { var old = Path.Combine(f.Cache, "game-" + i.ToString("X40")); Directory.CreateDirectory(old); File.WriteAllText(Path.Combine(old, "setup.exe"), "old"); }
    Check(await f.Service().CheckOnceAsync(f.Snapshot, true) == AutomaticUpdateOutcome.Prepared);
    Check(Directory.EnumerateDirectories(f.Cache, "game-*").Count() == 8);
});

Test("Full cache with unknown files is preserved and safely refuses an additional slot", async () =>
{
    using var f = new Fixture(); Directory.CreateDirectory(f.Cache);
    for (var i = 0; i < 8; i++) { var old = Path.Combine(f.Cache, "game-" + i.ToString("X40")); Directory.CreateDirectory(old); File.WriteAllText(Path.Combine(old, "keep.txt"), "unrelated"); }
    Check(await f.Service().CheckOnceAsync(f.Snapshot, true) == AutomaticUpdateOutcome.Skipped);
    Check(f.Launches.Count == 0 && Directory.EnumerateFiles(f.Cache, "keep.txt", SearchOption.AllDirectories).Count() == 8);
});

Test("Unrelated game directory junctions are refused before any request", async () =>
{
    using var f = new Fixture(); var link = Path.Combine(f.Root, "unrelated-game-link"); f.MakeLink(link, f.Game);
    Check(await f.Service().CheckOnceAsync(f.Snapshot with { GameDirectory = link }, true) == AutomaticUpdateOutcome.Skipped);
    Check(f.Http.Requests.Count == 0 && f.Launches.Count == 0); f.AssertGameUntouched();
});

Test("Linked update cache is refused and its target remains unchanged", async () =>
{
    using var f = new Fixture(); var external = Path.Combine(f.Root, "external"); Directory.CreateDirectory(external); f.MakeLink(f.Cache, external);
    Check(await f.Service().CheckOnceAsync(f.Snapshot, true) == AutomaticUpdateOutcome.Skipped);
    Check(f.Launches.Count == 0 && !Directory.EnumerateFileSystemEntries(external).Any());
});

Test("Only the exact documented Xbox package junction resolves to the editable Among Us target", async () =>
{
    using var f = new Fixture(); var packages = Path.Combine(f.Root, "Program Files", "WindowsApps"); Directory.CreateDirectory(packages);
    var xbox = Path.Combine(f.Root, "XboxGames"); var game = Path.Combine(xbox, "Among Us", "Content"); Directory.CreateDirectory(game);
    Directory.CreateDirectory(Path.Combine(game, "Among Us_Data")); File.WriteAllText(Path.Combine(game, "Among Us_Data", "app.info"), "Innersloth\nAmong Us\n");
    File.WriteAllText(Path.Combine(game, "MicrosoftGame.config"), "Xbox marker"); File.WriteAllText(Path.Combine(game, "AppxManifest.xml"), "Appx marker");
    var alias = Path.Combine(packages, "Innersloth.AmongUs_2026.9.293.0_x64__fw5x688tam7rm"); f.MakeLink(alias, game);
    Check(AutomaticUpdateService.NormalizeGameDirectory(alias, packages, xbox) == game);
    var service = f.Service(directory => AutomaticUpdateService.NormalizeGameDirectory(directory, packages, xbox));
    Check(await service.CheckOnceAsync(f.Snapshot with { GameDirectory = alias }, true) == AutomaticUpdateOutcome.Prepared);
    Check(f.Launches.Single().ArgumentList.Contains(game) && !f.Launches.Single().ArgumentList.Contains(alias));
});

Test("A documented Xbox package can resolve to a custom game location with verified app identity", () =>
{
    using var f = new Fixture(); var packages = Path.Combine(f.Root, "Program Files", "WindowsApps"); Directory.CreateDirectory(packages);
    var game = Path.Combine(f.Root, "custom-games", "Among Us", "Content"); Directory.CreateDirectory(Path.Combine(game, "Among Us_Data"));
    File.WriteAllText(Path.Combine(game, "Among Us_Data", "app.info"), "Innersloth\nAmong Us\n");
    var alias = Path.Combine(packages, "Innersloth.AmongUs_2026.9.293.0_x64__fw5x688tam7rm"); f.MakeLink(alias, game);
    Check(AutomaticUpdateService.NormalizeGameDirectory(alias, packages) == game);
    File.WriteAllText(Path.Combine(game, "Among Us_Data", "app.info"), "Other Publisher\nAnother Game\n");
    try { AutomaticUpdateService.NormalizeGameDirectory(alias, packages); throw new Exception("Expected game identity rejection"); }
    catch (IOException) { }
    return Task.CompletedTask;
});

foreach (var wrong in new[] { "publisher", "destination", "parent" })
    Test("Xbox package alias normalization rejects unrelated links: " + wrong, () =>
    {
        using var f = new Fixture(); var packages = Path.Combine(f.Root, "Program Files", "WindowsApps"); Directory.CreateDirectory(packages);
        var xbox = Path.Combine(f.Root, "XboxGames"); var game = Path.Combine(xbox, "Among Us", "Content"); Directory.CreateDirectory(game);
        var parent = wrong == "parent" ? Path.Combine(f.Root, "elsewhere") : packages; Directory.CreateDirectory(parent);
        var alias = Path.Combine(parent, "Innersloth.AmongUs_2026.9.293.0_x64__" + (wrong == "publisher" ? "otherpublisher" : "fw5x688tam7rm"));
        f.MakeLink(alias, wrong == "destination" ? f.Game : game);
        try { AutomaticUpdateService.NormalizeGameDirectory(alias, packages, xbox); throw new Exception("Expected alias rejection"); }
        catch (IOException) { }
        return Task.CompletedTask;
    });

var failures = 0;
foreach (var test in tests)
{
    try { await test.Run(); Console.WriteLine("PASS " + test.Name); }
    catch (Exception ex) { failures++; Console.WriteLine("FAIL " + test.Name + ": " + ex); }
}
Console.WriteLine($"{tests.Count - failures}/{tests.Count} automatic update checks passed.");
Environment.ExitCode = failures == 0 ? 0 : 1;

sealed class Fixture : IDisposable
{
    private static readonly RSA SigningKey = RSA.Create(2048);
    private readonly List<string> _links = new();
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "MalumMenuEnhanced-AutomaticUpdate.Tests-" + Guid.NewGuid().ToString("N"));
    public string Game => Path.Combine(Root, "Game with spaces");
    public string Cache => Path.Combine(Root, "Updates");
    public string Slot => Path.Combine(Cache, "game-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Game.ToUpperInvariant())))[..40]);
    public byte[] SetupBytes { get; } = Encoding.UTF8.GetBytes("MZ authenticated simulated setup bytes");
    public MockHttp Http { get; } = new();
    public List<ProcessStartInfo> Launches { get; } = new();
    public bool HelperAlive { get; set; }
    public UpdateStartupSnapshot Snapshot => new(Game, "2026.9.29", "1.0", 100);

    public Fixture()
    {
        Directory.CreateDirectory(Game); File.WriteAllText(Path.Combine(Game, "game-marker.txt"), "untouched");
        File.WriteAllText(Path.Combine(Game, "MicrosoftGame.config"), "Xbox marker"); File.WriteAllText(Path.Combine(Game, "AppxManifest.xml"), "Appx marker");
        Http.Manifest = Envelope("2.0"); Http.Setup = SetupBytes;
    }

    public byte[] Envelope(string version, string gameVersion = "2026.9.29")
    {
        object Asset(string name) => new { url = "https://github.com/ProXgram/MalumMenuEnhanced/releases/download/v" + version + "/" + name,
            sha256 = Convert.ToHexString(SHA256.HashData(SetupBytes)), size = SetupBytes.Length };
        var payload = JsonSerializer.SerializeToUtf8Bytes(new { product = "MalumMenuEnhanced", modVersion = version, supportedGameVersions = new[] { gameVersion },
            pluginZip = Asset("MalumMenuEnhanced-" + version + "-Plugin.zip"), setupExe = Asset("MalumMenuEnhancedSetup.exe") });
        return JsonSerializer.SerializeToUtf8Bytes(new { payload = Convert.ToBase64String(payload),
            signature = Convert.ToBase64String(SigningKey.SignData(payload, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)) });
    }

    public AutomaticUpdateService Service(Func<string, string>? normalize = null) => new(new HttpClient(Http, false),
        new UpdateManifestVerifier(SigningKey.ExportSubjectPublicKeyInfoPem()), new Uri(UpdateTrust.ManifestUrl), Cache,
        start => { Launches.Add(start); return new UpdateHelperIdentity(200, 1000); }, _ => HelperAlive, normalizeDirectory: normalize);

    public void AssertGameUntouched()
    {
        if (File.ReadAllText(Path.Combine(Game, "game-marker.txt")) != "untouched" || Directory.EnumerateFileSystemEntries(Game).Count() != 3 ||
            File.ReadAllText(Path.Combine(Game, "MicrosoftGame.config")) != "Xbox marker" || File.ReadAllText(Path.Combine(Game, "AppxManifest.xml")) != "Appx marker")
            throw new Exception("Game fixture was modified");
    }
    public string[] CacheSnapshot() => Directory.EnumerateFiles(Slot).Order().Select(p => Path.GetFileName(p) + ":" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p)))).ToArray();
    public void MakeLink(string link, string target)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(link)!);
        if (OperatingSystem.IsWindows())
        {
            var start = new ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            start.ArgumentList.Add("/c"); start.ArgumentList.Add("mklink"); start.ArgumentList.Add("/J"); start.ArgumentList.Add(link); start.ArgumentList.Add(target);
            using var process = Process.Start(start)!; process.WaitForExit();
            if (process.ExitCode != 0) throw new IOException("Temporary test junction failed: " + process.StandardError.ReadToEnd());
        }
        else Directory.CreateSymbolicLink(link, target);
        _links.Add(link);
    }
    public void Dispose()
    {
        foreach (var link in _links.AsEnumerable().Reverse()) if (Directory.Exists(link)) Directory.Delete(link, false);
        var full = Path.GetFullPath(Root);
        var temp = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(temp + "MalumMenuEnhanced-AutomaticUpdate.Tests-", StringComparison.OrdinalIgnoreCase)) throw new IOException("Unsafe test cleanup path");
        if (Directory.Exists(full)) Directory.Delete(full, true);
        Http.Dispose();
    }
}

sealed class MockHttp : HttpMessageHandler
{
    public byte[] Manifest { get; set; } = Array.Empty<byte>();
    public byte[] Setup { get; set; } = Array.Empty<byte>();
    public bool OmitLength { get; set; }
    public long? SetupLength { get; set; }
    public Func<Stream>? SetupStream { get; set; }
    public Exception? Failure { get; set; }
    public Func<Uri, CancellationToken, Task>? BeforeResponse { get; set; }
    public List<Uri> Requests { get; } = new();
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var uri = request.RequestUri!; Requests.Add(uri);
        if (Failure != null) throw Failure;
        if (BeforeResponse != null) await BeforeResponse(uri, ct);
        var manifest = uri.AbsoluteUri == UpdateTrust.ManifestUrl;
        if (!manifest && !(uri.Host == "github.com" && uri.AbsolutePath.EndsWith("/MalumMenuEnhancedSetup.exe"))) throw new Exception("Unexpected mocked request: " + uri);
        var content = new StreamContent(!manifest && SetupStream != null ? SetupStream() : new NonSeekableStream(manifest ? Manifest : Setup));
        if (!OmitLength) content.Headers.ContentLength = manifest ? Manifest.Length : SetupLength ?? Setup.Length;
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }
}

class NonSeekableStream(byte[] bytes) : MemoryStream(bytes)
{
    public override bool CanSeek => false;
}

sealed class CancelAfterReadStream(byte[] bytes, CancellationTokenSource cancellation) : NonSeekableStream(bytes)
{
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
    {
        var read = base.ReadAsync(buffer[..Math.Min(buffer.Length, 4)], ct);
        cancellation.Cancel();
        return read;
    }
}
