using System.Diagnostics;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace IndustrialFieldDataAcquisitionTerminal;

internal sealed partial class MainForm
{
    private async Task<bool> CompileCProgramAsync()
    {
        if (isCompiling)
        {
            return false;
        }

        if (codeEditor.ReadOnly)
        {
            SetStatus("固定库文件不能单独编译，请选择工程源文件");
            return false;
        }

        if (!IsCurrentProjectReady())
        {
            ShowCompileOutput();
            AppendCompileOutput("编译已停止：工程位置或入口文件不可用，请重新定位后重试。", true);
            return false;
        }
        try
        {
            if (currentSourceHash is not null && ComputeFileHash(currentUserSourcePath) != currentSourceHash)
            {
                ShowCompileOutput();
                AppendCompileOutput("编译已停止：源码在外部发生变化，请先保存副本或重新加载。", true);
                return false;
            }
        }
        catch (Exception ex)
        {
            ShowCompileOutput();
            AppendCompileOutput($"编译已停止：无法读取工程源码：{ex.Message}", true);
            return false;
        }

        await Task.Yield(); /* 让 UI 线程先处理一次消息，再开始编译 */

        isCompiling = true;
        compileOutputBox.Clear();
        AppendCompileOutput("开始编译 IO 用户程序...", false);
        logicCompileLabel.Text = "C逻辑程序：编译中...";
        logicCompileLabel.ForeColor = Color.FromArgb(190, 125, 35);
        codeStatusLabel.ForeColor = Color.FromArgb(190, 125, 35);
        codeStatusLabel.Text = "编译中";

        var errors = new List<string>();
        var source = codeEditor.Text;
        var tokens = TokenizeCSource(source, errors);
        if (!HasEntryPoint(tokens))
        {
            errors.Add("缺少主程序入口函数 void IO_Logic_Main(...)" );
        }

        ValidateDelimiters(tokens, errors);
        ValidateControlStatements(tokens, errors);

        if (tokens.Any(token => token.Text == "main"))
        {
            errors.Add("不允许定义或调用 main 函数");
        }

        if (tokens.Any(token => token.Text.StartsWith("HAL_", StringComparison.Ordinal)))
        {
            errors.Add("IO逻辑程序不能直接调用 HAL 接口");
        }

        if (errors.Count > 0)
        {
            var result = ReportCompileErrors(errors, "源码预检查失败");
            isCompiling = false;
            return result;
        }

        try
        {
            AppendCompileOutput("[1/2] 正在解析联合控制器 DSL（16 模式回调 + 主程序）...", false);
            var ioCompiler = new IoLogicCompiler();
            var ioCompile = ioCompiler.Compile(source);
            if (!ioCompile.Success || ioCompile.Image is null)
            {
                return ReportCompileErrors(ioCompile.Errors, "联合控制器编译失败");
            }

            AppendCompileOutput("[2/2] 字节码生成完成：校验通过", false);
            lastCompiledImage = ioCompile.Image;
            lastCompiledUsesSnapshotInputs = ioCompile.UsesSnapshotInputs;
            lastCompiledUsesCurrentBreath = ioCompile.UsesCurrentBreath;
            logicCompileLabel.Text = $"联合控制器：编译通过 · {ioCompile.Image.Bytes.Length} 字节 · {ioCompile.Image.ProgramSize} 条指令";
            logicCompileLabel.ForeColor = Color.FromArgb(35, 135, 85);
            codeStatusLabel.ForeColor = UiPalette.Muted;
            codeStatusLabel.Text = $"编译通过 · 联合控制器 {ioCompile.Image.Bytes.Length} 字节 · {ioCompile.Image.ProgramSize} 条指令";
            compileOutputToggleButton.ForeColor = SystemColors.ControlText;
            SetStatus("联合控制器程序编译通过，可下载到板卡");
            AppendCompileOutput($"联合控制器编译完成：{ioCompile.Image.Bytes.Length} 字节，{ioCompile.Image.ProgramSize} 条指令，CRC32=0x{ioCompile.Image.BodyCrc32:X8}", false);
            OnClientLog($"{DateTime.Now:HH:mm:ss.fff}  联合控制器编译通过：VM={ioCompile.Image.Bytes.Length}字节, insn={ioCompile.Image.ProgramSize}");
            return true;
        }
        catch (Exception ex)
        {
            return ReportCompileErrors(new[] { ex.Message }, "联合控制器编译异常");
        }
        finally
        {
            isCompiling = false;
        }
    }

