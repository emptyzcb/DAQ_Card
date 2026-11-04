using IndustrialFieldDataAcquisitionTerminal;

const string source = """
void IO_Logic_Main(void)
{
    if (IO_DI_Level(INPUT_1) == 1)
    {
        IO_DO_Set(RELAY_1, 1);
        IO_DO_Set(RELAY_2, 1);
        IO_DO_Set(RELAY_3, 1);
        IO_DO_Set(RELAY_4, 1);
    }
    else
    {
        IO_DO_Set(RELAY_1, 0);
        IO_DO_Set(RELAY_2, 0);
        IO_DO_Set(RELAY_3, 0);
        IO_DO_Set(RELAY_4, 0);
    }
    if (IO_DI_Rising(INPUT_2))
    {
        IO_DO_Pulse(TRANSISTOR_1, 1000);
        IO_DO_Pulse(TRANSISTOR_2, 1000);
        IO_DO_Pulse(TRANSISTOR_3, 1000);
        IO_DO_Pulse(TRANSISTOR_4, 1000);
    }
    if (IO_DI_Level(INPUT_3) == 1)
    {
        IO_Delay(500);
        IO_AO_SetVoltage(5000);
    }
    if (IO_DI_Level(INPUT_4) == 1)
    {
        IO_AO_SetCurrent(12000);
    }
}
""";

var result = new IoLogicCompiler().Compile(source);
if (!result.Success)
{
    throw new InvalidOperationException(string.Join(Environment.NewLine, result.Errors));
}
if (result.Image is null || result.Image.ProgramSize == 0)
{
    throw new InvalidOperationException("IOCF v2 编译结果为空。");
}
if (result.UsesSnapshotInputs)
{
    throw new InvalidOperationException("Legacy DI/output rule incorrectly requires snapshot-input firmware.");
}

var body = result.Image.Bytes.AsSpan(IoLogicBinaryFormat.BodyOffset);
var requiredOpcodes = new byte[]
{
    IoLogicOpcode.DoSet,
    IoLogicOpcode.DoPulse,
    IoLogicOpcode.AoSetVoltage,
    IoLogicOpcode.AoSetCurrent
};
foreach (var opcode in requiredOpcodes)
{
    if (!body.Contains(opcode))
    {
        throw new InvalidOperationException($"Compiled image is missing opcode 0x{opcode:X2}.");
    }
}

Console.WriteLine($"Logic compiler smoke test passed: {result.Image.ProgramSize} program bytes, {result.Image.Bytes.Length} image bytes.");

var currentBreath = new IoLogicCompiler().Compile("void IO_Logic_Main(void) { IO_AO_BreathCurrent(4000, 20000, 4000); }");
if (!currentBreath.Success || currentBreath.Image is null)
    throw new InvalidOperationException("Current breathing must compile.");
if (!currentBreath.UsesCurrentBreath || result.UsesCurrentBreath)
    throw new InvalidOperationException("Current-breath firmware requirement was not detected correctly.");
if (!currentBreath.Image.Bytes.AsSpan(IoLogicBinaryFormat.BodyOffset).Contains(IoLogicOpcode.AoBreathCurrent))
    throw new InvalidOperationException("Current breathing opcode was not emitted.");
if (new IoLogicCompiler().Compile("void IO_Logic_Main(void) { IO_AO_BreathCurrent(21000, 22000, 4000); }").Success)
    throw new InvalidOperationException("Current breathing accepted an out-of-range output.");
var currentLoopPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
    "..", "..", "..", "..", "..", "Projects", "电流回环测试", "io_logic_user.c"));
var currentLoopResult = new IoLogicCompiler().Compile(File.ReadAllText(currentLoopPath));
if (!currentLoopResult.Success)
    throw new InvalidOperationException("Current loop project did not compile: " +
        string.Join(Environment.NewLine, currentLoopResult.Errors));

var installedExample = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
    "..", "..", "..", "..", "..", "Projects", "示例代码", "io_logic_user.c"));
if (!File.Exists(installedExample))
{
    throw new InvalidOperationException("Installed example is missing: " + installedExample);
}
var exampleSource = File.ReadAllText(installedExample);
if (exampleSource.Contains("IO_Logic_Run", StringComparison.Ordinal) ||
    exampleSource.Contains("IO_INPUT_ACTIVE", StringComparison.Ordinal))
{
    throw new InvalidOperationException("The installed example still uses the legacy IO API.");
}

var exampleResult = new IoLogicCompiler().Compile(exampleSource);
if (!exampleResult.Success)
{
    throw new InvalidOperationException("The installed example must compile as IOCF v2: " +
        string.Join(Environment.NewLine, exampleResult.Errors));
}
if (!exampleResult.UsesCurrentBreath || exampleResult.Image is null ||
    !exampleResult.Image.Bytes.AsSpan(IoLogicBinaryFormat.BodyOffset).Contains(IoLogicOpcode.AoBreathCurrent) ||
    !exampleResult.Image.Bytes.AsSpan(IoLogicBinaryFormat.BodyOffset).Contains(IoLogicOpcode.AoBreath))
    throw new InvalidOperationException("Mode 0 example must include current and voltage breathing.");

