using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace IndustrialFieldDataAcquisitionTerminal;

/// <summary>编译结果：v2 字节码镜像或错误列表。</summary>
internal sealed class IoLogicCompileResult
{
    public IoLogicCompiledImage? Image { get; set; }
    public bool UsesSnapshotInputs { get; set; }
    public bool UsesCurrentBreath { get; set; }
    public List<string> Errors { get; } = new();
    public bool Success => Image is not null && Errors.Count == 0;
}

/// <summary>
/// v2 联合控制器 DSL 编译器。
/// 解析用户编写的模式回调（IO_Mode_0_Callback ~ IO_Mode_15_Callback）
/// 与主程序（IO_Logic_Main），生成字节码镜像 v2。
/// 支持 if/else、while、return 与 IO_* 动作/条件 API；语法与参数范围在编译期校验。
/// </summary>
internal sealed class IoLogicCompiler
{
    private const string MainFunctionName = "IO_Logic_Main";

    public IoLogicCompileResult Compile(string source, uint generation = 1)
    {
        var result = new IoLogicCompileResult();
        try
        {
            var functions = IoLogicParser.Parse(source, result.Errors);
            if (result.Errors.Count > 0)
            {
                return result;
            }

            var main = functions.FirstOrDefault(f => f.Name == MainFunctionName);
            if (main is null)
            {
                result.Errors.Add($"缺少主程序入口函数 {MainFunctionName}。");
                return result;
            }

            var modeFunctions = new IoLogicFunction?[IoLogicBinaryFormat.ModeCount];
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var function in functions)
            {
                if (!seen.Add(function.Name))
                {
                    result.Errors.Add($"函数 {function.Name} 重复定义。");
                    continue;
                }

                if (function.Name == MainFunctionName)
                {
                    continue;
                }

                if (!TryParseModeFunction(function.Name, out var modeIndex))
                {
                    result.Errors.Add($"不支持的函数 {function.Name}：联合控制器只允许 {MainFunctionName} 与 IO_Mode_0_Callback ~ IO_Mode_15_Callback。");
                    continue;
                }

                modeFunctions[modeIndex] = function;
            }

            if (result.Errors.Count > 0)
            {
                return result;
            }

            var mainCode = IoLogicCodeGen.CompileFunction(main, isMain: true, result.Errors);
            var modeCodes = new ReadOnlyMemory<byte>?[IoLogicBinaryFormat.ModeCount];
            for (var index = 0; index < IoLogicBinaryFormat.ModeCount; index++)
            {
                var modeFunction = modeFunctions[index];
                if (modeFunction is null)
                {
                    modeCodes[index] = null; /* 未定义模式：入口偏移 0 */
                }
                else
                {
                    modeCodes[index] = IoLogicCodeGen.CompileFunction(modeFunction, isMain: false, result.Errors);
                }
            }

            if (result.Errors.Count > 0)
            {
                return result;
            }

            result.UsesSnapshotInputs = functions.Any(function => UsesSnapshotInputs(function.Body));
            result.UsesCurrentBreath = functions.Any(function => UsesCurrentBreath(function.Body));
            result.Image = IoLogicBinaryFormat.Create(mainCode, modeCodes!, generation);
        }
        catch (Exception ex)
        {
            result.Errors.Add($"编译异常：{ex.Message}");
        }

        return result;
    }

    private static bool TryParseModeFunction(string name, out int modeIndex)
    {
        modeIndex = 0;
        const string prefix = "IO_Mode_";
        const string suffix = "_Callback";
        if (!name.StartsWith(prefix, StringComparison.Ordinal)
            || !name.EndsWith(suffix, StringComparison.Ordinal))
        {
            return false;
        }

        var numberText = name[prefix.Length..^suffix.Length];
        if (!int.TryParse(numberText, NumberStyles.None, CultureInfo.InvariantCulture, out var index)
            || index is < 0 or >= IoLogicBinaryFormat.ModeCount)
        {
            return false;
        }

        modeIndex = index;
        return true;
    }

    private static bool UsesSnapshotInputs(IEnumerable<IoLogicStatement> statements)
    {
        foreach (var statement in statements)
        {
            if (statement is IoLogicIf branch &&
                (UsesSnapshotInputs(branch.Condition) ||
                 UsesSnapshotInputs(branch.ThenBody) ||
                 (branch.ElseBody is not null && UsesSnapshotInputs(branch.ElseBody))))
            {
                return true;
            }
            if (statement is IoLogicWhile loop &&
                (UsesSnapshotInputs(loop.Condition) ||
                 UsesSnapshotInputs(loop.Body)))
            {
                return true;
            }
        }
        return false;
    }

    private static bool IsSnapshotInput(string name) => name is
        "IO_AI_Valid" or "IO_AI_Mv" or "IO_Current_Valid" or "IO_Current_uA" or
        "IO_RS485_Valid" or "IO_RS485_Value";

    private static bool UsesCurrentBreath(IEnumerable<IoLogicStatement> statements)
    {
        foreach (var statement in statements)
        {
            if (statement is IoLogicCallStatement action && action.Call.Name == "IO_AO_BreathCurrent")
                return true;
            if (statement is IoLogicIf branch &&
                (UsesCurrentBreath(branch.ThenBody) ||
                 (branch.ElseBody is not null && UsesCurrentBreath(branch.ElseBody)))) return true;
            if (statement is IoLogicWhile loop && UsesCurrentBreath(loop.Body)) return true;
        }
        return false;
    }

    private static bool UsesSnapshotInputs(IoLogicBoolExpr expression) => expression switch
    {
        IoLogicBoolTest test => IsSnapshotInput(test.Test.Call.Name),
        IoLogicBoolNot not => UsesSnapshotInputs(not.Operand),
        IoLogicBoolAnd and => UsesSnapshotInputs(and.Left) || UsesSnapshotInputs(and.Right),
        IoLogicBoolOr or => UsesSnapshotInputs(or.Left) || UsesSnapshotInputs(or.Right),
        _ => false
    };
}

