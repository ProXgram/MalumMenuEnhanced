using System.IO.Compression;
using System.Buffers.Binary;
using System.Net;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using MalumMenuEnhanced.Setup.Core;

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
    public string SteamRoot => Path.Combine(Root, "Steam");
    public string SteamManifest => Path.Combine(SteamRoot, "steamapps", "appmanifest_945360.acf");
    public string PluginPath => Path.Combine(Game, "BepInEx", "plugins", "MalumMenuEnhanced.dll");
    public byte[] PluginDll { get; } = AssemblyBytes("MalumMenuEnhanced", 1);
    public FakeHttp Http { get; } = new();
    public HttpClient Client { get; }
    public SetupCatalog Catalog { get; private set; }
    private readonly List<(string, byte[], int)> _loader;
    public Fixture(bool steam = false)
    {
        Game = steam ? Path.Combine(SteamRoot, "steamapps", "common", "Among Us") : Path.Combine(Root, "Among Us");
        Directory.CreateDirectory(Path.Combine(Game, "Among Us_Data"));
        foreach (var file in new[] { "Among Us.exe", "GameAssembly.dll", "UnityPlayer.dll" }) File.WriteAllBytes(Path.Combine(Game, file), NativeBytes());
        Write("Among Us_Data/app.info", "Innersloth\nAmong Us");
        var metadata = Path.Combine(Game, "Among Us_Data/il2cpp_data/Metadata/global-metadata.dat");
        Directory.CreateDirectory(Path.GetDirectoryName(metadata)!);
        File.WriteAllBytes(metadata, BitConverter.GetBytes(0xFAB11BAFu).Concat(BitConverter.GetBytes(31u)).ToArray());
        if (steam)
        {
            File.WriteAllText(SteamManifest, "\"AppState\" { \"appid\" \"945360\" \"name\" \"Among Us\" \"installdir\" \"Among Us\" \"buildid\" \"123\" }");
            Write("steam_api64.dll", "Steam native API marker");
            // Fixture contains a bounded PlayerSettings object, not just a version string elsewhere in the file.
            File.WriteAllBytes(Path.Combine(Game, "Among Us_Data", "globalgamemanagers"), UnitySettingsBytes("2026.9.29"));
        }
        else
        {
            Write("MicrosoftGame.config", "<Game><Identity Name=\"Innersloth.AmongUs\" Publisher=\"CN=5A57224C-EF56-4C83-83C1-11C78B125F60\" Version=\"2026.9.293.0\"/><DesktopRegistration><ProcessorArchitecture>x64</ProcessorArchitecture></DesktopRegistration><ExecutableList><Executable Name=\"Among Us.exe\"/></ExecutableList></Game>");
            Write("AppxManifest.xml", "<Package xmlns=\"http://schemas.microsoft.com/appx/manifest/foundation/windows10\"><Identity Name=\"Innersloth.AmongUs\" Publisher=\"CN=5A57224C-EF56-4C83-83C1-11C78B125F60\" Version=\"2026.9.293.0\" ProcessorArchitecture=\"x64\"/></Package>");
        }
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
    public void Dispose()
    {
        Client.Dispose();
        // Remove fixture junctions before their targets; never traverse an alias during cleanup.
        void RemoveLinks(string directory)
        {
            foreach (var child in Directory.EnumerateDirectories(directory))
                if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0) Directory.Delete(child, false);
                else RemoveLinks(child);
        }
        RemoveLinks(Root);
        Directory.Delete(Root, true);
    }
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    public static byte[] NativeBytes(GameArchitecture architecture = GameArchitecture.X64)
    {
        var bytes = new byte[256]; bytes[0] = 0x4D; bytes[1] = 0x5A; BitConverter.GetBytes(128).CopyTo(bytes, 60);
        bytes[128] = 0x50; bytes[129] = 0x45;
        BitConverter.GetBytes((ushort)(architecture == GameArchitecture.X64 ? 0x8664 : 0x014c)).CopyTo(bytes, 132);
        return bytes;
    }
    public static byte[] UnitySettingsBytes(string version)
    {
        using var settingsStream = new MemoryStream();
        using var settings = new BinaryWriter(settingsStream, Encoding.UTF8, true);
        void Align(BinaryWriter writer) { while ((writer.BaseStream.Position & 3) != 0) writer.Write((byte)0); }
        void String(string value)
        { var bytes = Encoding.UTF8.GetBytes(value); settings.Write(bytes.Length); settings.Write(bytes); Align(settings); }
        settings.Write(new byte[36]); String("Innersloth"); String("Among Us");
        settings.Write(new byte[104]); settings.Write(0); Align(settings);
        settings.Write(new byte[124]); settings.Write(0); settings.Write(0); Align(settings);
        settings.Write(new byte[76]); String("public.app-category.games"); settings.Write(new byte[144]);
        String("1.0"); String("1.0"); String(version);
        var objectBytes = settingsStream.ToArray();
        using var metadataStream = new MemoryStream();
        using var metadata = new BinaryWriter(metadataStream, Encoding.UTF8, true);
        metadata.Write(Encoding.UTF8.GetBytes("2022.3.44f1\0")); metadata.Write(19); metadata.Write(false);
        metadata.Write(1); metadata.Write(129); metadata.Write(false); metadata.Write((short)-1); metadata.Write(new byte[16]);
        metadata.Write(1); Align(metadata); metadata.Write(1L); metadata.Write(0L); metadata.Write((uint)objectBytes.Length); metadata.Write(0);
        var metadataBytes = metadataStream.ToArray();
        var dataOffset = (48 + metadataBytes.Length + 15) & ~15;
        var result = new byte[dataOffset + objectBytes.Length];
        BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(0, 4), (uint)metadataBytes.Length);
        BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(4, 4), (uint)result.Length);
        BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(8, 4), 22);
        BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(12, 4), (uint)dataOffset);
        BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(20, 4), (uint)metadataBytes.Length);
        BinaryPrimitives.WriteInt64BigEndian(result.AsSpan(24, 8), result.Length);
        BinaryPrimitives.WriteInt64BigEndian(result.AsSpan(32, 8), dataOffset);
        metadataBytes.CopyTo(result, 48); objectBytes.CopyTo(result, dataOffset);
        return result;
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
    public static byte[] RewriteZip(byte[] bytes, string name, byte[] replacement)
    {
        using var stream = new MemoryStream(bytes); using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        return Zip(archive.Entries.Select(entry =>
        {
            using var input = entry.Open(); using var contents = new MemoryStream(); input.CopyTo(contents);
            return (entry.FullName, entry.FullName == name ? replacement : contents.ToArray(), entry.ExternalAttributes);
        }));
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
