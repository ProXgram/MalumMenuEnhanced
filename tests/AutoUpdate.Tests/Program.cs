using System.Security.Cryptography;
using System.Text.Json;
using MalumMenuEnhanced.Setup;
using MalumMenuEnhanced.Setup.Core;
using MalumMenuEnhanced.Updates;

using var key = RSA.Create(2048);
var verifier = new UpdateManifestVerifier(key.ExportSubjectPublicKeyInfoPem());
var tests = new List<(string Name, Func<Task> Run)>();
void Test(string name, Func<Task> run) => tests.Add((name, run));
void Check(bool value, string message = "Assertion failed") { if (!value) throw new Exception(message); }
async Task Reject(Func<Task> run, string? contains = null)
{
    try { await run(); }
    catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException or IOException or ArgumentException or OperationCanceledException)
    { if (contains != null) Check(ex.Message.Contains(contains, StringComparison.OrdinalIgnoreCase), ex.Message); return; }
    throw new Exception("Expected the update to stop safely");
}

byte[] Envelope(Fixture f, string version = "2.0.0", string gameVersion = "2026.9.29", string? hash = null, long? size = null)
{
    var bytes = f.Http.Responses["https://example.test/plugin.zip"];
    var payload = JsonSerializer.SerializeToUtf8Bytes(new
    {
        product = "MalumMenuEnhanced", modVersion = version, supportedGameVersions = new[] { gameVersion },
        pluginZip = new { url = $"https://github.com/ProXgram/MalumMenuEnhanced/releases/download/v{version}/MalumMenuEnhanced-{version}-Plugin.zip",
            sha256 = hash ?? Convert.ToHexString(SHA256.HashData(bytes)), size = size ?? bytes.Length },
        setupExe = new { url = $"https://github.com/ProXgram/MalumMenuEnhanced/releases/download/v{version}/MalumMenuEnhancedSetup.exe",
            sha256 = new string('0', 64), size = 100L }
    });
    return JsonSerializer.SerializeToUtf8Bytes(new { payload = Convert.ToBase64String(payload),
        signature = Convert.ToBase64String(key.SignData(payload, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)) });
}

string Prepare(Fixture f, string version = "2.0.0", byte[]? envelope = null)
{
    f.ExistingLoader();
    Directory.CreateDirectory(Path.GetDirectoryName(f.PluginPath)!);
    File.WriteAllBytes(f.PluginPath, Fixture.AssemblyBytes("MalumMenuEnhanced", 1));
    var newBytes = Fixture.Zip([("BepInEx/plugins/MalumMenuEnhanced.dll", Fixture.AssemblyBytes("MalumMenuEnhanced", 2), 0)]);
    f.ReplacePlugin(newBytes);
    f.Http.Responses[$"https://github.com/ProXgram/MalumMenuEnhanced/releases/download/v{version}/MalumMenuEnhanced-{version}-Plugin.zip"] = newBytes;
    var path = Path.Combine(f.Root, "latest.json");
    File.WriteAllBytes(path, envelope ?? Envelope(f, version));
    return path;
}

AutoUpdateService Service(Fixture f, FakeProcesses processes, Action<string>? before = null,
    Func<CancellationToken, Task>? pause = null, Func<string, IDisposable>? acquireLock = null)
    => new(verifier, f.Catalog.Loader, f.Client, processes, before, pause,
        acquireLock ?? (_ => new MemoryStream()));
AutoUpdateRequest Request(Fixture f, string manifest) => new(f.Game, 123, manifest, "2026.9.29", "1.0");

Test("Waits for the requested game without HTTP or game writes", async () =>
{
    using var f = new Fixture(steam: true); var manifest = Prepare(f); var before = f.Snapshot();
    var processes = new FakeProcesses { HoldExit = true, Running = true };
    var update = Service(f, processes).ApplyAsync(Request(f, manifest));
    await processes.WaitStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
    f.AssertSnapshot(before); Check(f.Http.Requests.Count == 0 && !update.IsCompleted);
    processes.Running = false; processes.Exit.TrySetResult();
    var result = await update;
    Check(result.Installed && result.ModVersion == "2.0.0");
    Check(InstallerService.GetInstalledModVersion(f.Game) == new Version(2, 0, 0, 0));
});