var templateResult = new IoLogicCompiler().Compile(IoLogicExamples.Default);
if (!templateResult.Success)
{
    throw new InvalidOperationException("The new-project template must compile as IOCF v2: " +
        string.Join(Environment.NewLine, templateResult.Errors));
}
if (!templateResult.UsesCurrentBreath || templateResult.Image is null ||
    !templateResult.Image.Bytes.AsSpan(IoLogicBinaryFormat.BodyOffset).Contains(IoLogicOpcode.AoBreathCurrent) ||
    !templateResult.Image.Bytes.AsSpan(IoLogicBinaryFormat.BodyOffset).Contains(IoLogicOpcode.AoBreath))
    throw new InvalidOperationException("The new-project template must include current and voltage breathing.");
if (exampleSource.Trim() != IoLogicExamples.Default.Trim())
{
    throw new InvalidOperationException("The installed example and new-project template differ.");
}

var chaseOutputs = new[]
{
    "RELAY_1", "RELAY_2", "RELAY_3", "RELAY_4",
    "TRANSISTOR_1", "TRANSISTOR_2", "TRANSISTOR_3", "TRANSISTOR_4"
};
var nextAction = 0;
foreach (var output in chaseOutputs)
{
    var on = $"IO_DO_Set({output}, 1);";
    var off = $"IO_DO_Set({output}, 0);";
    var onIndex = exampleSource.IndexOf(on, nextAction, StringComparison.Ordinal);
    var offIndex = onIndex < 0 ? -1 : exampleSource.IndexOf(off, onIndex + on.Length, StringComparison.Ordinal);
    if (onIndex < 0 || offIndex < 0 ||
        !exampleSource.Substring(onIndex, offIndex - onIndex).Contains("IO_Delay(500);", StringComparison.Ordinal))
    {
        throw new InvalidOperationException($"Chase example must turn {output} on, wait 500 ms, then turn it off.");
    }
    nextAction = offIndex + off.Length;
}

const string inputRule = """
void IO_Logic_Main(void)
{
    if (IO_AI_Valid(3) == 1 && IO_AI_Mv(3) < -1000)
    {
        IO_DO_Set(RELAY_1, 1);
    }
    if (IO_Current_uA(1) > 12000 && IO_AI_Valid(1) == 1)
    {
        IO_DO_Set(RELAY_2, 1);
    }
    if (IO_RS485_Valid(1) == 1 && IO_RS485_Value(1, 0) > 500)
    {
        IO_DO_Set(TRANSISTOR_1, 1);
    }
}
""";
var inputResult = new IoLogicCompiler().Compile(inputRule);
if (!inputResult.Success || inputResult.Image is null || !inputResult.UsesSnapshotInputs)
{
    throw new InvalidOperationException("Analog/RS485 input rules must compile: " +
        string.Join(Environment.NewLine, inputResult.Errors));
}
var inputBody = inputResult.Image.Bytes.AsSpan(IoLogicBinaryFormat.BodyOffset);
foreach (var opcode in new byte[]
{
    IoLogicOpcode.TestAiValid, IoLogicOpcode.TestAiMv,
    IoLogicOpcode.TestCurrentUa, IoLogicOpcode.TestRs485Valid,
    IoLogicOpcode.TestRs485Value
})
{
    if (!inputBody.Contains(opcode))
    {
        throw new InvalidOperationException($"Missing input test opcode 0x{opcode:X2}.");
    }
}
var negativeImage = new IoLogicCompiler().Compile("""
void IO_Logic_Main(void)
{
    if (IO_AI_Mv(3) < -1000) { IO_DO_Set(RELAY_1, 1); }
}
""").Image ?? throw new InvalidOperationException("Signed AI comparison did not compile.");
var negativeBody = negativeImage.Bytes.AsSpan(IoLogicBinaryFormat.BodyOffset);
if (!negativeBody[..5].SequenceEqual(new byte[] { IoLogicOpcode.TestAiMv, 2, 3, 0xFC, 0x18 }))
{
    throw new InvalidOperationException("Signed AI comparison encoding/jump is wrong.");
}
var rsImage = new IoLogicCompiler().Compile("""
void IO_Logic_Main(void)
{
    if (IO_RS485_Value(2, 7) > 500) { IO_DO_Set(RELAY_1, 1); }
}
""").Image ?? throw new InvalidOperationException("RS485 comparison did not compile.");
var rsBody = rsImage.Bytes.AsSpan(IoLogicBinaryFormat.BodyOffset);
if (!rsBody[..6].SequenceEqual(new byte[] { IoLogicOpcode.TestRs485Value, 1, 7, 2, 1, 0xF4 }))
{
    throw new InvalidOperationException("RS485 item/offset/comparison encoding is wrong.");
}
foreach (var invalidRule in new[]
{
    "if (IO_AI_Mv(9) > 0) { IO_DO_Set(RELAY_1, 1); }",
    "if (IO_AI_Mv(1) > 65535) { IO_DO_Set(RELAY_1, 1); }",
    "if (IO_AI_Mv(1) < -10001) { IO_DO_Set(RELAY_1, 1); }",
    "if (IO_Current_uA(3) > 1000) { IO_DO_Set(RELAY_1, 1); }",
    "if (IO_RS485_Valid(33) == 1) { IO_DO_Set(RELAY_1, 1); }",
    "if (IO_RS485_Value(1, 16) > 0) { IO_DO_Set(RELAY_1, 1); }"
})
{
    if (new IoLogicCompiler().Compile($"void IO_Logic_Main(void) {{ {invalidRule} }}").Success)
    {
        throw new InvalidOperationException("Invalid input channel/index was accepted: " + invalidRule);
    }
}