/* ================= 语法树 ================= */

internal sealed record IoLogicFunction(string Name, IReadOnlyList<IoLogicStatement> Body);

internal abstract record IoLogicStatement;

internal sealed record IoLogicIf(IoLogicBoolExpr Condition,
                                 IReadOnlyList<IoLogicStatement> ThenBody,
                                 IReadOnlyList<IoLogicStatement>? ElseBody) : IoLogicStatement;

internal sealed record IoLogicWhile(IoLogicBoolExpr Condition,
                                    IReadOnlyList<IoLogicStatement> Body) : IoLogicStatement;

internal sealed record IoLogicReturn : IoLogicStatement;

internal sealed record IoLogicCallStatement(IoLogicCall Call) : IoLogicStatement;

internal sealed partial record IoLogicCall(string Name, IReadOnlyList<ushort> Arguments, string? ArgCallName = null);

internal abstract record IoLogicBoolExpr;
internal sealed record IoLogicBoolConstant(bool Value) : IoLogicBoolExpr;
internal sealed record IoLogicBoolTest(IoLogicTest Test) : IoLogicBoolExpr;
internal sealed record IoLogicBoolNot(IoLogicBoolExpr Operand) : IoLogicBoolExpr;
internal sealed record IoLogicBoolAnd(IoLogicBoolExpr Left, IoLogicBoolExpr Right) : IoLogicBoolExpr;
internal sealed record IoLogicBoolOr(IoLogicBoolExpr Left, IoLogicBoolExpr Right) : IoLogicBoolExpr;

internal sealed record IoLogicTest(IoLogicCall Call, string? Operator, ushort? Value)
{
    public bool WasNegative { get; init; }
}

/* ================= 词法与语法分析 ================= */

internal sealed class IoLogicParser
{
    private static readonly IReadOnlyDictionary<string, ushort> NamedConstants =
        new Dictionary<string, ushort>(StringComparer.Ordinal)
        {
            ["INPUT_1"] = 1, ["INPUT_2"] = 2, ["INPUT_3"] = 3, ["INPUT_4"] = 4,
            ["INPUT_5"] = 5, ["INPUT_6"] = 6, ["INPUT_7"] = 7, ["INPUT_8"] = 8,
            ["RELAY_1"] = 1, ["RELAY_2"] = 2, ["RELAY_3"] = 3, ["RELAY_4"] = 4,
            ["TRANSISTOR_1"] = 5, ["TRANSISTOR_2"] = 6,
            ["TRANSISTOR_3"] = 7, ["TRANSISTOR_4"] = 8,
            ["VOLTAGE_OUTPUT_1"] = 1, ["CURRENT_OUTPUT_1"] = 1,
            ["IO_FREQ_CLASS_NONE"] = 0, ["IO_FREQ_CLASS_LOW"] = 1,
            ["IO_FREQ_CLASS_NORMAL"] = 2, ["IO_FREQ_CLASS_HIGH"] = 3,
            ["IO_FREQ_CLASS_OVER"] = 4
        };

    private readonly string _source;
    private readonly List<string> _errors;
    private int _index;

    private IoLogicParser(string source, List<string> errors)
    {
        _source = source;
        _errors = errors;
    }

    public static IReadOnlyList<IoLogicFunction> Parse(string source, List<string> errors)
    {
        var parser = new IoLogicParser(source, errors);
        var functions = parser.ParseProgram();
        return functions;
    }

    private IReadOnlyList<IoLogicFunction> ParseProgram()
    {
        var functions = new List<IoLogicFunction>();
        while (true)
        {
            SkipTrivia();
            if (Peek() == '\0')
            {
                break;
            }

            if (PeekWord("void"))
            {
                var function = ParseFunction();
                if (function is not null)
                {
                    functions.Add(function);
                }
            }
            else
            {
                _errors.Add($"第 {Line()} 行：只能以 void 定义联合控制器函数。");
                SkipLine();
            }
        }

        return functions;
    }

    private IoLogicFunction? ParseFunction()
    {
        var startLine = Line();
        ExpectWord("void");
        var name = ReadIdentifier();
        if (name is null)
        {
            _errors.Add($"第 {Line()} 行：缺少函数名。");
            return null;
        }

        Expect('(');
        SkipTrivia();
        /* 形参固定为 void（模板约定），也接受空参数列表 */
        if (Peek() == 'v' && PeekWord("void"))
        {
            ReadWord("void");
        }
        Expect(')');
        SkipTrivia();
        Expect('{');

        var body = ParseStatements('}');
        SkipTrivia();
        Expect('}');
        if (_errors.Count > 0)
        {
            return null;
        }

        return new IoLogicFunction(name, body);
    }

    private IReadOnlyList<IoLogicStatement> ParseStatements(char terminator)
    {
        var statements = new List<IoLogicStatement>();
        while (true)
        {
            SkipTrivia();
            var current = Peek();
            if (current == '\0')
            {
                _errors.Add($"第 {Line()} 行：代码块缺少 '}}'。");
                return statements;
            }

            if (current == terminator)
            {
                return statements;
            }

            if (PeekWord("if"))
            {
                var statement = ParseIf();
                if (statement is not null)
                {
                    statements.Add(statement);
                }
            }
            else if (PeekWord("while"))
            {
                var statement = ParseWhile();
                if (statement is not null)
                {
                    statements.Add(statement);
                }
            }
            else if (PeekWord("return"))
            {
                ExpectWord("return");
                Expect(';');
                statements.Add(new IoLogicReturn());
            }
            else if (current == '{')
            {
                Expect('{');
                var nested = ParseStatements('}');
                Expect('}');
                statements.AddRange(nested);
            }
            else if (current == ';')
            {
                Expect(';'); /* 空语句 */
            }
            else
            {
                var call = ParseCall();
                if (call is not null)
                {
                    SkipTrivia();
                    Expect(';');
                    statements.Add(new IoLogicCallStatement(call));
                }
                else
                {
                    SkipLine();
                }
            }
        }
    }

