using System.Diagnostics;
using System.Buffers.Binary;
using System.IO.Compression;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Win32;

namespace MalumMenuEnhanced.Setup.Core;

public sealed record DownloadArtifact(Uri Url, string Sha256, string Name);
public sealed record SetupCatalog(string ModVersion, DownloadArtifact Loader, DownloadArtifact Plugin);
public enum GameArchitecture { X86, X64 }
public sealed record InstallProgress(string Message, int Percent);
public sealed record InstallResult(string GameDirectory, string BackupDirectory, bool LoaderInstalled);

/// <summary>Installs only verified, pinned releases. No administrator access is requested.</summary>
public sealed class InstallerService
{
    private const long MaxDownload = 128L * 1024 * 1024;
    private const long MaxExtracted = 512L * 1024 * 1024;
    private const int MaxEntries = 4096;
    private const string PluginRelative = "BepInEx/plugins/MalumMenuEnhanced.dll";
    private static readonly HttpClient SharedHttp = new() { Timeout = TimeSpan.FromMinutes(10) };
    private readonly SetupCatalog _catalog;
    private readonly HttpClient _http;
    private readonly Func<bool> _isGameRunning;
    private readonly Action<string>? _beforeChange;
    private static readonly string[] MenuNames = ["MalumMenu.dll", "HaddadMenu.dll", "MalumMenuEnhanced.dll"];

    public InstallerService(SetupCatalog catalog, HttpClient? http = null)
        : this(catalog, http ?? SharedHttp, IsGameRunning, null) { }

    internal InstallerService(SetupCatalog catalog, HttpClient http, Func<bool> isGameRunning,
        Action<string>? beforeChange = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(http);
        _catalog = catalog;
        _http = http;
        _isGameRunning = isGameRunning;
        _beforeChange = beforeChange;
        ValidateArtifact(catalog.Loader);
        ValidateArtifact(catalog.Plugin);
    }

    public async Task<InstallResult> InstallAsync(string gameDirectory,
        IProgress<InstallProgress>? progress = null, CancellationToken ct = default)
    {
        var error = ValidateGameDirectory(gameDirectory);
        if (error != null) throw new InvalidOperationException(error);
        var game = Path.TrimEndingDirectorySeparator(Path.GetFullPath(gameDirectory));
        var architecture = GetGameArchitecture(game);
        RequireGameClosed();
        ct.ThrowIfCancellationRequested();
        var reuseLoader = CheckExistingLoader(game, architecture);
        var stage = Path.Combine(Path.GetTempPath(), "MalumMenuEnhanced.Setup", Guid.NewGuid().ToString("N"));
        var backup = Path.Combine(game, "MalumMenuEnhanced-backups",
            DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..8]);
        FileTransaction? transaction = null;
        var preserveRecovery = false;
        try
        {
            SafePaths.CheckAncestors(stage);
            Directory.CreateDirectory(stage);
            progress?.Report(new("Downloading the verified mod", 10));
            var pluginZip = await DownloadAsync(_catalog.Plugin, stage, "plugin.zip", ct);
            var pluginFiles = await ExtractAsync(pluginZip, Path.Combine(stage, "plugin"), false, ct);
            var expectedPluginFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { PluginRelative, "README.md", "FORK.md", "CREDITS.md", "FEATURES.md", "LICENSE" };
            if (!pluginFiles.Contains(PluginRelative, StringComparer.OrdinalIgnoreCase) ||
                pluginFiles.Any(f => !expectedPluginFiles.Contains(f)))
                throw new InvalidDataException("The mod download does not have the expected files. Please download setup again.");
            RequireAssembly(SafePaths.Under(Path.Combine(stage, "plugin"), PluginRelative), "MalumMenuEnhanced", null);

            progress?.Report(new("Downloading and checking the verified game loader", 30));
            var loaderZip = await DownloadAsync(_catalog.Loader, stage, "loader.zip", ct);
            var loaderFiles = await ExtractAsync(loaderZip, Path.Combine(stage, "loader"), true, ct);
            if (!CheckCompatibleLoader(Path.Combine(stage, "loader"), architecture))
                throw new InvalidDataException("The downloaded loader is incomplete or incompatible. Please download setup again.");

            // Downloads and extraction finish before any changes to the game.
            ct.ThrowIfCancellationRequested();
            error = ValidateGameDirectory(game);
            if (error != null) throw new InvalidOperationException(error);
            RequireGameClosed();
            if (GetGameArchitecture(game) != architecture || CheckExistingLoader(game, architecture) != reuseLoader)
                throw new IOException("The game folder changed while downloading. Close other installers and try again.");
            if (reuseLoader)
            {
                if (!MatchesVerifiedLoader(game, Path.Combine(stage, "loader"), loaderFiles))
                    throw new InvalidOperationException("Your existing loader does not match this setup's verified loader release. Restore the original game files or remove that loader first. Your files were not changed.");
                loaderFiles = [];
            }
            var destinations = loaderFiles.Select(f => SafePaths.Under(game, f)).ToList();
            var plugins = Path.Combine(game, "BepInEx", "plugins");
            destinations.Add(Path.Combine(plugins, "MalumMenuEnhanced.dll"));
            foreach (var path in destinations) SafePaths.CheckAncestors(path);
            SafePaths.CheckAncestors(backup);

            transaction = new FileTransaction(game, stage, backup, () =>
            {
                ct.ThrowIfCancellationRequested();
                RequireGameClosed();
            }, _beforeChange);
            progress?.Report(new("Backing up your previous menu", 60));
            // These backups live outside the loader's plugin scan directory.
            foreach (var name in MenuNames)
            {
                var old = Path.Combine(plugins, name);
                if (File.Exists(old)) transaction.BackupMenu(old, name);
            }
            progress?.Report(new("Installing the verified files", 75));
            foreach (var file in loaderFiles)
                transaction.Put(SafePaths.Under(Path.Combine(stage, "loader"), file), SafePaths.Under(game, file));
            transaction.Put(SafePaths.Under(Path.Combine(stage, "plugin"), PluginRelative),
                Path.Combine(plugins, "MalumMenuEnhanced.dll"));
            ct.ThrowIfCancellationRequested();
            RequireGameClosed();
            progress?.Report(new("Installation finished. You can open Among Us.", 100));
            transaction.Complete();
            return new(game, Directory.Exists(backup) ? backup : "", !reuseLoader);
        }
        catch (Exception installError)
        {
            try { transaction?.Rollback(); }
            catch (Exception restoreError)
            {
                preserveRecovery = true;
                throw new IOException("Installation stopped and some files could not be restored. Your original files are preserved in: " +
                    stage + ". Close programs using the game files before restoring them. Original problem: " + installError.Message,
                    new AggregateException(installError, restoreError));
            }
            throw;
        }
        finally
        {
            try { if (!preserveRecovery && Directory.Exists(stage)) Directory.Delete(stage, true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    public static string? ValidateGameDirectory(string directory)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
                return "Choose the Among Us game folder, the folder containing Among Us.exe.";
            var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
            if (full.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Any(s => s.Equals("WindowsApps", StringComparison.OrdinalIgnoreCase)))
                return "This protected WindowsApps folder cannot be changed. Choose the XboxGames Among Us Content folder instead.";
            SafePaths.CheckAncestors(full);
            foreach (var name in new[] { "Among Us.exe", "GameAssembly.dll", "UnityPlayer.dll" })
            {
                var path = Path.Combine(full, name);
                SafePaths.CheckAncestors(path);
                if (!File.Exists(path)) return "This is not the Among Us game folder. Choose the folder containing Among Us.exe, GameAssembly.dll and UnityPlayer.dll.";
            }
            var data = Path.Combine(full, "Among Us_Data");
            SafePaths.CheckAncestors(data);
            if (!Directory.Exists(data)) return "Among Us_Data is missing. Repair Among Us in your game launcher, then try again.";
            // Xbox's launch executable can be an unreadable alias. Read the native Unity files instead.
            var native = ReadPeArchitecture(Path.Combine(full, "GameAssembly.dll"));
            if (native == null || ReadPeArchitecture(Path.Combine(full, "UnityPlayer.dll")) != native)
                return "The game's native files have an unsupported or mismatched architecture. Repair Among Us in your launcher, then try again.";
            var identityError = ValidateGameIdentity(full);
            if (identityError != null) return identityError;
            return null;
        }
        catch (UnauthorizedAccessException) { return "This folder is protected. Choose the writable Steam or XboxGames Among Us folder."; }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException or NotSupportedException)
        { return "This folder cannot be used safely. Choose the actual Among Us folder, without shortcuts or linked folders."; }
    }

