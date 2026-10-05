using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text.Json;

if (args.Length != 3) throw new ArgumentException("Usage: <game assembly> <plugin assembly> <report.json>");
var checks = new List<string>();
var provider = new TypeNames();
using var gamePe = new PEReader(File.OpenRead(args[0]));
var game = gamePe.GetMetadataReader();
using var pluginPe = new PEReader(File.OpenRead(args[1]));
var plugin = pluginPe.GetMetadataReader();
var targets = new Dictionary<string, string>
{
    ["IntroCutscene.CoBegin"] = "IEnumerator()",
    ["AmongUsClient.OnGameJoined"] = "Void(String)",
    ["AmongUsClient.OnGameEnd"] = "Void(EndGameResult)",
    ["MeetingHud.Awake"] = "Void()", ["MeetingHud.Close"] = "Void()",
    ["JudgeRole.OnMeetingStart"] = "Void()", ["JudgeRole.TryOverrule"] = "Boolean(PlayerId)",
    ["JudgeRole.ClearOverrule"] = "Void()", ["JudgeRole.ConsumeOverruleVotesUsage"] = "Void()",
    ["MeetingHud.CmdQueueOverruleVotes"] = "Void(PlayerId,PlayerId,UInt16)",
    ["MeetingHud.VotingComplete"] = "Void(VoterState[],NetworkedPlayerInfo,Boolean,Boolean,UInt16)",
    ["JudgeRole.get_HasAnOverruleUse"] = "Boolean()",
    ["JudgeRole.get_HasAlreadyOverruledThisMeeting"] = "Boolean()",
    ["JudgeRole.get_OverruledPlayerId"] = "PlayerId()", ["JudgeRole.get_OverruleNonce"] = "UInt16()",
};
var methods = Methods(game);
var actualTargets = new Dictionary<string, string>();
var knownVotingForms = new[]
{
    "Void(VoterState[],NetworkedPlayerInfo,Boolean,Boolean,UInt16)",
    "Void(Il2CppStructArray`1<VoterState>,NetworkedPlayerInfo,Boolean,Boolean,UInt16)",
};
foreach (var (name, expected) in targets)
{
    var found = methods.Where(x => x.Name == name).ToArray();
    bool matches = found.Length == 1 && (name == "MeetingHud.VotingComplete"
        ? knownVotingForms.Contains(found[0].Signature, StringComparer.Ordinal)
        : found[0].Signature == expected);
    Check(matches, "exact target " + name + " " + (found.Length == 1 ? found[0].Signature : "missing_or_ambiguous"));
    actualTargets[name] = found[0].Signature;
}
var hookSignatures = new Dictionary<string, string>
{
    ["IntroTrace.Prefix"] = "Void()", ["JoinTrace.Postfix"] = "Void()",
    ["EndTrace.Prefix"] = "Void()", ["EndTrace.Postfix"] = "Void()",
    ["MeetingStartTrace.Postfix"] = "Void(MeetingHud)",
    ["MeetingCloseTrace.Prefix"] = "Void(MeetingHud)", ["MeetingCloseTrace.Postfix"] = "Void(MeetingHud)",
    ["JudgeMeetingTrace.Prefix"] = "Void(JudgeRole)", ["JudgeMeetingTrace.Postfix"] = "Void(JudgeRole)",
    ["TryTrace.Prefix"] = "Void(JudgeRole,PlayerId)", ["TryTrace.Postfix"] = "Void(JudgeRole,PlayerId,Boolean)",
    ["CommandTrace.Prefix"] = "Void(MeetingHud,PlayerId,PlayerId,UInt16)",
    ["CommandTrace.Postfix"] = "Void(MeetingHud,PlayerId,PlayerId,UInt16)",
    ["ClearTrace.Prefix"] = "Void(JudgeRole)", ["ClearTrace.Postfix"] = "Void(JudgeRole)",
    ["ConsumeTrace.Prefix"] = "Void(JudgeRole)", ["ConsumeTrace.Postfix"] = "Void(JudgeRole)",
    ["VotingTrace.Prefix"] = "Void(MeetingHud,NetworkedPlayerInfo,Boolean,Boolean,UInt16)",
    ["VotingTrace.Postfix"] = "Void(MeetingHud,NetworkedPlayerInfo,Boolean,Boolean,UInt16)",
};
methods = Methods(plugin);
foreach (var (name, expected) in hookSignatures)
{
    var found = methods.Where(x => x.Name == name).ToArray();
    Check(found.Length == 1 && found[0].Signature == expected, "void observer signature without ref/out " + name + " " + expected);
}
var memberReferences = plugin.MemberReferences.Select(handle =>
{
    var reference = plugin.GetMemberReference(handle);
    var owner = reference.Parent.Kind switch
    {
        HandleKind.TypeReference => plugin.GetString(plugin.GetTypeReference((TypeReferenceHandle)reference.Parent).Name),
        HandleKind.TypeSpecification => plugin.GetTypeSpecification((TypeSpecificationHandle)reference.Parent).DecodeSignature(provider, (object?)null),
        _ => reference.Parent.Kind.ToString(),
    };
    return (Name: plugin.GetString(reference.Name), Owner: owner);
}).Distinct().ToArray();
var references = memberReferences.Select(x => x.Owner + "." + x.Name).Order().ToArray();
var forbidden = new HashSet<string> { "TryOverrule", "CmdQueueOverruleVotes", "ClearOverrule", "ConsumeOverruleVotesUsage", "VotingComplete", "StartRpc", "StartRpcImmediately", "FinishRpc", "FinishRpcImmediately", "SetValue", "Write", "WriteByte", "WriteBytes", "Copy", "Poke", "Send", "SendMessage" };
var prohibited = memberReferences.Where(x =>
    // Updating the diagnostic Dictionary is allowed; no game setter is.
    (x.Name.StartsWith("set_", StringComparison.Ordinal) && !(x.Name == "set_Item" && x.Owner.StartsWith("Dictionary`2<", StringComparison.Ordinal))) ||
    x.Name.StartsWith("Rpc", StringComparison.Ordinal) || forbidden.Contains(x.Name)).ToArray();
