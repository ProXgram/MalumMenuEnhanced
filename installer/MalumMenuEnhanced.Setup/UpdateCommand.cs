using MalumMenuEnhanced.Setup.Core;
using MalumMenuEnhanced.Updates;

namespace MalumMenuEnhanced.Setup;

/// <summary>The signed updater's noninteractive entry point. Never closes or relaunches Among Us.</summary>
internal static class UpdateCommand
{
    internal static async Task<int> RunAsync(string[] args)
    {
        try
        {
            var request = Parse(args);
            using var timeout = new CancellationTokenSource(TimeSpan.FromHours(24));
            Log("Update helper started.");
            var updates = new AutoUpdateService(new UpdateManifestVerifier(UpdateTrust.PublicKeyPem), SetupForm.Catalog.Loader);
            var result = await updates.ApplyAsync(request, new LogProgress(), timeout.Token);
            Log(result.Installed ? "Installed mod " + result.ModVersion + ". Open Among Us normally." : "The installed mod is already current.");
            return 0;
        }
        catch (OperationCanceledException)
        {
            Log("Update wait cancelled or timed out. Open Among Us normally and try again later.");
            return 2;
        }
        catch (Exception ex)
        {
            Log("Update stopped: " + ex.Message);
            return 1;
        }
    }

    internal static AutoUpdateRequest Parse(string[] args)
    {
        if (args.Length < 7 || args[0] != "--apply-update" || (args.Length & 1) == 0)
            throw new ArgumentException("The update command is incomplete.");
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var allowed = new HashSet<string>(StringComparer.Ordinal)
            { "--game-directory", "--game-pid", "--manifest", "--game-version", "--current-version" };
        for (var i = 1; i < args.Length; i += 2)
            if (!allowed.Contains(args[i]) || string.IsNullOrWhiteSpace(args[i + 1]) || !values.TryAdd(args[i], args[i + 1]))
                throw new ArgumentException("The update command contains an unknown or repeated option.");
        if (!values.TryGetValue("--game-directory", out var game) ||
            !values.TryGetValue("--manifest", out var manifest) ||
            !values.TryGetValue("--game-pid", out var pidText) || !int.TryParse(pidText, out var pid) || pid <= 0)
            throw new ArgumentException("The update command does not identify a game and signed manifest.");
        values.TryGetValue("--game-version", out var gameVersion);
        values.TryGetValue("--current-version", out var modVersion);
        return new(game, pid, manifest, gameVersion, modVersion);
    }

    private static void Log(string text)
    {
        try
        {
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MalumMenuEnhanced", "Updates");
            CheckLocalPath(folder);
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, "update.log");
            CheckLocalPath(path);
            if (File.Exists(path) && new FileInfo(path).Length > 64 * 1024) File.WriteAllText(path, "");
            var message = text.Replace('\r', ' ').Replace('\n', ' ');
            if (message.Length > 1024) message = message[..1024];
            File.AppendAllText(path, DateTime.UtcNow.ToString("O") + " " + message + Environment.NewLine);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private static void CheckLocalPath(string path)
    {
        for (var item = Path.GetFullPath(path); item != null; item = Path.GetDirectoryName(item))
            if ((File.Exists(item) || Directory.Exists(item)) && (File.GetAttributes(item) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("The updater log path contains a linked folder.");
    }

    private sealed class LogProgress : IProgress<InstallProgress>
    {
        public void Report(InstallProgress value) => Log(value.Message);
    }
}