    public static GameArchitecture GetGameArchitecture(string directory)
        => ReadPeArchitecture(Path.Combine(directory, "GameAssembly.dll"))
            ?? throw new InvalidDataException("The game's native architecture could not be verified.");

    private static string? ValidateGameIdentity(string folder)
    {
        var infoPath = Path.Combine(folder, "Among Us_Data", "app.info");
        SafePaths.CheckAncestors(infoPath);
        if (!File.Exists(infoPath) || new FileInfo(infoPath).Length > 4096)
            return "The game's identity could not be verified. Repair Among Us in your launcher, then try again.";
        var info = File.ReadAllLines(infoPath);
        if (info.Length < 2 || info[0].Trim() != "Innersloth" || info[1].Trim() != "Among Us")
            return "This folder does not contain the supported Among Us game.";
        var metadataPath = Path.Combine(folder, "Among Us_Data", "il2cpp_data", "Metadata", "global-metadata.dat");
        SafePaths.CheckAncestors(metadataPath);
        if (!File.Exists(metadataPath)) return "The IL2CPP game data is missing. Repair Among Us in your launcher, then try again.";
        using (var stream = File.OpenRead(metadataPath))
        using (var reader = new BinaryReader(stream))
            if (stream.Length < 8 || reader.ReadUInt32() != 0xFAB11BAF || reader.ReadUInt32() != 31)
                return "This game's IL2CPP data version is not supported. This setup supports Among Us 2026.9.29.";

        var xboxPath = Path.Combine(folder, "MicrosoftGame.config");
        var manifestPath = Path.Combine(folder, "AppxManifest.xml");
        if (IsSteamFolder(folder)) return ValidateSteamIdentity(folder);
        if (!File.Exists(xboxPath) || !File.Exists(manifestPath))
            return "This game version could not be verified. Automatic setup currently supports Xbox Among Us 2026.9.29. Use the manual installation guide for other stores.";
        var config = ReadSafeXml(xboxPath);
        var manifest = ReadSafeXml(manifestPath);
        var identity = config.Root?.Elements().FirstOrDefault(e => e.Name.LocalName == "Identity");
        var package = manifest.Root?.Elements().FirstOrDefault(e => e.Name.LocalName == "Identity");
        const string publisher = "CN=5A57224C-EF56-4C83-83C1-11C78B125F60";
        if (config.Root?.Name.LocalName != "Game" || manifest.Root?.Name.LocalName != "Package" ||
            identity?.Attribute("Name")?.Value != "Innersloth.AmongUs" || package?.Attribute("Name")?.Value != "Innersloth.AmongUs" ||
            identity.Attribute("Publisher")?.Value != publisher || package.Attribute("Publisher")?.Value != publisher)
            return "The game's publisher or identity does not match the supported Among Us release.";
        if (identity.Attribute("Version")?.Value != "2026.9.293.0" || package.Attribute("Version")?.Value != "2026.9.293.0")
            return "This Among Us version is not supported. This setup supports Xbox Among Us 2026.9.29.";
        var architecture = config.Root.Elements().FirstOrDefault(e => e.Name.LocalName == "DesktopRegistration")?
            .Elements().FirstOrDefault(e => e.Name.LocalName == "ProcessorArchitecture")?.Value;
        var executable = config.Root.Elements().FirstOrDefault(e => e.Name.LocalName == "ExecutableList")?
            .Elements().Any(e => e.Name.LocalName == "Executable" && e.Attribute("Name")?.Value == "Among Us.exe") == true;
        if (architecture != "x64" || package.Attribute("ProcessorArchitecture")?.Value != "x64" || !executable ||
            GetGameArchitecture(folder) != GameArchitecture.X64)
            return "This installer requires the supported Xbox 64-bit Among Us release.";
        return null;
    }