Check(prohibited.Length == 0, "no compiled gameplay setters, direct Judge actions, transport writes or reflection writes: " + string.Join(",", prohibited.Select(x => x.Owner + "." + x.Name)));
var typeNames = plugin.TypeReferences.Select(handle => plugin.GetString(plugin.GetTypeReference(handle).Name)).ToArray();
Check(!typeNames.Any(x => x.Contains("MessageReader", StringComparison.Ordinal) || x.Contains("MessageWriter", StringComparison.Ordinal)), "no shared reader or writer type reference");
var report = new
{
    schema = 2, passed = checks.Count, game_assembly_sha256 = Hash(args[0]), plugin_sha256 = Hash(args[1]),
    checks, exact_native_targets = actualTargets, known_voting_complete_forms = knownVotingForms,
    voting_complete_array_form = actualTargets["MeetingHud.VotingComplete"] == knownVotingForms[0] ? "sdk_managed_array" : "generated_interop_struct_array",
    exact_observer_hooks = hookSignatures, external_member_names = references,
    scope = "PE metadata only; no game assembly execution. Does not establish live Harmony binding or network acceptance.",
};
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[2]))!);
File.WriteAllText(args[2], JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
Console.WriteLine($"PASS {checks.Count} metadata checks; wrote {args[2]}");

List<(string Name, string Signature)> Methods(MetadataReader reader)
{
    var result = new List<(string, string)>();
    foreach (var typeHandle in reader.TypeDefinitions)
    {
        var type = reader.GetTypeDefinition(typeHandle);
        foreach (var methodHandle in type.GetMethods())
        {
            var method = reader.GetMethodDefinition(methodHandle);
            var signature = method.DecodeSignature(provider, (object?)null);
            result.Add((reader.GetString(type.Name) + "." + reader.GetString(method.Name), signature.ReturnType + "(" + string.Join(",", signature.ParameterTypes) + ")"));
        }
    }
    return result;
}
void Check(bool condition, string description)
{
    if (!condition) throw new InvalidOperationException("FAIL " + description);
    checks.Add(description);
}
static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

sealed class TypeNames : ISignatureTypeProvider<string, object?>
{
    public string GetArrayType(string elementType, ArrayShape shape) => elementType + "[]";
    public string GetByReferenceType(string elementType) => elementType + "&";
    public string GetFunctionPointerType(MethodSignature<string> signature) => "fnptr";
    public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments) => genericType + "<" + string.Join(",", typeArguments) + ">";
    public string GetGenericMethodParameter(object? genericContext, int index) => "!!" + index;
    public string GetGenericTypeParameter(object? genericContext, int index) => "!" + index;
    public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) => unmodifiedType;
    public string GetPinnedType(string elementType) => elementType;
    public string GetPointerType(string elementType) => elementType + "*";
    public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode.ToString();
    public string GetSZArrayType(string elementType) => elementType + "[]";
    public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) => reader.GetString(reader.GetTypeDefinition(handle).Name);
    public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind) => reader.GetString(reader.GetTypeReference(handle).Name);
    public string GetTypeFromSpecification(MetadataReader reader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind) => reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);
}
