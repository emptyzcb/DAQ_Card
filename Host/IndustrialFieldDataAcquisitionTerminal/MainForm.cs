using System.Diagnostics;
using System.ComponentModel;
using System.IO.Ports;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace IndustrialFieldDataAcquisitionTerminal;

internal sealed class MainForm : Form
{
    private readonly ModbusRtuClient client = new();
    private readonly ComboBox portComboBox = new();
    private readonly NumericUpDown slaveAddressInput = new();
    private readonly NumericUpDown pollIntervalInput = new();
    private readonly NumericUpDown outputMaskInput = new();
    private readonly Button connectButton = new();
    private readonly Button refreshPortsButton = new();
    private readonly Button pollNowButton = new();
    private readonly Button writeOutputButton = new();
    private readonly Button clearLogButton = new();
    private readonly Label connectionLabel = new();
    private readonly Label lastPollLabel = new();
    private readonly DataGridView dataGrid = new();
    private readonly RichTextBox logBox = new();
    private readonly RichTextBox codeEditor = new();
    private readonly Panel codeEditorHost = new();
    private readonly CodeLineNumberGutter lineNumberGutter = new();
    private readonly TreeView sourceTreeView = new();
    private readonly Label codeStatusLabel = new();
    private readonly Label codeEditorTitle = new();
    private readonly SplitContainer compileSplit = new();
    private readonly RichTextBox compileOutputBox = new();
    private readonly ListBox ruleListBox = new();
    private readonly TextBox ruleNameInput = new();
    private readonly CheckBox ruleEnabledInput = new();
    private readonly ComboBox ruleConditionModeInput = new();
    private readonly ComboBox ruleOperatorInput = new();
    private readonly ComboBox ruleActionInput = new();
    private readonly NumericUpDown rulePriorityInput = new();
    private readonly NumericUpDown ruleDelayInput = new();
    private readonly NumericUpDown rulePulseInput = new();
    private readonly CheckedListBox ruleInputs = new();
    private readonly CheckedListBox ruleOutputs = new();
    private readonly Label ruleCanvasLabel = new();
    private readonly Label logicCompileLabel = new();
    private readonly Label[] inputIndicators = new Label[8];
    private readonly Label[] outputIndicators = new Label[8];
    private readonly List<IoLogicRule> logicRules = new();
    private readonly Button downloadLogicButton = new();
    private int selectedRuleIndex = -1;
    private readonly StatusStrip statusStrip = new();
    private readonly ToolStripStatusLabel statusLabel = new();
    private readonly Dictionary<string, int> rowIndexes = new();
    private readonly SemaphoreSlim pollGate = new(1, 1);
    private CancellationTokenSource? pollCancellation;
    private Task? pollTask;
    private int pollIntervalMs = 250;
    private bool isHighlightingCode;
    private bool isCompiling;
    private bool commentChordPending;
    private bool isRestoringEditorState;
    private readonly List<EditorState> editorHistory = new();
    private int editorHistoryIndex = -1;
    private static readonly bool EnableRealtimePolling = false;
    private readonly Panel findBar = new();
    private readonly TextBox findInput = new();
    private readonly Label findResultLabel = new();
    private string currentProjectName = "DAQ_card";
    private string currentUserSourcePath = string.Empty;
    private IoLogicCompiledImage? lastCompiledImage;
    private const int WM_SETREDRAW = 0x000B;
    private const int EM_GETFIRSTVISIBLELINE = 0x00CE;
    private const int EM_LINESCROLL = 0x00B6;
    private static readonly string[] InputNames =
    {
        "INPUT_1", "INPUT_2", "INPUT_3", "INPUT_4",
        "INPUT_5", "INPUT_6", "INPUT_7", "INPUT_8"
    };
    private static readonly string[] OutputNames =
    {
        "RELAY_1", "RELAY_2", "RELAY_3", "RELAY_4",
        "TRANSISTOR_1", "TRANSISTOR_2", "TRANSISTOR_3", "TRANSISTOR_4"
    };
    private static readonly string[] InputDisplayNames =
    {
        "IN1", "IN2", "IN3", "IN4", "IN5", "IN6", "IN7", "IN8"
    };
    private static readonly string[] OutputDisplayNames =
    {
        "R1", "R2", "R3", "R4", "T1", "T2", "T3", "T4"
    };

    public MainForm()
    {
        Text = "工业现场数据采集终端";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1020, 650);
        Size = new Size(1240, 780);
        Font = new Font("Microsoft YaHei UI", 9F);
        BackColor = Color.FromArgb(242, 245, 248);