    private static XDocument ReadSafeXml(string path)
    {
        SafePaths.CheckAncestors(path);
        if (new FileInfo(path).Length > 1024 * 1024) throw new InvalidDataException("The game's identity file is too large.");
        using var reader = XmlReader.Create(path, new XmlReaderSettings
        { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 1024 * 1024 });
        try { return XDocument.Load(reader); }
        catch (XmlException) { throw new InvalidDataException("The game's identity file is invalid. Repair Among Us in your launcher."); }
    }

    public static IReadOnlyList<string> FindGameDirectories()
    {
        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in FindSteamGameDirectories(GetSteamRoots())) candidates.Add(candidate);
        foreach (var drive in DriveInfo.GetDrives())
            candidates.Add(Path.Combine(drive.Name, "XboxGames", "Among Us", "Content"));
        candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Epic Games", "AmongUs"));
        var manifests = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Epic", "EpicGamesLauncher", "Data", "Manifests");
        try
        {
            if (Directory.Exists(manifests)) foreach (var path in Directory.EnumerateFiles(manifests, "*.item"))
            {
                try
                {
                    var info = new FileInfo(path);
                    if (info.Length > 1024 * 1024) continue;
                    using var json = JsonDocument.Parse(File.ReadAllText(path));
                    if (json.RootElement.TryGetProperty("InstallLocation", out var location) && location.ValueKind == JsonValueKind.String)
                    {
                        var candidate = location.GetString();
                        if (!string.IsNullOrEmpty(candidate)) candidates.Add(candidate);
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        foreach (var process in Process.GetProcessesByName("Among Us"))
        {
            using (process)
            {
                try
                {
                    var exe = process.MainModule?.FileName;
                    if (exe != null) candidates.Add(Path.GetDirectoryName(exe)!);
                }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or NotSupportedException) { }
            }
        }
        return candidates.Where(c => ValidateGameDirectory(c) == null).Select(Path.GetFullPath).Order(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static bool IsSteamFolder(string folder)
    {
        var common = Path.GetDirectoryName(folder);
        var steamapps = common == null ? null : Path.GetDirectoryName(common);
        return (Path.GetFileName(common)?.Equals("common", StringComparison.OrdinalIgnoreCase) == true &&
            Path.GetFileName(steamapps)?.Equals("steamapps", StringComparison.OrdinalIgnoreCase) == true) ||
            new[] { "steam_api.dll", "steam_api64.dll", "steam_appid.txt" }.Any(name => File.Exists(Path.Combine(folder, name)));
    }

    private static string? ValidateSteamIdentity(string folder)
    {
        var common = Path.GetDirectoryName(folder);
        var steamapps = common == null ? null : Path.GetDirectoryName(common);
        if (Path.GetFileName(common)?.Equals("common", StringComparison.OrdinalIgnoreCase) != true ||
            Path.GetFileName(steamapps)?.Equals("steamapps", StringComparison.OrdinalIgnoreCase) != true ||
            !TryReadSteamInstall(steamapps!, out var installed) || !Path.GetFullPath(folder).Equals(installed, StringComparison.OrdinalIgnoreCase))
            return "The Steam installation could not be verified. Choose Steam's Among Us folder using Manage > Browse local files.";
        if (ReadPeArchitecture(Path.Combine(folder, "Among Us.exe")) != GetGameArchitecture(folder))
            return "The Steam game's native files have mismatched architectures. Verify Among Us files in Steam, then try again.";
        if (GetGameArchitecture(folder) != GameArchitecture.X64)
            return "This older 32-bit Steam build is not supported. Update Among Us in Steam to the 64-bit 2026.9.29 release, then try again.";
        // The Steam app manifest identifies the game, but does not establish its gameplay version.
        if (!HasSupportedUnityVersion(folder))
            return "This Steam Among Us version could not be verified or is not supported. Update or verify Among Us in Steam. This setup supports Among Us 2026.9.29.";
        return null;
    }

    private static bool HasSupportedUnityVersion(string folder)
    {
        var path = Path.Combine(folder, "Among Us_Data", "globalgamemanagers");
        try
        {
            SafePaths.CheckAncestors(path);
            using var stream = File.OpenRead(path);
            if (stream.Length < 48 || stream.Length > 16 * 1024 * 1024) return false;
            var bytes = new byte[(int)stream.Length];
            stream.ReadExactly(bytes);
            if (stream.ReadByte() != -1 || BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(8, 4)) != 22 || bytes[16] != 0) return false;
            var metadataSize = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(20, 4));
            var declaredSize = BinaryPrimitives.ReadInt64BigEndian(bytes.AsSpan(24, 8));
            var dataOffset = BinaryPrimitives.ReadInt64BigEndian(bytes.AsSpan(32, 8));
            if (declaredSize != bytes.Length || dataOffset < 48 || dataOffset > bytes.Length || metadataSize > dataOffset - 48) return false;
            var metadata = new UnityDataReader(bytes, 48, checked(48 + (int)metadataSize));
            // The following PlayerSettings layout is verified against this exact Unity release schema.
            if (metadata.CString() != "2022.3.44f1" || metadata.Int32() != 19 || metadata.Byte() != 0) return false;
            var types = new int[metadata.Count(4096)];
            for (var i = 0; i < types.Length; i++)
            {
                types[i] = metadata.Int32();
                metadata.Skip(3); // stripped flag and script type index
                if (types[i] == 114) metadata.Skip(16); // script identifier
                metadata.Skip(16); // old type hash; release builds contain no type tree
            }
            var objectCount = metadata.Count(65536);
            (int Start, int Size)? playerSettings = null;
            for (var i = 0; i < objectCount; i++)
            {
                metadata.Align();
                metadata.Skip(8); // path identifier
                var relative = metadata.Int64();
                var size = metadata.UInt32();
                var type = metadata.Int32();
                if (type < 0 || type >= types.Length || relative < 0 || relative > bytes.Length - dataOffset ||
                    size > bytes.Length - dataOffset - relative) return false;
                if (types[type] != 129) continue;
                if (playerSettings != null) return false;
                playerSettings = (checked((int)(dataOffset + relative)), checked((int)size));
            }
            if (playerSettings == null) return false;
            var settings = new UnityDataReader(bytes, playerSettings.Value.Start,
                playerSettings.Value.Start + playerSettings.Value.Size);
            // Fixed-size blocks and variable fields come from PlayerSettings' Unity 2022.3.44f1
            // release schema: AssetRipper/TypeTreeDumps, InfoJson/2022.3.44f1.json.
            settings.Skip(36);
            if (settings.String() != "Innersloth" || settings.String() != "Among Us") return false;
            settings.Skip(104);
            settings.Skip(settings.Count(256) * 16); // splash-screen logos
            settings.Align();
            settings.Skip(124);
            var mipmapGroups = settings.Count(256);
            for (var i = 0; i < mipmapGroups; i++) { settings.String(); settings.Skip(4); }
            settings.Skip(settings.Count(256) * 4); // stack-trace logging settings
            settings.Align();
            settings.Skip(76);
            settings.String(); // macAppStoreCategory
            settings.Skip(144);
            settings.String(); // visionOSBundleVersion
            settings.String(); // tvOSBundleVersion
            return settings.String() == "2026.9.29"; // bundleVersion, exposed by Application.version
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or OverflowException)
        { return false; }
    }

    private sealed class UnityDataReader(byte[] data, int start, int end)
    {
        private static readonly System.Text.UTF8Encoding Utf8 = new(false, true);
        private int _position = start;
        private ReadOnlySpan<byte> Take(int count)
        {
            if (count < 0 || _position > end - count) throw new InvalidDataException("The Unity game settings are incomplete.");
            var bytes = data.AsSpan(_position, count);
            _position += count;
            return bytes;
        }
        public void Skip(int count) => Take(count);
        public byte Byte() => Take(1)[0];
        public int Int32() => BinaryPrimitives.ReadInt32LittleEndian(Take(4));
        public uint UInt32() => BinaryPrimitives.ReadUInt32LittleEndian(Take(4));
        public long Int64() => BinaryPrimitives.ReadInt64LittleEndian(Take(8));
        public int Count(int maximum)
        {
            var count = Int32();
            if (count < 0 || count > maximum) throw new InvalidDataException("The Unity game settings contain an invalid array.");
            return count;
        }
        public void Align() => Skip((4 - (_position & 3)) & 3);
        public string CString()
        {
            var count = Math.Min(256, end - _position);
            var index = Array.IndexOf(data, (byte)0, _position, count);
            if (index < 0) throw new InvalidDataException("The Unity version is missing.");
            var value = Utf8.GetString(Take(index - _position));
            Skip(1);
            return value;
        }
        public string String()
        {
            var value = Utf8.GetString(Take(Count(4096)));
            Align();
            return value;
        }
    }

    private static IEnumerable<string> GetSteamRoots()
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Steam")
        };
        foreach (var drive in DriveInfo.GetDrives())
            foreach (var relative in new[] { "Steam", "SteamLibrary", "Program Files (x86)/Steam" })
                roots.Add(Path.Combine(drive.Name, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (OperatingSystem.IsWindows())
        {
            foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
                foreach (var view in new[] { RegistryView.Registry32, RegistryView.Registry64 })
                {
                    try
                    {
                        using var root = RegistryKey.OpenBaseKey(hive, view);
                        using var key = root.OpenSubKey(@"Software\Valve\Steam");
                        foreach (var name in new[] { "SteamPath", "InstallPath" })
                            if (key?.GetValue(name) is string path && !string.IsNullOrWhiteSpace(path)) roots.Add(path);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
                }
        }
        return roots;
    }

    internal static IReadOnlyList<string> FindSteamGameDirectories(IEnumerable<string> steamRoots)
    {
        var libraries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var steamRoot in steamRoots.Take(128))
        {
            try
            {
                if (string.IsNullOrWhiteSpace(steamRoot) || !Path.IsPathFullyQualified(steamRoot)) continue;
                var root = Path.GetFullPath(steamRoot);
                SafePaths.CheckAncestors(root);
                libraries.Add(root);
                var config = Path.Combine(root, "steamapps", "libraryfolders.vdf");
                if (!File.Exists(config)) continue;
                var entries = ReadVdf(config);
                var libraryFolders = entries.SingleOrDefault(e => e.Key.Equals("libraryfolders", StringComparison.OrdinalIgnoreCase));
                if (libraryFolders?.Children == null) continue;
                foreach (var library in libraryFolders.Children.Where(e => e.Key.All(char.IsAsciiDigit)).Take(128))
                {
                    var path = library.Value ?? library.Children?.SingleOrDefault(e => e.Key.Equals("path", StringComparison.OrdinalIgnoreCase))?.Value;
                    if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)) continue;
                    var full = Path.GetFullPath(path);
                    SafePaths.CheckAncestors(full);
                    if (libraries.Count < 256) libraries.Add(full);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or InvalidOperationException or NotSupportedException) { }
        }
        var games = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var library in libraries)
            if (TryReadSteamInstall(Path.Combine(library, "steamapps"), out var candidate)) games.Add(candidate!);
        return games.Order(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static bool TryReadSteamInstall(string steamapps, out string? directory)
    {
        directory = null;
        try
        {
            var path = Path.Combine(steamapps, "appmanifest_945360.acf");
            if (!File.Exists(path)) return false;
            var app = ReadVdf(path).SingleOrDefault(e => e.Key.Equals("AppState", StringComparison.OrdinalIgnoreCase));
            var values = app?.Children;
            if (values?.SingleOrDefault(e => e.Key.Equals("appid", StringComparison.OrdinalIgnoreCase))?.Value != "945360") return false;
            var installed = values.SingleOrDefault(e => e.Key.Equals("installdir", StringComparison.OrdinalIgnoreCase))?.Value;
            if (installed == null || installed.IndexOfAny(['/', '\\']) >= 0 || SafePaths.ZipName(installed) != installed) return false;
            directory = SafePaths.Under(Path.Combine(steamapps, "common"), installed);
            SafePaths.CheckAncestors(directory);
            return Directory.Exists(directory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or InvalidOperationException or NotSupportedException)
        { return false; }
    }

    private sealed record VdfEntry(string Key, string? Value, List<VdfEntry>? Children);

    private static List<VdfEntry> ReadVdf(string path)
    {
        SafePaths.CheckAncestors(path);
        if (new FileInfo(path).Length > 1024 * 1024) throw new InvalidDataException("The Steam library file is too large.");
        using var reader = new StreamReader(path);
        var buffer = new char[1024 * 1024 + 1];
        var count = reader.ReadBlock(buffer, 0, buffer.Length);
        if (count > 1024 * 1024 || !reader.EndOfStream) throw new InvalidDataException("The Steam library file is too large.");
        var input = new string(buffer, 0, count);
        var position = 0;
        var tokens = 0;
        string? Token()
        {
            while (position < input.Length)
            {
                if (char.IsWhiteSpace(input[position]) || input[position] == '\uFEFF') { position++; continue; }
                if (input[position] == '/' && position + 1 < input.Length && input[position + 1] == '/')
                { while (position < input.Length && input[position] != '\n') position++; continue; }
                break;
            }
            if (position == input.Length) return null;
            if (++tokens > 32768) throw new InvalidDataException("The Steam library file contains too many entries.");
            var current = input[position++];
            if (current is '{' or '}') return current.ToString();
            var text = new System.Text.StringBuilder();
            var quoted = current == '"';
            if (!quoted) text.Append(current);
            while (position < input.Length)
            {
                current = input[position];
                if (quoted && current == '"') { position++; return text.ToString(); }
                if (!quoted && (char.IsWhiteSpace(current) || current is '{' or '}')) return text.ToString();
                position++;
                if (quoted && current == '\\' && position < input.Length && input[position] is '\\' or '"') current = input[position++];
                if (current < 32 && current is not '\n' and not '\r' and not '\t') throw new InvalidDataException("Invalid Steam library text.");
                text.Append(current);
                if (text.Length > 4096) throw new InvalidDataException("The Steam library file contains a value that is too long.");
            }
            if (quoted) throw new InvalidDataException("The Steam library file is incomplete.");
            return text.ToString();
        }
        List<VdfEntry> Object(int depth)
        {
            if (depth > 16) throw new InvalidDataException("The Steam library file is too deeply nested.");
            var result = new List<VdfEntry>();
            while (true)
            {
                var key = Token();
                if (key == "}") { if (depth == 0) throw new InvalidDataException("Unexpected Steam library closing brace."); return result; }
                if (key == null) { if (depth != 0) throw new InvalidDataException("The Steam library file is incomplete."); return result; }
                if (key == "{") throw new InvalidDataException("Invalid Steam library key.");
                var value = Token();
                if (value == null || value == "}") throw new InvalidDataException("Missing Steam library value.");
                result.Add(value == "{" ? new(key, null, Object(depth + 1)) : new(key, value, null));
            }
        }
        return Object(0);
    }

    private static bool IsGameRunning()
    {
        var processes = Process.GetProcessesByName("Among Us");
        foreach (var process in processes) process.Dispose();
        return processes.Length != 0;
    }

    private void RequireGameClosed()
    {
        if (_isGameRunning()) throw new InvalidOperationException("Close Among Us completely, then press Install again.");
    }

    private static void ValidateArtifact(DownloadArtifact artifact)
    {
        if (artifact.Url == null || !artifact.Url.IsAbsoluteUri || artifact.Url.Scheme != Uri.UriSchemeHttps ||
            artifact.Sha256 == null || artifact.Sha256.Length != 64 || !artifact.Sha256.All(Uri.IsHexDigit))
            throw new ArgumentException("The installer download information is invalid. Please download setup again.");
    }

    private async Task<string> DownloadAsync(DownloadArtifact artifact, string stage, string name, CancellationToken ct)
    {
        using var response = await _http.GetAsync(artifact.Url, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode) throw new IOException("The download could not be completed. Check your connection and try again.");
        if (response.Content.Headers.ContentLength > MaxDownload)
            throw new InvalidDataException("The download is larger than expected. Installation stopped safely.");
        var path = Path.Combine(stage, name);
        await using var input = await response.Content.ReadAsStreamAsync(ct);
        await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81920];
        long size = 0;
        int read;
        while ((read = await input.ReadAsync(buffer, ct)) != 0)
        {
            size += read;
            if (size > MaxDownload) throw new InvalidDataException("The download is larger than expected. Installation stopped safely.");
            hash.AppendData(buffer, 0, read);
            await output.WriteAsync(buffer.AsMemory(0, read), ct);
        }
        if (!CryptographicOperations.FixedTimeEquals(hash.GetHashAndReset(), Convert.FromHexString(artifact.Sha256)))
            throw new InvalidDataException("The download did not pass its safety check. Nothing was installed. Please try again.");
        return path;
    }

    private static async Task<List<string>> ExtractAsync(string zipPath, string destination, bool loader, CancellationToken ct)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        if (archive.Entries.Count > MaxEntries) throw new InvalidDataException("The download contains too many files.");
        var files = new List<string>();
        var seen = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        // Validate the whole archive before extracting any entry.
        foreach (var entry in archive.Entries)
        {
            var path = SafePaths.ZipName(entry.FullName);
            var directory = entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\');
            if ((((uint)entry.ExternalAttributes >> 16) & 0xF000) == 0xA000 ||
                (entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("The download contains a linked file and cannot be installed safely.");
            if (!seen.TryAdd(path, directory)) throw new InvalidDataException("The download contains duplicate files.");
            if (entry.Length > MaxDownload || entry.Length < 0 || (total += entry.Length) > MaxExtracted)
                throw new InvalidDataException("The download expands beyond the safe size limit.");
            if (loader && !AllowedLoaderPath(path, directory)) throw new InvalidDataException("The loader contains unexpected files.");
            if (!directory) files.Add(path);
        }
        foreach (var file in files)
        {
            var parts = file.Split('/');
            for (var i = 1; i < parts.Length; i++)
                if (seen.TryGetValue(string.Join('/', parts[..i]), out var isDirectory) && !isDirectory)
                    throw new InvalidDataException("The download contains conflicting file paths.");
        }
        foreach (var entry in archive.Entries)
        {
            ct.ThrowIfCancellationRequested();
            var path = SafePaths.ZipName(entry.FullName);
            var target = SafePaths.Under(destination, path);
            SafePaths.CheckAncestors(target);
            if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\')) { Directory.CreateDirectory(target); continue; }
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await using var input = entry.Open();
            await using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
            var buffer = new byte[81920];
            long written = 0;
            int read;
            while ((read = await input.ReadAsync(buffer, ct)) != 0)
            {
                written += read;
                if (written > entry.Length || written > MaxDownload) throw new InvalidDataException("A file exceeded its expected size.");
                await output.WriteAsync(buffer.AsMemory(0, read), ct);
            }
            if (written != entry.Length) throw new InvalidDataException("The download contains an incomplete file.");
        }
        return files;
    }

    private static bool AllowedLoaderPath(string path, bool directory)
    {
        var parts = path.Split('/');
        if (parts[0].Equals("BepInEx", StringComparison.OrdinalIgnoreCase))
            return parts.Length == 1 || parts[1].Equals("core", StringComparison.OrdinalIgnoreCase) ||
                (directory && parts.Length == 2 && new[] { "plugins", "patchers" }.Contains(parts[1], StringComparer.OrdinalIgnoreCase));
        return parts[0].Equals("dotnet", StringComparison.OrdinalIgnoreCase) ||
            (parts.Length == 1 && new[] { "winhttp.dll", "doorstop_config.ini", ".doorstop_version", "changelog.txt" }
                .Contains(parts[0], StringComparer.OrdinalIgnoreCase));
    }

    private static bool CheckExistingLoader(string game, GameArchitecture architecture)
    {
        var signals = new[] { "winhttp.dll", "doorstop_config.ini", "BepInEx/core", "dotnet" };
        foreach (var signal in signals) SafePaths.CheckAncestors(SafePaths.Under(game, signal));
        if (!signals.Any(s => File.Exists(SafePaths.Under(game, s)) || Directory.Exists(SafePaths.Under(game, s)))) return false;
        if (CheckCompatibleLoader(game, architecture)) return true;
        throw new InvalidOperationException("An unknown or incomplete game loader is already installed. Restore the original game files or remove that loader first. Your files were not changed.");
    }

    internal static bool CheckCompatibleLoader(string folder, GameArchitecture architecture = GameArchitecture.X64)
    {
        try
        {
            RequireAssembly(Path.Combine(folder, "BepInEx", "core", "BepInEx.Core.dll"), "BepInEx.Core", 6);
            RequireAssembly(Path.Combine(folder, "BepInEx", "core", "BepInEx.Unity.IL2CPP.dll"), "BepInEx.Unity.IL2CPP", 6);
            RequireAssembly(Path.Combine(folder, "BepInEx", "core", "BepInEx.Preloader.Core.dll"), "BepInEx.Preloader.Core", 6);
            RequireAssembly(Path.Combine(folder, "BepInEx", "core", "BepInEx.Unity.Common.dll"), "BepInEx.Unity.Common", 6);
            RequireAssembly(Path.Combine(folder, "BepInEx", "core", "Il2CppInterop.Runtime.dll"), "Il2CppInterop.Runtime", null);
            RequireAssembly(Path.Combine(folder, "dotnet", "System.Private.CoreLib.dll"), "System.Private.CoreLib", null);
            var configPath = Path.Combine(folder, "doorstop_config.ini");
            SafePaths.CheckAncestors(configPath);
            var config = File.ReadAllLines(configPath);
            var entries = config.Select(l => l.Trim()).Where(l => !l.StartsWith('#') && !l.StartsWith(';'))
                .Where(l => l.Contains('=')).Select(l => l.Split('=', 2)).ToArray();
            var enabled = entries.FirstOrDefault(e => e[0].Trim().Equals("enabled", StringComparison.OrdinalIgnoreCase));
            var target = entries.FirstOrDefault(e => e[0].Trim().Equals("target_assembly", StringComparison.OrdinalIgnoreCase));
            var coreclr = entries.FirstOrDefault(e => e[0].Trim().Equals("coreclr_path", StringComparison.OrdinalIgnoreCase));
            var corlib = entries.FirstOrDefault(e => e[0].Trim().Equals("corlib_dir", StringComparison.OrdinalIgnoreCase));
            return enabled != null && enabled[1].Trim().Equals("true", StringComparison.OrdinalIgnoreCase) &&
                target != null && target[1].Trim().Replace('\\', '/').Equals("BepInEx/core/BepInEx.Unity.IL2CPP.dll", StringComparison.OrdinalIgnoreCase) &&
                coreclr != null && coreclr[1].Trim().Replace('\\', '/').Equals("dotnet/coreclr.dll", StringComparison.OrdinalIgnoreCase) &&
                corlib != null && corlib[1].Trim().Equals("dotnet", StringComparison.OrdinalIgnoreCase) &&
                ReadPeArchitecture(Path.Combine(folder, "winhttp.dll")) == architecture &&
                ReadPeArchitecture(Path.Combine(folder, "dotnet", "coreclr.dll")) == architecture;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or BadImageFormatException or InvalidDataException)
        { return false; }
    }

    private static bool MatchesVerifiedLoader(string existing, string verified, List<string> files)
    {
        var inventory = files.Where(f => f.StartsWith("BepInEx/core/", StringComparison.OrdinalIgnoreCase) ||
            f.StartsWith("dotnet/", StringComparison.OrdinalIgnoreCase) || f.Equals("winhttp.dll", StringComparison.OrdinalIgnoreCase))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var relative in inventory)
        {
            var current = SafePaths.Under(existing, relative);
            SafePaths.CheckAncestors(current);
            if (!File.Exists(current)) return false;
            using var actual = File.OpenRead(current);
            using var expected = File.OpenRead(SafePaths.Under(verified, relative));
            if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(actual), SHA256.HashData(expected))) return false;
        }
        foreach (var directory in new[] { "BepInEx/core", "dotnet" })
            foreach (var path in EnumerateSafeFiles(SafePaths.Under(existing, directory)))
                if (!inventory.Contains(Path.GetRelativePath(existing, path).Replace('\\', '/'))) return false;
        return true;
    }