    private IoLogicIf? ParseIf()
    {
        ExpectWord("if");
        Expect('(');
        var condition = ParseBoolExpr();
        Expect(')');
        SkipTrivia();
        if (Peek() != '{')
        {
            _errors.Add($"第 {Line()} 行：if 代码块必须使用大括号。");
            return null;
        }

        Expect('{');
        var thenBody = ParseStatements('}');
        Expect('}');

        SkipTrivia();
        IReadOnlyList<IoLogicStatement>? elseBody = null;
        if (PeekWord("else"))
        {
            ReadWord("else");
            SkipTrivia();
            if (Peek() != '{')
            {
                _errors.Add($"第 {Line()} 行：else 代码块必须使用大括号。");
                return null;
            }

            Expect('{');
            elseBody = ParseStatements('}');
            Expect('}');
        }

        return new IoLogicIf(condition, thenBody, elseBody);
    }

    private IoLogicWhile? ParseWhile()
    {
        ExpectWord("while");
        Expect('(');
        var condition = ParseBoolExpr();
        Expect(')');
        SkipTrivia();
        if (Peek() != '{')
        {
            _errors.Add($"第 {Line()} 行：while 代码块必须使用大括号。");
            return null;
        }

        Expect('{');
        var body = ParseStatements('}');
        Expect('}');
        return new IoLogicWhile(condition, body);
    }

    /* 优先级与 C 一致：! 高于 &&，&& 高于 ||；括号覆盖优先级。 */
    private IoLogicBoolExpr ParseBoolExpr() => ParseOrExpr();

    private IoLogicBoolExpr ParseOrExpr()
    {
        var left = ParseAndExpr();
        SkipTrivia();
        while (Peek() == '|' && Peek(1) == '|')
        {
            _index += 2;
            left = new IoLogicBoolOr(left, ParseAndExpr());
            SkipTrivia();
        }
        return left;
    }

    private IoLogicBoolExpr ParseAndExpr()
    {
        var left = ParseUnaryExpr();
        SkipTrivia();
        while (Peek() == '&' && Peek(1) == '&')
        {
            _index += 2;
            left = new IoLogicBoolAnd(left, ParseUnaryExpr());
            SkipTrivia();
        }
        return left;
    }

    private IoLogicBoolExpr ParseUnaryExpr()
    {
        SkipTrivia();
        if (Peek() == '!')
        {
            _index++;
            SkipTrivia();
            var grouped = Peek() == '(';
            var operand = ParseUnaryExpr();
            if (!grouped && operand is IoLogicBoolTest { Test.Operator: not null })
            {
                _errors.Add($"第 {Line()} 行：对比较结果取反时请写成 !(函数调用() 比较运算符 数值)。");
            }
            return new IoLogicBoolNot(operand);
        }
        if (Peek() == '(')
        {
            _index++;
            var nested = ParseBoolExpr();
            Expect(')');
            return nested;
        }
        if (char.IsDigit(Peek()))
        {
            var constant = ReadNumber();
            return new IoLogicBoolConstant(constant is not null && constant.Value != 0);
        }
        if (!char.IsLetter(Peek()) && Peek() != '_')
        {
            _errors.Add($"第 {Line()} 行：条件缺少表达式。");
            return new IoLogicBoolConstant(false);
        }
        var test = ParseTest();
        return test is null ? new IoLogicBoolConstant(false) : new IoLogicBoolTest(test);
    }

    /* test := call [比较运算符 (数字 | call)] */
    private IoLogicTest? ParseTest()
    {
        SkipTrivia();
        var call = ParseCall();
        if (call is null)
        {
            return null;
        }

        SkipTrivia();
        var current = Peek();
        if (current is '=' or '!' or '>' or '<')
        {
            string op;
            if (current == '=' && Peek(1) == '=')
            {
                _index += 2;
                op = "==";
            }
            else if (current == '!' && Peek(1) == '=')
            {
                _index += 2;
                op = "!=";
            }
            else if (current == '>')
            {
                _index++;
                op = ">";
            }
            else if (current == '<')
            {
                _index++;
                op = "<";
            }
            else
            {
                _errors.Add($"第 {Line()} 行：条件比较运算符必须是 ==、!=、>、<。");
                return null;
            }

            SkipTrivia();
            if (char.IsDigit(Peek()) || Peek() == '-')
            {
                var wasNegative = Peek() == '-';
                var value = ReadNumber();
                if (value is null)
                {
                    return null;
                }
                return new IoLogicTest(call, op, value) { WasNegative = wasNegative };
            }

            if (TryReadNamedConstant(out var namedValue))
            {
                return new IoLogicTest(call, op, namedValue);
            }

            /* 形如 IO_GetExternalMode() != IO_GetCurrentMode() */
            var rightCall = ParseCall();
            if (rightCall is not null && op == "!=" && rightCall.Name == "IO_GetCurrentMode"
                && call.Name == "IO_GetExternalMode")
            {
                return new IoLogicTest(call, "!=call", null);
            }

            _errors.Add($"第 {Line()} 行：比较右值必须是数字常量（或 IO_GetExternalMode() != IO_GetCurrentMode()）。");
            return null;
        }

        return new IoLogicTest(call, null, null);
    }

