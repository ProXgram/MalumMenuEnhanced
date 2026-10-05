using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using MalumMenuEnhanced.Updates;

namespace MalumMenuEnhanced.Setup.Core;

public sealed record AutoUpdateRequest(string GameDirectory, int GamePid, string ManifestPath,
    string? ExpectedGameVersion = null, string? ExpectedModVersion = null);
public sealed record AutoUpdateResult(bool Installed, string ModVersion, string BackupDirectory);

/// <summary>Only observes game processes. Updates never terminate or restart a game.</summary>
public interface IAutoUpdateProcessMonitor
{
    Task WaitForGameExitAsync(int pid, string gameDirectory, CancellationToken ct);
    bool IsAnyGameRunning();
}

public sealed class AutoUpdateService
{
    private static readonly HttpClient SharedHttp = new() { Timeout = TimeSpan.FromMinutes(10) };
    private readonly UpdateManifestVerifier verifier;
    private readonly DownloadArtifact pinnedLoader;
    private readonly HttpClient http;
    private readonly IAutoUpdateProcessMonitor processes;
    private readonly Action<string>? beforeChange;
    private readonly Func<CancellationToken, Task> pause;
    private readonly Func<string, IDisposable> acquireLock;

    public AutoUpdateService(UpdateManifestVerifier verifier, DownloadArtifact pinnedLoader, HttpClient? http = null)
        : this(verifier, pinnedLoader, http ?? SharedHttp, new GameProcessMonitor(), null,
            ct => Task.Delay(500, ct)) { }

    internal AutoUpdateService(UpdateManifestVerifier verifier, DownloadArtifact pinnedLoader, HttpClient http,
        IAutoUpdateProcessMonitor processes, Action<string>? beforeChange = null,
        Func<CancellationToken, Task>? pause = null, Func<string, IDisposable>? acquireLock = null)
    {
        this.verifier = verifier ?? throw new ArgumentNullException(nameof(verifier));
        this.pinnedLoader = pinnedLoader ?? throw new ArgumentNullException(nameof(pinnedLoader));
        this.http = http ?? throw new ArgumentNullException(nameof(http));
        this.processes = processes ?? throw new ArgumentNullException(nameof(processes));
        this.beforeChange = beforeChange;
        this.pause = pause ?? (ct => Task.Delay(500, ct));
        this.acquireLock = acquireLock ?? AcquireLock;
    }

    public static SetupCatalog CreateCatalog(UpdateManifest manifest, DownloadArtifact pinnedLoader)
        => new(manifest.ModVersion, pinnedLoader,
            new DownloadArtifact(manifest.PluginZip.Url, manifest.PluginZip.Sha256, "MalumMenu Enhanced")
            { ExpectedSize = manifest.PluginZip.Size }) { ExpectedPluginVersion = manifest.ParsedVersion };

    public async Task<AutoUpdateResult> ApplyAsync(AutoUpdateRequest request,
        IProgress<InstallProgress>? progress = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.GamePid <= 0) throw new ArgumentException("The game process identifier is invalid.");
        ct.ThrowIfCancellationRequested();
        var game = Path.TrimEndingDirectorySeparator(Path.GetFullPath(request.GameDirectory));
        using var updateLock = acquireLock(game);
        var gameVersion = InstallerService.GetGameVersion(game);
        var currentVersion = InstallerService.GetInstalledModVersion(game).ToString();
        if (UpdateManifestVerifier.ParseNumericVersion(currentVersion) < new Version(1, 0, 0, 0))
            throw new InvalidDataException("Automatic updates require an existing MalumMenu Enhanced installation.");
        if (request.ExpectedGameVersion != null && request.ExpectedGameVersion != gameVersion ||
            request.ExpectedModVersion != null &&
            UpdateManifestVerifier.ParseNumericVersion(request.ExpectedModVersion) !=
            UpdateManifestVerifier.ParseNumericVersion(currentVersion))
            throw new InvalidDataException("The installed game or mod changed before the update started.");

