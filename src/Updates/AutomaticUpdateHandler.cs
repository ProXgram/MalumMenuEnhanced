#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MalumMenuEnhanced.Updates;

/// <summary>Values captured on Unity's main thread before starting managed background work.</summary>
internal sealed record UpdateStartupSnapshot(string GameDirectory, string GameVersion, string CurrentVersion, int GameProcessId);
internal sealed record UpdateHelperIdentity(int ProcessId, long StartTimeUtcTicks);
internal enum AutomaticUpdateOutcome { Disabled, AlreadyChecked, UpToDate, UnsupportedGameVersion, ManualUpdateRequired, Prepared, AlreadyPending, Skipped }

internal static class AutomaticUpdateHandler
{
    private static int _started;
    private static AutomaticUpdateService? _service;
    private static string _initialStatus = "Automatic update check has not started.";
    public static string Status => Volatile.Read(ref _service)?.Status ?? Volatile.Read(ref _initialStatus);
    public static string PendingVersion => Volatile.Read(ref _service)?.PendingVersion ?? "";

    public static void Start(UpdateStartupSnapshot snapshot, bool enabled, Action<string> log)
    {
        if (Interlocked.Exchange(ref _started, 1) != 0) return;
        if (!enabled) { Volatile.Write(ref _initialStatus, "Automatic updates are disabled."); log(_initialStatus); return; }
        // Everything below is managed .NET work; no Unity properties or objects are accessed here.
        _ = Task.Run(async () =>
        {
            try
            {
                using var http = new HttpClient(new HttpClientHandler { UseCookies = false })
                { Timeout = TimeSpan.FromSeconds(45) };
                var service = new AutomaticUpdateService(http, new UpdateManifestVerifier(UpdateTrust.PublicKeyPem),
                    new Uri(UpdateTrust.ManifestUrl), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "MalumMenuEnhanced", "Updates"), LaunchHelper, IsHelperRunning, log);
                Volatile.Write(ref _service, service);
                using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
                await service.CheckOnceAsync(snapshot, true, timeout.Token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Volatile.Write(ref _initialStatus, "Automatic update check skipped: " + ex.Message);
                Volatile.Write(ref _service, null);
                log(_initialStatus);
            }
        });
    }

    private static UpdateHelperIdentity LaunchHelper(ProcessStartInfo start)
    {
        using var process = Process.Start(start) ?? throw new IOException("The update installer could not be started.");
        return new UpdateHelperIdentity(process.Id, process.StartTime.ToUniversalTime().Ticks);
    }

    private static bool IsHelperRunning(UpdateHelperIdentity identity)
    {
        try
        {
            using var process = Process.GetProcessById(identity.ProcessId);
            return !process.HasExited && process.StartTime.ToUniversalTime().Ticks == identity.StartTimeUtcTicks;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        { return false; }
    }
}

/// <summary>Bounded, signed update checking with injectable network and process boundaries.</summary>
internal sealed class AutomaticUpdateService
{
    private const int MaximumCacheSlots = 8;
    private static readonly string[] CacheFiles = { "setup.exe", "latest.json", "helper.json", "setup.download.tmp", "manifest.write.tmp", "receipt.write.tmp", "check.lock" };
    private readonly HttpClient _http;
    private readonly UpdateManifestVerifier _verifier;
    private readonly Uri _manifestUrl;
    private readonly string _cacheRoot;
    private readonly Func<ProcessStartInfo, UpdateHelperIdentity> _launchHelper;
    private readonly Func<UpdateHelperIdentity, bool> _isHelperRunning;
    private readonly Func<string, string> _normalizeDirectory;
    private readonly Action<string> _log;
    private int _started;
    private string _status = "Automatic update check has not started.";
    private string _pendingVersion = "";
    public string Status => Volatile.Read(ref _status);
    public string PendingVersion => Volatile.Read(ref _pendingVersion);