    private async void DownloadCProgramAsync()
    {
        if (!client.IsOpen)
        {
            SetStatus("请先连接以太网");
            ShowCompileOutput();
            AppendCompileOutput("下载失败：请先连接以太网。", true);
            return;
        }

        if (!await CompileCProgramAsync())
        {
            return;
        }

        if (lastCompiledImage is null)
        {
            SetStatus("没有可下载的联合控制器编译结果");
            ShowCompileOutput();
            AppendCompileOutput("下载失败：没有可下载的联合控制器编译结果。", true);
            return;
        }

        ShowCompileOutput();
        AppendCompileOutput("编译通过，开始下载到板卡...", false);
        await pollGate.WaitAsync();
        try
        {
            SetStatus("正在下载联合控制器程序到板卡，请勿发送其他以太网命令");
            logicCompileLabel.Text = "联合控制器：下载中...";
            var protocol = new IoLogicProtocol(client);
            var service = new IoLogicDownloadService(protocol, OnClientLog);
            var info = await Task.Run(() =>
            {
                if (lastCompiledUsesSnapshotInputs || lastCompiledUsesCurrentBreath)
                {
                    ushort[] capabilities;
                    try
                    {
                        capabilities = client.ReadHoldingRegisters(0x0006, 1);
                    }
                    catch (ModbusException ex)
                    {
                        throw new InvalidOperationException("无法读取板卡规则能力，请确认固件版本。", ex);
                    }
                    if (lastCompiledUsesSnapshotInputs &&
                        (capabilities.Length != 1 || (capabilities[0] & 0x0001) == 0))
                        throw new InvalidOperationException("板卡固件不支持模拟量/RS485 输入规则，请先升级到 V1.2.6。");
                    if (lastCompiledUsesCurrentBreath &&
                        (capabilities.Length != 1 || (capabilities[0] & 0x0002) == 0))
                        throw new InvalidOperationException("板卡固件不支持电流呼吸指令，请先烧录 V1.2.7。");
                }
                return service.Download(lastCompiledImage, CancellationToken.None);
            });
            logicCompileLabel.Text = $"联合控制器：下载完成 · generation {info.Generation}";
            logicCompileLabel.ForeColor = Color.FromArgb(35, 135, 85);
            SetStatus($"联合控制器下载完成，板卡 generation={info.Generation}");
            AppendCompileOutput($"下载成功：板卡已确认，generation={info.Generation}，配置槽={info.ConfigState}", false);
        }
        catch (Exception ex)
        {
            logicCompileLabel.Text = "联合控制器：下载失败";
            logicCompileLabel.ForeColor = Color.FromArgb(190, 55, 55);
            SetStatus($"联合控制器下载失败：{ex.Message}");
            AppendCompileOutput($"下载失败：{ex.Message}", true);
            OnClientLog($"{DateTime.Now:HH:mm:ss.fff}  ERROR  联合控制器下载失败：{ex.Message}");
            // 下载服务会使用当前会话号主动 ABORT；这里不再发送无效的 session=0。
        }
        finally
        {
            pollGate.Release();
        }
    }

    private bool ReportCompileErrors(IReadOnlyList<string> errors, string title)
    {
        ShowCompileOutput();
        compileOutputToggleButton.ForeColor = Color.FromArgb(190, 55, 55);
        logicCompileLabel.Text = $"C逻辑程序：编译失败 · {errors.Count} 个错误";
        logicCompileLabel.ForeColor = Color.FromArgb(190, 55, 55);
        codeStatusLabel.ForeColor = UiPalette.LogError;
        codeStatusLabel.Text = $"编译错误：{errors[0]}";
        SetStatus($"C 逻辑编译失败：{errors[0]}");
        AppendCompileOutput($"{title}：", true);
        foreach (var error in errors)
        {
            AppendCompileOutput($"ERROR: {error}", true);
            OnClientLog($"{DateTime.Now:HH:mm:ss.fff}  ERROR  {error}");
        }

        return false;
    }