        var manifestPath = Path.GetFullPath(request.ManifestPath);
        SafePaths.CheckAncestors(manifestPath);
        using var input = new FileStream(manifestPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (input.Length is <= 0 or > UpdateManifestVerifier.MaximumEnvelopeBytes)
            throw new InvalidDataException("The staged update manifest size is invalid.");
        var envelope = new byte[(int)input.Length];
        await input.ReadExactlyAsync(envelope, ct);
        if (input.ReadByte() != -1) throw new InvalidDataException("The staged update manifest changed while reading.");
        var manifest = verifier.Verify(envelope);
        if (!manifest.SupportsGameVersion(gameVersion))
            throw new InvalidDataException("The update does not support the installed Among Us version.");
        if (!manifest.IsNewerThan(currentVersion)) return new(false, currentVersion, "");

        progress?.Report(new("Update ready. Waiting for Among Us to close.", 0));
        await processes.WaitForGameExitAsync(request.GamePid, game, ct);
        while (processes.IsAnyGameRunning()) await pause(ct);
        ct.ThrowIfCancellationRequested();

        // The game or mod can change while the helper waits. Verify them again before downloading.
        gameVersion = InstallerService.GetGameVersion(game);
        currentVersion = InstallerService.GetInstalledModVersion(game).ToString();
        if (!manifest.SupportsGameVersion(gameVersion))
            throw new InvalidDataException("The game updated again. A compatible mod release is required.");
        if (!manifest.IsNewerThan(currentVersion)) return new(false, currentVersion, "");
        var catalog = CreateCatalog(manifest, pinnedLoader) with
            { ExpectedInstalledModVersion = UpdateManifestVerifier.ParseNumericVersion(currentVersion) };
        var installer = new InstallerService(catalog, http,
            processes.IsAnyGameRunning, beforeChange);
        var result = await installer.InstallAsync(game, progress, ct);
        return new(true, manifest.ModVersion, result.BackupDirectory);
    }

    private static IDisposable AcquireLock(string gameDirectory)
    {
        var name = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(gameDirectory.ToUpperInvariant()))) + ".lock";
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MalumMenuEnhanced", "Updates", "helper-locks");
        SafePaths.CheckAncestors(folder);
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, name);
        SafePaths.CheckAncestors(path);
        return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }
}

internal sealed class GameProcessMonitor : IAutoUpdateProcessMonitor
{
    public bool IsAnyGameRunning()
    {
        var games = Process.GetProcessesByName("Among Us");
        foreach (var game in games) game.Dispose();
        return games.Length != 0;
    }

    public async Task WaitForGameExitAsync(int pid, string gameDirectory, CancellationToken ct)
    {
        Process game;
        try { game = Process.GetProcessById(pid); }
        catch (ArgumentException) { return; } // The requested game already closed.
        using (game)
        {
            if (game.HasExited) return;
            if (!game.ProcessName.Equals("Among Us", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The requested process is not Among Us.");
            var executable = game.MainModule?.FileName;
            if (executable == null || !IsExpectedGameExecutable(executable, gameDirectory))
                throw new InvalidDataException("The requested process does not match the verified game folder.");
            await game.WaitForExitAsync(ct);
        }
    }

    internal static bool IsExpectedGameExecutable(string executable, string gameDirectory, string? windowsAppsRoot = null)
    {
        try
        {
            var path = Path.GetFullPath(executable);
            if (path.Equals(Path.Combine(gameDirectory, "Among Us.exe"), StringComparison.OrdinalIgnoreCase)) return true;
            if (!Path.GetFileName(path).Equals("Among Us.exe", StringComparison.OrdinalIgnoreCase)) return false;
            var package = new DirectoryInfo(Path.GetDirectoryName(path)!);
            windowsAppsRoot ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WindowsApps");
            if (package.Parent == null ||
                !package.Parent.FullName.Equals(Path.GetFullPath(windowsAppsRoot), StringComparison.OrdinalIgnoreCase) ||
                package.Name != "Innersloth.AmongUs_2026.9.293.0_x64__fw5x688tam7rm" ||
                (package.Attributes & FileAttributes.ReparsePoint) == 0) return false;
            // Xbox exposes a WindowsApps package alias. Resolve only that known publisher package;
            // the target still has to equal the separately validated writable game folder.
            var target = package.ResolveLinkTarget(true);
            return target is DirectoryInfo && Path.TrimEndingDirectorySeparator(target.FullName)
                .Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(gameDirectory)), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { return false; }
    }
}