    private static IEnumerable<string> EnumerateSafeFiles(string folder)
    {
        SafePaths.CheckAncestors(folder);
        foreach (var path in Directory.EnumerateFileSystemEntries(folder))
        {
            SafePaths.CheckAncestors(path);
            if (Directory.Exists(path))
            { foreach (var nested in EnumerateSafeFiles(path)) yield return nested; }
            else yield return path;
        }
    }

    private static void RequireAssembly(string path, string name, int? major)
    {
        SafePaths.CheckAncestors(path);
        using var input = File.OpenRead(path);
        using var pe = new PEReader(input);
        if (!pe.HasMetadata) throw new InvalidDataException("A downloaded component is not the expected assembly.");
        var metadata = pe.GetMetadataReader();
        var assembly = metadata.GetAssemblyDefinition();
        if (metadata.GetString(assembly.Name) != name || (major != null && assembly.Version.Major != major))
            throw new InvalidDataException("A downloaded component has an incompatible version.");
    }

    private static GameArchitecture? ReadPeArchitecture(string path)
    {
        SafePaths.CheckAncestors(path);
        using var input = File.OpenRead(path);
        using var reader = new BinaryReader(input);
        if (input.Length < 64 || reader.ReadUInt16() != 0x5A4D) return null;
        input.Position = 60;
        var offset = reader.ReadInt32();
        if (offset < 64 || offset > input.Length - 6) return null;
        input.Position = offset;
        if (reader.ReadUInt32() != 0x00004550) return null;
        return reader.ReadUInt16() switch { 0x014c => GameArchitecture.X86, 0x8664 => GameArchitecture.X64, _ => null };
    }
}

