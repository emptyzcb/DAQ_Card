using System.Globalization;
using System.Text.RegularExpressions;

namespace IndustrialFieldDataAcquisitionTerminal;

internal sealed class IoLogicCompileResult
{
    public IoLogicCompiledImage? Image { get; set; }
    public List<string> Errors { get; } = new();
    public bool Success => Image is not null && Errors.Count == 0;
}

internal sealed class IoLogicCompiler
{
    private static readonly Regex InputTerm = new(
        @"(?<not>!)?\s*IO_INPUT_ACTIVE\s*\(\s*INPUT_(?<index>[1-8])\s*\)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex EdgeTerm = new(
        @"IO_INPUT_(?<edge>RISING|FALLING)_EDGE\s*\(\s*INPUT_(?<index>[1-8])\s*\)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex SetAction = new(
        @"IO_OUTPUT_(?<action>SET|RESET|TOGGLE)\s*\(\s*(?<type>RELAY|TRANSISTOR)_(?<index>[1-4])\s*\)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex PulseAction = new(
        @"IO_OUTPUT_PULSE\s*\(\s*(?<type>RELAY|TRANSISTOR)_(?<index>[1-4])\s*,\s*(?<duration>[0-9]+)\s*\)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex TimePhaseTerm = new(
        @"IO_TIMER_PHASE\s*\(\s*(?<interval>[0-9]+)\s*,\s*(?<phase>[0-9]+)\s*\)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public IoLogicCompileResult Compile(string source, uint generation = 1)
    {
        var result = new IoLogicCompileResult();
        var rules = new List<IoLogicCompiledRule>();
        var timePhaseCount = 0;
        foreach (Match match in TimePhaseTerm.Matches(source))
        {
            if (ushort.TryParse(match.Groups["phase"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var phase))
            {
                if (phase > 7U)
                {
                    result.Errors.Add("IO_TIMER_PHASE 的 phase 必须为 0~7。 ");
                }
                else
                {
                    timePhaseCount = Math.Max(timePhaseCount, phase + 1);
                }
            }
        }
        if (timePhaseCount > 8U)
        {
            result.Errors.Add("IO_TIMER_PHASE 最多支持 8 个时间相位。");
        }

        var statements = FindIfStatements(source, result.Errors);
        foreach (var statement in statements)
        {
            var condition = ParseCondition(statement.Condition, result.Errors);
            if (condition is null)
            {
                continue;
            }

            var action = ParseAction(statement.Body, result.Errors);
            if (action is not null)
            {
                rules.Add(CreateRule(condition, action, rules.Count, (ushort)timePhaseCount));
            }

            if (!string.IsNullOrWhiteSpace(statement.ElseBody))
            {
                if (condition.EdgeType != 0)
                {
                    result.Errors.Add("边沿条件暂不支持 else 分支，请拆成独立逻辑。");
                }
                else
                {
                    var inverse = InvertCondition(condition);
                    var elseAction = ParseAction(statement.ElseBody, result.Errors);
                    if (elseAction is not null)
                    {
                        rules.Add(CreateRule(inverse, elseAction, rules.Count, (ushort)timePhaseCount));
                    }
                }
            }
        }

        if (rules.Count > IoLogicBinaryFormat.MaxRuleCount)
        {
            result.Errors.Add($"编译后的规则数量超过 {IoLogicBinaryFormat.MaxRuleCount} 条。");
        }

        var duplicateOutputs = rules
            .GroupBy(rule => rule.Priority)
            .SelectMany(group => group.SelectMany(rule => Enumerable.Range(0, 8)
                .Where(bit => (rule.OutputMask & (1 << bit)) != 0)
                .Select(bit => (group.Key, bit))))
            .GroupBy(item => (item.Key, item.bit))
            .Any(group => group.Count() > 1);
        if (duplicateOutputs)
        {
            result.Errors.Add("同一优先级下存在多个规则控制同一路输出，请调整源代码顺序或拆分逻辑。");
        }

        if (result.Errors.Count == 0)
        {
            try
            {
                result.Image = IoLogicBinaryFormat.Create(rules, generation);
            }
            catch (Exception ex)
            {
                result.Errors.Add(ex.Message);
            }
        }

        return result;
    }

    private static IoLogicCompiledRule CreateRule(Condition condition, ActionSpec action, int index, ushort timePhaseCount)
    {
        if (condition.IsTimePhase)
        {
            return new IoLogicCompiledRule(
                3,
                0,
                timePhaseCount,
                condition.TimerPhase,
                0,
                action.Action,
                action.OutputMask,
                0,
                condition.TimerIntervalMs,
                (byte)Math.Max(1, 255 - Math.Min(254, index)),
                true);
        }

        return new IoLogicCompiledRule(
            condition.EdgeType == 0 ? (byte)0 : (byte)1,
            condition.Operator,
            condition.InputMask,
            condition.Expected,
            condition.EdgeType,
            action.Action,
            action.OutputMask,
            0,
            action.PulseMs,
            (byte)Math.Max(1, 255 - Math.Min(254, index)),
            true);
    }

    private static Condition? ParseCondition(string condition, List<string> errors)
    {
        var timeMatches = TimePhaseTerm.Matches(condition);
        if (timeMatches.Count > 0)
        {
            if (timeMatches.Count != 1
                || InputTerm.Matches(condition).Count > 0
                || EdgeTerm.Matches(condition).Count > 0
                || condition.Contains("&&", StringComparison.Ordinal)
                || condition.Contains("||", StringComparison.Ordinal)
                || !ushort.TryParse(timeMatches[0].Groups["interval"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var interval)
                || !ushort.TryParse(timeMatches[0].Groups["phase"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var phase)
                || interval < 10U
                || phase > 7U)
            {
                errors.Add("IO_TIMER_PHASE 必须单独使用，间隔为 10~65535 毫秒，phase 为 0~7。");
                return null;
            }

            return new Condition(0, 0, 0, 0, interval, phase, true);
        }

        var edgeMatches = EdgeTerm.Matches(condition);
        if (edgeMatches.Count > 0)
        {
            if (edgeMatches.Count != 1 || InputTerm.Matches(condition).Count > 0)
            {
                errors.Add("边沿条件必须只包含一个 IO_INPUT_RISING_EDGE 或 IO_INPUT_FALLING_EDGE。");
                return null;
            }

            var match = edgeMatches[0];
            var bit = 1 << (int.Parse(match.Groups["index"].Value, CultureInfo.InvariantCulture) - 1);
            var edge = match.Groups["edge"].Value == "RISING" ? (byte)1 : (byte)2;
            return new Condition(0, (ushort)bit, (ushort)bit, edge);
        }

        var matches = InputTerm.Matches(condition);
        if (matches.Count == 0)
        {
            errors.Add($"无法识别条件：{condition.Trim()}。");
            return null;
        }

        var inputMask = 0;
        var expected = 0;
        foreach (Match match in matches)
        {
            var bit = 1 << (int.Parse(match.Groups["index"].Value, CultureInfo.InvariantCulture) - 1);
            inputMask |= bit;
            if (!match.Groups["not"].Success)
            {
                expected |= bit;
            }
        }

        var hasAnd = condition.Contains("&&", StringComparison.Ordinal);
        var hasOr = condition.Contains("||", StringComparison.Ordinal);
        if (hasAnd && hasOr)
        {
            errors.Add("条件暂不支持 && 和 || 混合，请使用单一逻辑运算符。");
            return null;
        }

        return new Condition(hasOr ? (byte)1 : (byte)0, (ushort)inputMask, (ushort)expected, 0);
    }

    private static Condition InvertCondition(Condition condition)
    {
        var inverseOperator = condition.Operator == 0 ? (byte)1 : (byte)0;
        return new Condition(inverseOperator, condition.InputMask, (ushort)(condition.InputMask ^ condition.Expected), 0);
    }

    private static ActionSpec? ParseAction(string body, List<string> errors)
    {
        var pulseMatches = PulseAction.Matches(body);
        var setMatches = SetAction.Matches(body);
        if (pulseMatches.Count > 0 && setMatches.Count > 0)
        {
            errors.Add("同一个条件分支不能混合脉冲动作和置位/复位动作。");
            return null;
        }

        if (pulseMatches.Count > 0)
        {
            ushort mask = 0;
            ushort pulse = 0;
            foreach (Match match in pulseMatches)
            {
                mask |= OutputBit(match.Groups["type"].Value, match.Groups["index"].Value);
                if (!ushort.TryParse(match.Groups["duration"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out pulse)
                    || pulse == 0)
                {
                    errors.Add("脉冲时间必须为 1~65535 毫秒。");
                    return null;
                }
            }

            return new ActionSpec(3, mask, pulse);
        }

        if (setMatches.Count == 0)
        {
            errors.Add("条件分支中没有识别到 IO_OUTPUT_SET、IO_OUTPUT_RESET、IO_OUTPUT_TOGGLE 或 IO_OUTPUT_PULSE。");
            return null;
        }

        var actionName = setMatches[0].Groups["action"].Value;
        if (setMatches.Cast<Match>().Any(match => match.Groups["action"].Value != actionName))
        {
            errors.Add("同一个条件分支只能使用一种输出动作。");
            return null;
        }

        ushort outputMask = 0;
        foreach (Match match in setMatches)
        {
            outputMask |= OutputBit(match.Groups["type"].Value, match.Groups["index"].Value);
        }

        var action = actionName switch
        {
            "SET" => (byte)0,
            "RESET" => (byte)1,
            _ => (byte)2
        };
        return new ActionSpec(action, outputMask, 0);
    }

    private static ushort OutputBit(string type, string indexText)
    {
        var index = int.Parse(indexText, CultureInfo.InvariantCulture) - 1;
        var bit = type == "TRANSISTOR" ? index + 4 : index;
        return (ushort)(1 << bit);
    }

    private static List<IfStatement> FindIfStatements(string source, List<string> errors)
    {
        var statements = new List<IfStatement>();
        for (var index = 0; index < source.Length; index++)
        {
            if (!IsWordAt(source, index, "if"))
            {
                continue;
            }

            var conditionStart = SkipWhitespace(source, index + 2);
            if (conditionStart >= source.Length || source[conditionStart] != '('
                || !TryFindMatching(source, conditionStart, '(', ')', out var conditionEnd))
            {
                errors.Add($"第 {LineOf(source, index)} 行的 if 条件括号不完整。");
                continue;
            }

            var bodyStart = SkipWhitespace(source, conditionEnd + 1);
            if (bodyStart >= source.Length || source[bodyStart] != '{'
                || !TryFindMatching(source, bodyStart, '{', '}', out var bodyEnd))
            {
                errors.Add($"第 {LineOf(source, index)} 行的 if 代码块必须使用大括号。");
                continue;
            }

            var afterBody = SkipWhitespace(source, bodyEnd + 1);
            string? elseBody = null;
            if (IsWordAt(source, afterBody, "else"))
            {
                var elseStart = SkipWhitespace(source, afterBody + 4);
                if (elseStart < source.Length && source[elseStart] == '{'
                    && TryFindMatching(source, elseStart, '{', '}', out var elseEnd))
                {
                    elseBody = source[(elseStart + 1)..elseEnd];
                    index = elseEnd;
                }
                else
                {
                    errors.Add($"第 {LineOf(source, afterBody)} 行的 else 代码块必须使用大括号。");
                }
            }

            statements.Add(new IfStatement(
                source[(conditionStart + 1)..conditionEnd],
                source[(bodyStart + 1)..bodyEnd],
                elseBody));
            index = bodyEnd;
        }

        return statements;
    }

    private static bool TryFindMatching(string source, int start, char open, char close, out int end)
    {
        var depth = 0;
        var quote = '\0';
        for (var index = start; index < source.Length; index++)
        {
            var current = source[index];
            if (quote != '\0')
            {
                if (current == '\\') index++;
                else if (current == quote) quote = '\0';
                continue;
            }

            if (current is '"' or '\'')
            {
                quote = current;
                continue;
            }

            if (current == '/' && index + 1 < source.Length && source[index + 1] == '/')
            {
                var newline = source.IndexOf('\n', index + 2);
                index = newline < 0 ? source.Length : newline;
                continue;
            }

            if (current == '/' && index + 1 < source.Length && source[index + 1] == '*')
            {
                var commentEnd = source.IndexOf("*/", index + 2, StringComparison.Ordinal);
                index = commentEnd < 0 ? source.Length : commentEnd + 1;
                continue;
            }

            if (current == open) depth++;
            if (current == close && --depth == 0)
            {
                end = index;
                return true;
            }
        }

        end = -1;
        return false;
    }

    private static bool IsWordAt(string source, int index, string word)
    {
        if (index < 0 || index + word.Length > source.Length || !source.AsSpan(index, word.Length).SequenceEqual(word))
        {
            return false;
        }

        var beforeOk = index == 0 || !char.IsLetterOrDigit(source[index - 1]) && source[index - 1] != '_';
        var after = index + word.Length;
        var afterOk = after >= source.Length || !char.IsLetterOrDigit(source[after]) && source[after] != '_';
        return beforeOk && afterOk;
    }

    private static int SkipWhitespace(string source, int index)
    {
        while (index < source.Length && char.IsWhiteSpace(source[index])) index++;
        return index;
    }

    private static int LineOf(string source, int index) => 1 + source[..Math.Min(index, source.Length)].Count(character => character == '\n');

    private sealed record IfStatement(string Condition, string Body, string? ElseBody);
    private sealed record Condition(
        byte Operator,
        ushort InputMask,
        ushort Expected,
        byte EdgeType,
        ushort TimerIntervalMs = 0,
        ushort TimerPhase = 0,
        bool IsTimePhase = false);
    private sealed record ActionSpec(byte Action, ushort OutputMask, ushort PulseMs);
}
