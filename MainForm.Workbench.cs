using System.Text.Json;

namespace IndustrialFieldDataAcquisitionTerminal;

internal sealed partial class MainForm
{
    private Dictionary<Guid, List<Rs485SensorConfig>>? workbenchSensors;
    private string? workbenchPath;
    private string? workbenchRevision;
    private string workbenchName = "未保存工程";
    private bool workbenchDirty;
    private bool restoringWorkbench;
    private string? archivedEditorText;
    private Func<DialogResult> workbenchSavePrompt = null!;
    private readonly ToolStripLabel workbenchCaption = new();
    private readonly ToolStripMenuItem closeWorkbenchItem = new("返回本机环境");
    private readonly ToolStripMenuItem recentWorkbenchItem = new("最近工程");
    private static string workbenchStorageRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "IndustrialFieldDataAcquisitionTerminal");
    private static string WorkbenchCacheRoot => Path.Combine(workbenchStorageRoot, "WorkbenchCopies");
    private static string RecentWorkbenchPath => Path.Combine(workbenchStorageRoot, "recent-workbenches.json");
    private static string LocalCodeRegistry => Path.Combine(AppContext.BaseDirectory, "Projects", "project_locations.json");

    private void InitializeWorkbench()
    {
        workbenchSavePrompt = () => MessageBox.Show(this, "当前工程有未保存的修改。\r\n是：保存后继续；否：放弃工程修改；取消：留在当前工程。",
            "保存工作台工程", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question, MessageBoxDefaultButton.Button3);
        var strip = new ToolStrip
        {
            Dock = DockStyle.Fill,
            GripStyle = ToolStripGripStyle.Hidden,
            AutoSize = false,
            CanOverflow = false,
            BackColor = UiPalette.Surface,
            ForeColor = UiPalette.Ink,
            Padding = new Padding(UiPalette.Space5 - 2, 0, UiPalette.Space4, 0),
            Font = UiPalette.BodyFont,
            Name = "workbenchProjectToolbar"
        };
        var menu = new ToolStripDropDownButton("工程");
        menu.DropDownItems.Add("新建工程", null, (_, _) => NewWorkbench());
        menu.DropDownItems.Add("打开工程…", null, (_, _) => ChooseWorkbench());
        menu.DropDownItems.Add("保存工程", null, (_, _) => SaveWorkbench(false));
        menu.DropDownItems.Add("另存工程…", null, (_, _) => SaveWorkbench(true));
        menu.DropDownItems.Add(new ToolStripSeparator());
        closeWorkbenchItem.Click += (_, _) => CloseWorkbench();
        menu.DropDownItems.Add(closeWorkbenchItem);
        menu.DropDownItems.Add(recentWorkbenchItem);
        strip.Items.Add(menu);
        strip.Items.Add(new ToolStripSeparator());
        strip.Items.Add(new ToolStripButton("打开工程", null, (_, _) => ChooseWorkbench()));
        strip.Items.Add(new ToolStripButton("保存工程", null, (_, _) => SaveWorkbench(false)));
        strip.Items.Add(new ToolStripSeparator());
        strip.Items.Add(new ToolStripSeparator());
        workbenchCaption.ForeColor = UiPalette.Muted;
        workbenchCaption.Font = UiPalette.MetaFont;
        strip.Items.Add(workbenchCaption);
        var stripHost = new Panel
        {
            Dock = DockStyle.Top,
            Height = 42,
            BackColor = UiPalette.Border,
            Padding = new Padding(0, 0, 0, 1),
            Name = "workbenchToolbarHost"
        };
        stripHost.Controls.Add(strip);
        Controls.Add(stripHost);
        LoadRecentWorkbenches();
        UpdateWorkbenchCaption();
        codeEditor.TextChanged += (_, _) => MarkWorkbenchDirty();
        sourceTreeView.AfterSelect += (_, _) => MarkWorkbenchDirty();
        rs485Grid.CellValueChanged += (_, e) =>
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0 || e.ColumnIndex > 7) return;
            MarkWorkbenchDirty();
            if (e.ColumnIndex is 1 or 3 or 4 or 5 or 6) verifiedEngineeringRs485 = null;
        };
        rs485Grid.RowsAdded += (_, _) => { verifiedEngineeringRs485 = null; MarkWorkbenchDirty(); };
        rs485Grid.RowsRemoved += (_, _) => { verifiedEngineeringRs485 = null; MarkWorkbenchDirty(); };
        hostInput.TextChanged += (_, _) => MarkWorkbenchDirty();
        ethernetPortInput.ValueChanged += (_, _) => MarkWorkbenchDirty();
        slaveAddressInput.ValueChanged += (_, _) => MarkWorkbenchDirty();
        pollIntervalInput.ValueChanged += (_, _) => MarkWorkbenchDirty();
        contentTabs.SelectedIndexChanged += (_, _) => MarkWorkbenchDirty();
        trendWindowInput.SelectedIndexChanged += (_, _) => MarkWorkbenchDirty();
        trendChannelTree.AfterCheck += (_, _) => { if (!updatingTrendTree) MarkWorkbenchDirty(); };
    }

    private void MarkWorkbenchDirty()
    {
        if (restoringWorkbench || workbenchSensors is null) return;
        workbenchDirty = true;
        UpdateWorkbenchCaption();
    }

    private void UpdateWorkbenchCaption()
    {
        workbenchCaption.Text = workbenchSensors is null ? "本机环境 · 可保存为工程" :
            $"{workbenchName}{(workbenchDirty ? " *" : "")}  ·  {deviceSnapshot?.Devices.Count ?? 0} 台设备";
        workbenchCaption.ToolTipText = workbenchPath ?? "工程包含设备、RS485 配置、趋势选择与当前联合控制程序。";
        closeWorkbenchItem.Enabled = workbenchSensors is not null;
    }

    private List<Rs485SensorConfig> ReadDeviceSensors(Guid id)
    {
        if (workbenchSensors is not null)
            return workbenchSensors.TryGetValue(id, out var sensors) ? sensors.ToList() : DefaultSensors();
        var path = DeviceSensorConfigurationPath(id);
        var result = File.Exists(path) ? JsonSerializer.Deserialize<List<Rs485SensorConfig>>(File.ReadAllText(path))
            ?? throw new InvalidDataException("该设备的 RS485 配置文件无效。") : DefaultSensors();
        Rs485ConfigurationProtocol.Validate(result);
        return result;
    }

    private static List<Rs485SensorConfig> DefaultSensors() => new() { new("传感器 1", true, 1, 4, 0, 1, 500) };

    private WorkbenchDocument CaptureWorkbench(string name)
    {
        if (deviceSnapshot is null) throw new InvalidOperationException("设备档案尚未正确加载，不能保存不完整工程。");
        if (!rs485Grid.EndEdit()) throw new InvalidOperationException("请先完成 RS485 表格中的编辑。");
        var devices = deviceSnapshot.Devices.Select(profile =>
        {
            if (profile.Id == selectedDeviceId)
                profile = profile with { Host = hostInput.Text.Trim(), Port = (int)ethernetPortInput.Value,
                    UnitId = (byte)slaveAddressInput.Value, PollIntervalMs = (int)pollIntervalInput.Value };
            var sensors = profile.Id == selectedDeviceId ? ReadRs485ConfigurationFromGrid().ToList() : ReadDeviceSensors(profile.Id);
            return new WorkbenchDevice(profile, sensors, ReadEngineeringForDevice(profile.Id));
        }).ToList();
        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        if (currentProject is null || string.IsNullOrWhiteSpace(currentUserSourcePath))
            throw new InvalidOperationException("请先选择有效的联合控制器代码工程。");
        var source = !codeEditor.ReadOnly ? codeEditor.Text : File.ReadAllText(currentUserSourcePath);
        files.Add("io_logic_user.c", source);
        var pending = new Stack<string>();
        pending.Push(currentProject.Path);
        var visitedDirectories = 0;
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            if (++visitedDirectories > 256) throw new InvalidDataException("联合控制代码目录过多，请只保留本工程所需文件。");
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("代码工程包含目录链接，请将需要的文件复制到工程后再保存。");
            foreach (var subdirectory in Directory.EnumerateDirectories(directory)) pending.Push(subdirectory);
            foreach (var path in Directory.EnumerateFiles(directory))
            {
                var relative = Path.GetRelativePath(currentProject.Path, path).Replace('\\', '/');
                if (string.Equals(relative, "io_logic_user.c", StringComparison.OrdinalIgnoreCase) ||
                    Path.GetExtension(path).ToLowerInvariant() is not (".c" or ".h" or ".txt")) continue;
                if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0 || new FileInfo(path).Length > 2 * 1024 * 1024)
                    throw new InvalidDataException($"附属文件过大或为链接：{relative}");
                if (files.Count >= 256) throw new InvalidDataException("联合控制代码最多包含 256 个文件。");
                files.Add(relative, File.ReadAllText(path));
            }
        }
        var document = new WorkbenchDocument(WorkbenchProject.Version, name, devices, selectedDeviceId, deviceWorkspaceOpen,
            contentTabs.SelectedIndex, new(selectedTrendKeys.OrderBy(k => k, StringComparer.Ordinal).ToList(), trendWindowInput.SelectedIndex,
                manualTrendAxisWindow?.TotalSeconds, expandedTrendUnit), new(currentProjectName, files), communicationLogVisible);
        WorkbenchProject.Validate(document);
        return document;
    }

    private bool SaveWorkbench(bool saveAs)
    {
        var path = workbenchPath;
        if (saveAs || path is null)
        {
            using var dialog = new SaveFileDialog { Title = "保存工作台工程", Filter = "工作台工程 (*.daqproj)|*.daqproj",
                DefaultExt = "daqproj", AddExtension = true, FileName = path is null ? "采集实验.daqproj" : Path.GetFileName(path) };
            if (dialog.ShowDialog(this) != DialogResult.OK) return false;
            path = dialog.FileName;
        }
        try
        {
            SaveWorkbenchTo(path);
            SetStatus($"工程已保存：{workbenchName}（包含当前代码与配置）");
            return true;
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "保存工程失败", MessageBoxButtons.OK, MessageBoxIcon.Error); return false; }
    }

    private void SaveWorkbenchTo(string path)
    {
        var document = CaptureWorkbench(Path.GetFileNameWithoutExtension(path));
        var expected = string.Equals(Path.GetFullPath(path), workbenchPath, StringComparison.OrdinalIgnoreCase) ? workbenchRevision : null;
        if (isCompiling) throw new InvalidOperationException("请等待编译/下载完成后再保存工程。");
        var savedFile = sourceTreeView.SelectedNode?.Tag is SourceFileEntry entry && entry.IsLibrary
            ? Path.GetRelativePath(entry.ProjectRoot, entry.FilePath) : "io_logic_user.c";
        var codeCopy = PrepareWorkbenchCode(document.Code);
        var revision = WorkbenchProject.Save(path, document, expected);
        restoringWorkbench = true;
        try { BindWorkbenchCode(codeCopy.Catalog, codeCopy.Project, savedFile); }
        finally { restoringWorkbench = false; }
        workbenchPath = Path.GetFullPath(path);
        workbenchRevision = revision;
        workbenchName = document.Name;
        workbenchSensors = document.Devices.ToDictionary(d => d.Profile.Id, d => d.Sensors.ToList());
        workbenchEngineering = document.Devices.ToDictionary(d => d.Profile.Id, d => (d.Engineering ?? new()).ToList());
        deviceSnapshot = new(document.Devices.Select(d => d.Profile).ToArray(), "workbench");
        RefreshDeviceGrid();
        archivedEditorText = document.Code.Files["io_logic_user.c"];
        workbenchDirty = false;
        UpdateWorkbenchCaption();
        RememberWorkbench(path);
    }

    private bool ConfirmLeaveWorkbench()
    {
        if (workbenchSensors is null) return true;
        rs485Grid.EndEdit();
        if (!codeEditor.ReadOnly && codeEditor.Text != archivedEditorText) workbenchDirty = true;
        if (!workbenchDirty) return true;
        var choice = workbenchSavePrompt();
        return choice == DialogResult.No || choice == DialogResult.Yes && SaveWorkbench(false);
    }

    private void CaptureActiveWorkbenchConnection()
    {
        if (workbenchSensors is null || selectedDeviceId is not Guid id || deviceSnapshot is null) return;
        var updated = deviceSnapshot.Devices.Select(p => p.Id == id ? p with
        {
            Host = hostInput.Text.Trim(), Port = (int)ethernetPortInput.Value,
            UnitId = (byte)slaveAddressInput.Value, PollIntervalMs = (int)pollIntervalInput.Value
        } : p).ToArray();
        DeviceCatalog.Validate(updated);
        deviceSnapshot = new(updated, "workbench");
    }

    private void RequireOfflineWorkbenchSwitch()
    {
        if (client.IsOpen || isConnecting || isCompiling)
            throw new InvalidOperationException("请先断开板卡以太网，并等待编译/下载结束，再切换工程。串口工具可以继续使用。");
    }

    private void ChooseWorkbench()
    {
        using var dialog = new OpenFileDialog { Title = "打开工作台工程", Filter = "工作台工程 (*.daqproj)|*.daqproj" };
        if (dialog.ShowDialog(this) == DialogResult.OK) OpenWorkbench(dialog.FileName);
    }

    private void OpenWorkbench(string path)
    {
        try
        {
            RequireOfflineWorkbenchSwitch();
            var file = WorkbenchProject.Load(path); // Validate fully before replacing the current workspace.
            if (!ConfirmLeaveWorkbench() || workbenchSensors is null && !ConfirmLeaveCurrentEditor()) return;
            file = WorkbenchProject.Load(path); // The save prompt may have just updated this same file.
            ApplyWorkbench(file.Document, Path.GetFullPath(path), file.Revision);
            RememberWorkbench(path);
            SetStatus($"已打开工程：{workbenchName} · 设备保持离线，规则尚未下载");
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "打开工程失败", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void NewWorkbench()
    {
        try
        {
            RequireOfflineWorkbenchSwitch();
            if (!ConfirmLeaveWorkbench() || workbenchSensors is null && !ConfirmLeaveCurrentEditor()) return;
            var files = new Dictionary<string, string> { ["io_logic_user.c"] = DefaultCProgram };
            foreach (var file in LibraryFileNames)
            {
                var path = Path.Combine(AppContext.BaseDirectory, "Library", file);
                if (File.Exists(path)) files["Library/" + file] = File.ReadAllText(path);
            }
            var manual = Path.Combine(AppContext.BaseDirectory, "Docs", ProjectManualFileName);
            if (File.Exists(manual)) files[ProjectManualFileName] = File.ReadAllText(manual);
            ApplyWorkbench(new(WorkbenchProject.Version, "未保存工程", new(), null, false, 6,
                new(new() { "LOOP:SET", "LOOP:IN" }, 1, null, null), new("示例代码", files), true), null, null);
            workbenchDirty = true;
            UpdateWorkbenchCaption();
            SetStatus("新工程已创建，请添加设备并保存工程。");
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "新建工程失败", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private (ProjectCatalog Catalog, ProjectReference Project) PrepareWorkbenchCode(WorkbenchCode code)
    {
        var root = WorkbenchProject.MaterializeCode(code, WorkbenchCacheRoot);
        EnsureProjectMetadata(root, code.Name);
        var catalog = new ProjectCatalog(Path.Combine(Path.GetDirectoryName(root)!, "project_locations.json"));
        return (catalog, catalog.Register(root));
    }

    private void BindWorkbenchCode(ProjectCatalog catalog, ProjectReference project, string selectedFile = "io_logic_user.c")
    {
        var oldSuppress = suppressProjectSwitchPrompt;
        var oldLoad = suppressProjectLoad;
        suppressProjectSwitchPrompt = true;
        suppressProjectLoad = true;
        try
        {
            projectCatalog = catalog;
            sourceTreeView.Nodes.Clear();
            var sourceNode = AddProjectTreeNode(project);
            TreeNode? FindFile(TreeNode node)
            {
                if (node.Tag is SourceFileEntry file && string.Equals(Path.GetRelativePath(project.Path, file.FilePath), selectedFile, StringComparison.OrdinalIgnoreCase)) return node;
                foreach (TreeNode child in node.Nodes) if (FindFile(child) is { } found) return found;
                return null;
            }
            sourceTreeView.SelectedNode = FindFile(sourceNode.Parent!) ?? sourceNode;
        }
        finally { suppressProjectSwitchPrompt = oldSuppress; suppressProjectLoad = oldLoad; }
        LoadCProgramFromSelection();
    }

    private void ApplyWorkbench(WorkbenchDocument document, string? path, string? revision)
    {
        RequireOfflineWorkbenchSwitch();
        WorkbenchProject.Validate(document);
        var codeCopy = PrepareWorkbenchCode(document.Code);
        // All package parsing and file writes above happen before the displayed state changes.
        restoringWorkbench = true;
        externalProjectTimer.Stop();
        try
        {
            StopPolling();
            trendRecorder.StartNewSession();
            trendRecordingFailed = trendPaused = false;
            frozenTrendPoints = null;
            frozenTrendChannels = null;
            trendPauseButton.Text = "暂停查看";
            trendExportButton.Enabled = false;
            trendDeviceEndpoint = null;
            ResetTrendView();
            workbenchSensors = document.Devices.ToDictionary(d => d.Profile.Id, d => d.Sensors.ToList());
            workbenchEngineering = document.Devices.ToDictionary(d => d.Profile.Id, d => (d.Engineering ?? new()).ToList());
            deviceSnapshot = new(document.Devices.Select(d => d.Profile).ToArray(), "workbench");
            selectedDeviceId = null;
            deviceWorkspaceOpen = false;
            rs485Grid.Rows.Clear();
            RefreshDeviceGrid();
            BindWorkbenchCode(codeCopy.Catalog, codeCopy.Project);
            if (document.ActiveDeviceId is Guid id)
                OpenDeviceWorkspace(document.Devices.Single(d => d.Profile.Id == id).Profile);
            deviceWorkspaceOpen = document.DeviceWorkspaceOpen;
            contentTabs.SelectedIndex = document.PageIndex;
            trendWindowInput.SelectedIndex = document.Trend.WindowIndex;
            selectedTrendKeys.Clear();
            selectedTrendKeys.UnionWith(document.Trend.Channels);
            manualTrendAxisWindow = document.Trend.TimeWindowSeconds is double seconds ? TimeSpan.FromSeconds(seconds) : null;
            expandedTrendUnit = document.Trend.ExpandedUnit;
            communicationLogVisible = document.LogVisible;
            lastCompiledImage = null;
            logicCompileLabel.Text = "工程已恢复 · 尚未编译/下载";
            workbenchPath = path;
            workbenchRevision = revision;
            workbenchName = document.Name;
                archivedEditorText = codeEditor.Text;
            workbenchDirty = false;
            UpdateNavigationSelection();
            UpdateCommunicationLogVisibility();
            RefreshTrendChannels();
            UpdateTrendStatus();
            UpdateWorkbenchCaption();
        }
        finally { suppressProjectSwitchPrompt = false; restoringWorkbench = false; externalProjectTimer.Start(); }
    }

    private void CloseWorkbench()
    {
        try
        {
            RequireOfflineWorkbenchSwitch();
            if (!ConfirmLeaveWorkbench()) return;
            restoringWorkbench = true;
            workbenchSensors = null;
            workbenchEngineering = null;
            ClearEngineeringContext();
            workbenchPath = workbenchRevision = archivedEditorText = null;
            workbenchDirty = false;
            selectedDeviceId = null;
            deviceWorkspaceOpen = false;
            trendRecorder.StartNewSession();
            trendPaused = trendRecordingFailed = false;
            frozenTrendChannels = null;
            frozenTrendPoints = null;
            trendPauseButton.Text = "暂停查看";
            trendExportButton.Enabled = false;
            selectedTrendKeys.Clear();
            selectedTrendKeys.UnionWith(new[] { "LOOP:SET", "LOOP:IN" });
            expandedTrendUnit = null;
            trendWindowInput.SelectedIndex = 1;
            ResetTrendView();
            lastCompiledImage = null;
            projectCatalog = new ProjectCatalog(LocalCodeRegistry);
            currentLoadedSourceText = codeEditor.Text;
            RefreshProjectList();
            rs485Grid.Rows.Clear();
            LoadDeviceCatalog();
            LoadRs485Configuration();
            RefreshTrendChannels();
            contentTabs.SelectedIndex = 6;
            UpdateWorkbenchCaption();
            SetStatus("已返回本机环境。");
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "切换环境失败", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        finally { restoringWorkbench = false; }
    }

    private void LoadRecentWorkbenches()
    {
        recentWorkbenchItem.DropDownItems.Clear();
        try
        {
            if (File.Exists(RecentWorkbenchPath))
                foreach (var path in (JsonSerializer.Deserialize<List<string>>(File.ReadAllText(RecentWorkbenchPath)) ?? new()).Take(8))
                {
                    var item = new ToolStripMenuItem(Path.GetFileNameWithoutExtension(path)) { ToolTipText = path };
                    item.Click += (_, _) => OpenWorkbench(path);
                    recentWorkbenchItem.DropDownItems.Add(item);
                }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }
        recentWorkbenchItem.Enabled = recentWorkbenchItem.DropDownItems.Count > 0;
    }

    private void RememberWorkbench(string path)
    {
        try
        {
            var entries = recentWorkbenchItem.DropDownItems.OfType<ToolStripMenuItem>().Select(i => i.ToolTipText)
                .Where(p => !string.IsNullOrWhiteSpace(p)).Cast<string>();
            var paths = new[] { Path.GetFullPath(path) }.Concat(entries).Distinct(StringComparer.OrdinalIgnoreCase).Take(8).ToArray();
            Directory.CreateDirectory(Path.GetDirectoryName(RecentWorkbenchPath)!);
            File.WriteAllText(RecentWorkbenchPath, JsonSerializer.Serialize(paths));
            LoadRecentWorkbenches();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* Recent history must not prevent saving a project. */ }
    }
}