    private IoLogicCall? ParseCall()
    {
        SkipTrivia();
        var startLine = Line();
        var name = ReadIdentifier();
        if (name is null)
        {
            return null;
        }

        SkipTrivia();
        if (Peek() != '(')
        {
            _errors.Add($"第 {Line()} 行：函数调用 {name} 缺少 '('。");
            return null;
        }

        Expect('(');
        var arguments = new List<ushort>();
        string? argCallName = null;
        while (true)
        {
            SkipTrivia();
            if (Peek() == ')')
            {
                _index++;
                break;
            }

            if (Peek() == ',')
            {
                _errors.Add($"第 {Line()} 行：参数不能为空。");
                _index++;
                continue;
            }

            if (char.IsLetter(Peek()) || Peek() == '_')
            {
                if (TryReadNamedConstant(out var namedValue))
                {
                    arguments.Add(namedValue);
                    SkipTrivia();
                    if (Peek() == ',')
                    {
                        _index++;
                    }
                    continue;
                }

                /* 函数调用参数：仅支持 IO_SetMode(IO_GetExternalMode()) */
                var nested = ParseCall();
                if (nested is null)
                {
                    return null;
                }

                if (nested.Name != "IO_GetExternalMode" || nested.Arguments.Count != 0 || arguments.Count > 0)
                {
                    _errors.Add($"第 {startLine} 行：{name} 的参数仅支持数字常量，或 IO_SetMode(IO_GetExternalMode())。");
                    return null;
                }

                argCallName = "IO_GetExternalMode";
            }
            else
            {
                var value = ReadNumber();
                if (value is null)
                {
                    return null;
                }
                arguments.Add(value.Value);
            }

            SkipTrivia();
            if (Peek() == ',')
            {
                _index++;
            }
        }

        return new IoLogicCall(name, arguments, argCallName) with { Line = startLine };
    }

    /* ================= 词法辅助 ================= */

    private void SkipTrivia()
    {
        while (true)
        {
            var current = Peek();
            if (current == '\0')
            {
                return;
            }

            if (char.IsWhiteSpace(current))
            {
                _index++;
                continue;
            }

            if (current == '/' && Peek(1) == '/')
            {
                _index += 2;
                while (Peek() != '\0' && Peek() != '\n')
                {
                    _index++;
                }
                continue;
            }

            if (current == '/' && Peek(1) == '*')
            {
                _index += 2;
                while (Peek() != '\0' && !(Peek() == '*' && Peek(1) == '/'))
                {
                    _index++;
                }
                _index += 2;
                continue;
            }

            break;
        }
    }

    private void SkipLine()
    {
        while (Peek() != '\0' && Peek() != '\n')
        {
            _index++;
        }
    }

    private char Peek(int lookahead = 0)
    {
        var index = _index + lookahead;
        return index < _source.Length ? _source[index] : '\0';
    }

    private bool PeekWord(string word)
    {
        if (_index + word.Length > _source.Length)
        {
            return false;
        }

        if (!_source.AsSpan(_index, word.Length).SequenceEqual(word))
        {
            return false;
        }

        var after = _index + word.Length;
        if (after < _source.Length && (char.IsLetterOrDigit(_source[after]) || _source[after] == '_'))
        {
            return false;
        }

        return true;
    }

    private void ReadWord(string word)
    {
        _index += word.Length;
    }

    private void ExpectWord(string word)
    {
        if (PeekWord(word))
        {
            _index += word.Length;
            return;
        }

        _errors.Add($"第 {Line()} 行：期望关键字 {word}。");
    }

    private void Expect(char expected)
    {
        SkipTrivia();
        if (Peek() == expected)
        {
            _index++;
            return;
        }

        _errors.Add($"第 {Line()} 行：期望字符 '{expected}'。");
    }

    private string? ReadIdentifier()
    {
        SkipTrivia();
        var start = _index;
        while (Peek() != '\0' && (char.IsLetterOrDigit(Peek()) || Peek() == '_'))
        {
            _index++;
        }

        return start == _index ? null : _source[start.._index];
    }

    private ushort? ReadNumber()
    {
        SkipTrivia();
        var start = _index;
        if (Peek() == '-')
        {
            _index++;
        }
        while (Peek() != '\0' && char.IsDigit(Peek()))
        {
            _index++;
        }

        if (start == _index || (_index == start + 1 && _source[start] == '-'))
        {
            _errors.Add($"第 {Line()} 行：期望数字常量。");
            return null;
        }

        if (!int.TryParse(_source[start.._index], NumberStyles.AllowLeadingSign,
                          CultureInfo.InvariantCulture, out var value) || value is < -32768 or > 65535)
        {
            _errors.Add($"第 {Line()} 行：数值超出 -32768~65535 范围。");
            return null;
        }

        return unchecked((ushort)value);
    }

    private bool TryReadNamedConstant(out ushort value)
    {
        SkipTrivia();
        var start = _index;
        var name = ReadIdentifier();
        if (name is not null && NamedConstants.TryGetValue(name, out value))
        {
            return true;
        }

        _index = start;
        value = 0;
        return false;
    }

    private int Line()
    {
        var count = 1;
        for (var index = 0; index < Math.Min(_index, _source.Length); index++)
        {
            if (_source[index] == '\n')
            {
                count++;
            }
        }

        return count;
    }
}

/* ================= 字节码生成 ================= */

internal sealed class IoLogicCodeBuilder
{
    private readonly List<byte> _code = new();

    public int Position => _code.Count;

    public void Emit(params byte[] bytes) => _code.AddRange(bytes);

    public void PatchRel(int relPosition, int targetPosition)
    {
        var delta = targetPosition - (relPosition + 2);
        _code[relPosition] = (byte)((delta >> 8) & 0xFF);
        _code[relPosition + 1] = (byte)(delta & 0xFF);
    }

    public byte[] ToArray() => _code.ToArray();
}

internal sealed class IoLogicCodeLabel
{
    public int Position = -1;
    public List<int> Fixups = new();
}