Test("Also waits for other Among Us processes after the original PID exits", async () =>
{
    using var f = new Fixture(steam: true); var manifest = Prepare(f); var before = f.Snapshot();
    var processes = new FakeProcesses { Running = true };
    var waiting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var allowClose = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var update = Service(f, processes, pause: async ct => { waiting.TrySetResult(); await allowClose.Task.WaitAsync(ct); })
        .ApplyAsync(Request(f, manifest));
    await waiting.Task.WaitAsync(TimeSpan.FromSeconds(5));
    f.AssertSnapshot(before); Check(f.Http.Requests.Count == 0 && !update.IsCompleted);
    processes.Running = false; allowClose.TrySetResult();
    Check((await update).Installed);
});

Test("New signed release installs, backs up the old menu and preserves settings", async () =>
{
    using var f = new Fixture(steam: true); var manifest = Prepare(f);
    f.Write("BepInEx/config/com.rifegul.cfg", "saved settings"); f.Write("BepInEx/plugins/Unrelated.dll", "unrelated");
    var prior = File.ReadAllBytes(f.PluginPath);
    var result = await Service(f, new FakeProcesses()).ApplyAsync(Request(f, manifest));
    Check(result.Installed && result.BackupDirectory.Length > 0);
    Check(File.ReadAllBytes(Path.Combine(result.BackupDirectory, "MalumMenuEnhanced.dll")).SequenceEqual(prior));
    Check(File.ReadAllText(Path.Combine(f.Game, "BepInEx/config/com.rifegul.cfg")) == "saved settings");
    Check(File.ReadAllText(Path.Combine(f.Game, "BepInEx/plugins/Unrelated.dll")) == "unrelated");
});

Test("Identical or older signed versions do not wait, download or change files", async () =>
{
    foreach (var version in new[] { "1.0.0", "0.9.0" })
    {
        using var f = new Fixture(steam: true); var manifest = Prepare(f, version); var before = f.Snapshot();
        var processes = new FakeProcesses { Running = true, HoldExit = true };
        var result = await Service(f, processes).ApplyAsync(Request(f, manifest));
        Check(!result.Installed && processes.WaitCount == 0 && f.Http.Requests.Count == 0); f.AssertSnapshot(before);
    }
});

Test("An incompatible signed release is rejected before waiting or downloading", async () =>
{
    using var f = new Fixture(steam: true); var manifest = Prepare(f);
    File.WriteAllBytes(manifest, Envelope(f, gameVersion: "2026.10.1")); var before = f.Snapshot();
    var processes = new FakeProcesses();
    await Reject(() => Service(f, processes).ApplyAsync(Request(f, manifest)), "does not support");
    Check(processes.WaitCount == 0 && f.Http.Requests.Count == 0); f.AssertSnapshot(before);
});

Test("Signature tampering is rejected without touching the game", async () =>
{
    using var f = new Fixture(steam: true); var manifest = Prepare(f);
    var data = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllBytes(manifest))!;
    var signature = Convert.FromBase64String(data["signature"]); signature[0] ^= 1;
    data["signature"] = Convert.ToBase64String(signature); File.WriteAllBytes(manifest, JsonSerializer.SerializeToUtf8Bytes(data));
    var before = f.Snapshot();
    await Reject(() => Service(f, new FakeProcesses()).ApplyAsync(Request(f, manifest)), "signature");
    Check(f.Http.Requests.Count == 0); f.AssertSnapshot(before);
});

Test("The signed plugin hash must match the downloaded bytes", async () =>
{
    using var f = new Fixture(steam: true); var manifest = Prepare(f);
    File.WriteAllBytes(manifest, Envelope(f, hash: new string('0', 64))); var before = f.Snapshot();
    await Reject(() => Service(f, new FakeProcesses()).ApplyAsync(Request(f, manifest)), "safety check"); f.AssertSnapshot(before);
});

Test("The signed plugin byte length must match the download", async () =>
{
    using var f = new Fixture(steam: true); var manifest = Prepare(f);
    File.WriteAllBytes(manifest, Envelope(f, size: 1)); var before = f.Snapshot();
    await Reject(() => Service(f, new FakeProcesses()).ApplyAsync(Request(f, manifest)), "size"); f.AssertSnapshot(before);
});

