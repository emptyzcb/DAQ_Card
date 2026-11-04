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
    private Control BuildLogicPage()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = UiPalette.Surface
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var topBar = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = UiPalette.Surface
        };
        topBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        topBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 250));
        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoScroll = false,
            BackColor = UiPalette.Surface,
            Padding = new Padding(10, 10, 0, 8)
        };
        var projectMenu = new ContextMenuStrip();
        projectMenu.Items.Add("新建工程", null, (_, _) => NewProject());
        projectMenu.Items.Add("导入工程", null, (_, _) => ImportProject());
        projectMenu.Items.Add("刷新工程列表", null, (_, _) => RefreshProjectList());
        projectMenu.Items.Add(new ToolStripSeparator());
        projectMenu.Items.Add("重新定位工程", null, (_, _) => RelocateSelectedProject());
        projectMenu.Items.Add("从列表移除", null, (_, _) => RemoveSelectedProject());
        projectMenu.Items.Add("将当前代码另存副本", null, (_, _) => SaveEditorCopy());
        var projectButton = AddLogicButton(toolbar, "工程 ▾", (_, _) => { });
        projectButton.Click += (_, _) => projectMenu.Show(projectButton, 0, projectButton.Height);
        AddLogicButton(toolbar, "保存", (_, _) => SaveCProgram());
        var editMenu = new ContextMenuStrip();
        editMenu.Items.Add("批量注释", null, (_, _) => ApplyLineComments(true));
        editMenu.Items.Add("取消注释", null, (_, _) => ApplyLineComments(false));
        var editButton = AddLogicButton(toolbar, "编辑 ▾", (_, _) => { });
        editButton.Click += (_, _) => editMenu.Show(editButton, 0, editButton.Height);
        toolbar.Controls.Add(new Panel { Width = 1, Height = 24, BackColor = Color.FromArgb(205, 216, 219), Margin = new Padding(8, 4, 9, 0) });
        var compileButton = AddLogicButton(toolbar, "编译", async (_, _) => await CompileCProgramAsync());
        compileButton.FlatAppearance.BorderSize = 1;
        compileButton.FlatAppearance.BorderColor = UiPalette.Blue;
        compileButton.ForeColor = UiPalette.Blue;
        StyleLogicButton(compileOutputToggleButton, "编译结果");
        compileOutputToggleButton.Click += (_, _) => ToggleCompileOutput();
        toolbar.Controls.Add(compileOutputToggleButton);
        StyleLogicButton(downloadLogicButton, "编译并下载");
        downloadLogicButton.Width = 108;
        downloadLogicButton.BackColor = UiPalette.Blue;
        downloadLogicButton.ForeColor = Color.White;
        downloadLogicButton.Click += (_, _) => DownloadCProgramAsync();
        toolbar.Controls.Add(downloadLogicButton);
        logicCompileLabel.Dock = DockStyle.Fill;
        logicCompileLabel.AutoEllipsis = true;
        logicCompileLabel.Text = "联合控制器 v2 · 未编译";
        logicCompileLabel.ForeColor = UiPalette.Muted;
        logicCompileLabel.TextAlign = ContentAlignment.MiddleRight;
        logicCompileLabel.Padding = new Padding(0, 0, 12, 0);
        topBar.Controls.Add(toolbar, 0, 0);
        topBar.Controls.Add(logicCompileLabel, 1, 0);
        root.Controls.Add(topBar, 0, 0);

        var body = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterWidth = 5,
            BackColor = UiPalette.Border,
        };
        body.Panel1.Padding = new Padding(0, 5, 0, 0);
        body.Panel1.Controls.Add(BuildCSourcePanel());

        var editorAndIo = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterWidth = 5,
            BackColor = UiPalette.Border
        };
        editorAndIo.Panel1.Padding = new Padding(0, 5, 0, 0);
        editorAndIo.Panel2.Padding = new Padding(0, 5, 0, 0);
        editorAndIo.Panel1.Controls.Add(BuildCEditorPanel());
        editorAndIo.Panel2.Controls.Add(BuildLiveIoPanel());
        body.Panel2.Controls.Add(editorAndIo);
        Shown += (_, _) =>
        {
            body.Panel1MinSize = 170;
            body.Panel2MinSize = 630;
            editorAndIo.Panel1MinSize = 420;
            editorAndIo.Panel2MinSize = 210;
            SetPreferredSplitterDistance(body, 190);
            SetPreferredSplitterDistance(editorAndIo, 700);
        };
        root.Controls.Add(body, 0, 1);
        return root;
    }

    private static void SetPreferredSplitterDistance(SplitContainer split, int preferredDistance)
    {
        var available = split.Orientation == Orientation.Vertical ? split.Width : split.Height;
        var minimum = split.Panel1MinSize;
        var maximum = available - split.Panel2MinSize - split.SplitterWidth;
        if (maximum >= minimum)
        {
            split.SplitterDistance = Math.Clamp(preferredDistance, minimum, maximum);
        }
    }

    private Control BuildRuleListPanel()
    {
        var group = new GroupBox
        {
            Dock = DockStyle.Fill,
            Text = "控制规则",
            BackColor = Color.White,
            Padding = new Padding(8, 24, 8, 8)
        };
        ruleListBox.Dock = DockStyle.Fill;
        ruleListBox.BorderStyle = BorderStyle.None;
        ruleListBox.IntegralHeight = false;
        ruleListBox.SelectedIndexChanged += (_, _) => LoadSelectedRule();
        group.Controls.Add(ruleListBox);

        var addButton = new Button { Text = "+ 新建规则", Dock = DockStyle.Bottom, Height = 32 };
        addButton.Click += (_, _) => AddNewLogicRule();
        group.Controls.Add(addButton);
        return group;
    }

    private Control BuildRuleEditorPanel()
    {
        var host = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(12) };
        host.Controls.Add(SectionTitle("图形化规则编辑"));

        var editor = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 10,
            Padding = new Padding(8, 48, 8, 8),
            AutoScroll = true,
            BackColor = Color.White
        };
        editor.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112));
        editor.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        ruleNameInput.Dock = DockStyle.Fill;
        ruleNameInput.Text = "新规则";
        AddEditorRow(editor, 0, "规则名称", ruleNameInput);

        ruleEnabledInput.Text = "启用此规则";
        ruleEnabledInput.AutoSize = true;
        ruleEnabledInput.Checked = true;
        AddEditorRow(editor, 1, "状态", ruleEnabledInput);

        ConfigureCombo(ruleConditionModeInput, "电平", "上升沿", "下降沿");
        AddEditorRow(editor, 2, "触发方式", ruleConditionModeInput);

        ConfigureCombo(ruleOperatorInput, "AND", "OR");
        AddEditorRow(editor, 3, "条件组合", ruleOperatorInput);

        ruleInputs.CheckOnClick = true;
        ruleInputs.Height = 52;
        ruleInputs.Items.AddRange(InputNames);
        AddEditorRow(editor, 4, "输入条件", ruleInputs);

        ConfigureCombo(ruleActionInput, "打开", "关闭", "翻转", "脉冲");
        AddEditorRow(editor, 5, "输出动作", ruleActionInput);

        ruleOutputs.CheckOnClick = true;
        ruleOutputs.Height = 52;
        ruleOutputs.Items.AddRange(OutputNames);
        AddEditorRow(editor, 6, "目标输出", ruleOutputs);

        ConfigureNumeric(ruleDelayInput, 0, 65535, 0, 100);
        AddEditorRow(editor, 7, "延时(ms)", ruleDelayInput);

        ConfigureNumeric(rulePulseInput, 0, 65535, 1000, 100);
        AddEditorRow(editor, 8, "脉冲(ms)", rulePulseInput);

        ConfigureNumeric(rulePriorityInput, 0, 255, 100, 100);
        AddEditorRow(editor, 9, "优先级", rulePriorityInput);

        foreach (var control in new Control[]
        {
            ruleNameInput, ruleEnabledInput, ruleConditionModeInput, ruleOperatorInput,
            ruleInputs, ruleActionInput, ruleOutputs, ruleDelayInput, rulePulseInput, rulePriorityInput
        })
        {
            control.Leave += OnRuleEditorChanged;
        }
        ruleInputs.ItemCheck += OnRuleInputChanged;
        ruleOutputs.ItemCheck += OnRuleOutputChanged;

        var canvas = BuildRuleCanvasPanel();
        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            SplitterDistance = 210,
            IsSplitterFixed = false,
            BackColor = Color.FromArgb(224, 230, 236)
        };
        split.Panel1.Controls.Add(canvas);
        split.Panel2.Controls.Add(editor);
        host.Controls.Add(split);
        return host;
    }

    private Control BuildRuleCanvasPanel()
    {
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(247, 249, 251), Padding = new Padding(14, 48, 14, 12) };
        panel.Controls.Add(SectionTitle("逻辑流程预览"));

        var flow = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 116,
            ColumnCount = 5,
            RowCount = 1,
            BackColor = Color.Transparent
        };
        flow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
        flow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 34));
        flow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
        flow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 34));
        flow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
        flow.Controls.Add(CreateFlowNode("输入条件", "INPUT_1 有效\nAND\nINPUT_2 无效", Color.FromArgb(227, 239, 250)), 0, 0);
        flow.Controls.Add(CreateArrowLabel(), 1, 0);
        flow.Controls.Add(CreateFlowNode("延时", "100 ms", Color.FromArgb(255, 243, 214)), 2, 0);
        flow.Controls.Add(CreateArrowLabel(), 3, 0);
        flow.Controls.Add(CreateFlowNode("输出动作", "RELAY_1 打开", Color.FromArgb(222, 244, 232)), 4, 0);
        panel.Controls.Add(flow);

        ruleCanvasLabel.AutoSize = false;
        ruleCanvasLabel.Dock = DockStyle.Top;
        ruleCanvasLabel.Height = 32;
        ruleCanvasLabel.TextAlign = ContentAlignment.MiddleLeft;
        ruleCanvasLabel.ForeColor = Color.FromArgb(86, 98, 110);
        ruleCanvasLabel.Padding = new Padding(4, 4, 4, 0);
        panel.Controls.Add(ruleCanvasLabel);
        return panel;
    }

    private Control BuildLiveIoPanel()
    {
        var host = new Panel { Dock = DockStyle.Fill, BackColor = Color.White };

        var content = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            Padding = new Padding(12, 12, 6, 8),
            BackColor = Color.White
        };
        content.Controls.Add(CreateIoGroup("数字输入 IN1-IN8", inputIndicators, InputDisplayNames));
        content.Controls.Add(CreateIoGroup("数字输出 R1-R4 / T1-T4", outputIndicators, OutputDisplayNames));
        var analogPanel = new Panel
        {
            Width = 204,
            Height = 108,
            BackColor = Color.White,
            Margin = new Padding(0, 0, 0, 12)
        };
        analogPanel.Controls.Add(new Label
        {
            Dock = DockStyle.Top,
            Height = 30,
            Text = "模拟输出",
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold),
            ForeColor = Color.FromArgb(43, 62, 68)
        });
        analogVoltageIndicator.SetBounds(0, 36, 204, 30);
        analogCurrentIndicator.SetBounds(0, 69, 204, 30);
        analogVoltageIndicator.Text = "U1  -- V";
        analogCurrentIndicator.Text = "I1  -- mA";
        analogVoltageIndicator.ForeColor = Color.FromArgb(74, 92, 100);
        analogCurrentIndicator.ForeColor = Color.FromArgb(74, 92, 100);
        analogPanel.Controls.Add(analogVoltageIndicator);
        analogPanel.Controls.Add(analogCurrentIndicator);
        content.Controls.Add(analogPanel);
        host.Controls.Add(content);
        host.Controls.Add(new Label
        {
            Dock = DockStyle.Top,
            Height = 40,
            Text = "板卡 IO",
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(14, 0, 0, 0),
            Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold),
            ForeColor = Color.FromArgb(44, 62, 68),
            BackColor = Color.FromArgb(248, 250, 250)
        });
        return host;
    }

    private Panel CreateIoGroup(string title, Label[] indicators, IReadOnlyList<string> names)
    {
        var group = new Panel
        {
            Width = 204,
            Height = 162,
            Margin = new Padding(0, 0, 0, 12),
            BackColor = Color.White
        };
        var heading = new Label
        {
            Dock = DockStyle.Top,
            Height = 30,
            Text = title,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold),
            ForeColor = Color.FromArgb(43, 62, 68)
        };
        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 4,
            BackColor = Color.White
        };
        for (var index = 0; index < 8; index++)
        {
            var label = new Label
            {
                AutoSize = false,
                Width = 95,
                Height = 28,
                Text = $"{names[index]}  关",
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Color.FromArgb(91, 108, 115),
                BackColor = Color.FromArgb(244, 247, 248),
                Margin = new Padding(2)
            };
            indicators[index] = label;
            grid.Controls.Add(label, index % 2, index / 2);
        }
        group.Controls.Add(grid);
        group.Controls.Add(heading);
        return group;
    }

    private static Label CreateFlowNode(string title, string content, Color color)
    {
        return new Label
        {
            Dock = DockStyle.Fill,
            Text = $"{title}\n\n{content}",
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = color,
            ForeColor = Color.FromArgb(38, 53, 68),
            BorderStyle = BorderStyle.FixedSingle,
            Margin = new Padding(4),
            Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold)
        };
    }

    private static Label CreateArrowLabel()
    {
        return new Label
        {
            Dock = DockStyle.Fill,
            Text = "→",
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Color.FromArgb(91, 110, 126),
            Font = new Font("Microsoft YaHei UI", 16F, FontStyle.Bold)
        };
    }

    private static Button AddLogicButton(FlowLayoutPanel panel, string text, EventHandler handler)
    {
        var button = new Button();
        StyleLogicButton(button, text);
        button.Click += handler;
        panel.Controls.Add(button);
        return button;
    }

    private static void StyleLogicButton(Button button, string text)
    {
        button.Text = text;
        button.Width = Math.Max(72, TextRenderer.MeasureText(text, SystemFonts.MessageBoxFont).Width + 22);
        button.Height = 30;
        button.Margin = new Padding(2, 0, 4, 0);
        UiPalette.StyleButton(button);
    }

    private static void AddEditorRow(TableLayoutPanel table, int row, string label, Control control)
    {
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.Controls.Add(new Label
        {
            Text = label,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Padding = new Padding(0, 5, 0, 0),
            ForeColor = Color.FromArgb(75, 87, 99)
        }, 0, row);
        control.Margin = new Padding(3, 3, 3, 3);
        table.Controls.Add(control, 1, row);
    }

    private static void ConfigureCombo(ComboBox combo, params string[] items)
    {
        combo.DropDownStyle = ComboBoxStyle.DropDownList;
        combo.Items.AddRange(items);
        combo.SelectedIndex = 0;
        combo.Dock = DockStyle.Fill;
    }

    private void SeedLogicRules()
    {
        logicRules.Add(new IoLogicRule
        {
            Name = "启动水泵",
            InputMask = 0x0003,
            InputExpected = 0x0001,
            OutputMask = 0x0001,
            DelayMs = 100,
            Priority = 100
        });
        logicRules.Add(new IoLogicRule
        {
            Name = "报警脉冲",
            ConditionMode = IoRuleConditionMode.RisingEdge,
            InputMask = 0x0004,
            InputExpected = 0x0004,
            OutputMask = 0x0010,
            Action = IoRuleAction.Pulse,
            PulseMs = 1000,
            Priority = 200
        });
        logicRules.Add(new IoLogicRule
        {
            Name = "停止水泵",
            InputMask = 0x0008,
            InputExpected = 0x0008,
            OutputMask = 0x0001,
            Action = IoRuleAction.Reset,
            Priority = 220
        });

        ruleListBox.Items.Clear();
        foreach (var rule in logicRules)
        {
            ruleListBox.Items.Add(rule);
        }

        if (logicRules.Count > 0)
        {
            ruleListBox.SelectedIndex = 0;
        }

        SetAllIndicators(0, 0);
    }

    private void AddNewLogicRule()
    {
        var rule = new IoLogicRule { Name = $"新规则 {logicRules.Count + 1}" };
        logicRules.Add(rule);
        ruleListBox.Items.Add(rule);
        ruleListBox.SelectedIndex = ruleListBox.Items.Count - 1;
        OnClientLog($"{DateTime.Now:HH:mm:ss.fff}  新建规则：{rule.Name}");
    }

    private void LoadSelectedRule()
    {
        if (ruleListBox.SelectedIndex < 0 || ruleListBox.SelectedIndex >= logicRules.Count)
        {
            selectedRuleIndex = -1;
            return;
        }

        selectedRuleIndex = -1;
        var rule = logicRules[ruleListBox.SelectedIndex];
        ruleNameInput.Text = rule.Name;
        ruleEnabledInput.Checked = rule.Enabled;
        ruleConditionModeInput.SelectedIndex = (int)rule.ConditionMode;
        ruleOperatorInput.SelectedIndex = (int)rule.Operator;
        ruleActionInput.SelectedIndex = (int)rule.Action;
        rulePriorityInput.Value = rule.Priority;
        ruleDelayInput.Value = rule.DelayMs;
        rulePulseInput.Value = rule.PulseMs;
        SetCheckedMask(ruleInputs, rule.InputMask);
        SetCheckedMask(ruleOutputs, rule.OutputMask);
        selectedRuleIndex = ruleListBox.SelectedIndex;
        RefreshRuleCanvas(rule);
    }

    private void ApplyEditorToRule()
    {
        if (selectedRuleIndex < 0 || selectedRuleIndex >= logicRules.Count)
        {
            return;
        }

        var rule = logicRules[selectedRuleIndex];
        rule.Name = string.IsNullOrWhiteSpace(ruleNameInput.Text) ? "未命名规则" : ruleNameInput.Text.Trim();
        rule.Enabled = ruleEnabledInput.Checked;
        rule.ConditionMode = (IoRuleConditionMode)Math.Max(0, ruleConditionModeInput.SelectedIndex);
        rule.Operator = (IoRuleOperator)Math.Max(0, ruleOperatorInput.SelectedIndex);
        rule.Action = (IoRuleAction)Math.Max(0, ruleActionInput.SelectedIndex);
        rule.Priority = (int)rulePriorityInput.Value;
        rule.DelayMs = (int)ruleDelayInput.Value;
        rule.PulseMs = (int)rulePulseInput.Value;
        rule.InputMask = GetCheckedMask(ruleInputs);
        rule.InputExpected = rule.InputMask;
        rule.OutputMask = GetCheckedMask(ruleOutputs);
        ruleListBox.Items[selectedRuleIndex] = rule;
        ruleListBox.SelectedIndex = selectedRuleIndex;
        RefreshRuleCanvas(rule);
    }

    private void OnRuleEditorChanged(object? sender, EventArgs e)
    {
        ApplyEditorToRule();
    }

    private void OnRuleInputChanged(object? sender, ItemCheckEventArgs e)
    {
        if (IsHandleCreated)
        {
            BeginInvoke((Action)ApplyEditorToRule);
        }
    }

    private void OnRuleOutputChanged(object? sender, ItemCheckEventArgs e)
    {
        if (IsHandleCreated)
        {
            BeginInvoke((Action)ApplyEditorToRule);
        }
    }

    private static ushort GetCheckedMask(CheckedListBox list)
    {
        ushort mask = 0;
        for (var index = 0; index < list.Items.Count && index < 16; index++)
        {
            if (list.GetItemChecked(index))
            {
                mask |= (ushort)(1 << index);
            }
        }

        return mask;
    }

    private static void SetCheckedMask(CheckedListBox list, ushort mask)
    {
        for (var index = 0; index < list.Items.Count; index++)
        {
            list.SetItemChecked(index, (mask & (1 << index)) != 0);
        }
    }

    private void RefreshRuleCanvas(IoLogicRule rule)
    {
        var inputs = DescribeMask(rule.InputMask, "X");
        var outputs = DescribeMask(rule.OutputMask, "Q");
        var mode = rule.ConditionMode switch
        {
            IoRuleConditionMode.RisingEdge => "上升沿",
            IoRuleConditionMode.FallingEdge => "下降沿",
            _ => "电平"
        };
        var action = rule.Action switch
        {
            IoRuleAction.Reset => "关闭",
            IoRuleAction.Toggle => "翻转",
            IoRuleAction.Pulse => $"脉冲 {rule.PulseMs} ms",
            _ => "打开"
        };
        ruleCanvasLabel.Text = $"{inputs} · {mode}  →  延时 {rule.DelayMs} ms  →  {outputs} {action}  · 优先级 {rule.Priority}";
    }

    private static string DescribeMask(ushort mask, string prefix)
    {
        var values = Enumerable.Range(0, 8)
            .Where(index => (mask & (1 << index)) != 0)
            .Select(index => $"{prefix}{index + 1}")
            .ToArray();
        return values.Length == 0 ? "未选择" : string.Join(" + ", values);
    }

    private void SaveLogicProject()
    {
        ApplyEditorToRule();
        var errors = ValidateLogicProject();
        if (errors.Count > 0)
        {
            logicCompileLabel.Text = $"保存失败：{errors[0]}";
            OnClientLog($"{DateTime.Now:HH:mm:ss.fff}  ERROR  {errors[0]}");
            return;
        }

        var folder = Path.Combine(AppContext.BaseDirectory, "Projects");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "IOLogicPreview.iojson");
        var project = new
        {
            projectName = "现场 IO 控制工程",
            slaveAddress = (int)slaveAddressInput.Value,
            scanPeriodMs = 10,
            rules = logicRules
        };
        var json = JsonSerializer.Serialize(project, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
        logicCompileLabel.Text = "工程已保存";
        OnClientLog($"{DateTime.Now:HH:mm:ss.fff}  IO工程已保存：{path}");
    }

    private void CompileLogicProject()
    {
        ApplyEditorToRule();
        var errors = ValidateLogicProject();
        if (errors.Count > 0)
        {
            logicCompileLabel.Text = $"编译失败：{errors.Count} 个错误";
            foreach (var error in errors)
            {
                OnClientLog($"{DateTime.Now:HH:mm:ss.fff}  ERROR  {error}");
            }
            return;
        }

        logicCompileLabel.Text = $"编译通过：{logicRules.Count} 条规则 · 约 {32 + logicRules.Count * 20} 字节";
        OnClientLog($"{DateTime.Now:HH:mm:ss.fff}  IO逻辑编译通过，等待下载协议接入");
    }

    private void PreviewLogicDownload()
    {
        if (!client.IsOpen)
        {
            SetStatus("请先连接以太网");
            return;
        }

        CompileLogicProject();
        OnClientLog($"{DateTime.Now:HH:mm:ss.fff}  预览：将通过 0x41 BEGIN/DATA/VERIFY/ACTIVATE 下载 IO 规则");
        SetStatus("IO逻辑下载预览完成，MCU协议尚未启用");
    }

    private List<string> ValidateLogicProject()
    {
        var errors = new List<string>();
        if (logicRules.Count == 0)
        {
            errors.Add("至少需要一条规则");
        }

        if (logicRules.Count > 32)
        {
            errors.Add("规则数量超过 32 条");
        }

        foreach (var rule in logicRules)
        {
            if (rule.InputMask == 0)
            {
                errors.Add($"规则“{rule.Name}”没有输入条件");
            }

            if (rule.OutputMask == 0)
            {
                errors.Add($"规则“{rule.Name}”没有目标输出");
            }

            if (rule.OutputMask > 0x00FF)
            {
                errors.Add($"规则“{rule.Name}”使用了无效输出位");
            }
        }

        return errors;
    }

    private void SetAllIndicators(ushort inputMask, ushort outputMask)
    {
        for (var index = 0; index < 8; index++)
        {
            SetIndicator(inputIndicators[index], InputDisplayNames[index], (inputMask & (1 << index)) != 0);
            SetIndicator(outputIndicators[index], OutputDisplayNames[index], (outputMask & (1 << index)) != 0);
        }
    }

    private static void SetIndicator(Label label, string name, bool active)
    {
        var text = $"{name}  {(active ? "开" : "关")}";
        if (label.Text == text) return;
        label.Text = text;
        label.ForeColor = active ? Color.FromArgb(0, 108, 83) : Color.FromArgb(91, 108, 115);
        label.BackColor = active ? Color.FromArgb(221, 244, 234) : Color.FromArgb(244, 247, 248);
    }

}
