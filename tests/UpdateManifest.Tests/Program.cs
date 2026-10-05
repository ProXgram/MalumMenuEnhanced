using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MalumMenuEnhanced.Updates;

if (args is ["--verify-manifest", var manifestPath])
{
    try
    {
        if (new FileInfo(manifestPath).Length > UpdateManifestVerifier.MaximumEnvelopeBytes)
            throw new InvalidDataException("Manifest envelope exceeds the byte limit.");
        UpdateManifest manifest = new UpdateManifestVerifier(UpdateTrust.PublicKeyPem).Verify(File.ReadAllBytes(manifestPath));
        Console.WriteLine($"Verified signed MalumMenuEnhanced {manifest.ModVersion}; supports 2026.9.29: {manifest.SupportsGameVersion("2026.9.29")}; plugin {manifest.PluginZip.Size} bytes; setup {manifest.SetupExe.Size} bytes.");
        return 0;
    }
    catch (Exception ex)
    {
        Console.WriteLine("Manifest verification failed: " + ex.Message);
        return 1;
    }
}

using var signingKey = RSA.Create(2048);
using var otherKey = RSA.Create(2048);
var verifier = new UpdateManifestVerifier(signingKey.ExportSubjectPublicKeyInfoPem());
var tests = new List<(string Name, Action Run)>();
void Test(string name, Action run) => tests.Add((name, run));
void Check(bool condition, string message = "Assertion failed") { if (!condition) throw new Exception(message); }
void Reject(Action run, string? messagePart = null)
{
    try { run(); }
    catch (InvalidDataException ex)
    {
        if (messagePart != null) Check(ex.Message.Contains(messagePart, StringComparison.OrdinalIgnoreCase), ex.Message);
        return;
    }
    throw new Exception("Expected an invalid manifest to be rejected");
}