Test("A validly signed ZIP with another assembly version cannot be installed", async () =>
{
    using var f = new Fixture(steam: true); var manifest = Prepare(f);
    var wrong = Fixture.Zip([("BepInEx/plugins/MalumMenuEnhanced.dll", Fixture.AssemblyBytes("MalumMenuEnhanced", 3), 0)]);
    f.ReplacePlugin(wrong);
    f.Http.Responses["https://github.com/ProXgram/MalumMenuEnhanced/releases/download/v2.0.0/MalumMenuEnhanced-2.0.0-Plugin.zip"] = wrong;
    File.WriteAllBytes(manifest, Envelope(f)); var before = f.Snapshot();
    await Reject(() => Service(f, new FakeProcesses()).ApplyAsync(Request(f, manifest)), "version"); f.AssertSnapshot(before);
});

Test("Commit failure restores the original DLL and every original file", async () =>
{
    using var f = new Fixture(steam: true); var manifest = Prepare(f); var before = f.Snapshot(); var step = 0;
    await Reject(() => Service(f, new FakeProcesses(), _ => { if (++step == 3) throw new IOException("Injected commit failure"); })
        .ApplyAsync(Request(f, manifest)), "commit failure");
    f.AssertSnapshot(before);
});

Test("Opening Among Us during commit stops and rolls back the update", async () =>
{
    using var f = new Fixture(steam: true); var manifest = Prepare(f); var before = f.Snapshot(); var step = 0;
    var processes = new FakeProcesses();
    await Reject(() => Service(f, processes, _ => { if (++step == 3) processes.Running = true; }).ApplyAsync(Request(f, manifest)), "Close Among Us");
    f.AssertSnapshot(before);
});

Test("Game version hints cannot override native game settings", async () =>
{
    using var f = new Fixture(steam: true); var manifest = Prepare(f); var before = f.Snapshot();
    await Reject(() => Service(f, new FakeProcesses()).ApplyAsync(Request(f, manifest) with { ExpectedGameVersion = "2026.10.1" }), "changed");
    Check(f.Http.Requests.Count == 0); f.AssertSnapshot(before);
});

Test("Mod version hints cannot override the installed DLL metadata", async () =>
{
    using var f = new Fixture(steam: true); var manifest = Prepare(f); var before = f.Snapshot();
    await Reject(() => Service(f, new FakeProcesses()).ApplyAsync(Request(f, manifest) with { ExpectedModVersion = "0.1" }), "changed");
    Check(f.Http.Requests.Count == 0); f.AssertSnapshot(before);
});

Test("Replacing the installed menu with another assembly fails before downloads", async () =>
{
    using var f = new Fixture(steam: true); var manifest = Prepare(f);
    File.WriteAllBytes(f.PluginPath, Fixture.AssemblyBytes("DifferentPlugin", 1)); var before = f.Snapshot();
    await Reject(() => Service(f, new FakeProcesses()).ApplyAsync(Request(f, manifest)), "identity");
    Check(f.Http.Requests.Count == 0); f.AssertSnapshot(before);
});

Test("Native version read works for Xbox as well as Steam without running the game", () =>
{
    using var f = new Fixture();
    File.WriteAllBytes(Path.Combine(f.Game, "Among Us_Data/globalgamemanagers"), Fixture.UnitySettingsBytes("2026.9.29"));
    Check(InstallerService.GetGameVersion(f.Game) == "2026.9.29");
    return Task.CompletedTask;
});

Test("Cancellation while waiting changes no game files", async () =>
{
    using var f = new Fixture(steam: true); var manifest = Prepare(f); var before = f.Snapshot();
    var processes = new FakeProcesses { HoldExit = true, Running = true }; using var cancellation = new CancellationTokenSource();
    var update = Service(f, processes).ApplyAsync(Request(f, manifest), ct: cancellation.Token);
    await processes.WaitStarted.Task.WaitAsync(TimeSpan.FromSeconds(5)); cancellation.Cancel();
    await Reject(() => update); Check(f.Http.Requests.Count == 0); f.AssertSnapshot(before);
});