internal static class IoLogicCodeGen
{
    /* 联合控制器规格上限 */
    private const int MaxDiIndex = 8;
    private const int MaxFreqChannel = 2;
    private const int MaxFreqHz = 2000;
    private const int MaxMode = 15;
    private const int MaxDoIndex = 8;
    private const int MaxVoltageMv = 10000;
    private const int MaxCurrentUa = 20000;
    private const int MaxHysteresisPercent = 50;
    private const int MaxAiIndex = 8;
    private const int MaxRs485Item = 32;
    private const int MaxRs485Register = 15;

    public static byte[] CompileFunction(IoLogicFunction function, bool isMain, List<string> errors)
    {
        var builder = new IoLogicCodeBuilder();
        foreach (var statement in function.Body)
        {
            CompileStatement(builder, statement, isMain, errors);
        }

        builder.Emit(isMain ? IoLogicOpcode.End : IoLogicOpcode.Ret);
        return builder.ToArray();
    }

    private static void CompileStatement(IoLogicCodeBuilder builder, IoLogicStatement statement,
                                         bool isMain, List<string> errors)
    {
        switch (statement)
        {
            case IoLogicIf ifStatement:
                CompileIf(builder, ifStatement, isMain, errors);
                break;
            case IoLogicWhile whileStatement:
                CompileWhile(builder, whileStatement, isMain, errors);
                break;
            case IoLogicReturn:
                builder.Emit(isMain ? IoLogicOpcode.End : IoLogicOpcode.Ret);
                break;
            case IoLogicCallStatement callStatement:
                CompileActionCall(builder, callStatement.Call, errors);
                break;
            default:
                errors.Add("不支持的语句类型。");
                break;
        }
    }

    private static void CompileIf(IoLogicCodeBuilder builder, IoLogicIf statement, bool isMain, List<string> errors)
    {
        var trueLabel = new IoLogicCodeLabel();
        var falseLabel = new IoLogicCodeLabel();
        var endLabel = new IoLogicCodeLabel();
        EmitCondition(builder, statement.Condition, trueLabel, falseLabel, errors);
        Resolve(builder, trueLabel);
        foreach (var inner in statement.ThenBody)
        {
            CompileStatement(builder, inner, isMain, errors);
        }

        if (statement.ElseBody is { } elseBody)
        {
            EmitJump(builder, endLabel);
            Resolve(builder, falseLabel);
            foreach (var inner in elseBody)
            {
                CompileStatement(builder, inner, isMain, errors);
            }
        }
        else
        {
            Resolve(builder, falseLabel);
        }

        Resolve(builder, endLabel);
    }

    private static void CompileWhile(IoLogicCodeBuilder builder, IoLogicWhile statement, bool isMain, List<string> errors)
    {
        var loopStart = builder.Position;
        var bodyLabel = new IoLogicCodeLabel();
        var exitLabel = new IoLogicCodeLabel();
        EmitCondition(builder, statement.Condition, bodyLabel, exitLabel, errors);
        Resolve(builder, bodyLabel);
        foreach (var inner in statement.Body)
        {
            CompileStatement(builder, inner, isMain, errors);
        }

        EmitJump(builder, loopStart);
        Resolve(builder, exitLabel);
    }

    private static void EmitCondition(IoLogicCodeBuilder builder, IoLogicBoolExpr expression,
                                      IoLogicCodeLabel trueTarget, IoLogicCodeLabel falseTarget,
                                      List<string> errors)
    {
        switch (expression)
        {
            case IoLogicBoolConstant constant:
                EmitJump(builder, constant.Value ? trueTarget : falseTarget);
                break;
            case IoLogicBoolTest test:
                EmitTestJump(builder, test.Test, falseTarget, errors);
                EmitJump(builder, trueTarget);
                break;
            case IoLogicBoolNot not:
                EmitCondition(builder, not.Operand, falseTarget, trueTarget, errors);
                break;
            case IoLogicBoolAnd and:
                var andNext = new IoLogicCodeLabel();
                EmitCondition(builder, and.Left, andNext, falseTarget, errors);
                Resolve(builder, andNext);
                EmitCondition(builder, and.Right, trueTarget, falseTarget, errors);
                break;
            case IoLogicBoolOr or:
                var orNext = new IoLogicCodeLabel();
                EmitCondition(builder, or.Left, trueTarget, orNext, errors);
                Resolve(builder, orNext);
                EmitCondition(builder, or.Right, trueTarget, falseTarget, errors);
                break;
            default:
                errors.Add("不支持的布尔表达式。");
                break;
        }
    }

    private static void EmitJump(IoLogicCodeBuilder builder, int targetPosition)
    {
        var relPosition = builder.Position + 1;
        builder.Emit(IoLogicOpcode.Jmp, 0, 0);
        builder.PatchRel(relPosition, targetPosition);
    }

    private static void EmitJump(IoLogicCodeBuilder builder, IoLogicCodeLabel target)
    {
        var relPosition = builder.Position + 1;
        builder.Emit(IoLogicOpcode.Jmp, 0, 0);
        target.Fixups.Add(relPosition);
    }

    private static void Resolve(IoLogicCodeBuilder builder, IoLogicCodeLabel label)
    {
        label.Position = builder.Position;
        foreach (var fixup in label.Fixups)
        {
            builder.PatchRel(fixup, label.Position);
        }
    }