JsonObject Payload(string modVersion = "1.1.0")
{
    JsonObject Asset(string filename) => new()
    {
        ["url"] = "https://github.com/ProXgram/MalumMenuEnhanced/releases/download/v" + modVersion + "/" + filename,
        ["sha256"] = new string('a', 64),
        ["size"] = 196172
    };
    return new JsonObject
    {
        ["product"] = "MalumMenuEnhanced",
        ["modVersion"] = modVersion,
        ["supportedGameVersions"] = new JsonArray("2026.9.29"),
        ["pluginZip"] = Asset("MalumMenuEnhanced-" + modVersion + "-Plugin.zip"),
        ["setupExe"] = Asset("MalumMenuEnhancedSetup.exe")
    };
}
byte[] EnvelopeBytes(byte[] payload, RSA? key = null)
{
    byte[] signature = (key ?? signingKey).SignData(payload, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
    return JsonSerializer.SerializeToUtf8Bytes(new { payload = Convert.ToBase64String(payload), signature = Convert.ToBase64String(signature) });
}
byte[] Envelope(JsonObject? payload = null, RSA? key = null) => EnvelopeBytes(Encoding.UTF8.GetBytes((payload ?? Payload()).ToJsonString()), key);
UpdateManifest Valid() => verifier.Verify(Envelope());
JsonObject ReadEnvelope(byte[] bytes) => JsonNode.Parse(bytes)!.AsObject();

Test("Signed release resolves exact assets and supported game", () =>
{
    UpdateManifest manifest = Valid();
    Check(manifest.ModVersion == "1.1.0");
    Check(manifest.ParsedVersion == new Version(1, 1, 0, 0));
    Check(manifest.SupportedGameVersions.SequenceEqual(new[] { "2026.9.29" }));
    Check(manifest.PluginZip.Url.AbsoluteUri.EndsWith("/v1.1.0/MalumMenuEnhanced-1.1.0-Plugin.zip"));
    Check(manifest.SetupExe.Url.AbsoluteUri.EndsWith("/v1.1.0/MalumMenuEnhancedSetup.exe"));
    Check(manifest.PluginZip.Sha256 == new string('A', 64));
    Check(manifest.PluginZip.Size == 196172);
    manifest.RequireCompatibleUpdate("1.0", "2026.9.29");
});
Test("Exact signed bytes permit insignificant JSON whitespace", () =>
{
    byte[] payload = Encoding.UTF8.GetBytes(" \r\n" + Payload().ToJsonString() + "\n\t ");
    Check(verifier.Verify(EnvelopeBytes(payload)).ModVersion == "1.1.0");
});
Test("Payload tampering rejects the original signature", () =>
{
    JsonObject envelope = ReadEnvelope(Envelope());
    string payload = Encoding.UTF8.GetString(Convert.FromBase64String(envelope["payload"]!.GetValue<string>()));
    envelope["payload"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(payload.Replace("1.1.0", "1.2.0")));
    Reject(() => verifier.Verify(Encoding.UTF8.GetBytes(envelope.ToJsonString())), "signature");
});
Test("A signature from another key is rejected", () => Reject(() => verifier.Verify(Envelope(key: otherKey)), "signature"));
Test("A modified signature is rejected", () =>
{
    JsonObject envelope = ReadEnvelope(Envelope());
    byte[] signature = Convert.FromBase64String(envelope["signature"]!.GetValue<string>());
    signature[signature.Length / 2] ^= 1;
    envelope["signature"] = Convert.ToBase64String(signature);
    Reject(() => verifier.Verify(Encoding.UTF8.GetBytes(envelope.ToJsonString())), "signature");
});
Test("Signature length must match the pinned RSA key", () =>
{
    JsonObject envelope = ReadEnvelope(Envelope());
    envelope["signature"] = Convert.ToBase64String(new byte[255]);
    Reject(() => verifier.Verify(Encoding.UTF8.GetBytes(envelope.ToJsonString())), "signature size");
});
Test("RSA PSS does not substitute for the required PKCS1 signature", () =>
{
    byte[] payload = Encoding.UTF8.GetBytes(Payload().ToJsonString());
    byte[] signature = signingKey.SignData(payload, HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
    byte[] envelope = JsonSerializer.SerializeToUtf8Bytes(new { payload = Convert.ToBase64String(payload), signature = Convert.ToBase64String(signature) });
    Reject(() => verifier.Verify(envelope), "signature");
});
Test("Current release is not an update", () =>
{
    UpdateManifest manifest = Valid();
    Check(!manifest.IsNewerThan("1.1.0"));
    Reject(() => manifest.RequireCompatibleUpdate("1.1.0", "2026.9.29"), "newer");
});
Test("Omitted version components compare as zero", () =>
{
    var manifest = verifier.Verify(Envelope(Payload("1.0")));
    Check(!manifest.IsNewerThan("1.0.0"));
    Check(!manifest.IsNewerThan("1.0.0.0"));
    Check(UpdateManifestVerifier.ParseNumericVersion("1.0") == UpdateManifestVerifier.ParseNumericVersion("1.0.0.0"));
});
Test("Downgrade release is rejected", () =>
{
    UpdateManifest manifest = Valid();
    Check(!manifest.IsNewerThan("1.2"));
    Reject(() => manifest.RequireCompatibleUpdate("2.0", "2026.9.29"), "newer");
});
Test("Version comparison is numeric", () => Check(verifier.Verify(Envelope(Payload("1.10.0"))).IsNewerThan("1.9.9")));
Test("A fourth revision part is supported", () => Check(verifier.Verify(Envelope(Payload("1.1.0.2"))).IsNewerThan("1.1.0.1")));
Test("Unsupported game blocks otherwise valid update", () =>
{
    UpdateManifest manifest = Valid();
    Check(!manifest.SupportsGameVersion("2026.10.4"));
    Reject(() => manifest.RequireCompatibleUpdate("1.0", "2026.10.4"), "does not support");
});
Test("Multiple supported games use exact version identity", () =>
{
    JsonObject payload = Payload();
    payload["supportedGameVersions"] = new JsonArray("2026.9.29", "2026.10.4");
    UpdateManifest manifest = verifier.Verify(Envelope(payload));
    Check(manifest.SupportsGameVersion("2026.10.4"));
    Check(!manifest.SupportsGameVersion("2026.10.4.0"));
});
Test("Supported game collection cannot be mutated", () =>
{
    var collection = (IList<string>)Valid().SupportedGameVersions;
    Check(collection.IsReadOnly);
    try { collection[0] = "2027.1.1"; } catch (NotSupportedException) { return; }
    throw new Exception("Supported versions unexpectedly mutable");
});

foreach (string version in new[] { "1", "1.2.3.4.5", "1.1.0-preview", "v1.1.0", "1. 1", "+1.1", "-1.1", "1..1", "1.01", "01.1", "1.2147483648", "１.1", "1.1\n" })
    Test("Invalid or prerelease version rejected: " + JsonSerializer.Serialize(version), () => Reject(() => verifier.Verify(Envelope(Payload(version)))));
Test("Malformed current version is rejected", () => Reject(() => Valid().IsNewerThan("1.0-rc")));
Test("Malformed local game version is rejected", () => Reject(() => Valid().SupportsGameVersion("19")));

foreach (string url in new[]
{
    "http://github.com/ProXgram/MalumMenuEnhanced/releases/download/v1.1.0/MalumMenuEnhanced-1.1.0-Plugin.zip",
    "https://github.com.evil.test/ProXgram/MalumMenuEnhanced/releases/download/v1.1.0/MalumMenuEnhanced-1.1.0-Plugin.zip",
    "https://github.com@evil.test/ProXgram/MalumMenuEnhanced/releases/download/v1.1.0/MalumMenuEnhanced-1.1.0-Plugin.zip",
    "https://github.com:443/ProXgram/MalumMenuEnhanced/releases/download/v1.1.0/MalumMenuEnhanced-1.1.0-Plugin.zip",
    "https://github.com/ProXgram/MalumMenuEnhanced/releases/download/v1.0/MalumMenuEnhanced-1.1.0-Plugin.zip",
    "https://github.com/ProXgram/MalumMenuEnhanced/releases/download/v1.1.0/Other.dll",
    "https://github.com/ProXgram/MalumMenuEnhanced/releases/download/v1.1.0/%4DalumMenuEnhanced-1.1.0-Plugin.zip",
    "https://github.com/ProXgram/MalumMenuEnhanced/releases/download/v1.1.0/../v1.1.0/MalumMenuEnhanced-1.1.0-Plugin.zip",
    "https://github.com/ProXgram/MalumMenuEnhanced/releases/download/v1.1.0/MalumMenuEnhanced-1.1.0-Plugin.zip?raw=1",
    "https://github.com/ProXgram/MalumMenuEnhanced/releases/download/v1.1.0/MalumMenuEnhanced-1.1.0-Plugin.zip#ignored",
    " https://github.com/ProXgram/MalumMenuEnhanced/releases/download/v1.1.0/MalumMenuEnhanced-1.1.0-Plugin.zip",
    "file:///C:/MalumMenuEnhancedSetup.exe"
})
    Test("Unsafe or mismatched plugin URL rejected: " + url, () =>
    {
        JsonObject payload = Payload();
        payload["pluginZip"]!["url"] = url;
        Reject(() => verifier.Verify(Envelope(payload)), "URL");
    });
Test("Setup URL has its own exact basename and matching tag", () =>
{
    JsonObject payload = Payload();
    payload["setupExe"]!["url"] = "https://github.com/ProXgram/MalumMenuEnhanced/releases/download/v1.0.1/MalumMenuEnhancedSetup.exe";
    Reject(() => verifier.Verify(Envelope(payload)), "URL");
});

foreach (string sha in new[] { "", new string('a', 63), new string('a', 65), new string('g', 64), new string('a', 63) + " " })
    Test("Invalid SHA256 rejected: length " + sha.Length + " suffix " + (sha.Length > 0 ? sha[^1] : '-'), () =>
    {
        JsonObject payload = Payload();
        payload["pluginZip"]!["sha256"] = sha;
        Reject(() => verifier.Verify(Envelope(payload)), "SHA256");
    });
foreach (long size in new[] { -1L, 0, UpdateManifestVerifier.MaximumAssetBytes, long.MaxValue })
    Test("Invalid asset size rejected: " + size, () =>
    {
        JsonObject payload = Payload();
        payload["setupExe"]!["size"] = size;
        Reject(() => verifier.Verify(Envelope(payload)), "size");
    });
Test("Fractional and string asset sizes are rejected", () =>
{
    JsonObject payload = Payload();
    payload["setupExe"]!["size"] = 1.5;
    Reject(() => verifier.Verify(Envelope(payload)), "size");
    payload["setupExe"]!["size"] = "1234";
    Reject(() => verifier.Verify(Envelope(payload)), "size");
});
Test("Maximum minus one byte is a valid asset bound", () =>
{
    JsonObject payload = Payload();
    payload["setupExe"]!["size"] = UpdateManifestVerifier.MaximumAssetBytes - 1;
    Check(verifier.Verify(Envelope(payload)).SetupExe.Size == UpdateManifestVerifier.MaximumAssetBytes - 1);
});

foreach (string missing in new[] { "product", "modVersion", "supportedGameVersions", "pluginZip", "setupExe" })
    Test("Missing payload property rejected: " + missing, () =>
    {
        JsonObject payload = Payload();
        payload.Remove(missing);
        Reject(() => verifier.Verify(Envelope(payload)), "missing");
    });
Test("Unknown payload field is rejected", () =>
{
    JsonObject payload = Payload();
    payload["run"] = "unexpected.exe";
    Reject(() => verifier.Verify(Envelope(payload)), "unknown");
});
Test("Wrong product is rejected", () =>
{
    JsonObject payload = Payload();
    payload["product"] = "DifferentMenu";
    Reject(() => verifier.Verify(Envelope(payload)), "product");
});
Test("Null or numeric version is rejected", () =>
{
    JsonObject payload = Payload();
    payload["modVersion"] = null;
    Reject(() => verifier.Verify(Envelope(payload)), "string");
    payload["modVersion"] = 1.1;
    Reject(() => verifier.Verify(Envelope(payload)), "string");
});
Test("Unknown and missing asset fields are rejected", () =>
{
    JsonObject payload = Payload();
    payload["pluginZip"]!["filename"] = "custom.dll";
    Reject(() => verifier.Verify(Envelope(payload)), "unknown");
    payload = Payload();
    payload["pluginZip"]!.AsObject().Remove("sha256");
    Reject(() => verifier.Verify(Envelope(payload)), "missing");
});
Test("Duplicate payload property is rejected even with a valid signature", () =>
{
    string payload = Payload().ToJsonString();
    payload = payload.Insert(1, "\"modVersion\":\"1.0\",");
    Reject(() => verifier.Verify(EnvelopeBytes(Encoding.UTF8.GetBytes(payload))), "duplicate");
});
Test("Escaped duplicate payload property is rejected", () =>
{
    string payload = Payload().ToJsonString().Insert(1, "\"mod\\u0056ersion\":\"1.0\",");
    Reject(() => verifier.Verify(EnvelopeBytes(Encoding.UTF8.GetBytes(payload))), "duplicate");
});
Test("Duplicate asset property is rejected", () =>
{
    string payload = Payload().ToJsonString().Replace("\"size\":196172", "\"size\":1,\"size\":196172");
    Reject(() => verifier.Verify(EnvelopeBytes(Encoding.UTF8.GetBytes(payload))), "duplicate");
});
Test("Duplicate and unknown envelope properties are rejected", () =>
{
    string envelope = Encoding.UTF8.GetString(Envelope()).Insert(1, "\"signature\":\"AAAA\",");
    Reject(() => verifier.Verify(Encoding.UTF8.GetBytes(envelope)), "duplicate");
    JsonObject parsed = ReadEnvelope(Envelope());
    parsed["extra"] = true;
    Reject(() => verifier.Verify(Encoding.UTF8.GetBytes(parsed.ToJsonString())), "unknown");
});

Test("Empty game version array is rejected", () =>
{
    JsonObject payload = Payload();
    payload["supportedGameVersions"] = new JsonArray();
    Reject(() => verifier.Verify(Envelope(payload)), "list");
});
Test("Duplicate and malformed supported game versions are rejected", () =>
{
    JsonObject payload = Payload();
    payload["supportedGameVersions"] = new JsonArray("2026.9.29", "2026.9.29");
    Reject(() => verifier.Verify(Envelope(payload)), "duplicate");
    payload["supportedGameVersions"] = new JsonArray("2026.9.29-beta");
    Reject(() => verifier.Verify(Envelope(payload)));
});
Test("Nonstring and oversized game version list is rejected", () =>
{
    JsonObject payload = Payload();
    payload["supportedGameVersions"] = new JsonArray(2026);
    Reject(() => verifier.Verify(Envelope(payload)), "string");
    payload["supportedGameVersions"] = new JsonArray(Enumerable.Range(1, 17).Select(v => JsonValue.Create("2026.9." + v) as JsonNode).ToArray());
    Reject(() => verifier.Verify(Envelope(payload)), "list");
});
Test("Nonobject signed payload is rejected", () => Reject(() => verifier.Verify(EnvelopeBytes(Encoding.UTF8.GetBytes("[]"))), "object"));
Test("Comments and trailing comma are rejected", () =>
{
    string payload = Payload().ToJsonString();
    Reject(() => verifier.Verify(EnvelopeBytes(Encoding.UTF8.GetBytes(payload.Insert(1, "/* ignored */")))));
    Reject(() => verifier.Verify(EnvelopeBytes(Encoding.UTF8.GetBytes(payload.Insert(payload.Length - 1, ",")))));
});
Test("Too deeply nested JSON is rejected", () =>
{
    string payload = "{\"extra\":" + new string('[', 20) + "0" + new string(']', 20) + "}";
    Reject(() => verifier.Verify(EnvelopeBytes(Encoding.UTF8.GetBytes(payload))));
});
Test("Invalid UTF8 payload is rejected despite a valid signature", () =>
{
    byte[] payload = Encoding.UTF8.GetBytes(Payload().ToJsonString());
    payload[2] = 0xFF;
    Reject(() => verifier.Verify(EnvelopeBytes(payload)), "format");
});
Test("Invalid UTF8 envelope is rejected", () =>
{
    byte[] envelope = Envelope();
    envelope[2] = 0xFF;
    Reject(() => verifier.Verify(envelope), "format");
});
Test("Empty null and oversized envelopes are rejected", () =>
{
    Reject(() => verifier.Verify(Array.Empty<byte>()));
    Reject(() => verifier.Verify(null!));
    Reject(() => verifier.Verify(new byte[UpdateManifestVerifier.MaximumEnvelopeBytes + 1]), "size");
});
Test("Payload bytes are bounded before signature verification", () =>
{
    byte[] payload = Encoding.UTF8.GetBytes(Payload().ToJsonString() + new string(' ', UpdateManifestVerifier.MaximumPayloadBytes));
    Reject(() => verifier.Verify(EnvelopeBytes(payload)), "Base64");
});
Test("Payload at the exact byte limit is accepted", () =>
{
    string json = Payload().ToJsonString();
    byte[] payload = Encoding.UTF8.GetBytes(json + new string(' ', UpdateManifestVerifier.MaximumPayloadBytes - Encoding.UTF8.GetByteCount(json)));
    Check(verifier.Verify(EnvelopeBytes(payload)).ModVersion == "1.1.0");
});
Test("Base64 whitespace is rejected", () =>
{
    JsonObject envelope = ReadEnvelope(Envelope());
    envelope["payload"] = envelope["payload"]!.GetValue<string>() + "    ";
    Reject(() => verifier.Verify(Encoding.UTF8.GetBytes(envelope.ToJsonString())), "Base64");
});
Test("Empty and malformed Base64 is rejected", () =>
{
    JsonObject envelope = ReadEnvelope(Envelope());
    envelope["signature"] = "";
    Reject(() => verifier.Verify(Encoding.UTF8.GetBytes(envelope.ToJsonString())), "Base64");
    envelope["signature"] = "????";
    Reject(() => verifier.Verify(Encoding.UTF8.GetBytes(envelope.ToJsonString())), "format");
});
Test("Noncanonical Base64 padding bits are rejected", () =>
{
    JsonObject envelope = ReadEnvelope(Envelope());
    // Both AB== and AA== decode to one zero byte, but only AA== is canonical.
    envelope["payload"] = "AB==";
    Reject(() => verifier.Verify(Encoding.UTF8.GetBytes(envelope.ToJsonString())), "Base64");
});
Test("Missing and nonstring envelope fields are rejected", () =>
{
    JsonObject envelope = ReadEnvelope(Envelope());
    envelope.Remove("signature");
    Reject(() => verifier.Verify(Encoding.UTF8.GetBytes(envelope.ToJsonString())), "missing");
    envelope = ReadEnvelope(Envelope());
    envelope["payload"] = 123;
    Reject(() => verifier.Verify(Encoding.UTF8.GetBytes(envelope.ToJsonString())), "string");
});
Test("Pinned publisher public key imports successfully", () => Check(new UpdateManifestVerifier(UpdateTrust.PublicKeyPem) != null));

int failures = 0;
foreach ((string name, Action run) in tests)
{
    try { run(); Console.WriteLine("PASS " + name); }
    catch (Exception ex) { failures++; Console.WriteLine("FAIL " + name + ": " + ex); }
}
Console.WriteLine($"{tests.Count - failures}/{tests.Count} update manifest checks passed.");
return failures == 0 ? 0 : 1;