    public AutomaticUpdateService(HttpClient http, UpdateManifestVerifier verifier, Uri manifestUrl, string cacheRoot,
        Func<ProcessStartInfo, UpdateHelperIdentity> launchHelper, Func<UpdateHelperIdentity, bool> isHelperRunning, Action<string>? log = null,
        Func<string, string>? normalizeDirectory = null)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _verifier = verifier ?? throw new ArgumentNullException(nameof(verifier));
        if (manifestUrl == null || manifestUrl.Scheme != Uri.UriSchemeHttps) throw new ArgumentException("An HTTPS update manifest is required.", nameof(manifestUrl));
        _manifestUrl = manifestUrl;
        if (!Path.IsPathFullyQualified(cacheRoot)) throw new ArgumentException("An absolute update cache path is required.", nameof(cacheRoot));
        _cacheRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(cacheRoot));
        _launchHelper = launchHelper ?? throw new ArgumentNullException(nameof(launchHelper));
        _isHelperRunning = isHelperRunning ?? throw new ArgumentNullException(nameof(isHelperRunning));
        _log = log ?? (_ => { });
        _normalizeDirectory = normalizeDirectory ?? (directory => NormalizeGameDirectory(directory));
    }

    public async Task<AutomaticUpdateOutcome> CheckOnceAsync(UpdateStartupSnapshot snapshot, bool enabled, CancellationToken ct = default)
    {
        if (Interlocked.Exchange(ref _started, 1) != 0) return AutomaticUpdateOutcome.AlreadyChecked;
        if (!enabled) return Finish(AutomaticUpdateOutcome.Disabled, "Automatic updates are disabled.");
        try
        {
            if (snapshot == null || snapshot.GameProcessId < 1 || !Path.IsPathFullyQualified(snapshot.GameDirectory))
                throw new InvalidDataException("The running game's update context is incomplete.");
            UpdateManifestVerifier.ParseNumericVersion(snapshot.CurrentVersion);
            UpdateManifestVerifier.ParseNumericVersion(snapshot.GameVersion);
            var game = _normalizeDirectory(snapshot.GameDirectory);
            if (!HasAutomaticInstallationMarkers(game))
                return Finish(AutomaticUpdateOutcome.ManualUpdateRequired, "This game installation uses manual updates. Download the latest ZIP.");
            SetStatus("Checking for a compatible mod update.");
            var envelope = await DownloadManifestAsync(ct).ConfigureAwait(false);
            var manifest = _verifier.Verify(envelope);
            if (!manifest.SupportsGameVersion(snapshot.GameVersion))
            {
                CleanCompletedSlot(game);
                return Finish(AutomaticUpdateOutcome.UnsupportedGameVersion, "No newer mod update supports this Among Us version yet.");
            }
            if (!manifest.IsNewerThan(snapshot.CurrentVersion))
            {
                CleanCompletedSlot(game);
                return Finish(AutomaticUpdateOutcome.UpToDate, "The installed mod is up to date.");
            }

            var slot = GetCacheSlot(game);
            using var slotLock = OpenLock(Path.Combine(slot, "check.lock"));
            var receipt = ReadReceipt(slot);
            if (receipt != null && _isHelperRunning(new UpdateHelperIdentity(receipt.ProcessId, receipt.StartTimeUtcTicks)))
            {
                Volatile.Write(ref _pendingVersion, receipt.ModVersion);
                return Finish(AutomaticUpdateOutcome.AlreadyPending, "Mod update " + receipt.ModVersion + " is already waiting for Among Us to close.");
            }
            DeleteKnownFile(slot, "helper.json");
            DeleteKnownFile(slot, "setup.download.tmp");
            DeleteKnownFile(slot, "manifest.write.tmp");
            DeleteKnownFile(slot, "receipt.write.tmp");
            var setupPath = Path.Combine(slot, "setup.exe");
            await StageSetupAsync(manifest.SetupExe, setupPath, ct).ConfigureAwait(false);
            var manifestPath = Path.Combine(slot, "latest.json");
            await WriteAtomicAsync(manifestPath, Path.Combine(slot, "manifest.write.tmp"), envelope, ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            CheckAncestors(setupPath);
            CheckAncestors(manifestPath);
            // Deny writes/deletes while rechecking and launching the exact authenticated files.
            using var setupHandle = new FileStream(setupPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var manifestHandle = new FileStream(manifestPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (!await MatchesAssetAsync(setupHandle, manifest.SetupExe, ct).ConfigureAwait(false))
                throw new InvalidDataException("The staged update installer changed before it could start.");
            var savedEnvelope = new byte[envelope.Length];
            await ReadExactlyAsync(manifestHandle, savedEnvelope, ct).ConfigureAwait(false);
            if (manifestHandle.Length != envelope.Length || !savedEnvelope.SequenceEqual(envelope))
                throw new InvalidDataException("The staged signed manifest changed before it could start.");
            ct.ThrowIfCancellationRequested();
            var start = new ProcessStartInfo(setupPath) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden, WorkingDirectory = slot };
            foreach (var argument in new[] { "--apply-update", "--game-directory", game, "--game-pid", snapshot.GameProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "--manifest", manifestPath, "--game-version", snapshot.GameVersion, "--current-version", snapshot.CurrentVersion }) start.ArgumentList.Add(argument);
            var helper = _launchHelper(start);
            if (helper.ProcessId < 1 || helper.StartTimeUtcTicks < 1) throw new IOException("The update installer did not return a valid process identity.");
            Volatile.Write(ref _pendingVersion, manifest.ModVersion);
            var receiptBytes = JsonSerializer.SerializeToUtf8Bytes(new HelperReceipt(helper.ProcessId, helper.StartTimeUtcTicks, manifest.ModVersion));
            try { await WriteAtomicAsync(Path.Combine(slot, "helper.json"), Path.Combine(slot, "receipt.write.tmp"), receiptBytes, CancellationToken.None).ConfigureAwait(false); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { _log("The update is prepared, but its cache receipt could not be saved: " + ex.Message); }
            return Finish(AutomaticUpdateOutcome.Prepared, "Mod update " + manifest.ModVersion + " is prepared. Close Among Us normally to apply it.");
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException or InvalidDataException or
            OperationCanceledException or ArgumentException or InvalidOperationException or CryptographicException or System.ComponentModel.Win32Exception)
        { return Finish(AutomaticUpdateOutcome.Skipped, "Automatic update check skipped: " + ex.Message); }
    }

    private async Task<byte[]> DownloadManifestAsync(CancellationToken ct)
    {
        using var response = await _http.GetAsync(_manifestUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is long length && (length < 1 || length > UpdateManifestVerifier.MaximumEnvelopeBytes))
            throw new InvalidDataException("The signed update manifest is too large or empty.");
        using var input = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        int count;
        while ((count = await input.ReadAsync(buffer.AsMemory(), ct).ConfigureAwait(false)) != 0)
        {
            if (output.Length + count > UpdateManifestVerifier.MaximumEnvelopeBytes) throw new InvalidDataException("The signed update manifest is too large.");
            await output.WriteAsync(buffer.AsMemory(0, count), ct).ConfigureAwait(false);
        }
        return output.ToArray();
    }

    private async Task StageSetupAsync(UpdateAsset asset, string destination, CancellationToken ct)
    {
        CheckAncestors(destination);
        if (File.Exists(destination))
        {
            using var existing = new FileStream(destination, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (await MatchesAssetAsync(existing, asset, ct).ConfigureAwait(false)) return;
        }
        var temporary = Path.Combine(Path.GetDirectoryName(destination)!, "setup.download.tmp");
        CheckAncestors(temporary);
        try
        {
            using var response = await _http.GetAsync(asset.Url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is long length && length != asset.Size)
                throw new InvalidDataException("The update installer size does not match its signed release.");
            using var input = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long total = 0;
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous))
            {
                var buffer = new byte[65536];
                int count;
                while ((count = await input.ReadAsync(buffer.AsMemory(), ct).ConfigureAwait(false)) != 0)
                {
                    if (total + count > asset.Size) throw new InvalidDataException("The update installer exceeds its signed size.");
                    total += count;
                    hash.AppendData(buffer, 0, count);
                    await output.WriteAsync(buffer.AsMemory(0, count), ct).ConfigureAwait(false);
                }
                await output.FlushAsync(ct).ConfigureAwait(false);
            }
            if (total != asset.Size || Convert.ToHexString(hash.GetHashAndReset()) != asset.Sha256)
                throw new InvalidDataException("The update installer checksum or size does not match its signed release.");
            ct.ThrowIfCancellationRequested();
            CheckAncestors(destination);
            CheckAncestors(temporary);
            File.Move(temporary, destination, true);
        }
        finally { DeleteChecked(temporary); }
    }

    private static async Task<bool> MatchesAssetAsync(FileStream stream, UpdateAsset asset, CancellationToken ct)
    {
        if (stream.Length != asset.Size) return false;
        stream.Position = 0;
        using var sha = SHA256.Create();
        return Convert.ToHexString(await sha.ComputeHashAsync(stream, ct).ConfigureAwait(false)) == asset.Sha256;
    }

    private static async Task WriteAtomicAsync(string destination, string temporary, byte[] bytes, CancellationToken ct)
    {
        CheckAncestors(destination);
        CheckAncestors(temporary);
        try
        {
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 8192, FileOptions.Asynchronous))
            {
                await output.WriteAsync(bytes.AsMemory(), ct).ConfigureAwait(false);
                await output.FlushAsync(ct).ConfigureAwait(false);
            }
            ct.ThrowIfCancellationRequested();
            CheckAncestors(destination);
            CheckAncestors(temporary);
            File.Move(temporary, destination, true);
        }
        finally { DeleteChecked(temporary); }
    }

    private string SlotPath(string game)
    {
        using var sha = SHA256.Create();
        var hash = Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(game.ToUpperInvariant())));
        return Path.Combine(_cacheRoot, "game-" + hash[..40]);
    }

    private string GetCacheSlot(string game)
    {
        EnsureDirectory(_cacheRoot);
        using var cacheLock = OpenLock(Path.Combine(_cacheRoot, "cache.lock"));
        var slot = SlotPath(game);
        CheckAncestors(slot);
        if (Directory.Exists(slot)) return slot;
        var slots = Directory.EnumerateDirectories(_cacheRoot, "game-*").Where(IsSlotPath).Take(MaximumCacheSlots + 1).ToList();
        if (slots.Count >= MaximumCacheSlots)
        {
            foreach (var old in slots.OrderBy(Directory.GetLastWriteTimeUtc))
                if (TryPruneSlot(old)) { slots.Remove(old); break; }
            if (slots.Count >= MaximumCacheSlots) throw new IOException("The update cache is busy. Try again after closing other Among Us copies.");
        }
        EnsureDirectory(slot);
        return slot;
    }

    private static bool IsSlotPath(string path)
    {
        var name = Path.GetFileName(path);
        return name.Length == 45 && name.StartsWith("game-", StringComparison.Ordinal) && name[5..].All(c => c is >= '0' and <= '9' or >= 'A' and <= 'F');
    }

    private void CleanCompletedSlot(string game)
    {
        try
        {
            var slot = SlotPath(game);
            CheckAncestors(slot);
            if (!Directory.Exists(slot)) return;
            using var held = OpenLock(Path.Combine(slot, "check.lock"));
            var receipt = ReadReceipt(slot);
            if (receipt != null && _isHelperRunning(new UpdateHelperIdentity(receipt.ProcessId, receipt.StartTimeUtcTicks))) return;
            foreach (var file in CacheFiles.Where(f => f != "check.lock")) DeleteKnownFile(slot, file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private bool TryPruneSlot(string slot)
    {
        try
        {
            CheckAncestors(slot);
            using (var held = OpenLock(Path.Combine(slot, "check.lock")))
            {
                var receipt = ReadReceipt(slot);
                if (receipt != null && _isHelperRunning(new UpdateHelperIdentity(receipt.ProcessId, receipt.StartTimeUtcTicks))) return false;
                if (Directory.EnumerateFileSystemEntries(slot).Any(p => !CacheFiles.Contains(Path.GetFileName(p), StringComparer.Ordinal))) return false;
                foreach (var file in CacheFiles.Where(f => f != "check.lock")) DeleteKnownFile(slot, file);
            }
            DeleteKnownFile(slot, "check.lock");
            CheckAncestors(slot);
            Directory.Delete(slot, false);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    private sealed record HelperReceipt(int ProcessId, long StartTimeUtcTicks, string ModVersion);

    private static HelperReceipt? ReadReceipt(string slot)
    {
        var path = Path.Combine(slot, "helper.json");
        CheckAncestors(path);
        if (!File.Exists(path)) return null;
        try
        {
            using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (input.Length < 1 || input.Length > 4096) return null;
            var bytes = new byte[(int)input.Length];
            var position = 0;
            while (position < bytes.Length)
            {
                var read = input.Read(bytes, position, bytes.Length - position);
                if (read == 0) return null;
                position += read;
            }
            var receipt = JsonSerializer.Deserialize<HelperReceipt>(bytes);
            if (receipt == null || receipt.ProcessId < 1 || receipt.StartTimeUtcTicks < 1) return null;
            UpdateManifestVerifier.ParseNumericVersion(receipt.ModVersion);
            return receipt;
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException) { return null; }
    }

    private static void EnsureDirectory(string path) { CheckAncestors(path); Directory.CreateDirectory(path); CheckAncestors(path); }
    private static FileStream OpenLock(string path) { CheckAncestors(path); return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
    private static void DeleteKnownFile(string slot, string name) => DeleteChecked(Path.Combine(slot, name));
    private static void DeleteChecked(string path) { CheckAncestors(path); if (File.Exists(path)) File.Delete(path); }
    private static void CheckAncestors(string path)
    {
        var current = Path.GetFullPath(path);
        while (!string.IsNullOrEmpty(current))
        {
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new IOException("Linked update files or folders cannot be used safely.");
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
            current = Path.GetDirectoryName(current);
        }
    }

    internal static string NormalizeGameDirectory(string directory, string? windowsAppsRoot = null, string? xboxGamesRoot = null)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        var source = new DirectoryInfo(full);
        var packages = Path.TrimEndingDirectorySeparator(Path.GetFullPath(windowsAppsRoot ??
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WindowsApps")));
        if (string.Equals(source.Parent?.FullName, packages, StringComparison.OrdinalIgnoreCase) &&
            source.Name.Length <= 160 && System.Text.RegularExpressions.Regex.IsMatch(source.Name,
                @"^Innersloth[.]AmongUs_[0-9]+[.][0-9]+[.][0-9]+[.][0-9]+_x64__fw5x688tam7rm$",
                System.Text.RegularExpressions.RegexOptions.CultureInvariant) &&
            (source.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            CheckAncestors(packages);
            var resolved = source.ResolveLinkTarget(true) as DirectoryInfo
                ?? throw new IOException("The Among Us Xbox package target could not be resolved.");
            var target = resolved.FullName;
            if (target.StartsWith(@"\\?\", StringComparison.Ordinal) && target.Length >= 7 && target[5] == ':') target = target[4..];
            target = Path.TrimEndingDirectorySeparator(Path.GetFullPath(target));
            // A test may constrain the target root; production also supports Xbox's custom install locations.
            if (xboxGamesRoot != null && !string.Equals(target, Path.GetFullPath(Path.Combine(xboxGamesRoot, "Among Us", "Content")), StringComparison.OrdinalIgnoreCase))
                throw new IOException("The Among Us Xbox package does not point to its editable game folder.");
            CheckAncestors(target);
            if (!Directory.Exists(target)) throw new IOException("The Among Us Xbox game folder is missing.");
            var info = Path.Combine(target, "Among Us_Data", "app.info");
            CheckAncestors(info);
            using (var input = new FileStream(info, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (input.Length < 1 || input.Length > 4096) throw new IOException("The Among Us Xbox game identity is invalid.");
                using var reader = new StreamReader(input, new UTF8Encoding(false, true));
                if (reader.ReadLine()?.Trim() != "Innersloth" || reader.ReadLine()?.Trim() != "Among Us")
                    throw new IOException("The Among Us Xbox package target is not the expected game.");
            }
            return target;
        }
        CheckAncestors(full);
        return full;
    }

    private static bool HasAutomaticInstallationMarkers(string game)
    {
        // This is an availability check only. The helper verifies the full native and publisher identity.
        var xbox = Path.Combine(game, "MicrosoftGame.config");
        var appx = Path.Combine(game, "AppxManifest.xml");
        CheckAncestors(xbox);
        CheckAncestors(appx);
        if (File.Exists(xbox) && File.Exists(appx)) return true;
        var common = Path.GetDirectoryName(game);
        var steamapps = common == null ? null : Path.GetDirectoryName(common);
        if (!string.Equals(Path.GetFileName(common), "common", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Path.GetFileName(steamapps), "steamapps", StringComparison.OrdinalIgnoreCase)) return false;
        var manifest = Path.Combine(steamapps!, "appmanifest_945360.acf");
        CheckAncestors(manifest);
        return File.Exists(manifest);
    }

    private static async Task ReadExactlyAsync(Stream stream, byte[] bytes, CancellationToken ct)
    {
        var position = 0;
        while (position < bytes.Length)
        {
            var count = await stream.ReadAsync(bytes.AsMemory(position), ct).ConfigureAwait(false);
            if (count == 0) throw new InvalidDataException("The staged manifest is incomplete.");
            position += count;
        }
    }

    private void SetStatus(string status) { Volatile.Write(ref _status, status); _log(status); }
    private AutomaticUpdateOutcome Finish(AutomaticUpdateOutcome outcome, string status) { SetStatus(status); return outcome; }
}