        BuildUi();
        SeedCProgram();
        client.LogMessage += OnClientLog;
        pollIntervalInput.ValueChanged += (_, _) => pollIntervalMs = (int)pollIntervalInput.Value;
        pollIntervalMs = (int)pollIntervalInput.Value;
        FormClosing += OnFormClosing;
        RefreshPorts();
        SetConnectedUi(false);
        SetStatus("未连接");
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = BackColor,
            Padding = new Padding(0)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 104));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        root.Controls.Add(BuildHeader(), 0, 0);
        root.Controls.Add(BuildStatusBar(), 0, 1);
        root.Controls.Add(BuildContent(), 0, 2);
        Controls.Add(root);
    }

    private Control BuildHeader()
    {
        var header = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(25, 53, 80),
            Padding = new Padding(18, 10, 18, 8)
        };

        var title = new Label
        {
            AutoSize = true,
            Text = "工业现场数据采集终端",
            ForeColor = Color.White,
            Font = new Font("Microsoft YaHei UI", 16F, FontStyle.Bold),
            Location = new Point(18, 8)
        };
        header.Controls.Add(title);

        var subtitle = new Label
        {
            AutoSize = true,
            Text = "Modbus RTU · RS485 · 采集板卡从机监视",
            ForeColor = Color.FromArgb(190, 211, 228),
            Location = new Point(20, 38)
        };
        header.Controls.Add(subtitle);

        var bar = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 42,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = Color.Transparent,
            Padding = new Padding(0, 5, 0, 0)
        };

        AddLabel(bar, "串口");
        portComboBox.Width = 105;
        portComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        bar.Controls.Add(portComboBox);

        refreshPortsButton.Text = "刷新";
        refreshPortsButton.Width = 58;
        refreshPortsButton.Click += (_, _) => RefreshPorts();
        bar.Controls.Add(refreshPortsButton);

        AddLabel(bar, "波特率");
        var baudLabel = new Label
        {
            AutoSize = false,
            Width = 66,
            Height = 28,
            Text = "115200",
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Color.White,
            BackColor = Color.FromArgb(45, 76, 105),
            Margin = new Padding(3, 0, 10, 0)
        };
        bar.Controls.Add(baudLabel);

        AddLabel(bar, "从机地址");
        ConfigureNumeric(slaveAddressInput, 1, 247, 1, 55);
        bar.Controls.Add(slaveAddressInput);

        AddLabel(bar, "轮询(ms)");
        ConfigureNumeric(pollIntervalInput, 100, 60000, 250, 70);
        bar.Controls.Add(pollIntervalInput);

        connectButton.Text = "连接串口";
        connectButton.Width = 94;
        connectButton.BackColor = Color.FromArgb(36, 166, 114);
        connectButton.ForeColor = Color.White;
        connectButton.FlatStyle = FlatStyle.Flat;
        connectButton.FlatAppearance.BorderSize = 0;
        connectButton.Click += OnConnectClicked;
        bar.Controls.Add(connectButton);

        pollNowButton.Text = "立即采集";
        pollNowButton.Width = 84;
        pollNowButton.Click += OnPollNowClicked;
        bar.Controls.Add(pollNowButton);

        header.Controls.Add(bar);
        return header;
    }

    private Control BuildStatusBar()
    {
        statusStrip.Dock = DockStyle.Fill;
        statusStrip.SizingGrip = false;
        statusStrip.BackColor = Color.White;
        statusLabel.Text = "未连接";
        statusLabel.ForeColor = Color.FromArgb(96, 106, 118);
        statusStrip.Items.Add(statusLabel);
        return statusStrip;
    }

    private Control BuildContent()
    {
        var tabs = new TabControl
        {
            Dock = DockStyle.Fill,
            Padding = new Point(14, 6)
        };

        var monitorPage = new TabPage("实时监视") { BackColor = BackColor, Padding = new Padding(12, 10, 12, 12) };
        var monitorSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterWidth = 6,
            BackColor = Color.FromArgb(224, 230, 236)
        };
        monitorSplit.Panel1.Controls.Add(BuildDataPanel());
        monitorSplit.Panel2.Controls.Add(BuildLogPanel());
        monitorPage.Controls.Add(monitorSplit);
        Shown += (_, _) =>
        {
            monitorSplit.Panel1MinSize = 420;
            monitorSplit.Panel2MinSize = 240;
            SetPreferredSplitterDistance(monitorSplit, 690);
        };

        var logicPage = new TabPage("IO逻辑编辑") { BackColor = BackColor, Padding = new Padding(12, 10, 12, 12) };
        logicPage.Controls.Add(BuildLogicPage());

        tabs.TabPages.Add(monitorPage);
        tabs.TabPages.Add(logicPage);
        tabs.SelectedIndex = 1;
        return tabs;
    }

    private Control BuildLogicPage()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = BackColor
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = Color.White,
            Padding = new Padding(10, 8, 10, 6)
        };
        AddLogicButton(toolbar, "新建工程", (_, _) => NewProject());
        AddLogicButton(toolbar, "删除工程", (_, _) => DeleteSelectedProject());
        AddLogicButton(toolbar, "保存 C 文件", (_, _) => SaveCProgram());
        AddLogicButton(toolbar, "编译", async (_, _) => await CompileCProgramAsync());
        AddLogicButton(toolbar, "导入 C 文件", (_, _) => ImportCProgram());
        AddLogicButton(toolbar, "批量注释", (_, _) => ApplyLineComments(true));
        AddLogicButton(toolbar, "取消注释", (_, _) => ApplyLineComments(false));
        downloadLogicButton.Text = "编译并下载";
        downloadLogicButton.Width = 126;
        downloadLogicButton.Click += (_, _) => DownloadCProgramAsync();
        toolbar.Controls.Add(downloadLogicButton);
        logicCompileLabel.AutoSize = true;
        logicCompileLabel.Text = "C逻辑程序：未检查";
        logicCompileLabel.ForeColor = Color.FromArgb(96, 106, 118);
        logicCompileLabel.Margin = new Padding(14, 5, 0, 0);
        toolbar.Controls.Add(logicCompileLabel);
        root.Controls.Add(toolbar, 0, 0);

        var body = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterWidth = 7,
            BackColor = Color.FromArgb(224, 230, 236),
        };
        body.Panel1.Padding = new Padding(0, 10, 5, 0);
        body.Panel1.Controls.Add(BuildCSourcePanel());

        var editorAndIo = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterWidth = 7,
            BackColor = Color.FromArgb(224, 230, 236)
        };
        editorAndIo.Panel1.Padding = new Padding(5, 10, 5, 0);
        editorAndIo.Panel2.Padding = new Padding(5, 10, 0, 0);
        editorAndIo.Panel1.Controls.Add(BuildCEditorPanel());
        editorAndIo.Panel2.Controls.Add(BuildLiveIoPanel());
        body.Panel2.Controls.Add(editorAndIo);
        Shown += (_, _) =>
        {
            body.Panel1MinSize = 180;
            body.Panel2MinSize = 680;
            editorAndIo.Panel1MinSize = 420;
            editorAndIo.Panel2MinSize = 240;
            SetPreferredSplitterDistance(body, 220);
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

    private Control BuildCSourcePanel()
    {
        var group = new GroupBox
        {
            Dock = DockStyle.Fill,
            Text = "C逻辑工程",
            BackColor = Color.White,
            Padding = new Padding(8, 24, 8, 8)
        };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Color.White
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        layout.Controls.Add(new Label
        {
            Text = "项目文件",
            Dock = DockStyle.Fill,
            ForeColor = Color.FromArgb(93, 105, 118),
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);

        sourceTreeView.Dock = DockStyle.Fill;
        sourceTreeView.BorderStyle = BorderStyle.None;
        sourceTreeView.HideSelection = false;
        sourceTreeView.ShowLines = true;
        sourceTreeView.ShowPlusMinus = true;
        sourceTreeView.ShowRootLines = true;
        sourceTreeView.FullRowSelect = true;
        sourceTreeView.AfterSelect += (_, _) => LoadCProgramFromSelection();
        sourceTreeView.NodeMouseClick += OnSourceTreeNodeMouseClick;
        sourceTreeView.KeyDown += OnSourceTreeKeyDown;
        var treeMenu = new ContextMenuStrip();
        treeMenu.Items.Add("删除工程", null, (_, _) => DeleteSelectedProject());
        sourceTreeView.ContextMenuStrip = treeMenu;

        var projectsRoot = Path.Combine(AppContext.BaseDirectory, "Projects");
        EnsureProjectsRoot(projectsRoot);
        sourceTreeView.Nodes.Clear();
        TreeNode? defaultSourceNode = null;
        var projectRoots = Directory.EnumerateDirectories(projectsRoot)
            .Concat(LoadRegisteredProjectRoots())
            .Where(Directory.Exists)
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase);
        foreach (var projectRoot in projectRoots)
        {
            var projectName = Path.GetFileName(projectRoot);
            var sourcePath = Path.Combine(projectRoot, "io_logic_user.c");
            if (File.Exists(sourcePath))
            {
                EnsureProjectLibrary(projectRoot);
                var sourceNode = AddProjectTreeNode(projectName, projectRoot);
                if (string.Equals(projectName, "DAQ_card", StringComparison.OrdinalIgnoreCase))
                {
                    defaultSourceNode = sourceNode;
                }
            }
        }
        defaultSourceNode ??= sourceTreeView.Nodes.Count > 0
            ? sourceTreeView.Nodes[0].Nodes[0]
            : null;
        if (defaultSourceNode?.Tag is SourceFileEntry defaultEntry)
        {
            currentProjectName = defaultEntry.ProjectName;
            currentUserSourcePath = defaultEntry.FilePath;
            sourceTreeView.SelectedNode = defaultSourceNode;
        }
        layout.Controls.Add(sourceTreeView, 0, 1);
        group.Controls.Add(layout);
        return group;
    }

    private TreeNode AddProjectTreeNode(string projectName, string projectRoot)
    {
        var projectNode = new TreeNode(projectName);
        var libraryNode = new TreeNode("Library");
        var libraryFileNode = new TreeNode("io_logic_library.c")
        {
            Tag = new SourceFileEntry(
                Path.Combine(projectRoot, "Library", "io_logic_library.c"),
                true,
                projectName,
                projectRoot)
        };
        var sourceNode = new TreeNode("io_logic_user.c")
        {
            Tag = new SourceFileEntry(
                Path.Combine(projectRoot, "io_logic_user.c"),
                false,
                projectName,
                projectRoot)
        };
        libraryNode.Nodes.Add(libraryFileNode);
        projectNode.Nodes.Add(libraryNode);
        projectNode.Nodes.Add(sourceNode);
        sourceTreeView.Nodes.Add(projectNode);
        projectNode.Expand();
        return sourceNode;
    }

    private void EnsureProjectsRoot(string projectsRoot)
    {
        Directory.CreateDirectory(projectsRoot);

        var defaultProjectRoot = Path.Combine(projectsRoot, "DAQ_card");
        Directory.CreateDirectory(defaultProjectRoot);
        var legacySourcePath = Path.Combine(projectsRoot, "io_logic_user.c");
        var defaultSourcePath = Path.Combine(defaultProjectRoot, "io_logic_user.c");
        if (!File.Exists(defaultSourcePath))
        {
            if (File.Exists(legacySourcePath))
            {
                File.Copy(legacySourcePath, defaultSourcePath);
            }
            else
            {
                File.WriteAllText(defaultSourcePath, DefaultCProgram);
            }
        }

        EnsureProjectLibrary(defaultProjectRoot);
        EnsureProjectMetadata(defaultProjectRoot, "DAQ_card");
    }

    private List<string> LoadRegisteredProjectRoots()
    {
        var registryPath = Path.Combine(AppContext.BaseDirectory, "Projects", "project_locations.json");
        if (!File.Exists(registryPath))
        {
            return new List<string>();
        }

        try
        {
            return JsonSerializer.Deserialize<List<string>>(File.ReadAllText(registryPath)) ?? new List<string>();
        }
        catch
        {
            return new List<string>();
        }
    }

    private void RegisterProjectRoot(string projectRoot)
    {
        var projectsRoot = Path.Combine(AppContext.BaseDirectory, "Projects");
        Directory.CreateDirectory(projectsRoot);
        var roots = LoadRegisteredProjectRoots()
            .Concat(new[] { Path.GetFullPath(projectRoot) })
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
        File.WriteAllText(
            Path.Combine(projectsRoot, "project_locations.json"),
            JsonSerializer.Serialize(roots, new JsonSerializerOptions { WriteIndented = true }));
    }

    private void UnregisterProjectRoot(string projectRoot)
    {
        var normalized = Path.GetFullPath(projectRoot);
        var roots = LoadRegisteredProjectRoots()
            .Where(path => !string.Equals(Path.GetFullPath(path), normalized, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var registryPath = Path.Combine(AppContext.BaseDirectory, "Projects", "project_locations.json");
        if (roots.Count == 0)
        {
            File.Delete(registryPath);
            return;
        }

        File.WriteAllText(
            registryPath,
            JsonSerializer.Serialize(roots, new JsonSerializerOptions { WriteIndented = true }));
    }

    private void EnsureProjectLibrary(string projectRoot)
    {
        var libraryDirectory = Path.Combine(projectRoot, "Library");
        var projectLibraryPath = Path.Combine(libraryDirectory, "io_logic_library.c");
        Directory.CreateDirectory(libraryDirectory);
        if (File.Exists(projectLibraryPath))
        {
            return;
        }

        var templatePath = Path.Combine(AppContext.BaseDirectory, "Library", "io_logic_library.c");
        if (File.Exists(templatePath) && !string.Equals(Path.GetFullPath(templatePath), Path.GetFullPath(projectLibraryPath), StringComparison.OrdinalIgnoreCase))
        {
            File.Copy(templatePath, projectLibraryPath);
        }
    }

    private static void EnsureProjectMetadata(string projectRoot, string projectName)
    {
        var metadataPath = Path.Combine(projectRoot, "project.json");
        if (File.Exists(metadataPath))
        {
            return;
        }

        File.WriteAllText(
            metadataPath,
            JsonSerializer.Serialize(new
            {
                projectName,
                sourceFile = "io_logic_user.c",
                libraryFile = "Library/io_logic_library.c"
            }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private Control BuildCEditorPanel()
    {
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(22, 29, 37), Padding = new Padding(10) };
        codeEditorTitle.Text = "C 语言 IO 控制程序";
        codeEditorTitle.Dock = DockStyle.Top;
        codeEditorTitle.Height = 42;
        codeEditorTitle.TextAlign = ContentAlignment.MiddleLeft;
        codeEditorTitle.Padding = new Padding(14, 0, 0, 0);
        codeEditorTitle.Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold);
        codeEditorTitle.ForeColor = Color.White;
        codeEditorTitle.BackColor = Color.FromArgb(35, 45, 56);
        panel.Controls.Add(codeEditorTitle);

        BuildFindBar();
        panel.Controls.Add(findBar);

        codeEditor.Dock = DockStyle.Fill;
        codeEditor.BorderStyle = BorderStyle.None;
        codeEditor.BackColor = Color.FromArgb(22, 29, 37);
        codeEditor.ForeColor = Color.FromArgb(221, 231, 239);
        codeEditor.Font = new Font("Cascadia Mono", 10F, FontStyle.Regular);
        codeEditor.AcceptsTab = true;
        codeEditor.WordWrap = false;
        codeEditor.ScrollBars = RichTextBoxScrollBars.Both;
        codeEditor.DetectUrls = false;
        codeEditor.ShortcutsEnabled = true;
        codeEditor.TextChanged += (_, _) =>
        {
            RecordEditorState();
            UpdateCodeStatus();
            HighlightCSource();
            lineNumberGutter.Invalidate();
        };
        codeEditor.KeyDown += OnCodeEditorKeyDown;
        codeEditor.MouseDown += OnCodeEditorMouseDown;
        codeEditor.VScroll += (_, _) => lineNumberGutter.Invalidate();
        codeEditor.Resize += (_, _) => lineNumberGutter.Invalidate();
        compileSplit.Dock = DockStyle.Fill;
        compileSplit.Orientation = Orientation.Horizontal;
        compileSplit.SplitterWidth = 6;

        codeEditorHost.Dock = DockStyle.Fill;
        codeEditorHost.BackColor = codeEditor.BackColor;
        lineNumberGutter.Dock = DockStyle.Left;
        lineNumberGutter.Width = 48;
        lineNumberGutter.Editor = codeEditor;
        codeEditorHost.Controls.Add(codeEditor);
        codeEditorHost.Controls.Add(lineNumberGutter);
        compileSplit.Panel1.Controls.Add(codeEditorHost);

        compileOutputBox.Dock = DockStyle.Fill;
        compileOutputBox.ReadOnly = true;
        compileOutputBox.BorderStyle = BorderStyle.None;
        compileOutputBox.BackColor = Color.FromArgb(15, 20, 25);
        compileOutputBox.ForeColor = Color.FromArgb(190, 205, 215);
        compileOutputBox.Font = new Font("Cascadia Mono", 9F, FontStyle.Regular);
        compileOutputBox.WordWrap = false;
        compileOutputBox.ScrollBars = RichTextBoxScrollBars.Both;
        compileSplit.Panel2.Controls.Add(compileOutputBox);
        compileSplit.Panel2Collapsed = true;
        panel.Controls.Add(compileSplit);

        codeStatusLabel.Dock = DockStyle.Bottom;
        codeStatusLabel.Height = 26;
        codeStatusLabel.Text = "行 1 · 字符 0 · 未检查";
        codeStatusLabel.ForeColor = Color.FromArgb(166, 183, 197);
        codeStatusLabel.TextAlign = ContentAlignment.MiddleLeft;
        codeStatusLabel.Padding = new Padding(4, 0, 0, 0);
        panel.Controls.Add(codeStatusLabel);
        Shown += (_, _) =>
        {
            compileSplit.Panel1MinSize = 220;
            compileSplit.Panel2MinSize = 120;
            compileSplit.SplitterDistance = Math.Max(220, compileSplit.Height - 170);
        };
        return panel;
    }

    private void BuildFindBar()
    {
        findBar.Dock = DockStyle.Top;
        findBar.Height = 38;
        findBar.BackColor = Color.FromArgb(31, 40, 50);
        findBar.Padding = new Padding(8, 5, 8, 5);
        findBar.Visible = false;

        var findLabel = new Label
        {
            Text = "查找",
            AutoSize = true,
            ForeColor = Color.FromArgb(190, 205, 215),
            Dock = DockStyle.Left,
            Padding = new Padding(0, 5, 8, 0)
        };
        findInput.BorderStyle = BorderStyle.FixedSingle;
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
        findResultLabel.ForeColor = Color.FromArgb(166, 183, 197);
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
        var host = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(10) };
        host.Controls.Add(SectionTitle("实时 IO 状态"));

        var content = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            Padding = new Padding(8, 50, 8, 8),
            BackColor = Color.White
        };
        content.Controls.Add(CreateIoGroup("输入 IN1-IN8", inputIndicators, InputDisplayNames));
        content.Controls.Add(CreateIoGroup("输出 R1-R4 / T1-T4", outputIndicators, OutputDisplayNames));
        var hint = new Label
        {
            AutoSize = false,
            Width = 200,
            Height = 54,
            Text = "刷新周期：250 ms\n未连接时显示为灰色",
            ForeColor = Color.FromArgb(106, 117, 128),
            Padding = new Padding(4, 8, 4, 4)
        };
        content.Controls.Add(hint);
        host.Controls.Add(content);
        return host;
    }

    private GroupBox CreateIoGroup(string title, Label[] indicators, IReadOnlyList<string> names)
    {
        var group = new GroupBox
        {
            Text = title,
            Width = 204,
            Height = 170,
            Padding = new Padding(8, 22, 8, 6),
            BackColor = Color.White
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
                Width = 86,
                Height = 28,
                Text = $"{names[index]}  关闭",
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Color.FromArgb(110, 120, 130),
                BackColor = Color.FromArgb(235, 239, 243),
                Margin = new Padding(2)
            };
            indicators[index] = label;
            grid.Controls.Add(label, index % 2, index / 2);
        }
        group.Controls.Add(grid);
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

    private static void AddLogicButton(FlowLayoutPanel panel, string text, EventHandler handler)
    {
        var button = new Button { Text = text, Width = 88, Height = 28, Margin = new Padding(2, 0, 4, 0) };
        button.Click += handler;
        panel.Controls.Add(button);
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

    private const string DefaultCProgram = """
/* API is supplied by the fixed io_logic_library.c file. */

// Called every 10 ms by the MCU IO logic task.
void IO_Logic_Run(void)
{
    // INPUT_1 active and INPUT_2 inactive: energize RELAY_1.
    if (IO_INPUT_ACTIVE(INPUT_1) && !IO_INPUT_ACTIVE(INPUT_2))
    {
        IO_OUTPUT_SET(RELAY_1);
    }
    else
    {
        IO_OUTPUT_RESET(RELAY_1);
    }

    // A rising edge on INPUT_3 generates a 1000 ms pulse on TRANSISTOR_1.
    if (IO_INPUT_RISING_EDGE(INPUT_3))
    {
        IO_OUTPUT_PULSE(TRANSISTOR_1, 1000);
    }
}
""";

    private void SeedCProgram()
    {
        SetEditorText(DefaultCProgram);
        UpdateCodeStatus();
        SetAllIndicators(0, 0);
    }

    private void LoadCProgramFromSelection()
    {
        if (sourceTreeView.SelectedNode?.Tag is not SourceFileEntry file)
        {
            return;
        }

        if (file.IsLibrary)
        {
            codeEditor.ReadOnly = true;
            codeEditor.BackColor = Color.FromArgb(28, 36, 45);
            codeEditorHost.BackColor = codeEditor.BackColor;
            codeEditorTitle.Text = "C 语言 IO 库文件（只读）";
            SetEditorText(File.Exists(file.FilePath)
                ? File.ReadAllText(file.FilePath)
                : "/* io_logic_library.c 未部署，请检查程序安装目录。 */");
        }
        else
        {
            currentProjectName = file.ProjectName;
            currentUserSourcePath = file.FilePath;
            codeEditor.ReadOnly = false;
            codeEditor.BackColor = Color.FromArgb(22, 29, 37);
            codeEditorHost.BackColor = codeEditor.BackColor;
            codeEditorTitle.Text = $"C 语言 IO 用户控制程序 · {currentProjectName}";
            SetEditorText(File.Exists(currentUserSourcePath)
                ? File.ReadAllText(currentUserSourcePath)
                : DefaultCProgram);
        }

        UpdateCodeStatus();
    }

    private void NewProject()
    {
        var projectName = PromptForProjectName();
        if (string.IsNullOrWhiteSpace(projectName))
        {
            return;
        }

        var parentDirectory = PromptForProjectParentDirectory();
        if (string.IsNullOrWhiteSpace(parentDirectory))
        {
            return;
        }

        var projectRoot = Path.GetFullPath(Path.Combine(parentDirectory, projectName));
        if (Directory.Exists(projectRoot))
        {
            MessageBox.Show(this, $"工程已存在：{projectName}", "新建工程", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var sourcePath = Path.Combine(projectRoot, "io_logic_user.c");
        Directory.CreateDirectory(projectRoot);
        File.WriteAllText(sourcePath, DefaultCProgram);
        EnsureProjectLibrary(projectRoot);
        EnsureProjectMetadata(projectRoot, projectName);
        RegisterProjectRoot(projectRoot);

        var sourceNode = AddProjectTreeNode(projectName, projectRoot);
        sourceTreeView.SelectedNode = sourceNode;

        currentProjectName = projectName;
        currentUserSourcePath = sourcePath;
        logicCompileLabel.Text = "C逻辑程序：新工程，未编译";
        logicCompileLabel.ForeColor = Color.FromArgb(96, 106, 118);
        SetStatus($"已创建工程：{projectName}");
        OnClientLog($"{DateTime.Now:HH:mm:ss.fff}  已创建 C IO 工程：{projectRoot}");
    }

    private void OnSourceTreeNodeMouseClick(object? sender, TreeNodeMouseClickEventArgs e)
    {
        sourceTreeView.SelectedNode = e.Node;
    }

    private void OnSourceTreeKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode != Keys.Delete)
        {
            return;
        }

        DeleteSelectedProject();
        e.SuppressKeyPress = true;
        e.Handled = true;
    }

    private void DeleteSelectedProject()
    {
        var selected = sourceTreeView.SelectedNode;
        if (selected is null)
        {
            SetStatus("请先选择要删除的工程");
            return;
        }

        var projectNode = selected;
        while (projectNode.Parent is not null)
        {
            projectNode = projectNode.Parent;
        }

        var projectName = projectNode.Text;
        if (string.Equals(projectName, "DAQ_card", StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(this, "DAQ_card 是默认工程，不能删除。", "删除工程", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var projectRoot = GetProjectRootFromNode(projectNode);
        if (string.IsNullOrWhiteSpace(projectRoot) || !Directory.Exists(projectRoot))
        {
            SetStatus("工程目录不存在，无法删除");
            return;
        }

        var confirm = MessageBox.Show(
            this,
            $"确定删除工程“{projectName}”吗？\r\n该工程的用户代码、库文件和工程配置都会删除。",
            "删除工程",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);
        if (confirm != DialogResult.Yes)
        {
            return;
        }

        try
        {
            Directory.Delete(projectRoot, recursive: true);
            UnregisterProjectRoot(projectRoot);
            sourceTreeView.Nodes.Remove(projectNode);
            SelectDefaultProjectSource();
            SetStatus($"已删除工程：{projectName}");
            OnClientLog($"{DateTime.Now:HH:mm:ss.fff}  已删除 C IO 工程：{projectRoot}");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"删除失败：{ex.Message}", "删除工程", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static string? GetProjectRootFromNode(TreeNode projectNode)
    {
        foreach (TreeNode child in projectNode.Nodes)
        {
            if (child.Tag is SourceFileEntry entry)
            {
                return entry.ProjectRoot;
            }

            foreach (TreeNode nestedChild in child.Nodes)
            {
                if (nestedChild.Tag is SourceFileEntry nestedEntry)
                {
                    return nestedEntry.ProjectRoot;
                }
            }
        }

        return null;
    }

    private void SelectDefaultProjectSource()
    {
        foreach (TreeNode project in sourceTreeView.Nodes)
        {
            if (!string.Equals(project.Text, "DAQ_card", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (TreeNode file in project.Nodes)
            {
                if (file.Tag is SourceFileEntry entry && !entry.IsLibrary)
                {
                    sourceTreeView.SelectedNode = file;
                    return;
                }
            }
        }

        if (sourceTreeView.Nodes.Count > 0 && sourceTreeView.Nodes[0].Nodes.Count > 0)
        {
            sourceTreeView.SelectedNode = sourceTreeView.Nodes[0].Nodes[^1];
        }
    }

    private string? PromptForProjectName()
    {
        using var dialog = new Form
        {
            Text = "新建工程",
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MinimizeBox = false,
            MaximizeBox = false,
            ClientSize = new Size(360, 126),
            ShowInTaskbar = false
        };

        var label = new Label { Text = "工程名称：", AutoSize = true, Location = new Point(18, 20) };
        var nameInput = new TextBox { Text = "IO_Project", Location = new Point(92, 16), Width = 238 };
        var okButton = new Button { Text = "创建", DialogResult = DialogResult.OK, Location = new Point(178, 72), Width = 70 };
        var cancelButton = new Button { Text = "取消", DialogResult = DialogResult.Cancel, Location = new Point(258, 72), Width = 70 };
        dialog.Controls.AddRange(new Control[] { label, nameInput, okButton, cancelButton });
        dialog.AcceptButton = okButton;
        dialog.CancelButton = cancelButton;

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return null;
        }

        var projectName = nameInput.Text.Trim();
        if (projectName.Length == 0 || projectName is "." or ".."
            || projectName.Any(character => Path.GetInvalidFileNameChars().Contains(character)))
        {
            MessageBox.Show(this, "工程名称不能为空，也不能包含 \\/:*?\"<>| 等字符。", "新建工程", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return null;
        }

        return projectName;
    }

    private string? PromptForProjectParentDirectory()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "选择工程保存位置，软件将在此位置创建工程文件夹",
            ShowNewFolderButton = true,
            SelectedPath = Path.Combine(AppContext.BaseDirectory, "Projects")
        };

        return dialog.ShowDialog(this) == DialogResult.OK
            ? dialog.SelectedPath
            : null;
    }

    private void SaveCProgram()
    {
        if (codeEditor.ReadOnly)
        {
            SetStatus("固定库文件不可保存，请选择工程源文件");
            return;
        }

        if (string.IsNullOrWhiteSpace(currentUserSourcePath))
        {
            SetStatus("当前没有可保存的工程");
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(currentUserSourcePath)!);
        File.WriteAllText(currentUserSourcePath, codeEditor.Text);

        logicCompileLabel.Text = "C逻辑程序：已保存";
        OnClientLog($"{DateTime.Now:HH:mm:ss.fff}  C逻辑程序已保存：{currentUserSourcePath}");
    }

    private void ImportCProgram()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "导入 C 逻辑文件",
            Filter = "C 源文件 (*.c;*.h)|*.c;*.h|所有文件 (*.*)|*.*",
            Multiselect = false,
            CheckFileExists = true,
            RestoreDirectory = true
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            var source = File.ReadAllText(dialog.FileName);
            sourceTreeView.SelectedNode = FindUserSourceNode();
            codeEditor.ReadOnly = false;
            codeEditor.BackColor = Color.FromArgb(22, 29, 37);
            codeEditorHost.BackColor = codeEditor.BackColor;
            codeEditorTitle.Text = $"C 语言 IO 用户控制程序 · {currentProjectName}";
            SetEditorText(source);
            logicCompileLabel.Text = "C逻辑程序：已导入，未编译";
            logicCompileLabel.ForeColor = Color.FromArgb(96, 106, 118);
            SetStatus($"已导入：{Path.GetFileName(dialog.FileName)}，请保存或编译");
            OnClientLog($"{DateTime.Now:HH:mm:ss.fff}  已导入 C 文件：{dialog.FileName}");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"导入失败：{ex.Message}", "导入 C 文件", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private TreeNode? FindUserSourceNode()
    {
        foreach (TreeNode project in sourceTreeView.Nodes)
        {
            foreach (TreeNode file in project.Nodes)
            {
                if (file.Tag is SourceFileEntry entry
                    && !entry.IsLibrary
                    && string.Equals(Path.GetFullPath(entry.FilePath), Path.GetFullPath(currentUserSourcePath), StringComparison.OrdinalIgnoreCase))
                {
                    return file;
                }
            }
        }

        return null;
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
            ImportCProgram();
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
        lineNumberGutter.Invalidate();
    }

    private void ResetEditorHistory()
    {
        editorHistory.Clear();
        editorHistory.Add(new EditorState(codeEditor.Text, codeEditor.SelectionStart, codeEditor.SelectionLength));
        editorHistoryIndex = 0;
    }

    private void RecordEditorState()
    {
        if (isRestoringEditorState || editorHistoryIndex < 0 || editorHistory.Count == 0)
        {
            return;
        }

        if (string.Equals(editorHistory[editorHistoryIndex].Text, codeEditor.Text, StringComparison.Ordinal))
        {
            return;
        }

        if (editorHistoryIndex + 1 < editorHistory.Count)
        {
            editorHistory.RemoveRange(editorHistoryIndex + 1, editorHistory.Count - editorHistoryIndex - 1);
        }

        editorHistory.Add(new EditorState(codeEditor.Text, codeEditor.SelectionStart, codeEditor.SelectionLength));
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
        HighlightCSource();
        lineNumberGutter.Invalidate();
    }

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

        isCompiling = true;
        compileSplit.Panel2Collapsed = false;
        compileOutputBox.Clear();
        AppendCompileOutput("开始编译 IO 用户程序...", false);
        logicCompileLabel.Text = "C逻辑程序：编译中...";
        logicCompileLabel.ForeColor = Color.FromArgb(190, 125, 35);
        codeStatusLabel.ForeColor = Color.FromArgb(190, 125, 35);
        codeStatusLabel.Text = "编译中，请查看下方编译输出";

        var errors = new List<string>();
        var source = codeEditor.Text;
        var tokens = TokenizeCSource(source, errors);
        if (!HasEntryPoint(tokens))
        {
            errors.Add("缺少入口函数 void IO_Logic_Run(...)" );
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
            var compiler = ResolveCCompiler();
            if (compiler is null)
            {
                return ReportCompileErrors(new[] { "找不到 GCC 编译器，请配置 DAQ_GCC_PATH 环境变量" }, "编译器不可用");
            }

            var projectRoot = Path.GetDirectoryName(currentUserSourcePath);
            var libraryPath = string.IsNullOrWhiteSpace(projectRoot)
                ? Path.Combine(AppContext.BaseDirectory, "Library", "io_logic_library.c")
                : Path.Combine(projectRoot, "Library", "io_logic_library.c");
            EnsureProjectLibrary(projectRoot ?? AppContext.BaseDirectory);
            if (!File.Exists(libraryPath))
            {
                return ReportCompileErrors(new[] { $"找不到固定库文件：{libraryPath}" }, "固定库缺失");
            }

            var tempDirectory = Path.Combine(Path.GetTempPath(), "DAQ_card_compile", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDirectory);
            var translationUnit = Path.Combine(tempDirectory, "io_logic_compile.c");
            var librarySource = File.ReadAllText(libraryPath);
            File.WriteAllText(translationUnit, $"{librarySource}\r\n#line 1 \"{Path.GetFileName(currentUserSourcePath)}\"\r\n{source}");

            AppendCompileOutput("[1/3] 已加载固定 IO 库：io_logic_library.c", false);
            AppendCompileOutput($"[2/3] 使用编译器：{compiler}", false);
            AppendCompileOutput("[3/3] GCC 正在执行 C11 语法、类型和警告检查...", false);

            var startInfo = new ProcessStartInfo
            {
                FileName = compiler,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = tempDirectory
            };
            startInfo.ArgumentList.Add("-std=c11");
            startInfo.ArgumentList.Add("-Wall");
            startInfo.ArgumentList.Add("-Wextra");
            startInfo.ArgumentList.Add("-Werror");
            startInfo.ArgumentList.Add("-fsyntax-only");
            startInfo.ArgumentList.Add(translationUnit);

            using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            process.OutputDataReceived += (_, args) => AppendCompileOutput(args.Data, false);
            process.ErrorDataReceived += (_, args) => AppendCompileOutput(args.Data, true);
            if (!process.Start())
            {
                return ReportCompileErrors(new[] { "GCC 进程启动失败" }, "编译失败");
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await process.WaitForExitAsync();
            process.WaitForExit();

            if (process.ExitCode != 0)
            {
                var error = $"GCC 返回错误码 {process.ExitCode}，请查看下方编译输出";
                return ReportCompileErrors(new[] { error }, "C 编译失败");
            }

            var ioCompiler = new IoLogicCompiler();
            var ioCompile = ioCompiler.Compile(source);
            if (!ioCompile.Success || ioCompile.Image is null)
            {
                return ReportCompileErrors(ioCompile.Errors, "IOCF 编译失败");
            }

            lastCompiledImage = ioCompile.Image;
            logicCompileLabel.Text = $"C逻辑程序：编译通过 · {ioCompile.Image.Bytes.Length} 字节 · {ioCompile.Image.RuleCount} 条规则";
            logicCompileLabel.ForeColor = Color.FromArgb(35, 135, 85);
            codeStatusLabel.ForeColor = Color.FromArgb(166, 183, 197);
            codeStatusLabel.Text = $"编译通过 · IOCF {ioCompile.Image.Bytes.Length} 字节 · {ioCompile.Image.RuleCount} 条规则";
            SetStatus("C 逻辑程序编译通过，可下载到板卡");
            AppendCompileOutput($"IOCF 编译完成：{ioCompile.Image.Bytes.Length} 字节，{ioCompile.Image.RuleCount} 条规则，CRC32=0x{ioCompile.Image.BodyCrc32:X8}", false);
            OnClientLog($"{DateTime.Now:HH:mm:ss.fff}  C IO逻辑编译通过：IOCF={ioCompile.Image.Bytes.Length}字节, rules={ioCompile.Image.RuleCount}");
            return true;
        }
        catch (Exception ex)
        {
            return ReportCompileErrors(new[] { ex.Message }, "C 编译异常");
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
            SetStatus("请先连接串口");
            return;
        }

        if (!await CompileCProgramAsync())
        {
            return;
        }

        if (lastCompiledImage is null)
        {
            SetStatus("没有可下载的 IOCF 编译结果");
            return;
        }

        await pollGate.WaitAsync();
        try
        {
            SetStatus("正在下载 IOCF 到板卡，请勿发送其他串口命令");
            logicCompileLabel.Text = "C逻辑程序：下载中...";
            var protocol = new IoLogicProtocol(client);
            var service = new IoLogicDownloadService(protocol, OnClientLog);
            var info = await Task.Run(() => service.Download(lastCompiledImage, CancellationToken.None));
            logicCompileLabel.Text = $"C逻辑程序：下载完成 · generation {info.Generation}";
            logicCompileLabel.ForeColor = Color.FromArgb(35, 135, 85);
            SetStatus($"IOCF 下载完成，板卡 generation={info.Generation}");
            AppendCompileOutput($"下载完成：generation={info.Generation}，配置槽={info.ConfigState}", false);
        }
        catch (Exception ex)
        {
            logicCompileLabel.Text = "C逻辑程序：下载失败";
            logicCompileLabel.ForeColor = Color.FromArgb(190, 55, 55);
            SetStatus($"IOCF 下载失败：{ex.Message}");
            OnClientLog($"{DateTime.Now:HH:mm:ss.fff}  ERROR  IOCF 下载失败：{ex.Message}");
            // 下载服务会使用当前会话号主动 ABORT；这里不再发送无效的 session=0。
        }
        finally
        {
            pollGate.Release();
        }
    }

    private bool ReportCompileErrors(IReadOnlyList<string> errors, string title)
    {
        logicCompileLabel.Text = $"C逻辑程序：编译失败 · {errors.Count} 个错误";
        logicCompileLabel.ForeColor = Color.FromArgb(190, 55, 55);
        codeStatusLabel.ForeColor = Color.FromArgb(220, 85, 85);
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
            compileOutputBox.SelectionColor = isError ? Color.FromArgb(245, 125, 125) : Color.FromArgb(190, 205, 215);
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
        var lineCount = Math.Max(1, codeEditor.Lines.Length);
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
            if (tokens[index].Text == "void" && tokens[index + 1].Text == "IO_Logic_Run"
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

    private void HighlightCSource()
    {
        if (isHighlightingCode || !codeEditor.IsHandleCreated)
        {
            return;
        }

        isHighlightingCode = true;
        var selectionStart = codeEditor.SelectionStart;
        var selectionLength = codeEditor.SelectionLength;
        var source = codeEditor.Text;
        var firstVisibleLine = SendMessage(codeEditor.Handle, EM_GETFIRSTVISIBLELINE, IntPtr.Zero, IntPtr.Zero).ToInt32();

        codeEditor.SuspendLayout();
        SendMessage(codeEditor.Handle, WM_SETREDRAW, IntPtr.Zero, IntPtr.Zero);
        try
        {
            codeEditor.SelectAll();
            codeEditor.SelectionFont = codeEditor.Font;
            codeEditor.SelectionColor = Color.FromArgb(221, 231, 239);
            codeEditor.SelectionBackColor = codeEditor.BackColor;

            var index = 0;
            while (index < source.Length)
            {
                if (source[index] == '/' && index + 1 < source.Length && source[index + 1] == '/')
                {
                    var end = source.IndexOf('\n', index);
                    if (end < 0) end = source.Length;
                    ColorCodeSpan(index, end - index, Color.FromArgb(106, 153, 85));
                    index = end;
                    continue;
                }

                if (source[index] == '/' && index + 1 < source.Length && source[index + 1] == '*')
                {
                    var end = source.IndexOf("*/", index + 2, StringComparison.Ordinal);
                    end = end < 0 ? source.Length : end + 2;
                    ColorCodeSpan(index, end - index, Color.FromArgb(106, 153, 85));
                    index = end;
                    continue;
                }

                if ((source[index] == '"') || (source[index] == '\'') || source[index] == '`')
                {
                    var quote = source[index];
                    var end = FindQuotedEnd(source, index, quote);
                    ColorCodeSpan(index, end - index, Color.FromArgb(206, 145, 120));
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

                    ColorCodeSpan(index, directiveEnd - index, Color.FromArgb(197, 134, 192));
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

                    ColorCodeSpan(index, end - index, Color.FromArgb(181, 206, 168));
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
                    var color = GetIdentifierColor(source, index, end, token);
                    if (color.HasValue)
                    {
                        ColorCodeSpan(index, end - index, color.Value);
                    }

                    index = end;
                    continue;
                }

                if (IsCSymbol(source[index]))
                {
                    var symbolColor = "{}[]()".Contains(source[index])
                        ? Color.FromArgb(212, 212, 212)
                        : ";,.".Contains(source[index])
                            ? Color.FromArgb(184, 184, 184)
                            : Color.FromArgb(220, 150, 130);
                    ColorCodeSpan(index, 1, symbolColor);
                }

                index++;
            }

            HighlightMatchingBrace(source, selectionStart, selectionLength);

            var safeSelectionStart = Math.Min(selectionStart, codeEditor.TextLength);
            var safeSelectionLength = Math.Min(selectionLength, codeEditor.TextLength - safeSelectionStart);
            codeEditor.Select(safeSelectionStart, safeSelectionLength);
            codeEditor.SelectionFont = codeEditor.Font;
            if (safeSelectionLength == 0)
            {
                codeEditor.SelectionColor = Color.FromArgb(221, 231, 239);
            }
            SendMessage(codeEditor.Handle, EM_LINESCROLL, IntPtr.Zero, new IntPtr(firstVisibleLine - SendMessage(codeEditor.Handle, EM_GETFIRSTVISIBLELINE, IntPtr.Zero, IntPtr.Zero).ToInt32()));
        }
        finally
        {
            SendMessage(codeEditor.Handle, WM_SETREDRAW, new IntPtr(1), IntPtr.Zero);
            codeEditor.ResumeLayout();
            codeEditor.Invalidate();
            isHighlightingCode = false;
        }
    }

    private void ColorCodeSpan(int start, int length, Color color)
    {
        if (length <= 0 || start < 0 || start + length > codeEditor.TextLength)
        {
            return;
        }

        codeEditor.Select(start, length);
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

        var highlight = Color.FromArgb(75, 95, 116);
        codeEditor.Select(braceIndex, 1);
        codeEditor.SelectionBackColor = highlight;
        codeEditor.Select(matchIndex, 1);
        codeEditor.SelectionBackColor = highlight;
    }

    private static Color? GetIdentifierColor(string source, int start, int end, string token)
    {
        if (CKeywords.Contains(token))
        {
            return Color.FromArgb(86, 156, 214);
        }

        if (CTypes.Contains(token))
        {
            return Color.FromArgb(78, 201, 176);
        }

        if (IsIoConstant(token))
        {
            return Color.FromArgb(79, 193, 255);
        }

        var next = end;
        while (next < source.Length && char.IsWhiteSpace(source[next]))
        {
            next++;
        }

        return next < source.Length && source[next] == '('
            ? Color.FromArgb(220, 220, 170)
            : null;
    }

    private static bool IsIoConstant(string token)
    {
        return token is "INPUT_1" or "INPUT_2" or "INPUT_3" or "INPUT_4" or "INPUT_5" or "INPUT_6" or "INPUT_7" or "INPUT_8"
            or "RELAY_1" or "RELAY_2" or "RELAY_3" or "RELAY_4"
            or "TRANSISTOR_1" or "TRANSISTOR_2" or "TRANSISTOR_3" or "TRANSISTOR_4";
    }

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

    private static bool IsCSymbol(char value) => "{}[]();,.?:+-*/%=&|!<>~^#".Contains(value);

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
            SetStatus("请先连接串口");
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
        label.Text = $"{name}  {(active ? "打开" : "关闭")}";
        label.ForeColor = active ? Color.FromArgb(20, 112, 72) : Color.FromArgb(110, 120, 130);
        label.BackColor = active ? Color.FromArgb(216, 244, 228) : Color.FromArgb(235, 239, 243);
    }

    private Control BuildDataPanel()
    {
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = Color.White };
        var title = SectionTitle("实时采集数据");
        panel.Controls.Add(title);

        lastPollLabel.AutoSize = true;
        lastPollLabel.Text = "最近采集：--";
        lastPollLabel.ForeColor = Color.FromArgb(102, 112, 123);
        lastPollLabel.Location = new Point(18, 45);
        panel.Controls.Add(lastPollLabel);

        var gridPanel = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(14, 70, 14, 56)
        };
        ConfigureDataGrid();
        gridPanel.Controls.Add(dataGrid);
        panel.Controls.Add(gridPanel);

        var writePanel = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 48,
            BackColor = Color.FromArgb(248, 250, 252),
            Padding = new Padding(14, 8, 14, 8)
        };
        var writeLabel = new Label
        {
            AutoSize = true,
            Text = "数字输出寄存器 0x0002：",
            Location = new Point(14, 13)
        };
        writePanel.Controls.Add(writeLabel);
        ConfigureNumeric(outputMaskInput, 0, 1023, 0, 70);
        outputMaskInput.Location = new Point(166, 8);
        writePanel.Controls.Add(outputMaskInput);
        writeOutputButton.Text = "写入输出";
        writeOutputButton.Width = 80;
        writeOutputButton.Location = new Point(244, 8);
        writeOutputButton.Click += OnWriteOutputClicked;
        writePanel.Controls.Add(writeOutputButton);
        panel.Controls.Add(writePanel);
        return panel;
    }

    private void ConfigureDataGrid()
    {
        dataGrid.Dock = DockStyle.Fill;
        dataGrid.ReadOnly = true;
        dataGrid.AllowUserToAddRows = false;
        dataGrid.AllowUserToDeleteRows = false;
        dataGrid.AllowUserToResizeRows = false;
        dataGrid.RowHeadersVisible = false;
        dataGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        dataGrid.MultiSelect = false;
        dataGrid.BackgroundColor = Color.White;
        dataGrid.BorderStyle = BorderStyle.None;
        dataGrid.GridColor = Color.FromArgb(226, 231, 236);
        dataGrid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None;
        dataGrid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Color.FromArgb(234, 240, 245),
            ForeColor = Color.FromArgb(38, 53, 68),
            Font = new Font(Font, FontStyle.Bold),
            Alignment = DataGridViewContentAlignment.MiddleLeft
        };
        dataGrid.EnableHeadersVisualStyles = false;
        dataGrid.Columns.Add("name", "数据项");
        dataGrid.Columns.Add("address", "寄存器");
        dataGrid.Columns.Add("value", "当前值");
        dataGrid.Columns.Add("description", "说明");
        dataGrid.Columns[0].Width = 175;
        dataGrid.Columns[1].Width = 90;
        dataGrid.Columns[2].Width = 160;
        dataGrid.Columns[3].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;

        AddDataRow("设备状态", "0x0000", "--", "bit0 IMU，bit1 AD7606，bit2/3 最近读取结果", "status");
        AddDataRow("数字输入", "0x0001", "--", "8 路输入位图", "digitalInputs");
        AddDataRow("数字输出", "0x0002", "--", "8 路数字输出位图，可写", "digitalOutputs");
        for (var index = 0; index < 8; index++)
        {
            AddDataRow($"AD7606 CH{index + 1}", $"0x{0x10 + index:X4}", "--", "有符号原始采样值", $"ad{index}");
        }

        var imuNames = new[] { "加速度 X", "加速度 Y", "加速度 Z", "陀螺仪 X", "陀螺仪 Y", "陀螺仪 Z" };
        for (var index = 0; index < imuNames.Length; index++)
        {
            AddDataRow(imuNames[index], $"0x{0x20 + index:X4}", "--", "IMU 有符号原始值", $"imu{index}");
        }

        AddDataRow("Roll", "0x0030", "--", "单位 0.01°", "roll");
        AddDataRow("Pitch", "0x0031", "--", "单位 0.01°", "pitch");
        AddDataRow("Yaw", "0x0032", "--", "单位 0.01°", "yaw");
    }

    private Control BuildLogPanel()
    {
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = Color.White };
        panel.Controls.Add(SectionTitle("通信日志"));

        logBox.Dock = DockStyle.Fill;
        logBox.BorderStyle = BorderStyle.None;
        logBox.BackColor = Color.FromArgb(22, 29, 37);
        logBox.ForeColor = Color.FromArgb(218, 228, 237);
        logBox.Font = new Font("Consolas", 9F);
        logBox.ReadOnly = true;
        logBox.WordWrap = false;
        logBox.ScrollBars = RichTextBoxScrollBars.Both;
        logBox.DetectUrls = false;
        logBox.Margin = new Padding(0);
        var logHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(14, 52, 14, 52) };
        logHost.Controls.Add(logBox);
        panel.Controls.Add(logHost);

        var logFooter = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 44,
            BackColor = Color.FromArgb(248, 250, 252),
            Padding = new Padding(14, 7, 14, 7)
        };
        clearLogButton.Text = "清空日志";
        clearLogButton.Width = 82;
        clearLogButton.Click += (_, _) => logBox.Clear();
        logFooter.Controls.Add(clearLogButton);
        panel.Controls.Add(logFooter);
        return panel;
    }

    private static Label SectionTitle(string text)
    {
        return new Label
        {
            AutoSize = false,
            Dock = DockStyle.Top,
            Height = 42,
            Text = text,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(16, 0, 0, 0),
            Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold),
            ForeColor = Color.FromArgb(34, 53, 70),
            BackColor = Color.FromArgb(248, 250, 252)
        };
    }

    private static void AddLabel(FlowLayoutPanel panel, string text)
    {
        panel.Controls.Add(new Label
        {
            AutoSize = false,
            Width = 58,
            Height = 28,
            Text = text,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.FromArgb(220, 232, 242),
            Margin = new Padding(8, 0, 2, 0)
        });
    }

    private static void ConfigureNumeric(NumericUpDown input, decimal minimum, decimal maximum, decimal value, int width)
    {
        input.Minimum = minimum;
        input.Maximum = maximum;
        input.Value = value;
        input.Width = width;
        input.Height = 28;
        input.TextAlign = HorizontalAlignment.Center;
        input.Margin = new Padding(3, 0, 8, 0);
    }

    private void AddDataRow(string name, string address, string value, string description, string key)
    {
        var row = dataGrid.Rows.Add(name, address, value, description);
        rowIndexes[key] = row;
    }

    private void RefreshPorts()
    {
        var selected = portComboBox.Text;
        portComboBox.Items.Clear();
        portComboBox.Items.AddRange(SerialPort.GetPortNames().OrderBy(name => name).ToArray());
        if (portComboBox.Items.Contains(selected))
        {
            portComboBox.SelectedItem = selected;
        }
        else if (portComboBox.Items.Count > 0)
        {
            portComboBox.SelectedIndex = 0;
        }

        OnClientLog($"{DateTime.Now:HH:mm:ss.fff}  检测到串口：{(portComboBox.Items.Count == 0 ? "无" : string.Join(", ", portComboBox.Items.Cast<string>()))}");
    }

    private void OnConnectClicked(object? sender, EventArgs e)
    {
        if (client.IsOpen)
        {
            Disconnect();
            return;
        }

        try
        {
            client.Open(portComboBox.Text, 115200, (byte)slaveAddressInput.Value);
            if (EnableRealtimePolling)
            {
                pollCancellation = new CancellationTokenSource();
                var token = pollCancellation.Token;
                pollTask = Task.Run(() => PollLoopAsync(token), token);
            }
            SetConnectedUi(true);
            SetStatus(EnableRealtimePolling ? "已连接，正在轮询" : "已连接，仅支持固件规则下载");
        }
        catch (Exception ex)
        {
            SetStatus("连接失败");
            OnClientLog($"{DateTime.Now:HH:mm:ss.fff}  ERROR  {ex.Message}");
            client.Close();
        }
    }

    private void Disconnect()
    {
        pollCancellation?.Cancel();
        client.Close();
        pollCancellation?.Dispose();
        pollCancellation = null;
        pollTask = null;
        SetConnectedUi(false);
        SetStatus("未连接");
        OnClientLog($"{DateTime.Now:HH:mm:ss.fff}  串口已关闭");
    }

    private async Task PollLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            await PollOnceAsync(token);
            try
            {
                await Task.Delay(pollIntervalMs, token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task PollOnceAsync(CancellationToken token)
    {
        if (!client.IsOpen || token.IsCancellationRequested)
        {
            return;
        }

        await pollGate.WaitAsync(token);
        try
        {
            var status = client.ReadHoldingRegisters(0x0000, 3);
            var ad7606 = client.ReadHoldingRegisters(0x0010, 8);
            var imu = client.ReadHoldingRegisters(0x0020, 6);
            var angles = client.ReadHoldingRegisters(0x0030, 3);

            var snapshot = new AcquisitionSnapshot
            {
                Timestamp = DateTime.Now,
                Status = status[0],
                DigitalInputs = status[1],
                DigitalOutputs = status[2],
                Ad7606Raw = ad7606.Select(value => unchecked((short)value)).ToArray(),
                ImuRaw = imu.Select(value => unchecked((short)value)).ToArray(),
                RollCentiDegrees = unchecked((short)angles[0]),
                PitchCentiDegrees = unchecked((short)angles[1]),
                YawCentiDegrees = unchecked((short)angles[2])
            };

            Ui(() => UpdateSnapshot(snapshot));
            SetStatus($"在线 · 最后响应 {snapshot.Timestamp:HH:mm:ss}");
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            OnClientLog($"{DateTime.Now:HH:mm:ss.fff}  ERROR  {ex.Message}");
            SetStatus("通信异常，请查看日志");
        }
        finally
        {
            pollGate.Release();
        }
    }

    private void OnPollNowClicked(object? sender, EventArgs e)
    {
        if (!client.IsOpen)
        {
            SetStatus("请先连接串口");
            return;
        }

        if (!EnableRealtimePolling)
        {
            SetStatus("实时数据更新已停用，当前仅支持固件规则下载");
            return;
        }

        _ = Task.Run(() => PollOnceAsync(CancellationToken.None));
    }

    private void OnWriteOutputClicked(object? sender, EventArgs e)
    {
        if (!client.IsOpen)
        {
            SetStatus("请先连接串口");
            return;
        }

        var value = (ushort)outputMaskInput.Value;
        _ = Task.Run(async () =>
        {
            await pollGate.WaitAsync();
            try
            {
                client.WriteSingleRegister(0x0002, value);
                OnClientLog($"{DateTime.Now:HH:mm:ss.fff}  输出寄存器写入成功：0x{value:X4}");
            }
            catch (Exception ex)
            {
                OnClientLog($"{DateTime.Now:HH:mm:ss.fff}  ERROR  写输出失败：{ex.Message}");
            }
            finally
            {
                pollGate.Release();
            }
        });
    }

    private void UpdateSnapshot(AcquisitionSnapshot snapshot)
    {
        SetValue("status", $"0x{snapshot.Status:X4}");
        SetValue("digitalInputs", $"0x{snapshot.DigitalInputs:X4}");
        SetValue("digitalOutputs", $"0x{snapshot.DigitalOutputs:X4}");
        SetAllIndicators(snapshot.DigitalInputs, snapshot.DigitalOutputs);
        outputMaskInput.Value = Math.Min(outputMaskInput.Maximum, snapshot.DigitalOutputs);
        SetValue("status", $"0x{snapshot.Status:X4} ({DescribeStatus(snapshot.Status)})");

        for (var index = 0; index < snapshot.Ad7606Raw.Length; index++)
        {
            SetValue($"ad{index}", snapshot.Ad7606Raw[index].ToString());
        }

        for (var index = 0; index < snapshot.ImuRaw.Length; index++)
        {
            SetValue($"imu{index}", snapshot.ImuRaw[index].ToString());
        }

        SetValue("roll", FormatAngle(snapshot.RollCentiDegrees));
        SetValue("pitch", FormatAngle(snapshot.PitchCentiDegrees));
        SetValue("yaw", FormatAngle(snapshot.YawCentiDegrees));
        lastPollLabel.Text = $"最近采集：{snapshot.Timestamp:yyyy-MM-dd HH:mm:ss.fff}";
    }

    private static string DescribeStatus(ushort status)
    {
        var flags = new List<string>();
        if ((status & 0x0001) != 0) flags.Add("IMU");
        if ((status & 0x0002) != 0) flags.Add("AD7606");
        if ((status & 0x0004) != 0) flags.Add("IMU读取OK");
        if ((status & 0x0008) != 0) flags.Add("AD读取OK");
        return flags.Count == 0 ? "无就绪标志" : string.Join(", ", flags);
    }

    private static string FormatAngle(short centiDegrees)
    {
        return $"{centiDegrees / 100.0:0.00}°";
    }

    private void SetValue(string key, string value)
    {
        if (rowIndexes.TryGetValue(key, out var row))
        {
            dataGrid.Rows[row].Cells[2].Value = value;
        }
    }

    private void SetConnectedUi(bool connected)
    {
        portComboBox.Enabled = !connected;
        slaveAddressInput.Enabled = !connected;
        refreshPortsButton.Enabled = !connected;
        connectButton.Text = connected ? "断开串口" : "连接串口";
        connectButton.BackColor = connected ? Color.FromArgb(196, 78, 70) : Color.FromArgb(36, 166, 114);
        pollIntervalInput.Enabled = connected && EnableRealtimePolling;
        pollNowButton.Enabled = connected && EnableRealtimePolling;
        writeOutputButton.Enabled = connected;
        downloadLogicButton.Enabled = connected;
    }

    private void SetStatus(string text)
    {
        Ui(() =>
        {
            statusLabel.Text = text;
            connectionLabel.Text = text;
        });
    }

    private void OnClientLog(string message)
    {
        Ui(() =>
        {
            if (logBox.IsDisposed)
            {
                return;
            }

            logBox.AppendText(message + Environment.NewLine);
            if (logBox.Lines.Length > 1200)
            {
                logBox.Select(0, logBox.GetFirstCharIndexFromLine(200));
                logBox.SelectedText = string.Empty;
            }

            logBox.SelectionStart = logBox.TextLength;
            logBox.ScrollToCaret();
        });
    }

    private void Ui(Action action)
    {
        if (IsDisposed || !IsHandleCreated)
        {
            return;
        }

        try
        {
            BeginInvoke(action);
        }
        catch (InvalidOperationException)
        {
        }
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        pollCancellation?.Cancel();
        client.Dispose();
        pollGate.Dispose();
    }

    private readonly record struct EditorState(string Text, int SelectionStart, int SelectionLength);
    private readonly record struct SourceFileEntry(string FilePath, bool IsLibrary, string ProjectName, string ProjectRoot);
}

internal sealed class CodeLineNumberGutter : Control
{
    private RichTextBox? editor;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public RichTextBox? Editor
    {
        get => editor;
        set
        {
            editor = value;
            Invalidate();
        }
    }

    public CodeLineNumberGutter()
    {
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw,
            true);
        BackColor = Color.FromArgb(29, 37, 47);
        ForeColor = Color.FromArgb(119, 137, 153);
        Font = new Font("Cascadia Mono", 10F, FontStyle.Regular);
        TabStop = false;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor);
        if (editor is null || editor.IsDisposed || !editor.IsHandleCreated)
        {
            return;
        }

        var firstLine = SendMessage(editor.Handle, 0x00CE, IntPtr.Zero, IntPtr.Zero).ToInt32();
        var lineHeight = Math.Max(1, TextRenderer.MeasureText(e.Graphics, "Ag", Font).Height);
        using var brush = new SolidBrush(ForeColor);
        using var format = new StringFormat
        {
            Alignment = StringAlignment.Far,
            LineAlignment = StringAlignment.Near
        };

        for (var line = firstLine; line < editor.Lines.Length; line++)
        {
            var charIndex = editor.GetFirstCharIndexFromLine(line);
            if (charIndex < 0)
            {
                break;
            }

            var position = editor.GetPositionFromCharIndex(charIndex);
            var y = position.Y;
            if (y > ClientSize.Height)
            {
                break;
            }

            if (y + lineHeight >= 0)
            {
                e.Graphics.DrawString(
                    (line + 1).ToString(),
                    Font,
                    brush,
                    new RectangleF(0, y, ClientSize.Width - 8, lineHeight),
                    format);
            }
        }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int message, IntPtr wParam, IntPtr lParam);
}