    private static string? ResolveCCompiler()
    {
        var configured = Environment.GetEnvironmentVariable("DAQ_GCC_PATH");
        var candidates = new[]
        {
            configured,
            @"D:\APP_MING\mingw64\bin\gcc.exe",
            Path.Combine(AppContext.BaseDirectory, "tools", "gcc.exe")
        };

        foreach (var candidate in candidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate))
            {
                return candidate;
            }
        }

        return "gcc.exe";
    }

    private void AppendCompileOutput(string? text, bool isError)
    {
        if (string.IsNullOrWhiteSpace(text) || compileOutputBox.IsDisposed)
        {
            return;
        }

        void Append()
        {
            compileOutputBox.SelectionStart = compileOutputBox.TextLength;
            compileOutputBox.SelectionLength = 0;
            compileOutputBox.SelectionColor = isError ? UiPalette.LogError : UiPalette.LogText;
            compileOutputBox.AppendText(text + Environment.NewLine);
            compileOutputBox.ScrollToCaret();
        }

        if (compileOutputBox.InvokeRequired)
        {
            try
            {
                compileOutputBox.BeginInvoke((Action)Append);
            }
            catch (InvalidOperationException)
            {
                // The form is closing while GCC is returning its last line.
            }
        }
        else
        {
            Append();
        }
    }

    private void UpdateCodeStatus()
    {
        var lineCount = codeEditor.IsHandleCreated
            ? Math.Max(1, SendMessage(codeEditor.Handle, 0x00BA, IntPtr.Zero, IntPtr.Zero).ToInt32())
            : 1;
        codeStatusLabel.Text = codeEditor.ReadOnly
            ? $"行 {lineCount} · 字符 {codeEditor.TextLength} · 固定库文件，只读"
            : $"行 {lineCount} · 字符 {codeEditor.TextLength} · 可编辑 C 逻辑源码";
    }

    private sealed record CSourceToken(string Text, int Start, int Line, int Column);

    private static List<CSourceToken> TokenizeCSource(string source, List<string> errors)
    {
        var tokens = new List<CSourceToken>();
        var index = 0;
        while (index < source.Length)
        {
            if (char.IsWhiteSpace(source[index]))
            {
                index++;
                continue;
            }

            if (source[index] == '/' && index + 1 < source.Length && source[index + 1] == '/')
            {
                index += 2;
                while (index < source.Length && source[index] != '\n') index++;
                continue;
            }

            if (source[index] == '/' && index + 1 < source.Length && source[index + 1] == '*')
            {
                var start = index;
                index += 2;
                var closed = false;
                while (index + 1 < source.Length)
                {
                    if (source[index] == '*' && source[index + 1] == '/')
                    {
                        index += 2;
                        closed = true;
                        break;
                    }

                    index++;
                }

                if (!closed)
                {
                    AddSourceError(errors, source, start, "多行注释没有结束标记 */");
                    index = source.Length;
                }

                continue;
            }

            var tokenStart = index;
            if (IsIdentifierStart(source[index]))
            {
                index++;
                while (index < source.Length && IsIdentifierPart(source[index])) index++;
                tokens.Add(CreateSourceToken(source, tokenStart, index));
                continue;
            }

            if (char.IsDigit(source[index]))
            {
                index++;
                while (index < source.Length && (char.IsLetterOrDigit(source[index]) || source[index] == '.' || source[index] == '_')) index++;
                tokens.Add(CreateSourceToken(source, tokenStart, index));
                continue;
            }

            if (source[index] is '"' or '\'')
            {
                var quote = source[index++];
                var closed = false;
                var escaped = false;
                while (index < source.Length)
                {
                    var current = source[index++];
                    if (escaped)
                    {
                        escaped = false;
                        continue;
                    }

                    if (current == '\\')
                    {
                        escaped = true;
                        continue;
                    }

                    if (current == quote)
                    {
                        closed = true;
                        break;
                    }

                    if (current == '\n' && quote == '\'')
                    {
                        break;
                    }
                }

                if (!closed)
                {
                    AddSourceError(errors, source, tokenStart, quote == '"' ? "字符串没有结束引号" : "字符常量没有结束引号");
                }

                tokens.Add(CreateSourceToken(source, tokenStart, index));
                continue;
            }

            var symbolLength = index + 1 < source.Length && IsTwoCharacterOperator(source[index], source[index + 1]) ? 2 : 1;
            index += symbolLength;
            tokens.Add(CreateSourceToken(source, tokenStart, index));
        }

        return tokens;
    }

    private static CSourceToken CreateSourceToken(string source, int start, int end)
    {
        var line = 1;
        var lastLineBreak = -1;
        for (var index = 0; index < start; index++)
        {
            if (source[index] == '\n')
            {
                line++;
                lastLineBreak = index;
            }
        }

        return new CSourceToken(source[start..end], start, line, start - lastLineBreak);
    }

    private static void ValidateDelimiters(IReadOnlyList<CSourceToken> tokens, List<string> errors)
    {
        var stack = new Stack<CSourceToken>();
        var matching = new Dictionary<string, string>
        {
            [")"] = "(", ["]"] = "[", ["}"] = "{"
        };

        foreach (var token in tokens)
        {
            if (token.Text is "(" or "[" or "{")
            {
                stack.Push(token);
                continue;
            }

            if (!matching.TryGetValue(token.Text, out var expectedOpening))
            {
                continue;
            }

            if (stack.Count == 0 || stack.Peek().Text != expectedOpening)
            {
                errors.Add($"第 {token.Line} 行，第 {token.Column} 列：符号 {token.Text} 没有匹配的 {expectedOpening}");
                continue;
            }

            stack.Pop();
        }

        while (stack.Count > 0)
        {
            var token = stack.Pop();
            errors.Add($"第 {token.Line} 行，第 {token.Column} 列：符号 {token.Text} 没有结束");
        }
    }

    private static void ValidateControlStatements(IReadOnlyList<CSourceToken> tokens, List<string> errors)
    {
        for (var index = 0; index < tokens.Count; index++)
        {
            var token = tokens[index];
            if (token.Text is not ("if" or "for" or "while" or "switch"))
            {
                continue;
            }

            var next = index + 1 < tokens.Count ? tokens[index + 1] : null;
            if (next is null || next.Text != "(")
            {
                var actual = next is null ? "文件结束" : $"{next.Text}（第 {next.Line} 行，第 {next.Column} 列）";
                errors.Add($"第 {token.Line} 行，第 {token.Column} 列：关键字 {token.Text} 后必须是 '('，实际为 {actual}");
            }
        }

        for (var index = 0; index + 1 < tokens.Count; index++)
        {
            if (tokens[index].Text == ")" && tokens[index + 1].Text == "(")
            {
                errors.Add($"第 {tokens[index + 1].Line} 行，第 {tokens[index + 1].Column} 列：检测到异常的 ')('");
            }
        }
    }

    private static bool HasEntryPoint(IReadOnlyList<CSourceToken> tokens)
    {
        for (var index = 0; index + 3 < tokens.Count; index++)
        {
            if (tokens[index].Text == "void" && tokens[index + 1].Text == "IO_Logic_Main"
                && tokens[index + 2].Text == "(")
            {
                if (tokens[index + 3].Text == ")")
                {
                    return true;
                }

                if (tokens[index + 3].Text == "void"
                    && index + 4 < tokens.Count
                    && tokens[index + 4].Text == ")")
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool IsTwoCharacterOperator(char first, char second)
    {
        return (first, second) is ('=', '=') or ('!', '=') or ('<', '=') or ('>', '=') or ('&', '&') or ('|', '|')
            or ('+', '+') or ('-', '-') or ('-', '>') or ('+', '=') or ('-', '=') or ('*', '=') or ('/', '=')
            or ('%', '=') or ('<', '<') or ('>', '>');
    }

    private static void AddSourceError(List<string> errors, string source, int index, string message)
    {
        var line = 1;
        var lastLineBreak = -1;
        for (var cursor = 0; cursor < index; cursor++)
        {
            if (source[cursor] == '\n')
            {
                line++;
                lastLineBreak = cursor;
            }
        }

        errors.Add($"第 {line} 行，第 {index - lastLineBreak} 列：{message}");
    }

}