internal static class SafePaths
{
    internal static string ZipName(string name)
    {
        var path = name.Replace('\\', '/').TrimEnd('/');
        if (path.Length == 0 || path.StartsWith('/') || path.Length > 240)
            throw new InvalidDataException("The download contains an unsafe file path.");
        foreach (var part in path.Split('/'))
        {
            if (part.Length == 0 || part is "." or ".." || part.EndsWith('.') || part.EndsWith(' ') ||
                part.Any(c => c < 32 || "<>:\"|?*".Contains(c)))
                throw new InvalidDataException("The download contains an unsafe file path.");
            var stem = part.Split('.')[0].ToUpperInvariant();
            if (stem is "CON" or "PRN" or "AUX" or "NUL" ||
                (stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT")) && stem[3] is >= '1' and <= '9'))
                throw new InvalidDataException("The download contains a reserved file name.");
        }
        return path;
    }

    internal static string Under(string root, string relative)
    {
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!full.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("A file path is outside the installation folder.");
        return full;
    }

    internal static void CheckAncestors(string path)
    {
        var current = Path.GetFullPath(path);
        while (!string.IsNullOrEmpty(current))
        {
            // GetAttributes also detects broken links, unlike File.Exists/Directory.Exists.
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Linked folders or files cannot be installed safely.");
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
            current = Path.GetDirectoryName(current);
        }
    }
}

/// <summary>Records originals before each change and reverses changes in reverse order.</summary>
internal sealed class FileTransaction(string game, string stage, string backup, Action guard, Action<string>? beforeChange)
{
    private readonly List<(string Path, string? Original)> _changes = [];
    private readonly List<string> _directories = [];
    private bool _completed;

    private void MakeDirectory(string path)
    {
        SafePaths.CheckAncestors(path);
        if (Directory.Exists(path)) return;
        var parent = Path.GetDirectoryName(path)!;
        if (!Directory.Exists(parent)) MakeDirectory(parent);
        Directory.CreateDirectory(path);
        _directories.Add(path);
    }

    private void Before(string path)
    {
        guard();
        if (!Path.GetFullPath(path).StartsWith(Path.GetFullPath(game).TrimEnd('\\', '/') + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new IOException("The destination is outside the game folder.");
        beforeChange?.Invoke(path);
        guard();
        SafePaths.CheckAncestors(path);
        if (Directory.Exists(path)) throw new IOException("A folder is blocking a required installation file.");
    }

    private void Record(string path)
    {
        string? original = null;
        if (File.Exists(path))
        {
            original = Path.Combine(stage, "rollback", _changes.Count.ToString());
            Directory.CreateDirectory(Path.GetDirectoryName(original)!);
            File.Copy(path, original, false);
        }
        _changes.Add((path, original));
        File.WriteAllText(Path.Combine(stage, "recovery.json"), JsonSerializer.Serialize(
            _changes.Select(c => new { Destination = c.Path, Original = c.Original }),
            new JsonSerializerOptions { WriteIndented = true }));
    }

    internal void BackupMenu(string old, string name)
    {
        Before(old);
        var target = Path.Combine(backup, name);
        Before(target);
        MakeDirectory(backup);
        Record(target);
        File.Copy(old, target, false);
        Record(old);
        File.Delete(old);
    }

    internal void Put(string source, string destination)
    {
        Before(destination);
        MakeDirectory(Path.GetDirectoryName(destination)!);
        Record(destination);
        var temporary = Path.Combine(Path.GetDirectoryName(destination)!, ".mme-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            File.Copy(source, temporary, false);
            guard();
            SafePaths.CheckAncestors(destination);
            File.Move(temporary, destination, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    internal void Complete() => _completed = true;

    internal void Rollback()
    {
        if (_completed) return;
        List<Exception> failures = [];
        foreach (var change in _changes.AsEnumerable().Reverse())
        {
            try
            {
                SafePaths.CheckAncestors(change.Path);
                if (change.Original == null) { if (File.Exists(change.Path)) File.Delete(change.Path); }
                else File.Copy(change.Original, change.Path, true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { failures.Add(ex); }
        }
        foreach (var directory in _directories.AsEnumerable().Reverse())
        {
            try
            {
                SafePaths.CheckAncestors(directory);
                if (Directory.Exists(directory)) Directory.Delete(directory, false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { failures.Add(ex); }
        }
        if (failures.Count != 0)
            throw new IOException("Installation stopped, but some files could not be restored. Keep the backup folder and restore it before playing.", new AggregateException(failures));
    }
}