    /* 发射一条测试指令：条件不成立时跳转到 falseTarget。 */
    private static void EmitTestJump(IoLogicCodeBuilder builder, IoLogicTest test,
                                     IoLogicCodeLabel falseTarget, List<string> errors)
    {
        var call = test.Call;
        var name = call.Name;
        var op = test.Operator;

        /* 无条件测试（真/假调用） */
        if (op is null)
        {
            switch (name)
            {
                case "IO_DI_Level":
                case "IO_AI_Valid":
                case "IO_Current_Valid":
                case "IO_RS485_Valid":
                    EmitTestJump(builder, test with { Operator = "==", Value = 1 }, falseTarget, errors);
                    return;
                case "IO_DI_Rising":
                case "IO_DI_Falling":
                    if (ValidateChannel(call, 0, 1, MaxDiIndex, "DI 通道", errors))
                    {
                        var relPosition = builder.Position + 2;
                        builder.Emit(name == "IO_DI_Rising" ? IoLogicOpcode.TestDiRising : IoLogicOpcode.TestDiFalling,
                                     (byte)(call.Arguments[0] - 1), 0, 0);
                        falseTarget.Fixups.Add(relPosition);
                    }
                    return;
                default:
                    errors.Add($"第 {call.Line} 行：{name} 不能作为条件使用。");
                    return;
            }
        }

        switch (name)
        {
            case "IO_DI_Level":
                if (!ValidateChannel(call, 0, 1, MaxDiIndex, "DI 通道", errors)
                    || !ValidateValue(test, 0, 1, "电平值（0/1）", errors))
                {
                    return;
                }

                if (op is not ("==" or "!="))
                {
                    errors.Add($"第 {call.Line} 行：IO_DI_Level 只支持 == 和 !=。");
                    return;
                }

                {
                    var expect = op == "==" ? (byte)test.Value!.Value : (byte)(test.Value!.Value == 1 ? 0 : 1);
                    var relPosition = builder.Position + 3;
                    builder.Emit(IoLogicOpcode.TestDi, (byte)(call.Arguments[0] - 1), expect, 0, 0);
                    falseTarget.Fixups.Add(relPosition);
                }
                return;

            case "IO_AI_Valid":
            case "IO_Current_Valid":
            case "IO_RS485_Valid":
                var maxItem = name == "IO_RS485_Valid" ? MaxRs485Item
                    : name == "IO_Current_Valid" ? 2 : MaxAiIndex;
                if (!ValidateChannel(call, 0, 1, maxItem, "输入通道/轮询项", errors)
                    || !ValidateValue(test, 0, 1, "有效标志（0/1）", errors))
                {
                    return;
                }
                if (op is not ("==" or "!="))
                {
                    errors.Add($"第 {call.Line} 行：{name} 只支持 == 和 !=。");
                    return;
                }
                {
                    var expect = op == "==" ? (byte)test.Value!.Value : (byte)(1 - test.Value!.Value);
                    var relPosition = builder.Position + 3;
                    builder.Emit(name == "IO_RS485_Valid" ? IoLogicOpcode.TestRs485Valid : IoLogicOpcode.TestAiValid,
                                 (byte)(call.Arguments[0] - 1), expect, 0, 0);
                    falseTarget.Fixups.Add(relPosition);
                }
                return;

            case "IO_AI_Mv":
            case "IO_Current_uA":
            case "IO_RS485_Value":
                if (!ValidateChannel(call, 0, 1,
                        name == "IO_RS485_Value" ? MaxRs485Item
                        : name == "IO_Current_uA" ? 2 : MaxAiIndex,
                        "输入通道/轮询项", errors))
                {
                    return;
                }
                if (name == "IO_RS485_Value" &&
                    !ValidateValue(call, 1, 0, MaxRs485Register, "寄存器偏移（0~15）", errors))
                {
                    return;
                }
                if (name == "IO_AI_Mv")
                {
                    var signed = test.Value is { } bits ?
                        test.WasNegative ? unchecked((short)bits) : bits : 0;
                    if (test.Value is null || signed is < -10000 or > 10000)
                    {
                        errors.Add($"第 {call.Line} 行：IO_AI_Mv 的比较值必须为 -10000~10000 mV。");
                        return;
                    }
                }
                else if (!ValidateValue(test, 0, 65535, "比较值（0~65535）", errors))
                {
                    return;
                }
                var comparison = op switch { "==" => 0, "!=" => 1, ">" => 2, "<" => 3, _ => -1 };
                if (comparison < 0)
                {
                    errors.Add($"第 {call.Line} 行：{name} 只支持 ==、!=、>、<。");
                    return;
                }
                {
                    var value = test.Value!.Value;
                    var relPosition = builder.Position + (name == "IO_RS485_Value" ? 6 : 5);
                    if (name == "IO_RS485_Value")
                    {
                        builder.Emit(IoLogicOpcode.TestRs485Value, (byte)(call.Arguments[0] - 1),
                                     (byte)call.Arguments[1], (byte)comparison,
                                     (byte)(value >> 8), (byte)value, 0, 0);
                    }
                    else
                    {
                        builder.Emit(name == "IO_AI_Mv" ? IoLogicOpcode.TestAiMv : IoLogicOpcode.TestCurrentUa,
                                     (byte)(call.Arguments[0] - 1), (byte)comparison,
                                     (byte)(value >> 8), (byte)value, 0, 0);
                    }
                    falseTarget.Fixups.Add(relPosition);
                }
                return;

            case "IO_Freq_Class":
                if (!ValidateChannel(call, 0, 1, MaxFreqChannel, "频率通道", errors)
                    || !ValidateValue(test, 0, 4, "频率档位（0~4）", errors))
                {
                    return;
                }

                if (op != "==")
                {
                    errors.Add($"第 {call.Line} 行：IO_Freq_Class 只支持 ==。");
                    return;
                }

                {
                    var relPosition = builder.Position + 3;
                    builder.Emit(IoLogicOpcode.TestFreqClass, (byte)(call.Arguments[0] - 1), (byte)test.Value!.Value, 0, 0);
                    falseTarget.Fixups.Add(relPosition);
                }
                return;

            case "IO_Freq_Hz":
                if (!ValidateChannel(call, 0, 1, MaxFreqChannel, "频率通道", errors)
                    || !ValidateValue(test, 0, MaxFreqHz, "频率值（0~2000Hz）", errors))
                {
                    return;
                }

                if (op is not (">" or "<"))
                {
                    errors.Add($"第 {call.Line} 行：IO_Freq_Hz 只支持 > 和 <。");
                    return;
                }

                {
                    var opcode = op == ">" ? IoLogicOpcode.TestFreqGt : IoLogicOpcode.TestFreqLt;
                    var relPosition = builder.Position + 4;
                    builder.Emit(opcode, (byte)(call.Arguments[0] - 1),
                                 (byte)((test.Value!.Value >> 8) & 0xFF), (byte)(test.Value!.Value & 0xFF), 0, 0);
                    falseTarget.Fixups.Add(relPosition);
                }
                return;

            case "IO_GetExternalMode":
                if (op == "!=call")
                {
                    if (call.Arguments.Count != 0)
                    {
                        errors.Add($"第 {call.Line} 行：IO_GetExternalMode() 不接受参数。");
                        return;
                    }

                    var relPosition = builder.Position + 1;
                    builder.Emit(IoLogicOpcode.TestExtNeqCur, 0, 0);
                    falseTarget.Fixups.Add(relPosition);
                    return;
                }

                if (!ValidateValue(test, 0, MaxMode, "模式号（0~15）", errors))
                {
                    return;
                }

                if (op != "==")
                {
                    errors.Add($"第 {call.Line} 行：IO_GetExternalMode 只支持 ==（或 != IO_GetCurrentMode()）。");
                    return;
                }

                {
                    var relPosition = builder.Position + 2;
                    builder.Emit(IoLogicOpcode.TestExtModeEq, (byte)test.Value!.Value, 0, 0);
                    falseTarget.Fixups.Add(relPosition);
                }
                return;

            case "IO_GetCurrentMode":
                if (!ValidateValue(test, 0, MaxMode, "模式号（0~15）", errors))
                {
                    return;
                }

                if (op != "==")
                {
                    errors.Add($"第 {call.Line} 行：IO_GetCurrentMode 只支持 ==。");
                    return;
                }

                {
                    var relPosition = builder.Position + 2;
                    builder.Emit(IoLogicOpcode.TestCurModeEq, (byte)test.Value!.Value, 0, 0);
                    falseTarget.Fixups.Add(relPosition);
                }
                return;

            default:
                errors.Add($"第 {call.Line} 行：不支持的 IO 条件函数 {name}。");
                return;
        }
    }

