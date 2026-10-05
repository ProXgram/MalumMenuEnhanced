using AssetRipper.Primitives;
using LibCpp2IL;
using Iced.Intel;
using System.Text;

var root = @"C:\Program Files\WindowsApps\Innersloth.AmongUs_2026.9.293.0_x64__fw5x688tam7rm";
LibCpp2IlMain.LoadFromFile(Path.Combine(root, "GameAssembly.dll"), Path.Combine(root, "Among Us_Data", "il2cpp_data", "Metadata", "global-metadata.dat"), UnityVersion.Parse("2022.3.44f1"));
var metadata = LibCpp2IlMain.TheMetadata!;
var binary = LibCpp2IlMain.Binary!;
var names = new[] { "NormalPlayerTask", "SampleMinigame", "DiagnosticGame", "WifiGame", "PhotosMinigame", "CollectSamplesMinigame", "IncubateEggMinigame", "<CoStartProcessing>d__41" };
var allMethods = metadata.methodDefs.Where(method => method.MethodPointer > 0).OrderBy(method => method.MethodPointer).ToArray();
var output = new StringBuilder();
foreach (var type in metadata.typeDefs.Where(type => names.Contains(type.Name)))
{
    output.AppendLine("TYPE " + type.FullName);
    foreach (var field in type.Fields!)
    {
        var fieldIndex = Array.IndexOf(type.Fields, field);
        var offset = binary.GetFieldOffsetFromIndex(type.TypeIndex, fieldIndex, type.FirstFieldIdx + fieldIndex, false, false);
        output.AppendLine($"FIELD {field.Name} offset=0x{offset:X}");
    }
    foreach (var method in type.Methods!.Where(method => method.Name is "NextStep" or "FixedUpdate" or "Initialize" or "MoveNext" or "StartDiagnostic" or "TurnOff" or "Begin" or "WriteInitialData" or "Update"))
    {
        var next = allMethods.FirstOrDefault(other => other.MethodPointer > method.MethodPointer);
        var size = (int)Math.Min(8000, (next?.MethodPointer ?? method.MethodPointer + 8000) - method.MethodPointer);
        var raw = binary.GetRawBinaryContent();
        var bytes = raw.AsSpan((int)method.MethodOffsetInFile, size).ToArray();
        output.AppendLine($"METHOD {type.Name}.{method.Name} RVA=0x{method.Rva:X} length={size}");
        var decoder = Iced.Intel.Decoder.Create(64, new ByteArrayCodeReader(bytes)); decoder.IP = method.MethodPointer;
        var formatter = new IntelFormatter(); var line = new TextOutput();
        while (decoder.IP < method.MethodPointer + (uint)bytes.Length)
        {
            decoder.Decode(out var instruction); line.Text.Clear(); formatter.Format(in instruction, line);
            var target = instruction.FlowControl is FlowControl.Call or FlowControl.UnconditionalBranch ? instruction.NearBranchTarget : 0;
            var callee = target == 0 ? null : allMethods.FirstOrDefault(other => other.MethodPointer == target);
            output.AppendLine($"{binary.GetRva(instruction.IP):X}: {line.Text}" + (callee == null ? "" : " ; " + callee.DeclaringType!.Name + "." + callee.Name));
        }
    }
}
var destination = Path.Combine(AppContext.BaseDirectory, "native-task-review.txt"); File.WriteAllText(destination, output.ToString());
Console.WriteLine("Read-only native review saved: " + destination);

sealed class TextOutput : FormatterOutput
{
    public readonly StringBuilder Text = new();
    public override void Write(string text, FormatterTextKind kind) => Text.Append(text);
}