Test("A mod installed by another updater while waiting is never downgraded", async () =>
{
    using var f = new Fixture(steam: true); var manifest = Prepare(f);
    var processes = new FakeProcesses { HoldExit = true, Running = true };
    var update = Service(f, processes).ApplyAsync(Request(f, manifest));
    await processes.WaitStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
    File.WriteAllBytes(f.PluginPath, Fixture.AssemblyBytes("MalumMenuEnhanced", 3)); var afterManualUpdate = f.Snapshot();
    processes.Running = false; processes.Exit.TrySetResult();
    Check(!(await update).Installed && f.Http.Requests.Count == 0); f.AssertSnapshot(afterManualUpdate);
});

Test("The game is revalidated after waiting, before any download", async () =>
{
    using var f = new Fixture(steam: true); var manifest = Prepare(f);
    var processes = new FakeProcesses { HoldExit = true, Running = true };
    var update = Service(f, processes).ApplyAsync(Request(f, manifest));
    await processes.WaitStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
    File.WriteAllBytes(Path.Combine(f.Game, "Among Us_Data/globalgamemanagers"), Fixture.UnitySettingsBytes("2026.10.1"));
    var afterGameUpdate = f.Snapshot(); processes.Running = false; processes.Exit.TrySetResult();
    await Reject(() => update, "not supported"); Check(f.Http.Requests.Count == 0); f.AssertSnapshot(afterGameUpdate);
});

Test("A mod installed during download is preserved instead of being overwritten", async () =>
{
    using var f = new Fixture(steam: true); var manifest = Prepare(f); Dictionary<string, byte[]>? afterManualUpdate = null;
    f.Http.OnResponse = () =>
    {
        if (afterManualUpdate != null) return;
        File.WriteAllBytes(f.PluginPath, Fixture.AssemblyBytes("MalumMenuEnhanced", 3));
        afterManualUpdate = f.Snapshot();
    };
    await Reject(() => Service(f, new FakeProcesses()).ApplyAsync(Request(f, manifest)), "changed while downloading");
    f.AssertSnapshot(afterManualUpdate!);
});

Test("Automatic update requires an installed edition version of at least 1.0", async () =>
{
    using var f = new Fixture(steam: true); var manifest = Prepare(f);
    File.WriteAllBytes(f.PluginPath, Fixture.AssemblyBytes("MalumMenuEnhanced", 0)); var before = f.Snapshot();
    await Reject(() => Service(f, new FakeProcesses()).ApplyAsync(Request(f, manifest)), "existing");
    Check(f.Http.Requests.Count == 0); f.AssertSnapshot(before);
});

Test("The real process monitor refuses a live process belonging to another app", async () =>
{
    using var f = new Fixture(steam: true);
    await Reject(() => new GameProcessMonitor().WaitForGameExitAsync(Environment.ProcessId, f.Game, CancellationToken.None), "not Among Us");
});

Test("Process path policy accepts the verified regular executable and rejects another folder", () =>
{
    using var f = new Fixture();
    Check(GameProcessMonitor.IsExpectedGameExecutable(Path.Combine(f.Game, "Among Us.exe"), f.Game));
    Check(!GameProcessMonitor.IsExpectedGameExecutable(Path.Combine(f.Root, "Other/Among Us.exe"), f.Game));
    return Task.CompletedTask;
});

Test("Process path policy resolves the exact official Xbox WindowsApps package alias", () =>
{
    using var f = new Fixture(); var windowsApps = Path.Combine(f.Root, "WindowsApps"); Directory.CreateDirectory(windowsApps);
    var package = Path.Combine(windowsApps, "Innersloth.AmongUs_2026.9.293.0_x64__fw5x688tam7rm");
    Fixture.MakeLink(package, f.Game);
    Check(GameProcessMonitor.IsExpectedGameExecutable(Path.Combine(package, "Among Us.exe"), f.Game, windowsApps));
    Check(!GameProcessMonitor.IsExpectedGameExecutable(Path.Combine(package, "Other.exe"), f.Game, windowsApps));
    Check(!GameProcessMonitor.IsExpectedGameExecutable(Path.Combine(package, "Among Us.exe"), Path.Combine(f.Root, "Different"), windowsApps));
    return Task.CompletedTask;
});