static (bool output, int[] tested) RunDiRule(string condition, byte inputMask)
{
    var source = $"void IO_Logic_Main(void) {{ if ({condition}) {{ IO_DO_Set(RELAY_1, 1); }} else {{ IO_DO_Set(RELAY_1, 0); }} }}";
    var compiled = new IoLogicCompiler().Compile(source);
    if (!compiled.Success || compiled.Image is null)
    {
        throw new InvalidOperationException($"Failed to compile '{condition}': {string.Join("; ", compiled.Errors)}");
    }
    var code = compiled.Image.Bytes.AsSpan(IoLogicBinaryFormat.BodyOffset);
    var tested = new List<int>();
    var output = false;
    var pc = 0;
    for (var steps = 0; steps < 100; steps++)
    {
        var opcode = code[pc++];
        switch (opcode)
        {
            case IoLogicOpcode.Jmp:
                pc = (pc + 2 + (short)((code[pc] << 8) | code[pc + 1])) & 0xFFFF;
                break;
            case IoLogicOpcode.TestDi:
                var channel = code[pc++];
                var expected = code[pc++];
                tested.Add(channel + 1);
                var rel = (short)((code[pc] << 8) | code[pc + 1]);
                pc += 2;
                if (((inputMask >> channel) & 1) != expected) pc = (pc + rel) & 0xFFFF;
                break;
            case IoLogicOpcode.DoSet:
                if (code[pc++] != 0) throw new InvalidOperationException("Unexpected output channel.");
                output = code[pc++] != 0;
                break;
            case IoLogicOpcode.End:
                return (output, tested.ToArray());
            default:
                throw new InvalidOperationException($"Unexpected opcode 0x{opcode:X2} in boolean test.");
        }
    }
    throw new InvalidOperationException("Boolean rule did not terminate.");
}

const string a = "IO_DI_Level(INPUT_1) == 1";
const string b = "IO_DI_Level(INPUT_2) == 1";
const string c = "IO_DI_Level(INPUT_3) == 1";
foreach (var (mask, expected) in new (byte mask, bool expected)[]
{
    (0, false), (1, true), (2, true), (4, false), (6, false), (7, true)
})
{
    var actual = RunDiRule($"{a} || {b} && !({c})", mask).output;
    if (actual != expected) throw new InvalidOperationException($"Operator precedence failed for mask {mask}.");
}
foreach (var (mask, expected) in new (byte mask, bool expected)[]
{
    (0, false), (1, false), (2, false), (4, true), (5, false)
})
{
    var actual = RunDiRule($"!({a} || {b}) && {c}", mask).output;
    if (actual != expected) throw new InvalidOperationException($"Grouped negation failed for mask {mask}.");
}
if (!RunDiRule($"{a} || {b}", 1).tested.SequenceEqual(new[] { 1 }) ||
    !RunDiRule($"{a} && {b}", 0).tested.SequenceEqual(new[] { 1 }) ||
    !RunDiRule("!!IO_DI_Level(INPUT_1)", 1).output ||
    !RunDiRule("!IO_DI_Level(INPUT_1)", 0).output ||
    RunDiRule("!IO_DI_Level(INPUT_1)", 1).output)
{
    throw new InvalidOperationException("Short circuit or double negation failed.");
}
var whileFalse = new IoLogicCompiler().Compile("void IO_Logic_Main(void) { while (0 || !1) { IO_DO_Set(RELAY_1, 1); } }");
if (!whileFalse.Success || whileFalse.Image is null)
{
    throw new InvalidOperationException("Boolean expression in while failed to compile.");
}
if (new IoLogicCompiler().Compile($"void IO_Logic_Main(void) {{ if ({a} ||) {{ IO_DO_Set(RELAY_1, 1); }} }}").Success)
{
    throw new InvalidOperationException("Malformed OR expression was accepted.");
}
if (new IoLogicCompiler().Compile("void IO_Logic_Main(void) { if (!IO_AI_Mv(1) > 2500) { IO_DO_Set(RELAY_1, 1); } }").Success)
{
    throw new InvalidOperationException("Ambiguous ungrouped negated comparison was accepted.");
}
var snapshotBoolean = new IoLogicCompiler().Compile("void IO_Logic_Main(void) { if (!IO_AI_Valid(1) || IO_RS485_Valid(1) == 1) { IO_DO_Set(RELAY_1, 1); } }");
if (!snapshotBoolean.Success || !snapshotBoolean.UsesSnapshotInputs)
{
    throw new InvalidOperationException("Nested snapshot input must trigger the firmware capability guard.");
}
