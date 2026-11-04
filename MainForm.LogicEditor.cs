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
    private Control BuildCEditorPanel()
    {
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = UiPalette.CodeBackground };
        codeEditorTitle.Text = "C 语言 IO 控制程序 · 联合控制器 v2";
        codeEditorTitle.Dock = DockStyle.Top;
        codeEditorTitle.Height = 40;
        codeEditorTitle.TextAlign = ContentAlignment.MiddleLeft;
        codeEditorTitle.Padding = new Padding(14, 0, 0, 0);
        codeEditorTitle.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
        codeEditorTitle.ForeColor = UiPalette.Ink;
        codeEditorTitle.BackColor = UiPalette.CodeChrome;
        BuildFindBar();

        codeEditor.Dock = DockStyle.Fill;
        codeEditor.BorderStyle = BorderStyle.None;
        codeEditor.BackColor = UiPalette.CodeBackground;
        codeEditor.ForeColor = UiPalette.CodeText;
        codeEditor.Font = new Font("Cascadia Mono", 10F, FontStyle.Regular);
        codeEditor.AcceptsTab = true;
        codeEditor.WordWrap = false;
        codeEditor.ScrollBars = RichTextBoxScrollBars.Both;
        codeEditor.DetectUrls = false;
        codeEditor.ShortcutsEnabled = true;
        codeEditor.TextChanged += (_, _) =>
        {
            if (isHighlightingCode) return;
            RecordEditorState(codeEditor.Text);
            UpdateCodeStatus();
            lineNumberGutter.Invalidate();
            highlightedLines.Clear();
            codeMinimap.MarkContentDirty();
            if (isRestoringEditorState) return;
            editorDecorationTimer.Stop();
            editorDecorationTimer.Start();
        };
        codeEditor.KeyDown += OnCodeEditorKeyDown;
        codeEditor.MouseDown += OnCodeEditorMouseDown;
        codeEditor.VScroll += (_, _) =>
        {
            lineNumberGutter.Invalidate();
            codeMinimap.Invalidate();
            HighlightVisibleCode();
        };
        codeEditor.Resize += (_, _) =>
        {
            lineNumberGutter.Invalidate();
            codeMinimap.Invalidate();
            HighlightVisibleCode();
        };
        compileSplit.Dock = DockStyle.Fill;
        compileSplit.Orientation = Orientation.Horizontal;
        compileSplit.SplitterWidth = 6;

        codeEditorHost.Dock = DockStyle.Fill;
        codeEditorHost.BackColor = codeEditor.BackColor;
        lineNumberGutter.Dock = DockStyle.Left;
        lineNumberGutter.Width = 48;
        lineNumberGutter.Editor = codeEditor;
        codeMinimap.Dock = DockStyle.Right;
        codeMinimap.Width = 104;
        codeMinimap.Editor = codeEditor;
        codeEditorHost.Controls.Add(codeEditor);
        codeEditorHost.Controls.Add(codeMinimap);
        codeEditorHost.Controls.Add(lineNumberGutter);
        compileSplit.Panel1.Controls.Add(codeEditorHost);

        compileOutputBox.Dock = DockStyle.Fill;
        compileOutputBox.ReadOnly = true;
        compileOutputBox.BorderStyle = BorderStyle.None;
        compileOutputBox.BackColor = UiPalette.Surface;
        compileOutputBox.ForeColor = UiPalette.LogText;
        compileOutputBox.Font = new Font("Cascadia Mono", 9F, FontStyle.Regular);
        compileOutputBox.WordWrap = false;
        compileOutputBox.ScrollBars = RichTextBoxScrollBars.Both;
        compileSplit.Panel2.Controls.Add(compileOutputBox);
        compileSplit.Panel2Collapsed = true;
        panel.Controls.Add(compileSplit);
        panel.Controls.Add(findBar);
        panel.Controls.Add(codeEditorTitle);

        codeStatusLabel.Dock = DockStyle.Bottom;
        codeStatusLabel.Height = 28;
        codeStatusLabel.Text = "行 1 · 字符 0 · 未检查";
        codeStatusLabel.ForeColor = UiPalette.Muted;
        codeStatusLabel.TextAlign = ContentAlignment.MiddleLeft;
        codeStatusLabel.Padding = new Padding(4, 0, 0, 0);
        codeStatusLabel.BackColor = UiPalette.CodeChrome;
        panel.Controls.Add(codeStatusLabel);
        Shown += (_, _) =>
        {
            compileSplit.Panel1MinSize = 220;
            compileSplit.Panel2MinSize = 120;
            compileSplit.SplitterDistance = Math.Max(220, compileSplit.Height - 170);
            HighlightVisibleCode();
        };
        return panel;
    }

    private void ToggleCompileOutput()
    {
        compileSplit.Panel2Collapsed = !compileSplit.Panel2Collapsed;
        compileOutputToggleButton.Text = compileSplit.Panel2Collapsed ? "编译结果" : "收起结果";
    }

    private void ShowCompileOutput()
    {
        if (compileSplit.Panel2Collapsed)
        {
            ToggleCompileOutput();
        }
    }

    private void BuildFindBar()
    {
        findBar.Dock = DockStyle.Top;
        findBar.Height = 38;
        findBar.BackColor = UiPalette.CodeChrome;
        findBar.Padding = new Padding(8, 5, 8, 5);
        findBar.Visible = false;

        var findLabel = new Label
        {
            Text = "查找",
            AutoSize = true,
            ForeColor = UiPalette.CodeText,
            Dock = DockStyle.Left,
            Padding = new Padding(0, 5, 8, 0)
        };
        findInput.BorderStyle = BorderStyle.FixedSingle;
        findInput.BackColor = UiPalette.Surface;
        findInput.ForeColor = UiPalette.CodeText;
        findInput.Width = 220;
        findInput.Dock = DockStyle.Left;
        findInput.KeyDown += OnFindInputKeyDown;

        var nextButton = new Button { Text = "下一个", Width = 64, Dock = DockStyle.Left };
        nextButton.Click += (_, _) => FindNext(false);
        var previousButton = new Button { Text = "上一个", Width = 64, Dock = DockStyle.Left };
        previousButton.Click += (_, _) => FindNext(true);
        var closeButton = new Button { Text = "关闭", Width = 52, Dock = DockStyle.Left };
        closeButton.Click += (_, _) => HideFindBar();
        findResultLabel.AutoSize = true;
        findResultLabel.ForeColor = UiPalette.CodeMuted;
        findResultLabel.Dock = DockStyle.Left;
        findResultLabel.Padding = new Padding(8, 5, 0, 0);

        findBar.Controls.Add(findResultLabel);
        findBar.Controls.Add(closeButton);
        findBar.Controls.Add(previousButton);
        findBar.Controls.Add(nextButton);
        findBar.Controls.Add(findInput);
        findBar.Controls.Add(findLabel);
    }

    private void ShowFindBar()
    {
        findBar.Visible = true;
        findInput.Focus();
        findInput.SelectAll();
        if (string.IsNullOrEmpty(findInput.Text) && codeEditor.SelectionLength > 0)
        {
            findInput.Text = codeEditor.SelectedText;
            findInput.SelectAll();
        }

        FindNext(false);
    }

    private void HideFindBar()
    {
        findBar.Visible = false;
        codeEditor.Focus();
    }

    private void OnFindInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter)
        {
            FindNext(e.Shift);
            e.SuppressKeyPress = true;
            e.Handled = true;
        }
        else if (e.KeyCode == Keys.Escape)
        {
            HideFindBar();
            e.SuppressKeyPress = true;
            e.Handled = true;
        }
    }

    private void FindNext(bool backwards)
    {
        var query = findInput.Text;
        if (string.IsNullOrEmpty(query))
        {
            findResultLabel.Text = "请输入内容";
            return;
        }

        var text = codeEditor.Text;
        var start = backwards
            ? Math.Max(0, codeEditor.SelectionStart - 1)
            : Math.Min(text.Length, codeEditor.SelectionStart + codeEditor.SelectionLength);
        var index = backwards
            ? text.LastIndexOf(query, start, StringComparison.OrdinalIgnoreCase)
            : text.IndexOf(query, start, StringComparison.OrdinalIgnoreCase);

        if (index < 0)
        {
            index = backwards
                ? text.LastIndexOf(query, text.Length, StringComparison.OrdinalIgnoreCase)
                : text.IndexOf(query, 0, StringComparison.OrdinalIgnoreCase);
        }

        if (index < 0)
        {
            findResultLabel.Text = "未找到";
            return;
        }

        codeEditor.Select(index, query.Length);
        codeEditor.ScrollToCaret();
        findResultLabel.Text = $"位置 {codeEditor.GetLineFromCharIndex(index) + 1} 行";
    }

    private void ShowGotoLineDialog()
    {
        using var dialog = new Form
        {
            Text = "跳转到行",
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MinimizeBox = false,
            MaximizeBox = false,
            ClientSize = new Size(280, 108),
            ShowInTaskbar = false
        };

        var label = new Label { Text = "行号：", AutoSize = true, Location = new Point(18, 20) };
        var lineInput = new NumericUpDown
        {
            Minimum = 1,
            Maximum = Math.Max(1, codeEditor.Lines.Length),
            Value = Math.Min(codeEditor.GetLineFromCharIndex(codeEditor.SelectionStart) + 1, Math.Max(1, codeEditor.Lines.Length)),
            Location = new Point(72, 16),
            Width = 170
        };
        var okButton = new Button { Text = "确定", DialogResult = DialogResult.OK, Location = new Point(92, 62), Width = 70 };
        var cancelButton = new Button { Text = "取消", DialogResult = DialogResult.Cancel, Location = new Point(172, 62), Width = 70 };
        dialog.Controls.AddRange(new Control[] { label, lineInput, okButton, cancelButton });
        dialog.AcceptButton = okButton;
        dialog.CancelButton = cancelButton;

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        var line = (int)lineInput.Value - 1;
        var index = codeEditor.GetFirstCharIndexFromLine(line);
        if (index >= 0)
        {
            codeEditor.Select(index, 0);
            codeEditor.ScrollToCaret();
            codeEditor.Focus();
        }
    }

    private void OnCodeEditorMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || ModifierKeys != Keys.Control)
        {
            return;
        }

        var charIndex = codeEditor.GetCharIndexFromPosition(e.Location);
        if (charIndex < 0 || charIndex >= codeEditor.TextLength)
        {
            return;
        }

        codeEditor.SelectionStart = charIndex;
        codeEditor.SelectionLength = 0;
        JumpToLibrarySymbol();
    }

    private void OnCodeEditorKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.F12)
        {
            JumpToLibrarySymbol();
            e.SuppressKeyPress = true;
            e.Handled = true;
            return;
        }

        if (e.Control && e.KeyCode == Keys.F)
        {
            ShowFindBar();
            e.SuppressKeyPress = true;
            e.Handled = true;
            return;
        }

        if (e.KeyCode == Keys.F3)
        {
            if (!findBar.Visible)
            {
                ShowFindBar();
            }
            else
            {
                FindNext(e.Shift);
            }

            e.SuppressKeyPress = true;
            e.Handled = true;
            return;
        }

        if (e.Control && e.KeyCode == Keys.G)
        {
            ShowGotoLineDialog();
            e.SuppressKeyPress = true;
            e.Handled = true;
            return;
        }

        if (e.Control && e.KeyCode == Keys.S)
        {
            SaveCProgram();
            e.SuppressKeyPress = true;
            e.Handled = true;
            return;
        }

        if (e.Control && e.KeyCode == Keys.N)
        {
            NewProject();
            e.SuppressKeyPress = true;
            e.Handled = true;
            return;
        }

        if (e.Control && e.KeyCode == Keys.O)
        {
            ImportProject();
            e.SuppressKeyPress = true;
            e.Handled = true;
            return;
        }

        if (e.KeyCode == Keys.F5)
        {
            _ = CompileCProgramAsync();
            e.SuppressKeyPress = true;
            e.Handled = true;
            return;
        }

        if (e.Control && e.KeyCode == Keys.Z)
        {
            UndoEditorChange();
            e.SuppressKeyPress = true;
            e.Handled = true;
            return;
        }

        if (e.Control && e.KeyCode == Keys.Y)
        {
            RedoEditorChange();
            e.SuppressKeyPress = true;
            e.Handled = true;
            return;
        }

        if (e.Control && e.KeyCode == Keys.K)
        {
            commentChordPending = true;
            e.SuppressKeyPress = true;
            e.Handled = true;
            codeStatusLabel.Text = "等待快捷键：Ctrl+C 批量注释，Ctrl+U 取消注释";
            return;
        }

        if (commentChordPending)
        {
            if (e.Control && e.KeyCode == Keys.C)
            {
                ApplyLineComments(true);
            }
            else if (e.Control && e.KeyCode == Keys.U)
            {
                ApplyLineComments(false);
            }

            commentChordPending = false;
            e.SuppressKeyPress = true;
            e.Handled = true;
            return;
        }

        if (e.Control && (e.KeyCode == Keys.OemQuestion || e.KeyCode == Keys.Divide))
        {
            ApplyLineComments(null);
            e.SuppressKeyPress = true;
            e.Handled = true;
        }
    }

    private void JumpToLibrarySymbol()
    {
        var symbol = GetEditorSymbolAtCaret();
        if (string.IsNullOrWhiteSpace(symbol))
        {
            SetStatus("请先把光标放在库函数或 IO 标识符上");
            return;
        }

        var projectRoot = Path.GetDirectoryName(currentUserSourcePath);
        if (string.IsNullOrWhiteSpace(projectRoot))
        {
            SetStatus("当前没有可跳转的工程");
            return;
        }

        var libraryPath = Path.Combine(projectRoot, "Library", "io_logic_library.c");
        TreeNode? libraryNode = null;
        foreach (TreeNode projectNode in sourceTreeView.Nodes)
        {
            foreach (TreeNode child in projectNode.Nodes)
            {
                foreach (TreeNode node in child.Nodes)
                {
                    if (node.Tag is SourceFileEntry entry
                        && entry.IsLibrary
                        && string.Equals(Path.GetFullPath(entry.FilePath), Path.GetFullPath(libraryPath), StringComparison.OrdinalIgnoreCase))
                    {
                        libraryNode = node;
                        break;
                    }
                }

                if (libraryNode is not null) break;
            }

            if (libraryNode is not null) break;
        }

        if (libraryNode is null)
        {
            SetStatus($"找不到工程库文件：{libraryPath}");
            return;
        }

        sourceTreeView.SelectedNode = libraryNode;
        var declarationIndex = FindLibraryDeclaration(codeEditor.Text, symbol);
        if (declarationIndex < 0)
        {
            SetStatus($"库文件中找不到标识符：{symbol}");
            return;
        }

        codeEditor.Select(declarationIndex, symbol.Length);
        codeEditor.ScrollToCaret();
        codeEditor.Focus();
        SetStatus($"已跳转到库文件：{symbol}");
    }

    private string? GetEditorSymbolAtCaret()
    {
        if (codeEditor.SelectionLength > 0 && IsIdentifier(codeEditor.SelectedText))
        {
            return codeEditor.SelectedText;
        }

        if (codeEditor.TextLength == 0)
        {
            return null;
        }

        var position = Math.Min(codeEditor.SelectionStart, codeEditor.TextLength - 1);
        if (!IsIdentifierCharacter(codeEditor.Text[position]))
        {
            if (position > 0 && IsIdentifierCharacter(codeEditor.Text[position - 1]))
            {
                position--;
            }
            else
            {
                return null;
            }
        }

        var start = position;
        while (start > 0 && IsIdentifierCharacter(codeEditor.Text[start - 1])) start--;
        var end = position + 1;
        while (end < codeEditor.TextLength && IsIdentifierCharacter(codeEditor.Text[end])) end++;
        return codeEditor.Text[start..end];
    }

    private static int FindLibraryDeclaration(string source, string symbol)
    {
        var escaped = Regex.Escape(symbol);
        var function = Regex.Match(
            source,
            $@"(?m)^\s*(?:int|void|uint16_t)\s+{escaped}\s*\(",
            RegexOptions.CultureInvariant);
        if (function.Success)
        {
            return function.Index + function.Value.IndexOf(symbol, StringComparison.Ordinal);
        }

        var enumValue = Regex.Match(
            source,
            $@"(?m)^\s*{escaped}\s*=",
            RegexOptions.CultureInvariant);
        if (enumValue.Success)
        {
            return enumValue.Index + enumValue.Value.IndexOf(symbol, StringComparison.Ordinal);
        }

        return source.IndexOf(symbol, StringComparison.Ordinal);
    }

    private static bool IsIdentifier(string value)
    {
        return value.Length > 0 && value.All(IsIdentifierCharacter);
    }

    private static bool IsIdentifierCharacter(char value)
    {
        return char.IsLetterOrDigit(value) || value == '_';
    }

    private void ApplyLineComments(bool? forceComment)
    {
        if (codeEditor.ReadOnly)
        {
            SetStatus("固定库文件不可修改，请选择工程源文件");
            return;
        }

        var selectionStart = codeEditor.SelectionStart;
        var selectionLength = codeEditor.SelectionLength;
        var selectionEnd = selectionStart + selectionLength;
        var firstLine = codeEditor.GetLineFromCharIndex(selectionStart);
        var lastCharacter = Math.Max(selectionStart, selectionEnd - 1);
        var lastLine = codeEditor.GetLineFromCharIndex(lastCharacter);
        var replaceStart = codeEditor.GetFirstCharIndexFromLine(firstLine);
        var replaceEnd = lastLine + 1 < codeEditor.Lines.Length
            ? codeEditor.GetFirstCharIndexFromLine(lastLine + 1)
            : codeEditor.TextLength;
        var selectedText = codeEditor.Text.Substring(replaceStart, replaceEnd - replaceStart);
        var newline = selectedText.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var lines = selectedText.Split(new[] { newline }, StringSplitOptions.None);
        var nonEmptyLines = lines.Where(line => !string.IsNullOrWhiteSpace(line)).ToArray();
        var allCommented = nonEmptyLines.Length > 0 && nonEmptyLines.All(line =>
        {
            var firstNonWhitespace = line.TakeWhile(char.IsWhiteSpace).Count();
            return firstNonWhitespace < line.Length && line[firstNonWhitespace..].StartsWith("//", StringComparison.Ordinal);
        });
        var shouldComment = forceComment ?? !allCommented;

        var transformed = string.Join(newline, lines.Select(line =>
        {
            var firstNonWhitespace = line.TakeWhile(char.IsWhiteSpace).Count();
            if (string.IsNullOrWhiteSpace(line))
            {
                return line;
            }

            if (shouldComment)
            {
                if (firstNonWhitespace < line.Length && line[firstNonWhitespace..].StartsWith("//", StringComparison.Ordinal))
                {
                    return line;
                }

                return line.Insert(firstNonWhitespace, "// ");
            }

            if (firstNonWhitespace < line.Length && line[firstNonWhitespace..].StartsWith("//", StringComparison.Ordinal))
            {
                var removeLength = firstNonWhitespace + 2;
                if (removeLength < line.Length && line[removeLength] == ' ')
                {
                    removeLength++;
                }

                return line.Remove(firstNonWhitespace, removeLength - firstNonWhitespace);
            }

            return line;
        }));

        codeEditor.Select(replaceStart, replaceEnd - replaceStart);
        codeEditor.SelectedText = transformed;
        var newSelectionEnd = Math.Min(replaceStart + transformed.Length, codeEditor.TextLength);
        codeEditor.Select(replaceStart, Math.Max(0, newSelectionEnd - replaceStart));
        codeStatusLabel.Text = shouldComment ? "已批量注释选中行" : "已取消注释选中行";
    }

    private void SetEditorText(string text)
    {
        editorDecorationTimer.Stop();
        isRestoringEditorState = true;
        try
        {
            codeEditor.Text = text;
            codeEditor.ClearUndo();
        }
        finally
        {
            isRestoringEditorState = false;
        }

        ResetEditorHistory();
        editorDecorationTimer.Stop();
        HighlightVisibleCode();
        HighlightCSource();
        codeMinimap.InvalidateContent();
        lineNumberGutter.Invalidate();
    }

    private void ResetEditorHistory()
    {
        editorHistory.Clear();
        editorHistory.Add(new EditorState(codeEditor.Text, codeEditor.SelectionStart, codeEditor.SelectionLength));
        editorHistoryIndex = 0;
    }

    private void RecordEditorState(string currentText)
    {
        if (isRestoringEditorState || editorHistoryIndex < 0 || editorHistory.Count == 0)
        {
            return;
        }

        if (string.Equals(editorHistory[editorHistoryIndex].Text, currentText, StringComparison.Ordinal))
        {
            return;
        }

        if (editorHistoryIndex + 1 < editorHistory.Count)
        {
            editorHistory.RemoveRange(editorHistoryIndex + 1, editorHistory.Count - editorHistoryIndex - 1);
        }

        editorHistory.Add(new EditorState(currentText, codeEditor.SelectionStart, codeEditor.SelectionLength));
        editorHistoryIndex++;
        if (editorHistory.Count > 200)
        {
            editorHistory.RemoveAt(0);
            editorHistoryIndex--;
        }
    }

    private void UndoEditorChange()
    {
        if (editorHistoryIndex <= 0)
        {
            return;
        }

        editorHistoryIndex--;
        RestoreEditorState(editorHistory[editorHistoryIndex]);
    }

    private void RedoEditorChange()
    {
        if (editorHistoryIndex + 1 >= editorHistory.Count)
        {
            return;
        }

        editorHistoryIndex++;
        RestoreEditorState(editorHistory[editorHistoryIndex]);
    }

    private void RestoreEditorState(EditorState state)
    {
        isRestoringEditorState = true;
        try
        {
            codeEditor.Text = state.Text;
            codeEditor.Select(
                Math.Min(state.SelectionStart, codeEditor.TextLength),
                Math.Min(state.SelectionLength, Math.Max(0, codeEditor.TextLength - state.SelectionStart)));
            codeEditor.ClearUndo();
        }
        finally
        {
            isRestoringEditorState = false;
        }

        UpdateCodeStatus();
        HighlightVisibleCode();
        HighlightCSource();
        codeMinimap.InvalidateContent();
        lineNumberGutter.Invalidate();
    }

    private void HighlightVisibleCode()
    {
        if (isHighlightingCode || !codeEditor.IsHandleCreated || codeEditor.TextLength == 0 || editorDecorationTimer.Enabled)
        {
            return;
        }

        var firstLine = Math.Max(0, SendMessage(codeEditor.Handle, EM_GETFIRSTVISIBLELINE, IntPtr.Zero, IntPtr.Zero).ToInt32());
        var bottom = new Point(2, Math.Max(0, codeEditor.ClientSize.Height - 1));
        var lastLine = Math.Max(firstLine, codeEditor.GetLineFromCharIndex(codeEditor.GetCharIndexFromPosition(bottom)));
        var firstMissingLine = -1;
        var lastMissingLine = -1;
        for (var line = firstLine; line <= lastLine; line++)
        {
            if (!highlightedLines.Contains(line))
            {
                if (firstMissingLine < 0) firstMissingLine = line;
                lastMissingLine = line;
            }
        }
        if (firstMissingLine >= 0) HighlightCSource(firstMissingLine, lastMissingLine);
    }

    private void HighlightCSource(int? targetLine = null, int? lastLine = null)
    {
        if (isHighlightingCode || !codeEditor.IsHandleCreated)
        {
            return;
        }

        isHighlightingCode = true;
        var selectionStart = codeEditor.SelectionStart;
        var selectionLength = codeEditor.SelectionLength;
        var source = codeEditor.Text;
        var line = targetLine ?? codeEditor.GetLineFromCharIndex(selectionStart);
        highlightRangeStart = codeEditor.GetFirstCharIndexFromLine(line);
        if (highlightRangeStart < 0)
        {
            isHighlightingCode = false;
            return;
        }
        var finalLine = lastLine ?? line;
        var nextLineStart = codeEditor.GetFirstCharIndexFromLine(finalLine + 1);
        highlightRangeEnd = nextLineStart >= 0 ? nextLineStart : source.Length;
        var firstVisibleLine = SendMessage(codeEditor.Handle, EM_GETFIRSTVISIBLELINE, IntPtr.Zero, IntPtr.Zero).ToInt32();

        codeEditor.SuspendLayout();
        SendMessage(codeEditor.Handle, WM_SETREDRAW, IntPtr.Zero, IntPtr.Zero);
        try
        {
            codeEditor.Select(highlightRangeStart, highlightRangeEnd - highlightRangeStart);
            codeEditor.SelectionFont = codeEditor.Font;
            codeEditor.SelectionColor = UiPalette.CodeText;
            codeEditor.SelectionBackColor = codeEditor.BackColor;

            var index = 0;
            while (index < highlightRangeEnd)
            {
                if (source[index] == '/' && index + 1 < source.Length && source[index + 1] == '/')
                {
                    var end = source.IndexOf('\n', index);
                    if (end < 0) end = source.Length;
                    ColorCodeSpan(index, end - index, UiPalette.CodeComment);
                    index = end;
                    continue;
                }

                if (source[index] == '/' && index + 1 < source.Length && source[index + 1] == '*')
                {
                    var end = source.IndexOf("*/", index + 2, StringComparison.Ordinal);
                    end = end < 0 ? source.Length : end + 2;
                    ColorCodeSpan(index, end - index, UiPalette.CodeComment);
                    index = end;
                    continue;
                }

                if ((source[index] == '"') || (source[index] == '\'') || source[index] == '`')
                {
                    var quote = source[index];
                    var end = FindQuotedEnd(source, index, quote);
                    ColorCodeSpan(index, end - index, UiPalette.CodeString);
                    index = end;
                    continue;
                }

                if (source[index] == '#' && IsLineStart(source, index))
                {
                    var directiveEnd = index + 1;
                    while (directiveEnd < source.Length && char.IsWhiteSpace(source[directiveEnd]) && source[directiveEnd] != '\n')
                    {
                        directiveEnd++;
                    }

                    while (directiveEnd < source.Length && (char.IsLetter(source[directiveEnd]) || source[directiveEnd] == '_'))
                    {
                        directiveEnd++;
                    }

                    ColorCodeSpan(index, directiveEnd - index, UiPalette.CodeDirective);
                    index = directiveEnd;
                    continue;
                }

                if (char.IsDigit(source[index]))
                {
                    var end = index + 1;
                    while (end < source.Length && (char.IsLetterOrDigit(source[end]) || source[end] == 'x' || source[end] == 'X' || source[end] == '.'))
                    {
                        end++;
                    }

                    ColorCodeSpan(index, end - index, UiPalette.CodeNumber);
                    index = end;
                    continue;
                }

                if (IsIdentifierStart(source[index]))
                {
                    var end = index + 1;
                    while (end < source.Length && IsIdentifierPart(source[end]))
                    {
                        end++;
                    }

                    var token = source[index..end];
                    if (end > highlightRangeStart)
                    {
                        var color = GetIdentifierColor(source, index, end, token);
                        if (color.HasValue) ColorCodeSpan(index, end - index, color.Value);
                    }

                    index = end;
                    continue;
                }

                index++;
            }

            if (targetLine is null) HighlightMatchingBrace(source, selectionStart, selectionLength);

            var safeSelectionStart = Math.Min(selectionStart, codeEditor.TextLength);
            var safeSelectionLength = Math.Min(selectionLength, codeEditor.TextLength - safeSelectionStart);
            codeEditor.Select(safeSelectionStart, safeSelectionLength);
            codeEditor.SelectionFont = codeEditor.Font;
            if (safeSelectionLength == 0)
            {
                codeEditor.SelectionColor = UiPalette.CodeText;
            }
            SendMessage(codeEditor.Handle, EM_LINESCROLL, IntPtr.Zero, new IntPtr(firstVisibleLine - SendMessage(codeEditor.Handle, EM_GETFIRSTVISIBLELINE, IntPtr.Zero, IntPtr.Zero).ToInt32()));
        }
        finally
        {
            SendMessage(codeEditor.Handle, WM_SETREDRAW, new IntPtr(1), IntPtr.Zero);
            codeEditor.ResumeLayout();
            codeEditor.Invalidate();
            isHighlightingCode = false;
            for (var highlightedLine = line; highlightedLine <= finalLine; highlightedLine++)
            {
                highlightedLines.Add(highlightedLine);
            }
        }
    }

    private void ColorCodeSpan(int start, int length, Color color)
    {
        var from = Math.Max(start, highlightRangeStart);
        var to = Math.Min(start + length, highlightRangeEnd);
        if (to <= from || from < 0 || to > codeEditor.TextLength)
        {
            return;
        }

        codeEditor.Select(from, to - from);
        codeEditor.SelectionColor = color;
    }

    private void HighlightMatchingBrace(string source, int selectionStart, int selectionLength)
    {
        if (selectionLength != 0 || source.Length == 0)
        {
            return;
        }

        var braceIndex = -1;
        if (selectionStart < source.Length && "{}[]()".Contains(source[selectionStart]))
        {
            braceIndex = selectionStart;
        }
        else if (selectionStart > 0 && "{}[]()".Contains(source[selectionStart - 1]))
        {
            braceIndex = selectionStart - 1;
        }

        if (braceIndex < 0)
        {
            return;
        }

        var parseErrors = new List<string>();
        var tokens = TokenizeCSource(source, parseErrors);
        var tokenIndex = tokens.FindIndex(token => token.Start == braceIndex && token.Text.Length == 1);
        if (tokenIndex < 0)
        {
            return;
        }

        var brace = tokens[tokenIndex].Text[0];
        var matching = brace switch
        {
            '(' => ')',
            '[' => ']',
            '{' => '}',
            ')' => '(',
            ']' => '[',
            '}' => '{',
            _ => '\0'
        };
        if (matching == '\0')
        {
            return;
        }

        var matchIndex = -1;
        var opens = "([{".Contains(brace);
        var depth = 0;
        if (opens)
        {
            for (var index = tokenIndex; index < tokens.Count; index++)
            {
                if (tokens[index].Text.Length != 1)
                {
                    continue;
                }

                if (tokens[index].Text[0] == brace)
                {
                    depth++;
                }
                else if (tokens[index].Text[0] == matching && --depth == 0)
                {
                    matchIndex = tokens[index].Start;
                    break;
                }
            }
        }
        else
        {
            for (var index = tokenIndex; index >= 0; index--)
            {
                if (tokens[index].Text.Length != 1)
                {
                    continue;
                }

                if (tokens[index].Text[0] == brace)
                {
                    depth++;
                }
                else if (tokens[index].Text[0] == matching && --depth == 0)
                {
                    matchIndex = tokens[index].Start;
                    break;
                }
            }
        }

        if (matchIndex < 0)
        {
            return;
        }

        var highlight = Color.FromArgb(215, 234, 249);
        codeEditor.Select(braceIndex, 1);
        codeEditor.SelectionBackColor = highlight;
        codeEditor.Select(matchIndex, 1);
        codeEditor.SelectionBackColor = highlight;
    }

    private static Color? GetIdentifierColor(string source, int start, int end, string token)
    {
        if (CKeywords.Contains(token))
        {
            return UiPalette.CodeKeyword;
        }

        if (CTypes.Contains(token))
        {
            return UiPalette.CodeType;
        }

        if (IsIoConstant(token))
        {
            return UiPalette.CodeIo;
        }

        var next = end;
        while (next < source.Length && char.IsWhiteSpace(source[next]))
        {
            next++;
        }

        return next < source.Length && source[next] == '('
            ? UiPalette.CodeFunction
            : null;
    }

    private static bool IsIoConstant(string token)
    {
        return token is "INPUT_1" or "INPUT_2" or "INPUT_3" or "INPUT_4" or "INPUT_5" or "INPUT_6" or "INPUT_7" or "INPUT_8"
            or "RELAY_1" or "RELAY_2" or "RELAY_3" or "RELAY_4"
            or "TRANSISTOR_1" or "TRANSISTOR_2" or "TRANSISTOR_3" or "TRANSISTOR_4"
            or "VOLTAGE_OUTPUT_1" or "CURRENT_OUTPUT_1";
    }

    internal static bool IsMinimapKeyword(string token) => CKeywords.Contains(token) || CTypes.Contains(token);

    internal static bool IsMinimapIoSymbol(string token) => IsIoConstant(token) || token.StartsWith("IO_", StringComparison.Ordinal);

    private static int FindQuotedEnd(string source, int start, char quote)
    {
        var escaped = false;
        for (var index = start + 1; index < source.Length; index++)
        {
            if (escaped)
            {
                escaped = false;
                continue;
            }

            if (source[index] == '\\')
            {
                escaped = true;
                continue;
            }

            if (source[index] == quote)
            {
                return index + 1;
            }
        }

        return source.Length;
    }

    private static bool IsLineStart(string source, int index)
    {
        for (var cursor = index - 1; cursor >= 0 && source[cursor] != '\n'; cursor--)
        {
            if (!char.IsWhiteSpace(source[cursor]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsIdentifierStart(char value) => char.IsLetter(value) || value == '_';

    private static bool IsIdentifierPart(char value) => char.IsLetterOrDigit(value) || value == '_';

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int message, IntPtr wParam, IntPtr lParam);

    private static readonly HashSet<string> CKeywords = new(StringComparer.Ordinal)
    {
        "auto", "break", "case", "char", "const", "continue", "default", "do", "else", "enum",
        "extern", "for", "goto", "if", "inline", "register", "restrict", "return", "sizeof", "static",
        "struct", "switch", "typedef", "union", "volatile", "while", "_Atomic", "_Bool", "_Complex",
        "_Generic", "_Imaginary", "_Noreturn", "_Static_assert", "_Thread_local"
    };

    private static readonly HashSet<string> CTypes = new(StringComparer.Ordinal)
    {
        "void", "bool", "char", "double", "float", "int", "long", "short", "signed", "unsigned",
        "size_t", "uint8_t", "uint16_t", "uint32_t", "uint64_t", "int8_t", "int16_t", "int32_t", "int64_t",
        "IO_InputState", "IO_OutputState"
    };

}