Test("Process path policy does not accept other linked packages or arbitrary linked game folders", () =>
{
    using var f = new Fixture(); var windowsApps = Path.Combine(f.Root, "WindowsApps"); Directory.CreateDirectory(windowsApps);
    foreach (var name in new[] { "Innersloth.AmongUs_2026.9.293.0_x64__different", "DifferentPackage", "Among Us" })
    {
        var alias = Path.Combine(windowsApps, name); Fixture.MakeLink(alias, f.Game);
        Check(!GameProcessMonitor.IsExpectedGameExecutable(Path.Combine(alias, "Among Us.exe"), f.Game, windowsApps));
    }
    return Task.CompletedTask;
});

Test("Concurrent helpers for one game cannot both enter the updater", async () =>
{
    using var f = new Fixture(steam: true); var manifest = Prepare(f);
    var processes = new FakeProcesses { HoldExit = true, Running = true };
    IDisposable Acquire(string _) => new FileStream(Path.Combine(f.Root, "helper.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    var first = Service(f, processes, acquireLock: Acquire).ApplyAsync(Request(f, manifest));
    await processes.WaitStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
    await Reject(() => Service(f, new FakeProcesses(), acquireLock: Acquire).ApplyAsync(Request(f, manifest)));
    Check(f.Http.Requests.Count == 0); processes.Running = false; processes.Exit.TrySetResult();
    Check((await first).Installed);
});

Test("Oversized local manifests are rejected before waiting or downloads", async () =>
{
    using var f = new Fixture(steam: true); var manifest = Prepare(f);
    File.WriteAllBytes(manifest, new byte[UpdateManifestVerifier.MaximumEnvelopeBytes + 1]); var before = f.Snapshot();
    await Reject(() => Service(f, new FakeProcesses()).ApplyAsync(Request(f, manifest)), "size");
    Check(f.Http.Requests.Count == 0); f.AssertSnapshot(before);
});

Test("CLI parses paths with spaces and all optional consistency hints", () =>
{
    var parsed = UpdateCommand.Parse(["--apply-update", "--game-directory", "C:/Games/Among Us", "--game-pid", "123",
        "--manifest", "C:/Cache/latest.json", "--game-version", "2026.9.29", "--current-version", "1.0"]);
    Check(parsed.GamePid == 123 && parsed.GameDirectory == "C:/Games/Among Us" && parsed.ExpectedModVersion == "1.0");
    return Task.CompletedTask;
});

foreach (var arguments in new[]
{
    new[] { "--apply-update" },
    new[] { "--apply-update", "--game-directory", "game", "--game-pid", "0", "--manifest", "manifest" },
    new[] { "--apply-update", "--game-directory", "game", "--game-pid", "12", "--manifest", "manifest", "--restart-game", "true" },
    new[] { "--apply-update", "--game-directory", "game", "--game-pid", "12", "--manifest", "manifest", "--game-pid", "12" },
    new[] { "--apply-update", "--game-directory", "game", "--game-pid", "12", "--manifest", "manifest", "--command", "anything" }
}) Test("CLI rejects incomplete, unknown or repeated arguments: " + string.Join(' ', arguments),
    () => Reject(() => Task.FromResult(UpdateCommand.Parse(arguments))));

var failures = 0;
foreach (var test in tests)
{
    try { await test.Run(); Console.WriteLine("PASS " + test.Name); }
    catch (Exception ex) { failures++; Console.WriteLine("FAIL " + test.Name + ": " + ex); }
}
Console.WriteLine($"{tests.Count - failures}/{tests.Count} updater checks passed. Isolated fixtures; no installed game was changed.");
return failures == 0 ? 0 : 1;

sealed class FakeProcesses : IAutoUpdateProcessMonitor
{
    public bool Running { get; set; }
    public bool HoldExit { get; set; }
    public int WaitCount { get; private set; }
    public TaskCompletionSource WaitStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Exit { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public bool IsAnyGameRunning() => Running;
    public async Task WaitForGameExitAsync(int pid, string gameDirectory, CancellationToken ct)
    {
        WaitCount++; WaitStarted.TrySetResult();
        if (HoldExit) await Exit.Task.WaitAsync(ct);
    }
}