    private static void CompileActionCall(IoLogicCodeBuilder builder, IoLogicCall call, List<string> errors)
    {
        var name = call.Name;
        switch (name)
        {
            case "IO_DO_Set":
                if (!ValidateChannel(call, 0, 1, MaxDoIndex, "DO 通道", errors)
                    || !ValidateValue(call, 1, 0, 1, "输出电平（0/1）", errors))
                {
                    return;
                }
                builder.Emit(IoLogicOpcode.DoSet, (byte)(call.Arguments[0] - 1), (byte)call.Arguments[1]);
                return;

            case "IO_DO_Pulse":
                if (!ValidateChannel(call, 0, 1, MaxDoIndex, "DO 通道", errors)
                    || !ValidateValue(call, 1, 1, 65535, "脉冲宽度（1~65535ms）", errors))
                {
                    return;
                }
                builder.Emit(IoLogicOpcode.DoPulse, (byte)(call.Arguments[0] - 1),
                             (byte)((call.Arguments[1] >> 8) & 0xFF), (byte)(call.Arguments[1] & 0xFF));
                return;

            case "IO_SetMode":
                if (call.ArgCallName == "IO_GetExternalMode")
                {
                    builder.Emit(IoLogicOpcode.SetModeExt);
                    return;
                }

                if (!ValidateValue(call, 0, 0, MaxMode, "模式号（0~15）", errors))
                {
                    return;
                }
                builder.Emit(IoLogicOpcode.SetMode, (byte)call.Arguments[0]);
                return;

            case "IO_Delay":
                if (!ValidateValue(call, 0, 1, 65535, "延时（1~65535ms）", errors))
                {
                    return;
                }
                builder.Emit(IoLogicOpcode.Delay, (byte)((call.Arguments[0] >> 8) & 0xFF),
                             (byte)(call.Arguments[0] & 0xFF));
                return;

            case "IO_RunCurrentMode":
                builder.Emit(IoLogicOpcode.RunCurrentMode);
                return;

            case "IO_Mode_0_Callback":
            case "IO_Mode_1_Callback":
            case "IO_Mode_2_Callback":
            case "IO_Mode_3_Callback":
            case "IO_Mode_4_Callback":
            case "IO_Mode_5_Callback":
            case "IO_Mode_6_Callback":
            case "IO_Mode_7_Callback":
            case "IO_Mode_8_Callback":
            case "IO_Mode_9_Callback":
            case "IO_Mode_10_Callback":
            case "IO_Mode_11_Callback":
            case "IO_Mode_12_Callback":
            case "IO_Mode_13_Callback":
            case "IO_Mode_14_Callback":
            case "IO_Mode_15_Callback":
                if (int.TryParse(name.AsSpan("IO_Mode_".Length, name.Length - "IO_Mode_".Length - "_Callback".Length),
                                 NumberStyles.None, CultureInfo.InvariantCulture, out var modeIndex)
                    && modeIndex is >= 0 and <= MaxMode)
                {
                    builder.Emit(IoLogicOpcode.ModeCall, (byte)modeIndex);
                }
                else
                {
                    errors.Add($"第 {Line(call)} 行：非法模式回调 {name}。");
                }
                return;

            case "IO_SetFreqTh":
                if (!ValidateChannel(call, 0, 1, MaxFreqChannel, "频率通道", errors)
                    || !ValidateValue(call, 1, 0, MaxFreqHz, "阈值下限（0~2000Hz）", errors)
                    || !ValidateValue(call, 2, 0, MaxFreqHz, "阈值上限（0~2000Hz）", errors)
                    || !ValidateValue(call, 3, 0, MaxHysteresisPercent, "迟滞百分比（0~50）", errors)
                    || !ValidateValue(call, 4, 1, 65535, "无脉冲超时（1~65535ms）", errors))
                {
                    return;
                }

                if (call.Arguments[1] >= call.Arguments[2])
                {
                    errors.Add($"第 {Line(call)} 行：频率阈值要求 下限 < 上限。");
                    return;
                }

                builder.Emit(IoLogicOpcode.SetFreqTh, (byte)(call.Arguments[0] - 1),
                             (byte)((call.Arguments[1] >> 8) & 0xFF), (byte)(call.Arguments[1] & 0xFF),
                             (byte)((call.Arguments[2] >> 8) & 0xFF), (byte)(call.Arguments[2] & 0xFF),
                             (byte)call.Arguments[3],
                             (byte)((call.Arguments[4] >> 8) & 0xFF), (byte)(call.Arguments[4] & 0xFF));
                return;

            case "IO_AO_SetVoltage":
                if (!ValidateValue(call, 0, 0, MaxVoltageMv, "电压（0~10000mV）", errors))
                {
                    return;
                }
                builder.Emit(IoLogicOpcode.AoSetVoltage,
                             (byte)((call.Arguments[0] >> 8) & 0xFF), (byte)(call.Arguments[0] & 0xFF));
                return;

            case "IO_AO_SetCurrent":
                if (!ValidateValue(call, 0, 0, MaxCurrentUa, "电流（0~20000uA）", errors))
                {
                    return;
                }
                builder.Emit(IoLogicOpcode.AoSetCurrent,
                             (byte)((call.Arguments[0] >> 8) & 0xFF), (byte)(call.Arguments[0] & 0xFF));
                return;

            case "IO_AO_Breath":
                if (!ValidateValue(call, 0, 0, MaxVoltageMv, "呼吸下限（0~10000mV）", errors)
                    || !ValidateValue(call, 1, 0, MaxVoltageMv, "呼吸上限（0~10000mV）", errors)
                    || !ValidateValue(call, 2, 20, 65535, "呼吸周期（20~65535ms）", errors))
                {
                    return;
                }

                if (call.Arguments[0] > call.Arguments[1])
                {
                    errors.Add($"第 {Line(call)} 行：呼吸下限不能大于上限。");
                    return;
                }

                builder.Emit(IoLogicOpcode.AoBreath,
                             (byte)((call.Arguments[0] >> 8) & 0xFF), (byte)(call.Arguments[0] & 0xFF),
                             (byte)((call.Arguments[1] >> 8) & 0xFF), (byte)(call.Arguments[1] & 0xFF),
                             (byte)((call.Arguments[2] >> 8) & 0xFF), (byte)(call.Arguments[2] & 0xFF));
                return;

            case "IO_AO_BreathCurrent":
                if (!ValidateValue(call, 0, 0, MaxCurrentUa, "电流呼吸下限（0~20000uA）", errors)
                    || !ValidateValue(call, 1, 0, MaxCurrentUa, "电流呼吸上限（0~20000uA）", errors)
                    || !ValidateValue(call, 2, 20, 65535, "呼吸周期（20~65535ms）", errors))
                    return;
                if (call.Arguments[0] > call.Arguments[1])
                {
                    errors.Add($"第 {Line(call)} 行：电流呼吸下限不能大于上限。");
                    return;
                }
                builder.Emit(IoLogicOpcode.AoBreathCurrent,
                             (byte)((call.Arguments[0] >> 8) & 0xFF), (byte)(call.Arguments[0] & 0xFF),
                             (byte)((call.Arguments[1] >> 8) & 0xFF), (byte)(call.Arguments[1] & 0xFF),
                             (byte)((call.Arguments[2] >> 8) & 0xFF), (byte)(call.Arguments[2] & 0xFF));
                return;

            default:
                errors.Add($"第 {Line(call)} 行：不支持的 IO 动作函数 {name}。");
                return;
        }
    }

