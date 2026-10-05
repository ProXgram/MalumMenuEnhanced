#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MalumMenuEnhanced.Updates
{
    /// <summary>A release asset authenticated by the publisher's signed manifest.</summary>
    public sealed class UpdateAsset
    {
        public Uri Url { get; }
        public string Sha256 { get; }
        public long Size { get; }

        internal UpdateAsset(Uri url, string sha256, long size)
        {
            Url = url;
            Sha256 = sha256;
            Size = size;
        }
    }

    /// <summary>Release information accepted only after verifying its signature and schema.</summary>
    public sealed class UpdateManifest
    {
        public string ModVersion { get; }
        public Version ParsedVersion { get; }
        public IReadOnlyList<string> SupportedGameVersions { get; }
        public UpdateAsset PluginZip { get; }
        public UpdateAsset SetupExe { get; }

        internal UpdateManifest(string modVersion, Version parsedVersion, string[] supportedGameVersions,
            UpdateAsset pluginZip, UpdateAsset setupExe)
        {
            ModVersion = modVersion;
            ParsedVersion = parsedVersion;
            SupportedGameVersions = new ReadOnlyCollection<string>(supportedGameVersions);
            PluginZip = pluginZip;
            SetupExe = setupExe;
        }

        public bool IsNewerThan(string currentVersion)
        {
            return ParsedVersion.CompareTo(UpdateManifestVerifier.ParseNumericVersion(currentVersion)) > 0;
        }

        public bool SupportsGameVersion(string gameVersion)
        {
            UpdateManifestVerifier.ParseNumericVersion(gameVersion);
            foreach (string supported in SupportedGameVersions)
                if (string.Equals(supported, gameVersion, StringComparison.Ordinal)) return true;
            return false;
        }

        public void RequireCompatibleUpdate(string currentVersion, string gameVersion)
        {
            if (!SupportsGameVersion(gameVersion))
                throw new InvalidDataException("This release does not support the installed Among Us version.");
            if (!IsNewerThan(currentVersion))
                throw new InvalidDataException("The release must be newer than the installed mod version.");
        }
    }

    /// <summary>Verifies the bounded JSON envelope before interpreting any release data.</summary>
    public sealed class UpdateManifestVerifier
    {
        public const int MaximumEnvelopeBytes = 64 * 1024;
        public const int MaximumPayloadBytes = 32 * 1024;
        public const long MaximumAssetBytes = 100L * 1024 * 1024;
        private const string AssetPrefix = "https://github.com/ProXgram/MalumMenuEnhanced/releases/download/v";
        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);
        private readonly RSAParameters publicKey;
        private readonly int signatureBytes;

        public UpdateManifestVerifier(string publicKeyPem)
        {
            if (string.IsNullOrWhiteSpace(publicKeyPem) || publicKeyPem.Length > 16 * 1024)
                throw new ArgumentException("A publisher public key is required.", nameof(publicKeyPem));
            using RSA rsa = RSA.Create();
            rsa.ImportFromPem(publicKeyPem);
            if (rsa.KeySize < 2048 || rsa.KeySize > 8192)
                throw new ArgumentException("The publisher RSA key size is unsupported.", nameof(publicKeyPem));
            publicKey = rsa.ExportParameters(false);
            signatureBytes = rsa.KeySize / 8;
        }

        public UpdateManifest Verify(byte[] envelopeJson)
        {
            if (envelopeJson == null || envelopeJson.Length == 0 || envelopeJson.Length > MaximumEnvelopeBytes)
                throw new InvalidDataException("The update manifest envelope size is invalid.");
            try
            {
                ValidateUtf8(envelopeJson);
                using JsonDocument envelope = ParseDocument(envelopeJson);
                RequireProperties(envelope.RootElement, "payload", "signature");
                byte[] payload = DecodeCanonicalBase64(GetString(envelope.RootElement, "payload"), MaximumPayloadBytes);
                byte[] signature = DecodeCanonicalBase64(GetString(envelope.RootElement, "signature"), signatureBytes);
                if (signature.Length != signatureBytes)
                    throw new InvalidDataException("The update manifest signature size is invalid.");
                using RSA rsa = RSA.Create();
                rsa.ImportParameters(publicKey);
                if (!rsa.VerifyData(payload, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
                    throw new InvalidDataException("The update manifest signature is invalid.");

                // Interpret only the exact payload bytes authenticated above.
                ValidateUtf8(payload);
                using JsonDocument document = ParseDocument(payload);
                JsonElement root = document.RootElement;
                RequireProperties(root, "product", "modVersion", "supportedGameVersions", "pluginZip", "setupExe");
                if (GetString(root, "product") != "MalumMenuEnhanced")
                    throw new InvalidDataException("The update manifest product is incorrect.");
                string modVersion = GetString(root, "modVersion");
                Version parsedVersion = ParseNumericVersion(modVersion);
                string[] games = ReadSupportedGameVersions(root.GetProperty("supportedGameVersions"));
                UpdateAsset plugin = ReadAsset(root.GetProperty("pluginZip"), modVersion,
                    "MalumMenuEnhanced-" + modVersion + "-Plugin.zip");
                UpdateAsset setup = ReadAsset(root.GetProperty("setupExe"), modVersion, "MalumMenuEnhancedSetup.exe");
                return new UpdateManifest(modVersion, parsedVersion, games, plugin, setup);
            }
            catch (Exception ex) when (ex is JsonException || ex is FormatException || ex is DecoderFallbackException || ex is OverflowException)
            {
                throw new InvalidDataException("The update manifest format is invalid.", ex);
            }
        }

        public static Version ParseNumericVersion(string version)
        {
            if (string.IsNullOrEmpty(version) || version.Length > 43)
                throw new InvalidDataException("The version must contain two to four numeric parts.");
            string[] parts = version.Split('.');
            if (parts.Length < 2 || parts.Length > 4)
                throw new InvalidDataException("The version must contain two to four numeric parts.");
            int[] values = new int[4];
            for (int index = 0; index < parts.Length; index++)
            {
                string part = parts[index];
                if (part.Length == 0 || (part.Length > 1 && part[0] == '0'))
                    throw new InvalidDataException("The version contains an invalid numeric part.");
                foreach (char value in part)
                    if (value < '0' || value > '9')
                        throw new InvalidDataException("Prerelease and nonnumeric versions are unsupported.");
                if (!int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out values[index]))
                    throw new InvalidDataException("The version contains an invalid numeric part.");
            }
            // Version treats missing components as -1; updates compare omitted components as zero.
            return new Version(values[0], values[1], values[2], values[3]);
        }

        private static JsonDocument ParseDocument(byte[] json)
        {
            return JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 8
            });
        }

        private static void ValidateUtf8(byte[] bytes)
        {
            StrictUtf8.GetCharCount(bytes);
        }

        private static void RequireProperties(JsonElement element, params string[] names)
        {
            if (element.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("The update manifest requires a JSON object.");
            var expected = new HashSet<string>(names, StringComparer.Ordinal);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonProperty property in element.EnumerateObject())
                if (!expected.Contains(property.Name) || !seen.Add(property.Name))
                    throw new InvalidDataException("The update manifest has an unknown or duplicate property.");
            if (seen.Count != names.Length)
                throw new InvalidDataException("The update manifest has a missing property.");
        }

        private static string GetString(JsonElement element, string name)
        {
            JsonElement value = element.GetProperty(name);
            if (value.ValueKind != JsonValueKind.String)
                throw new InvalidDataException("The update manifest property must be a string.");
            return value.GetString() ?? throw new InvalidDataException("The update manifest string is missing.");
        }

        private static byte[] DecodeCanonicalBase64(string text, int maximumBytes)
        {
            if (text.Length == 0 || text.Length > ((maximumBytes + 2) / 3) * 4 || text.Length % 4 != 0)
                throw new InvalidDataException("The update manifest Base64 size is invalid.");
            byte[] bytes = Convert.FromBase64String(text);
            if (bytes.Length == 0 || bytes.Length > maximumBytes || Convert.ToBase64String(bytes) != text)
                throw new InvalidDataException("The update manifest Base64 is invalid.");
            return bytes;
        }

        private static string[] ReadSupportedGameVersions(JsonElement value)
        {
            if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() < 1 || value.GetArrayLength() > 16)
                throw new InvalidDataException("The supported game version list is invalid.");
            var games = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonElement item in value.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String)
                    throw new InvalidDataException("The supported game version must be a string.");
                string game = item.GetString() ?? "";
                ParseNumericVersion(game);
                if (!seen.Add(game)) throw new InvalidDataException("The supported game version list has a duplicate.");
                games.Add(game);
            }
            return games.ToArray();
        }

        private static UpdateAsset ReadAsset(JsonElement value, string modVersion, string filename)
        {
            RequireProperties(value, "url", "sha256", "size");
            string url = GetString(value, "url");
            string expected = AssetPrefix + modVersion + "/" + filename;
            // Comparing the original string rejects encoded separators, alternate hosts, ports,
            // user info, fragments, queries and path normalization before URI parsing.
            if (!string.Equals(url, expected, StringComparison.Ordinal) || !Uri.TryCreate(url, UriKind.Absolute, out Uri? parsed))
                throw new InvalidDataException("The update asset URL is not the expected release asset.");
            string sha = GetString(value, "sha256");
            if (sha.Length != 64) throw new InvalidDataException("The update asset SHA256 length is invalid.");
            foreach (char digit in sha)
                if (!((digit >= '0' && digit <= '9') || (digit >= 'a' && digit <= 'f') || (digit >= 'A' && digit <= 'F')))
                    throw new InvalidDataException("The update asset SHA256 contains an invalid digit.");
            JsonElement sizeElement = value.GetProperty("size");
            if (sizeElement.ValueKind != JsonValueKind.Number || !sizeElement.TryGetInt64(out long size) ||
                size < 1 || size >= MaximumAssetBytes)
                throw new InvalidDataException("The update asset size is invalid.");
            return new UpdateAsset(parsed, sha.ToUpperInvariant(), size);
        }
    }
}
