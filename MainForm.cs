using System.Diagnostics;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace IndustrialFieldDataAcquisitionTerminal;

internal sealed partial class MainForm : Form
{
    private readonly ModbusTcpClient client = new();
    private Func<string, int, byte, CancellationToken, Task> openConnection;
    private readonly Rs485ConfigurationProtocol rs485Protocol;
    private readonly TextBox hostInput = new();
    private readonly NumericUpDown ethernetPortInput = new();
    private readonly NumericUpDown slaveAddressInput = new();
    private readonly NumericUpDown pollIntervalInput = new();
    private readonly Button connectButton = new();
    private readonly Button refreshPortsButton = new();    private readonly Button pollNowButton = new();
    private readonly Button pollToggleButton = new();
    private readonly Button rs485AddButton = new();
    private readonly Button rs485RemoveButton = new();
    private readonly Button rs485UploadButton = new();
    private readonly Button rs485ReadButton = new();
    private readonly Button clearLogButton = new();
    private readonly Button saveLogButton = new();
    private readonly Label logStatsLabel = new();
    private readonly Label connectionLabel = new();
    private readonly Label activeDeviceLabel = new();
    private readonly Label lastPollLabel = new();
    private readonly DataGridView dataGrid = new();
    private readonly DataGridView rs485Grid = new();
    private readonly RichTextBox logBox = new();
    private readonly RichTextBox codeEditor = new();
    private readonly Panel codeEditorHost = new();
    private readonly CodeLineNumberGutter lineNumberGutter = new();
    private readonly CodeMinimap codeMinimap = new();
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
    private readonly Label analogVoltageIndicator = new();
    private readonly Label analogCurrentIndicator = new();
    private readonly List<IoLogicRule> logicRules = new();
    private readonly Button downloadLogicButton = new();
    private int selectedRuleIndex = -1;
    private readonly StatusStrip statusStrip = new();
    private readonly ToolStripStatusLabel statusLabel = new();
    private readonly ToolStripButton logToggleButton = new();
    private readonly SplitContainer workspaceSplit = new();
    private readonly TabControl contentTabs = new ContentTabControl();
    private readonly Button[] navigationButtons = new Button[9];
    private readonly FlowLayoutPanel connectionBar = new();
    private readonly FlowLayoutPanel samplingBar = new();
    private readonly Label samplingTimingLabel = new();
    private readonly StatTile kpiLink = new();
    private readonly StatTile kpiInterval = new();
    private readonly StatTile kpiAd = new();
    private readonly StatTile kpiIo = new();
    private long trendSegment;
    private long lastPollStartTimestamp;
    private long lastSnapshotDisplayTimestamp;
    private AcquisitionSnapshot? pendingSnapshotDisplay;
    private AcquisitionSnapshot? latestSnapshotForDisplay;
    private IReadOnlyList<Rs485SensorTelemetry>? latestRs485ForDisplay;
    private long lastOnlineStatusSecond;
    private int pendingPageRefreshVersion;
    private readonly Button connectionSummaryButton = new();
    private readonly Button deviceManagerButton = new();
    private readonly FlowLayoutPanel globalNavigation = new();
    private readonly FlowLayoutPanel deviceNavigation = new();
    private readonly FlowLayoutPanel toolsNavigation = new();
    private readonly Label workspaceTitle = new();
    private readonly Label deviceNavigationTitle = new();
    private readonly NavItem deviceHomeButton = new();
    private readonly StatusPill headerStatusPill = new();
    private Control? headerHost;
    private Control? connectionDrawer;
    private bool connectionExpanded;
    private readonly Button compileOutputToggleButton = new();
    private readonly System.Windows.Forms.Timer editorDecorationTimer = new() { Interval = 450 };
    private readonly System.Windows.Forms.Timer externalProjectTimer = new() { Interval = 2000 };
    private readonly System.Windows.Forms.Timer communicationLogTimer = new() { Interval = 200 };
    private readonly object pendingLogLock = new();
    private readonly Queue<string> pendingLogMessages = new();
    private int droppedLogMessages;
    private readonly HashSet<int> highlightedLines = new();
    private int highlightRangeStart;
    private int highlightRangeEnd;
    private bool communicationLogVisible = true;
    private readonly Dictionary<string, int> rowIndexes = new();
    private readonly SemaphoreSlim pollGate = new(1, 1);
    private CancellationTokenSource? pollCancellation;
    private CancellationTokenSource? connectCancellation;
    private bool isConnecting;
    private bool userCancelledConnect;
    private Task? pollTask;
    private int connectionEpoch;
    private volatile int pollIntervalMs = 250;
    private bool isHighlightingCode;
    private bool isCompiling;
    private bool commentChordPending;
    private bool isRestoringEditorState;
    private readonly List<EditorState> editorHistory = new();
    private int editorHistoryIndex = -1;
    private int txLogCount;
    private int rxLogCount;
    private int errorLogCount;
    private bool firmwareStatsSupported = true;
    private DateTime nextRs485TelemetryPollUtc = DateTime.MinValue;
    private readonly Panel findBar = new();
    private readonly TextBox findInput = new();
    private readonly Label findResultLabel = new();
    private const string DefaultProjectName = "示例代码";
    private const string ProjectManualFileName = "编程手册.txt";
    private static bool IsDefaultProject(ProjectReference project) =>
        string.Equals(project.Path, Path.Combine(AppContext.BaseDirectory, "Projects", DefaultProjectName), StringComparison.OrdinalIgnoreCase);
    private ProjectCatalog projectCatalog = new(Path.Combine(AppContext.BaseDirectory, "Projects", "project_locations.json"));
    private ProjectReference? currentProject;
    private string? currentSourceHash;
    private string? currentLoadedSourceText;
    private string? dismissedExternalSourceHash;
    private bool missingSourcePrompted;
    private bool isCheckingExternalChanges;
    private Func<bool, DialogResult> externalSourceChangePrompt;
    private bool suppressProjectSwitchPrompt;
    private bool suppressProjectLoad;
    private string currentProjectName = DefaultProjectName;
    private string currentUserSourcePath = string.Empty;
    private IoLogicCompiledImage? lastCompiledImage;
    private bool lastCompiledUsesSnapshotInputs;
    private bool lastCompiledUsesCurrentBreath;
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
    private static readonly string[] AnalogOutputNames =
    {
        "VOLTAGE_OUTPUT_1", "CURRENT_OUTPUT_1"
    };
    private static readonly string[] LibraryFileNames =
    {
        "io_logic_library.h",
        "io_logic_types.h",
        "io_logic_input.h",
        "io_logic_output.h",
        "io_logic_timer.h",
        "io_logic_analog.h"
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
        openConnection = client.OpenAsync;
        externalSourceChangePrompt = ShowExternalSourceChangePrompt;
        rs485Protocol = new Rs485ConfigurationProtocol(client);
        editorDecorationTimer.Tick += (_, _) =>
        {
            editorDecorationTimer.Stop();
            if (codeEditor.IsDisposed) return;
            HighlightVisibleCode();
            HighlightCSource();
            codeMinimap.InvalidateContent();
        };
        Text = "设备工作台";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1100, 680);
        Size = new Size(1400, 850);
        Font = UiPalette.BodyFont;
        BackColor = UiPalette.Canvas;
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "daq-workbench.ico");
        if (File.Exists(iconPath)) Icon = new Icon(iconPath);

        BuildUi();
        InitializeDeviceCatalog();
        InitializeSerialFeatures();
        LoadRs485Configuration();
        SeedCProgram();
        InitializeWorkbench();
        UiPalette.ApplyWorkbenchTheme(this);
        externalProjectTimer.Tick += (_, _) => CheckForExternalProjectChanges();
        communicationLogTimer.Tick += (_, _) => FlushCommunicationLog();
        Shown += (_, _) =>
        {
            externalProjectTimer.Start();
            communicationLogTimer.Start();
        };
        Activated += (_, _) => { if (externalProjectTimer.Enabled) CheckForExternalProjectChanges(); };
        client.LogMessage += OnClientLog;
        pollIntervalInput.ValueChanged += (_, _) => pollIntervalMs = (int)pollIntervalInput.Value;
        pollIntervalMs = (int)pollIntervalInput.Value;
        FormClosing += OnFormClosing;
        SetConnectedUi(false);
        SetStatus("未连接");
    }

    private void BuildUi()
    {
        var shell = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = UiPalette.Canvas,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, UiPalette.SidebarWidth));
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        shell.Controls.Add(BuildNavigation(), 0, 0);

        var main = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            BackColor = UiPalette.Canvas,
            Padding = new Padding(0)
        };
        main.RowStyles.Add(new RowStyle(SizeType.Absolute, 160));
        main.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        main.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        main.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        main.Controls.Add(BuildHeader(), 0, 0);
        connectionDrawer = BuildConnectionDrawer();
        main.Controls.Add(connectionDrawer, 0, 1);
        main.Controls.Add(BuildStatusBar(), 0, 2);
        main.Controls.Add(BuildWorkspaceWithLog(), 0, 3);
        shell.Controls.Add(main, 1, 0);
        Controls.Add(shell);
    }

    private Control BuildNavigation()
    {
        var sidebar = new Panel
        {
            Name = "moduleNavigation",
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            BackColor = UiPalette.Sidebar,
            Padding = new Padding(UiPalette.Space3, UiPalette.Space4, UiPalette.Space3, UiPalette.Space3)
        };
        sidebar.Paint += (_, e) =>
        {
            using var pen = new Pen(UiPalette.Border);
            e.Graphics.DrawLine(pen, sidebar.ClientSize.Width - 1, 0, sidebar.ClientSize.Width - 1, sidebar.ClientSize.Height);
        };

        var navigation = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            BackColor = UiPalette.Sidebar
        };
        foreach (var group in new[] { globalNavigation, deviceNavigation, toolsNavigation })
        {
            group.AutoSize = true;
            group.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            group.Margin = new Padding(0, 0, 0, UiPalette.Space2);
            group.Padding = Padding.Empty;
            group.FlowDirection = FlowDirection.TopDown;
            group.WrapContents = false;
            group.BackColor = UiPalette.Sidebar;
            navigation.Controls.Add(group);
        }

        // Brand block: mark plus two quiet lines of identity.
        var brand = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 88,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = UiPalette.Sidebar,
            Margin = Padding.Empty,
            Padding = new Padding(2, 0, 0, 0)
        };
        brand.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 50));
        brand.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        brand.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var mark = new PictureBox
        {
            Image = AppBrand.CreateBitmap(34),
            SizeMode = PictureBoxSizeMode.Zoom,
            Size = new Size(34, 34),
            Anchor = AnchorStyles.Left,
            Margin = new Padding(2, 0, UiPalette.Space3, 0)
        };
        var brandText = new TableLayoutPanel
        {
            ColumnCount = 1,
            RowCount = 2,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Anchor = AnchorStyles.Left,
            Margin = Padding.Empty,
            BackColor = UiPalette.Sidebar
        };
        brandText.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        brandText.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        brandText.Controls.Add(new Label
        {
            Text = "设备工作台",
            AutoSize = true,
            Font = new Font("Microsoft YaHei UI", 11.5F, FontStyle.Bold),
            ForeColor = UiPalette.Ink,
            Margin = new Padding(0, 0, 0, 2),
            BackColor = UiPalette.Sidebar
        }, 0, 0);
        brandText.Controls.Add(new Label
        {
            Text = DeviceCatalog.ProductModel + " · 现场采集",
            AutoSize = true,
            Font = UiPalette.MetaFont,
            ForeColor = UiPalette.Muted,
            Margin = Padding.Empty,
            BackColor = UiPalette.Sidebar
        }, 0, 1);
        brand.Controls.Add(mark, 0, 0);
        brand.Controls.Add(brandText, 1, 0);

        // Home entry sits above the workspace groups so it reads as the product root.
        deviceHomeButton.Text = "我的设备";
        deviceHomeButton.Glyph = NavigationGlyphKind.Devices;
        deviceHomeButton.Height = 42;
        deviceHomeButton.Margin = new Padding(0, 0, 0, UiPalette.Space2);
        deviceHomeButton.Click += (_, _) => contentTabs.SelectedIndex = 6;
        globalNavigation.Controls.Add(deviceHomeButton);

        deviceNavigationTitle.AutoSize = false;
        deviceNavigationTitle.Height = 36;
        deviceNavigationTitle.AutoEllipsis = true;
        deviceNavigationTitle.TextAlign = ContentAlignment.MiddleLeft;
        deviceNavigationTitle.Padding = new Padding(14, 0, 8, 4);
        deviceNavigationTitle.Margin = Padding.Empty;
        deviceNavigationTitle.ForeColor = UiPalette.Ink;
        deviceNavigationTitle.Font = UiPalette.BodyStrongFont;
        deviceNavigationTitle.BackColor = UiPalette.Sidebar;
        deviceNavigationTitle.Text = DeviceCatalog.ProductModel;
        deviceNavigation.Controls.Add(deviceNavigationTitle);

        toolsNavigation.Controls.Add(new Label
        {
            Text = "通用工具",
            AutoSize = false,
            Height = 34,
            ForeColor = UiPalette.Muted,
            Font = UiPalette.MetaStrongFont,
            TextAlign = ContentAlignment.BottomLeft,
            Padding = new Padding(14, 0, 0, 6),
            Margin = Padding.Empty,
            BackColor = UiPalette.Sidebar
        });

        var labels = new[] { "实时监视", "RS485", "程序编译器", "趋势分析", "串口", "通信测试", "CAN", "工具箱", "数据误差分析" };
        for (var index = 0; index < labels.Length; index++)
        {
            var pageIndex = index >= 6 ? index + 1 : index;
            var kind = index == 6 ? NavigationGlyphKind.Can : index == 7 ? NavigationGlyphKind.Tools : index == 8 ? NavigationGlyphKind.Trends : (NavigationGlyphKind)index;
            var item = new NavItem
            {
                Text = labels[index],
                Glyph = kind,
                Height = 42,
                Width = 200,
                Margin = new Padding(0, 0, 0, 2),
                BackColor = UiPalette.Sidebar
            };
            item.Click += (_, _) => contentTabs.SelectedIndex = pageIndex;
            (index < 4 ? deviceNavigation : toolsNavigation).Controls.Add(item);
            navigationButtons[index] = item;
        }
        var fittingNavigation = false;
        navigation.Layout += (_, _) =>
        {
            if (fittingNavigation || navigation.ClientSize.Width <= 0) return;
            fittingNavigation = true;
            try
            {
                foreach (var group in new[] { globalNavigation, deviceNavigation, toolsNavigation })
                    foreach (Control item in group.Controls)
                    {
                        var width = Math.Max(1, navigation.ClientSize.Width - item.Margin.Horizontal);
                        if (item.Width != width) item.Width = width;
                    }
            }
            finally { fittingNavigation = false; }
        };
        var footer = new Label
        {
            Dock = DockStyle.Bottom,
            Height = 30,
            Text = "DAQ-800 · 设备工作台",
            ForeColor = UiPalette.Muted,
            Font = new Font("Microsoft YaHei UI", 8.5F),
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = UiPalette.Sidebar
        };
        sidebar.Controls.Add(navigation);
        sidebar.Controls.Add(footer);
        sidebar.Controls.Add(brand);
        return sidebar;
    }
    private void UpdateNavigationSelection()
    {
        UpdateWorkspaceShell();
        var home = contentTabs.SelectedIndex == 6;
        deviceHomeButton.BackColor = UiPalette.Sidebar;
        deviceHomeButton.Active = home;
        deviceManagerButton.BackColor = home ? UiPalette.AccentSoft : UiPalette.Surface;
        deviceManagerButton.ForeColor = home ? UiPalette.AccentText : UiPalette.Ink;
        for (var index = 0; index < navigationButtons.Length; index++)
        {
            var button = navigationButtons[index];
            if (button is null) continue;
            var active = (index >= 6 ? index + 1 : index) == contentTabs.SelectedIndex;
            button.BackColor = UiPalette.Sidebar;
            button.ForeColor = active ? UiPalette.AccentText : UiPalette.InkSecondary;
            if (button is NavItem navItem) navItem.Active = active;
        }
    }

    private static void AddFieldGroup(FlowLayoutPanel container, string text, Color background, params Control[] inputs)
    {
        var group = new TableLayoutPanel
        {
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = inputs.Length + 1, RowCount = 1,
            Margin = new Padding(0, 4, UiPalette.Space5, 4), Padding = Padding.Empty,
            BackColor = background
        };
        group.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        group.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        group.Controls.Add(new Label
        {
            Text = text, AutoSize = true, ForeColor = UiPalette.Muted,
            Font = UiPalette.MetaFont,
            Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, 10, 0)
        }, 0, 0);
        for (var index = 0; index < inputs.Length; index++)
        {
            inputs[index].Anchor = AnchorStyles.Left;
            inputs[index].Margin = new Padding(0, 0, index == inputs.Length - 1 ? 0 : 6, 0);
            group.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            group.Controls.Add(inputs[index], index + 1, 0);
        }
        container.Controls.Add(group);
    }

    private Control BuildHeader()
    {
        var header = new CardPanel
        {
            Name = "connectionHeader",
            Dock = DockStyle.Fill,
            BackColor = UiPalette.Surface,
            FillColor = UiPalette.Surface,
            Radius = UiPalette.CardCornerRadius,
            Padding = new Padding(UiPalette.Space6, UiPalette.Space4, UiPalette.Space5, UiPalette.Space4),
            Margin = new Padding(UiPalette.Space5, UiPalette.Space5, UiPalette.Space5, UiPalette.Space2),
            Font = UiPalette.BodyFont
        };
        var overview = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = UiPalette.Surface,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        overview.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        overview.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        overview.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        // Identity block keeps a fixed rhythm: one 18pt line plus one muted meta line.
        var identity = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = UiPalette.Surface,
            Margin = new Padding(0, 0, UiPalette.Space6, 0),
            Padding = Padding.Empty
        };
        identity.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        identity.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
        identity.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        workspaceTitle.AutoSize = false;
        workspaceTitle.Dock = DockStyle.Fill;
        workspaceTitle.AutoEllipsis = true;
        workspaceTitle.TextAlign = ContentAlignment.MiddleLeft;
        workspaceTitle.Text = "设备工作台";
        workspaceTitle.ForeColor = UiPalette.Ink;
        workspaceTitle.Font = UiPalette.PageTitleFont;
        workspaceTitle.Margin = Padding.Empty;
        activeDeviceLabel.AutoSize = false;
        activeDeviceLabel.Dock = DockStyle.Fill;
        activeDeviceLabel.AutoEllipsis = true;
        activeDeviceLabel.TextAlign = ContentAlignment.MiddleLeft;
        activeDeviceLabel.Text = "从「我的设备」选择一台采集终端开始";
        activeDeviceLabel.ForeColor = UiPalette.Muted;
        activeDeviceLabel.Font = UiPalette.MetaFont;
        activeDeviceLabel.Margin = Padding.Empty;
        identity.Controls.Add(workspaceTitle, 0, 0);
        identity.Controls.Add(activeDeviceLabel, 0, 1);
        overview.Controls.Add(identity, 0, 0);
        // Both lines stay single-line and truncate with an ellipsis, so the card keeps a
        // predictable height at any window width.

        var actions = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            Margin = new Padding(UiPalette.Space6, 0, 0, 0),
            Padding = Padding.Empty,
            Anchor = AnchorStyles.Right,
            BackColor = UiPalette.Surface
        };
        void StyleHeaderButton(Button button, string text, int width, bool primary = false)
        {
            button.Text = text;
            button.AutoSize = true;
            button.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            button.MinimumSize = new Size(width, 38);
            button.Padding = new Padding(14, 0, 14, 0);
            button.Margin = new Padding(0, 0, UiPalette.Space2, 0);
            button.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            UiPalette.StyleButton(button, primary);
        }
        headerStatusPill.Size = new Size(112, 26);
        headerStatusPill.Font = UiPalette.MetaFont;
        headerStatusPill.SetState("设备总览", UiPalette.Muted, UiPalette.SurfaceMuted);
        StyleHeaderButton(deviceManagerButton, "我的设备", 104);
        deviceManagerButton.Click += (_, _) => contentTabs.SelectedIndex = 6;
        StyleHeaderButton(connectionSummaryButton, "网络设置 ▾", 116);
        connectionSummaryButton.Click += (_, _) => ToggleConnectionDetails();
        StyleHeaderButton(connectButton, "连接以太网", 126, primary: true);
        connectButton.Click += OnConnectClicked;
        actions.Controls.Add(deviceManagerButton);
        actions.Controls.Add(connectionSummaryButton);
        actions.Controls.Add(connectButton);
        overview.Controls.Add(actions, 1, 0);
        header.Controls.Add(overview);
        headerHost = header;
        return header;
    }

    private Control BuildConnectionDrawer()
    {
        var drawer = new CardPanel
        {
            Name = "connectionDrawer",
            Dock = DockStyle.Fill,
            FillColor = UiPalette.Surface,
            Radius = 12,
            Padding = new Padding(UiPalette.Space6, UiPalette.Space3, UiPalette.Space5, UiPalette.Space3),
            Margin = new Padding(UiPalette.Space5, 0, UiPalette.Space5, UiPalette.Space2),
            Visible = false
        };
        var bar = connectionBar;
        bar.Dock = DockStyle.Fill;
        bar.AutoSize = true;
        bar.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        bar.FlowDirection = FlowDirection.LeftToRight;
        bar.WrapContents = true;
        bar.BackColor = UiPalette.Surface;
        bar.Margin = Padding.Empty;
        bar.Padding = Padding.Empty;
        bar.Visible = false;


        hostInput.Width = 148;
        hostInput.Text = "192.168.1.10";
        hostInput.BorderStyle = BorderStyle.FixedSingle;
        refreshPortsButton.Text = "恢复默认";
        refreshPortsButton.AutoSize = true;
        refreshPortsButton.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        refreshPortsButton.MinimumSize = new Size(78, 32);
        refreshPortsButton.Padding = new Padding(12, 0, 12, 0);
        refreshPortsButton.Margin = Padding.Empty;
        refreshPortsButton.Anchor = AnchorStyles.Left;
        UiPalette.StyleButton(refreshPortsButton);
        refreshPortsButton.Click += (_, _) => hostInput.Text = "192.168.1.10";
        AddFieldGroup(bar, "板卡 IP", UiPalette.Surface, hostInput, refreshPortsButton);
        ConfigureNumeric(ethernetPortInput, 1, 65535, 5000, 84);
        AddFieldGroup(bar, "端口", UiPalette.Surface, ethernetPortInput);
        ConfigureNumeric(slaveAddressInput, 1, 247, 1, 64);
        AddFieldGroup(bar, "Unit ID", UiPalette.Surface, slaveAddressInput);
        drawer.Controls.Add(bar);
        return drawer;
    }

    private void ToggleConnectionDetails()
    {
        connectionExpanded = !connectionExpanded;
        UpdateWorkspaceShell();
        UpdateConnectionSummary();
    }

    private void UpdateConnectionSummary()
    {
        connectionSummaryButton.Text = $"网络设置 {(connectionExpanded ? "▴" : "▾")}";
        connectionSummaryButton.ForeColor = UiPalette.Ink;
    }

    private Control BuildStatusBar()
    {
        statusStrip.Dock = DockStyle.Fill;
        statusStrip.GripStyle = ToolStripGripStyle.Hidden;
        statusStrip.SizingGrip = false;
        statusStrip.CanOverflow = false;
        statusStrip.AutoSize = false;
        statusStrip.BackColor = UiPalette.Canvas;
        statusStrip.Padding = new Padding(UiPalette.Space5, 0, UiPalette.Space4, 0);
        statusLabel.Text = "未连接";
        statusLabel.ForeColor = UiPalette.Muted;
        statusLabel.Font = UiPalette.MetaFont;
        statusLabel.Spring = true;
        statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        headerStatusPill.Size = new Size(126, 24);
        headerStatusPill.Font = UiPalette.MetaFont;
        headerStatusPill.Margin = Padding.Empty;
        headerStatusPill.SetState("设备总览", UiPalette.Muted, UiPalette.SurfaceMuted);
        statusStrip.Items.Add(new ToolStripControlHost(headerStatusPill)
        {
            AutoSize = false,
            Size = new Size(126, 24),
            Margin = new Padding(0, 5, UiPalette.Space3, 4)
        });
        statusStrip.Items.Add(statusLabel);
        logToggleButton.Text = "隐藏日志";
        logToggleButton.DisplayStyle = ToolStripItemDisplayStyle.Text;
        logToggleButton.ForeColor = UiPalette.Muted;
        logToggleButton.Font = UiPalette.MetaFont;
        logToggleButton.Margin = new Padding(UiPalette.Space3, 0, 0, 0);
        logToggleButton.Click += (_, _) => ToggleCommunicationLog();
        statusStrip.Items.Add(logToggleButton);
        return statusStrip;
    }

    private Control BuildWorkspaceWithLog()
    {
        workspaceSplit.Dock = DockStyle.Fill;
        workspaceSplit.Orientation = Orientation.Horizontal;
        workspaceSplit.FixedPanel = FixedPanel.Panel2;
        workspaceSplit.SplitterWidth = 12;
        workspaceSplit.BackColor = UiPalette.Canvas;
        workspaceSplit.Panel1MinSize = 220;
        workspaceSplit.Panel2MinSize = 120;
        workspaceSplit.Panel2.Padding = new Padding(UiPalette.Space5, 0, UiPalette.Space5, UiPalette.Space3);
        workspaceSplit.Panel1.Controls.Add(BuildContent());
        workspaceSplit.Panel2.Controls.Add(BuildLogPanel());

        Shown += (_, _) =>
        {
            var preferredLogHeight = Math.Clamp(workspaceSplit.Height / 4, 130, 220);
            SetPreferredSplitterDistance(workspaceSplit,
                Math.Max(workspaceSplit.Panel1MinSize, workspaceSplit.Height - preferredLogHeight));
        };
        return workspaceSplit;
    }

    private void ToggleCommunicationLog()
    {
        MarkWorkbenchDirty();
        communicationLogVisible = !communicationLogVisible;
        UpdateCommunicationLogVisibility();
    }

    private void UpdateCommunicationLogVisibility()
    {
        var hasOwnLog = contentTabs.SelectedIndex is 2 or 4 or 5 or 6 or 7 or 8 or 9;
        var collapsed = hasOwnLog || !communicationLogVisible;
        if (workspaceSplit.Panel2Collapsed != collapsed) workspaceSplit.Panel2Collapsed = collapsed;
        logToggleButton.Visible = !hasOwnLog;
        logToggleButton.Text = communicationLogVisible ? "隐藏日志" : "显示日志";
    }

    private Control BuildContent()
    {
        var tabs = contentTabs;
        tabs.Dock = DockStyle.Fill;
        tabs.Padding = new Point(0, 0);
        tabs.DrawMode = TabDrawMode.OwnerDrawFixed;
        tabs.SizeMode = TabSizeMode.Fixed;
        tabs.Appearance = TabAppearance.FlatButtons;
        tabs.ItemSize = new Size(1, 1);
        tabs.DrawItem += DrawPrimaryTab;
        TabPage? errorPage = null;
        tabs.SelectedIndexChanged += (_, _) =>
        {
            if (tabs.SelectedIndex == 9 && errorPage is not null && errorPage.Controls.Count == 0)
            {
                var content = BuildErrorAnalysisTool();
                errorPage.Controls.Add(content);
                UiPalette.ApplyWorkbenchTheme(content);
            }
            if (tabs.SelectedIndex == 6) deviceWorkspaceOpen = false;
            UpdateNavigationSelection();
            UpdateCommunicationLogVisibility();
            if (latestSnapshotForDisplay is { } latest) ApplySnapshotDisplay(latest, forcePageRefresh: true);
            var selectedPage = tabs.SelectedIndex;
            var refreshVersion = ++pendingPageRefreshVersion;
            if (selectedPage is 3 or 4 or 7)
                Ui(() =>
                {
                    if (IsDisposed || Disposing || refreshVersion != pendingPageRefreshVersion || tabs.SelectedIndex != selectedPage) return;
                    if (selectedPage == 3) RefreshTrendChannels();
                    if (selectedPage == 4) RefreshSerialWaveform();
                    if (selectedPage == 7 && !canAdapter.IsOpen && canChannelInput.Items.Count == 0) RefreshCanChannels();
                });
        };

        var monitorPage = new TabPage("实时监视") { BackColor = UiPalette.Canvas, Padding = new Padding(UiPalette.Space5, UiPalette.Space2, UiPalette.Space5, UiPalette.Space4) };
        monitorPage.Controls.Add(BuildDataPanel());

        var logicPage = new TabPage("程序编译器") { BackColor = UiPalette.Canvas, Padding = new Padding(UiPalette.Space5, UiPalette.Space2, UiPalette.Space5, UiPalette.Space4) };
        logicPage.Controls.Add(BuildLogicPage());

        var rs485Page = new TabPage("RS485传感器") { BackColor = UiPalette.Canvas, Padding = new Padding(UiPalette.Space5, UiPalette.Space2, UiPalette.Space5, UiPalette.Space4) };
        rs485Page.Controls.Add(BuildRs485Page());

        tabs.TabPages.Add(monitorPage);
        tabs.TabPages.Add(rs485Page);
        tabs.TabPages.Add(logicPage);
        var trendPage = new TabPage("趋势记录") { BackColor = UiPalette.Canvas, Padding = new Padding(UiPalette.Space5, UiPalette.Space2, UiPalette.Space5, UiPalette.Space4) };
        trendPage.Controls.Add(BuildTrendPage());
        tabs.TabPages.Add(trendPage);
        var serialPage = new TabPage("串口") { BackColor = UiPalette.Surface, Padding = Padding.Empty };
        serialPage.Controls.Add(BuildSerialPage());
        tabs.TabPages.Add(serialPage);
        var testPage = new TabPage("测试") { BackColor = UiPalette.Canvas, Padding = new Padding(UiPalette.Space5, UiPalette.Space2, UiPalette.Space5, UiPalette.Space4) };
        testPage.Controls.Add(BuildTestPage());
        tabs.TabPages.Add(testPage);
        var devicesPage = new TabPage("设备管理") { BackColor = UiPalette.Canvas, Padding = new Padding(UiPalette.Space5, UiPalette.Space2, UiPalette.Space5, UiPalette.Space4) };
        devicesPage.Controls.Add(BuildDevicePage());
        tabs.TabPages.Add(devicesPage);
        var canPage = new TabPage("CAN") { BackColor = UiPalette.Canvas, Padding = new Padding(UiPalette.Space5, UiPalette.Space2, UiPalette.Space5, UiPalette.Space4) };
        canPage.Controls.Add(BuildCanPage());
        tabs.TabPages.Add(canPage);
        var toolsPage = new TabPage("工具箱") { BackColor = UiPalette.Canvas, Padding = new Padding(UiPalette.Space5, UiPalette.Space2, UiPalette.Space5, UiPalette.Space4) };
        toolsPage.Controls.Add(BuildNetworkToolbox());
        tabs.TabPages.Add(toolsPage);
        errorPage = new TabPage("数据误差分析") { BackColor = UiPalette.Canvas, Padding = new Padding(UiPalette.Space5, UiPalette.Space2, UiPalette.Space5, UiPalette.Space4) };
        tabs.TabPages.Add(errorPage);
        // Page content already carries its own card surfaces; keep the tab body untouched.
        tabs.SelectedIndex = 6;
        tabs.Selecting += (_, e) =>
        {
            if (e.TabPageIndex is >= 0 and < 4 && selectedDeviceId is null) e.Cancel = true;
        };
        UpdateNavigationSelection();
        UpdateCommunicationLogVisibility();
        return tabs;
    }

    private void DrawPrimaryTab(object? sender, DrawItemEventArgs e)
    {
        if (contentTabs.ItemSize.Height <= 1) return;
        var selected = e.Index == contentTabs.SelectedIndex;
        var bounds = contentTabs.GetTabRect(e.Index);
        using var background = new SolidBrush(selected ? Color.White : Color.FromArgb(231, 239, 244));
        e.Graphics.FillRectangle(background, bounds);
        using var border = new Pen(Color.FromArgb(206, 220, 229));
        e.Graphics.DrawLine(border, bounds.Left, bounds.Bottom - 1, bounds.Right, bounds.Bottom - 1);
        if (selected)
        {
            using var accent = new SolidBrush(Color.FromArgb(59, 189, 245));
            e.Graphics.FillRectangle(accent, bounds.Left + 8, bounds.Bottom - 4, bounds.Width - 16, 3);
        }
        using var tabFont = new Font(Font, selected ? FontStyle.Bold : FontStyle.Regular);
        TextRenderer.DrawText(e.Graphics, contentTabs.TabPages[e.Index].Text, tabFont, bounds,
            selected ? Color.FromArgb(17, 62, 83) : Color.FromArgb(78, 100, 115),
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }

    private Control BuildDataPanel()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, BackColor = UiPalette.Canvas,
            Margin = Padding.Empty, Padding = Padding.Empty
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 116));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var pageHeader = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2,
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = UiPalette.Canvas, Margin = Padding.Empty, Padding = Padding.Empty
        };
        pageHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        pageHeader.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        pageHeader.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        pageHeader.Controls.Add(SectionTitle("实时采集数据", "板卡寄存器实时快照 · 数值随采集周期刷新"), 0, 0);

        samplingBar.Margin = Padding.Empty;
        samplingBar.Dock = DockStyle.Fill;
        samplingBar.AutoSize = true;
        samplingBar.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        samplingBar.BackColor = UiPalette.Canvas;
        samplingBar.FlowDirection = FlowDirection.LeftToRight;
        samplingBar.WrapContents = true;
        ConfigureNumeric(pollIntervalInput, 100, 60000, 250, 84);
        AddFieldGroup(samplingBar, "采集周期(ms)", UiPalette.Canvas, pollIntervalInput);
        void StyleSamplingButton(Button button, string text, int width, bool primary = false)
        {
            button.Text = text;
            button.AutoSize = true;
            button.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            button.MinimumSize = new Size(width, 38);
            button.Padding = new Padding(14, 0, 14, 0);
            button.Margin = new Padding(0, 0, UiPalette.Space2, 6);
            UiPalette.StyleButton(button, primary);
        }
        StyleSamplingButton(pollNowButton, "立即采集", 88);
        pollNowButton.Click += OnPollNowClicked;
        StyleSamplingButton(pollToggleButton, "开始轮询", 96);
        pollToggleButton.Click += OnPollToggleClicked;
        samplingBar.Controls.Add(pollNowButton);
        samplingBar.Controls.Add(pollToggleButton);
        samplingTimingLabel.Text = "实际间隔：--";
        samplingTimingLabel.AutoSize = true;
        samplingTimingLabel.ForeColor = UiPalette.Muted;
        samplingTimingLabel.Font = UiPalette.MetaFont;
        samplingTimingLabel.Margin = new Padding(UiPalette.Space2, 12, 0, 6);
        samplingBar.Controls.Add(samplingTimingLabel);
        var engineeringButton = new Button
        {
            Text = "工程量配置", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(118, 38), Padding = new Padding(14, 0, 14, 0),
            Margin = new Padding(0, 0, 0, 6)
        };
        UiPalette.StyleButton(engineeringButton);
        engineeringButton.Click += (_, _) => EditEngineeringChannels();
        samplingBar.Controls.Add(engineeringButton);
        pageHeader.Controls.Add(samplingBar, 0, 1);
        layout.Controls.Add(pageHeader, 0, 0);

        var kpiRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1, BackColor = UiPalette.Canvas,
            Margin = new Padding(0, UiPalette.Space3, 0, UiPalette.Space2), Padding = Padding.Empty
        };
        kpiRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        kpiLink.Caption = "链路状态";
        kpiLink.Value = "--";
        kpiLink.Hint = "等待连接板卡";
        kpiInterval.Caption = "实际采集间隔";
        kpiInterval.Value = "--";
        kpiInterval.Hint = "目标周期见上方设置";
        kpiAd.Caption = "AD7606 通道";
        kpiAd.Value = "0/8";
        kpiAd.Hint = "8 路 16 位同步采样";
        kpiIo.Caption = "数字 IO";
        kpiIo.Value = "IN 0 · OUT 0";
        kpiIo.Hint = "输入位图 / 输出位图";
        var tiles = new[] { kpiLink, kpiInterval, kpiAd, kpiIo };
        for (var index = 0; index < tiles.Length; index++)
        {
            kpiRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
            tiles[index].Dock = DockStyle.Fill;
            tiles[index].Margin = new Padding(0, 0, index == tiles.Length - 1 ? 0 : UiPalette.Space3, 0);
            kpiRow.Controls.Add(tiles[index], index, 0);
        }
        layout.Controls.Add(kpiRow, 0, 1);

        lastPollLabel.AutoSize = false;
        lastPollLabel.Dock = DockStyle.Fill;
        lastPollLabel.Margin = Padding.Empty;
        lastPollLabel.Padding = new Padding(2, 0, 0, 0);
        lastPollLabel.TextAlign = ContentAlignment.MiddleLeft;
        lastPollLabel.Text = "最近接收：--";
        lastPollLabel.ForeColor = UiPalette.Muted;
        lastPollLabel.Font = UiPalette.MetaFont;
        layout.Controls.Add(lastPollLabel, 0, 2);

        var gridCard = new CardPanel
        {
            Dock = DockStyle.Fill,
            FillColor = UiPalette.Surface,
            Radius = UiPalette.CardCornerRadius,
            Padding = new Padding(UiPalette.Space2, UiPalette.Space2, UiPalette.Space2, UiPalette.Space2),
            Margin = Padding.Empty
        };
        ConfigureDataGrid();
        gridCard.Controls.Add(dataGrid);
        layout.Controls.Add(gridCard, 0, 3);
        return layout;
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
        dataGrid.BackgroundColor = UiPalette.Surface;
        dataGrid.BorderStyle = BorderStyle.None;
        dataGrid.GridColor = UiPalette.Border;
        dataGrid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None;
        ConfigureOperationalGridStyle(dataGrid);
        dataGrid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = UiPalette.Surface,
            ForeColor = UiPalette.Muted,
            Font = UiPalette.MetaStrongFont,
            Alignment = DataGridViewContentAlignment.MiddleLeft,
            Padding = new Padding(14, 0, 8, 0)
        };
        dataGrid.EnableHeadersVisualStyles = false;
        dataGrid.Columns.Add("name", "数据项");
        dataGrid.Columns.Add("address", "寄存器");
        dataGrid.Columns.Add("value", "当前值");
        dataGrid.Columns.Add("description", "说明");
        dataGrid.Columns[0].Width = 210;
        dataGrid.Columns[1].Width = 110;
        dataGrid.Columns[2].Width = 230;
        dataGrid.Columns[3].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;

        AddDataRow("设备状态", "0x0000", "--", "bit1 AD7606 就绪，bit3 最近读取成功", "status");
        AddDataRow("数字输入", "0x0001", "--", "8 路输入位图", "digitalInputs");
        AddDataRow("数字输出", "0x0002", "--", "8 路输出位图，由 IO 联合控制器驱动", "digitalOutputs");
        AddDataRow("联合控制器", "0x0077", "--", "运行状态、外部编码模式与当前模式", "ioControllerState");
        AddDataRow("联合控制器标志", "0x0079", "--", "规则引擎与数字 IO 诊断标志", "ioControllerFlags");
        AddDataRow("模拟电压输出", "0x007A", "--", "规则引擎当前设定值，单位 mV", "analogVoltage");
        AddDataRow("模拟电流输出", "0x007B", "--", "规则引擎当前设定值，单位 uA", "analogCurrent");
        for (var index = 0; index < 8; index++)
        {
            AddDataRow($"AD7606 CH{index + 1}", $"0x{0x10 + index:X4}", "--", "有符号原始采样值", $"ad{index}");
        }

        AddDataRow("链路·板卡接收帧", "0x0060", "--", "RS485 主机轮询外部传感器帧数", "statRx");
        AddDataRow("链路·板卡坏帧", "0x0061", "--", "CRC/地址/长度校验失败帧数（需正式固件）", "statBad");
        AddDataRow("链路·板卡丢帧", "0x0062", "--", "接收队列溢出丢弃帧数（需正式固件）", "statDrop");
        AddDataRow("链路·板卡发送帧", "0x0063", "--", "RS485 主机发送轮询帧数", "statTx");
    }

    private static void ConfigureOperationalGridStyle(DataGridView grid)
    {
        var selection = UiPalette.AccentSoft;
        var selectedText = UiPalette.Ink;
        grid.RowTemplate.Height = 44;
        grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        grid.ColumnHeadersHeight = 42;
        grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        grid.GridColor = UiPalette.BorderSoft;
        grid.DefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = UiPalette.Surface,
            ForeColor = UiPalette.Ink,
            SelectionBackColor = selection,
            SelectionForeColor = selectedText,
            Padding = new Padding(13, 0, 8, 0),
            Alignment = DataGridViewContentAlignment.MiddleLeft
        };
        grid.AlternatingRowsDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = UiPalette.Surface,
            ForeColor = UiPalette.Ink,
            SelectionBackColor = selection,
            SelectionForeColor = selectedText
        };
    }

    private Control BuildLogPanel()
    {
        var panel = new CardPanel
        {
            Dock = DockStyle.Fill,
            FillColor = UiPalette.Surface,
            Radius = UiPalette.CardCornerRadius,
            Padding = new Padding(UiPalette.Space3, 0, UiPalette.Space3, UiPalette.Space3),
            Margin = Padding.Empty
        };
        logBox.Dock = DockStyle.Fill;
        logBox.BorderStyle = BorderStyle.None;
        logBox.BackColor = UiPalette.Surface;
        logBox.ForeColor = UiPalette.LogText;
        logBox.Font = UiPalette.MonoFont;
        logBox.ReadOnly = true;
        logBox.WordWrap = false;
        logBox.ScrollBars = RichTextBoxScrollBars.Both;
        logBox.DetectUrls = false;
        logBox.Margin = new Padding(0);
        var logHost = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = UiPalette.Surface,
            Padding = new Padding(UiPalette.Space2, UiPalette.Space1, 2, 0)
        };
        logHost.Controls.Add(logBox);
        panel.Controls.Add(logHost);
        panel.Controls.Add(BuildLogHeader());
        return panel;
    }

    private Control BuildLogHeader()
    {
        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 58,
            ColumnCount = 3,
            RowCount = 1,
            BackColor = UiPalette.Surface,
            Padding = new Padding(0, 0, 0, 4),
            Margin = Padding.Empty
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var title = SectionTitle("通信日志", "收发报文与错误事件", UiPalette.Surface);
        title.Dock = DockStyle.Fill;
        title.Margin = Padding.Empty;
        logStatsLabel.AutoSize = true;
        logStatsLabel.Text = "TX 0 · RX 0 · 错误 0";
        logStatsLabel.ForeColor = UiPalette.Muted;
        logStatsLabel.Font = UiPalette.MonoSmallFont;
        logStatsLabel.Anchor = AnchorStyles.Right;
        logStatsLabel.Margin = new Padding(0, 0, UiPalette.Space4, 4);
        logStatsLabel.TextAlign = ContentAlignment.MiddleRight;
        var logButtons = new FlowLayoutPanel
        {
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false,
            FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(0, UiPalette.Space3, 2, UiPalette.Space3),
            Padding = Padding.Empty, BackColor = UiPalette.Surface
        };
        saveLogButton.Text = "保存日志";
        saveLogButton.Size = new Size(88, 30);
        saveLogButton.Margin = new Padding(0, 0, UiPalette.Space2, 0);
        saveLogButton.Font = UiPalette.MetaFont;
        UiPalette.StyleButton(saveLogButton);
        saveLogButton.Click += OnSaveLogClicked;
        clearLogButton.Text = "清空日志";
        clearLogButton.Size = new Size(88, 30);
        clearLogButton.Margin = Padding.Empty;
        clearLogButton.Font = UiPalette.MetaFont;
        UiPalette.StyleButton(clearLogButton);
        clearLogButton.Click += (_, _) =>
        {
            lock (pendingLogLock)
            {
                pendingLogMessages.Clear();
                droppedLogMessages = 0;
            }
            logBox.Clear();
        };
        logButtons.Controls.Add(saveLogButton);
        logButtons.Controls.Add(clearLogButton);
        header.Controls.Add(title, 0, 0);
        header.Controls.Add(logStatsLabel, 1, 0);
        header.Controls.Add(logButtons, 2, 0);
        return header;
    }

    private void UpdateLogStats()
    {
        logStatsLabel.Text = $"TX {txLogCount} · RX {rxLogCount} · 错误 {errorLogCount}";
    }

    private void OnSaveLogClicked(object? sender, EventArgs e)
    {
        if (logBox.TextLength == 0)
        {
            SetStatus("日志为空，无需保存");
            return;
        }

        using var dialog = new SaveFileDialog
        {
            Filter = "文本文件 (*.txt)|*.txt",
            FileName = $"通信日志_{DateTime.Now:yyyyMMdd_HHmmss}.txt",
            Title = "保存通信日志"
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        File.WriteAllText(dialog.FileName, logBox.Text, Encoding.UTF8);
        OnClientLog($"{DateTime.Now:HH:mm:ss.fff}  日志已保存：{dialog.FileName}");
    }

    private static Control SectionTitle(string text) => SectionTitle(text, string.Empty, UiPalette.Surface);

    private static Control SectionTitle(string text, string subtitle) => SectionTitle(text, subtitle, UiPalette.Canvas);

    private static Control SectionTitle(string text, string subtitle, Color background)
    {
        var host = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = background,
            Margin = Padding.Empty,
            Height = subtitle.Length == 0 ? 38 : 60
        };
        host.Controls.Add(new Label
        {
            Dock = DockStyle.Top,
            Height = 32,
            Text = text,
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = UiPalette.CardTitleFont,
            ForeColor = UiPalette.Ink,
            BackColor = background,
            Margin = Padding.Empty
        });
        if (subtitle.Length > 0)
        {
            host.Controls.Add(new Label
            {
                Dock = DockStyle.Bottom,
                Height = 20,
                Text = subtitle,
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = UiPalette.MetaFont,
                ForeColor = UiPalette.Muted,
                BackColor = background,
                Margin = Padding.Empty
            });
        }
        return host;
    }

    private static void AddLabel(FlowLayoutPanel panel, string text)
    {
        panel.Controls.Add(new Label
        {
            AutoSize = false,
            Width = text switch
            {
                "端口" => 34,
                "Unit ID" => 48,
                "轮询(ms)" => 65,
                _ => 54
            },
            Height = 28,
            Text = text,
            TextAlign = ContentAlignment.MiddleLeft,
            BackColor = Color.Transparent,
            ForeColor = UiPalette.Muted,
            Margin = new Padding(5, 0, 1, 0)
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
        if (IsDisposed || Disposing) return;
        lock (pendingLogLock)
        {
            if (pendingLogMessages.Count >= 5000)
            {
                pendingLogMessages.Dequeue();
                droppedLogMessages++;
            }
            pendingLogMessages.Enqueue(message);
        }
    }

    private void FlushCommunicationLog()
    {
        if (logBox.IsDisposed) return;
        var batch = new List<string>(100);
        int dropped;
        lock (pendingLogLock)
        {
            dropped = droppedLogMessages;
            droppedLogMessages = 0;
            while (batch.Count < 100 && pendingLogMessages.Count > 0)
                batch.Add(pendingLogMessages.Dequeue());
        }
        if (dropped > 0)
            batch.Insert(0, $"{DateTime.Now:HH:mm:ss.fff}  ERROR  日志积压，省略较早的 {dropped} 条消息");
        if (batch.Count == 0) return;

        // Batch updates to RichEdit into one repaint, while retaining each coloured log entry.
        var suspendRedraw = logBox.IsHandleCreated && logBox.Visible;
        if (suspendRedraw) SendMessage(logBox.Handle, WM_SETREDRAW, IntPtr.Zero, IntPtr.Zero);
        try
        {
            var run = new StringBuilder();
            Color? runColor = null;
            void AppendRun()
            {
                if (run.Length == 0) return;
                logBox.SelectionStart = logBox.TextLength;
                logBox.SelectionColor = runColor!.Value;
                logBox.AppendText(run.ToString());
                run.Clear();
            }
    
            foreach (var message in batch)
            {
                var color = UiPalette.LogText;
                if (message.Contains("  ERROR  ", StringComparison.Ordinal))
                {
                    color = UiPalette.LogError;
                    errorLogCount++;
                }
                else if (message.Contains("  TX  ", StringComparison.Ordinal))
                {
                    color = UiPalette.LogTx;
                    txLogCount++;
                }
                else if (message.Contains("  RX  ", StringComparison.Ordinal))
                {
                    color = UiPalette.LogRx;
                    rxLogCount++;
                }
    
                if (runColor.HasValue && runColor.Value != color) AppendRun();
                runColor = color;
                run.AppendLine(message);
            }
            AppendRun();
            UpdateLogStats();
    
            var lineCount = logBox.GetLineFromCharIndex(logBox.TextLength);
            if (lineCount > 1200)
            {
                var end = logBox.GetFirstCharIndexFromLine(lineCount - 1000);
                if (end > 0)
                {
                    logBox.Select(0, end);
                    logBox.SelectedText = string.Empty;
                }
            }
            logBox.SelectionStart = logBox.TextLength;
            logBox.ScrollToCaret();
        }
        finally
        {
            if (suspendRedraw && !logBox.IsDisposed && logBox.IsHandleCreated)
            {
                SendMessage(logBox.Handle, WM_SETREDRAW, new IntPtr(1), IntPtr.Zero);
                logBox.Invalidate();
            }
        }
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
        if (!ConfirmLeaveWorkbench() || workbenchSensors is null && !ConfirmLeaveCurrentEditor())
        {
            e.Cancel = true;
            return;
        }
        // Finish the bounded CAN recording queue before the process exits.
        if (canTrace is not null)
        {
            try { canTrace.FinishAsync().GetAwaiter().GetResult(); }
            catch (Exception ex) { MessageBox.Show(this, "CAN 记录不完整：" + ex.Message, "CAN 记录", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            canTrace = null;
        }
        editorDecorationTimer.Stop();
        editorDecorationTimer.Dispose();
        connectCancellation?.Cancel();
        externalProjectTimer.Stop();
        externalProjectTimer.Dispose();
        communicationLogTimer.Stop();
        communicationLogTimer.Dispose();
        StopSimulation();
        StopNetworkTools();
        StopAllCanPlans("程序关闭，周期发送已停止。");
        canTimer.Stop();
        canTimer.Dispose();
        try { canAdapter.Dispose(); } catch (Exception ex) { OnClientLog($"CAN 断开失败：{ex.Message}"); }
        serialTrafficTimer.Stop();
        serialTrafficTimer.Dispose();
        serialConnection.Dispose();
        try
        {
            SaveRs485Configuration(ReadRs485ConfigurationFromGrid());
        }
        catch
        {
            /* Keep the last valid local configuration when cells contain unfinished edits. */
        }
        StopPolling();
        trendRecorder.Dispose();
        client.Dispose();
        pollGate.Dispose();
    }

    private readonly record struct EditorState(string Text, int SelectionStart, int SelectionLength);
    private readonly record struct SourceFileEntry(string FilePath, bool IsLibrary, string ProjectName, string ProjectRoot);
}