    private static bool ValidateChannel(IoLogicCall call, int argumentIndex, int minimum, int maximum,
                                        string displayName, List<string> errors)
    {
        if (call.Arguments.Count <= argumentIndex)
        {
            errors.Add($"第 {Line(call)} 行：{call.Name} 缺少参数。");
            return false;
        }

        var value = call.Arguments[argumentIndex];
        if (value < minimum || value > maximum)
        {
            errors.Add($"第 {Line(call)} 行：{call.Name} 的 {displayName} 必须为 {minimum}~{maximum}。");
            return false;
        }

        return true;
    }

    private static bool ValidateValue(IoLogicCall call, int argumentIndex, ushort minimum, ushort maximum,
                                      string displayName, List<string> errors)
    {
        if (call.Arguments.Count <= argumentIndex)
        {
            errors.Add($"第 {call.Line} 行：{call.Name} 缺少参数。");
            return false;
        }

        var value = call.Arguments[argumentIndex];
        if (value < minimum || value > maximum)
        {
            errors.Add($"第 {call.Line} 行：{call.Name} 的 {displayName} 必须为 {minimum}~{maximum}。");
            return false;
        }

        return true;
    }

    /* 校验条件比较右值（test.Value） */
    private static bool ValidateValue(IoLogicTest test, ushort minimum, ushort maximum,
                                      string displayName, List<string> errors)
    {
        if (test.Value is not { } value)
        {
            errors.Add($"第 {test.Call.Line} 行：{test.Call.Name} 缺少比较右值。");
            return false;
        }

        if (value < minimum || value > maximum)
        {
            errors.Add($"第 {test.Call.Line} 行：{test.Call.Name} 的 {displayName} 必须为 {minimum}~{maximum}。");
            return false;
        }

        return true;
    }

    private static int Line(IoLogicCall call) => call.Line;
}

/* 扩展：为调用记录行号（编译错误定位） */
internal partial record IoLogicCall
{
    public int Line { get; init; }
}


