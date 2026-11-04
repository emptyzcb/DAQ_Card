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
    private Control BuildCSourcePanel()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = UiPalette.Surface
        };
        var title = new Label
        {
            Dock = DockStyle.Top,
            Height = 40,
            Text = "工程文件",
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(14, 0, 0, 0),
            Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold),
            ForeColor = Color.FromArgb(44, 62, 68),
            BackColor = Color.FromArgb(248, 250, 250)
        };

        sourceTreeView.Dock = DockStyle.Fill;
        sourceTreeView.BorderStyle = BorderStyle.None;
        sourceTreeView.HideSelection = false;
        sourceTreeView.ShowLines = true;
        sourceTreeView.ShowPlusMinus = true;
        sourceTreeView.ShowRootLines = true;
        sourceTreeView.FullRowSelect = true;
        sourceTreeView.ShowNodeToolTips = true;
        sourceTreeView.BeforeSelect += (_, e) =>
        {
            if (!suppressProjectSwitchPrompt && e.Node != sourceTreeView.SelectedNode && !ConfirmLeaveCurrentEditor()) e.Cancel = true;
        };
        sourceTreeView.AfterSelect += (_, _) => { if (!suppressProjectLoad) LoadCProgramFromSelection(); };
        sourceTreeView.NodeMouseClick += OnSourceTreeNodeMouseClick;
        sourceTreeView.KeyDown += OnSourceTreeKeyDown;
        var treeMenu = new ContextMenuStrip();
        treeMenu.Items.Add("重命名工程", null, (_, _) => RenameSelectedProject());
        treeMenu.Items.Add("刷新工程列表", null, (_, _) => RefreshProjectList());
        treeMenu.Items.Add("重新定位工程", null, (_, _) => RelocateSelectedProject());
        treeMenu.Items.Add("从列表移除", null, (_, _) => RemoveSelectedProject());
        sourceTreeView.ContextMenuStrip = treeMenu;

        var projectsRoot = Path.Combine(AppContext.BaseDirectory, "Projects");
        EnsureProjectsRoot(projectsRoot);
        sourceTreeView.Nodes.Clear();
        TreeNode? defaultSourceNode = null;
        IReadOnlyList<ProjectReference> projects;
        try { projects = projectCatalog.Load(); }
        catch (Exception ex)
        {
            projects = projectCatalog.LoadLocalOnly();
            MessageBox.Show(this, $"工程列表暂时无法读取：{ex.Message}\r\n已只加载本地工程；原列表文件未被覆盖。",
                "工程列表", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        foreach (var project in projects.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
        {
            if (projectCatalog.Inspect(project) == ProjectHealth.Ready)
            {
                EnsureProjectLibrary(project.Path);
                var sourceNode = AddProjectTreeNode(project);
                if (IsDefaultProject(project))
                {
                    defaultSourceNode = sourceNode;
                }
            }
            else
            {
                AddUnavailableProjectTreeNode(project);
            }
        }
        defaultSourceNode ??= sourceTreeView.Nodes.Count > 0
            ? sourceTreeView.Nodes[0].Nodes[0]
            : null;
        if (defaultSourceNode?.Tag is SourceFileEntry defaultEntry)
        {
            currentProjectName = defaultEntry.ProjectName;
            currentUserSourcePath = defaultEntry.FilePath;
            currentProject = defaultSourceNode.Parent?.Tag as ProjectReference;
            sourceTreeView.SelectedNode = defaultSourceNode;
        }
        panel.Controls.Add(sourceTreeView);
        panel.Controls.Add(title);
        return panel;
    }

    private TreeNode AddProjectTreeNode(ProjectReference project)
    {
        var projectName = project.Name;
        var projectRoot = project.Path;
        var projectNode = new TreeNode(projectName) { Tag = project, ToolTipText = projectRoot };
        var libraryNode = new TreeNode("Library");
        foreach (var libraryFileName in LibraryFileNames)
        {
            libraryNode.Nodes.Add(new TreeNode(libraryFileName)
            {
                Tag = new SourceFileEntry(
                    Path.Combine(projectRoot, "Library", libraryFileName),
                    true,
                    projectName,
                    projectRoot)
            });
        }
        var sourceNode = new TreeNode("io_logic_user.c")
        {
            Tag = new SourceFileEntry(
                Path.Combine(projectRoot, "io_logic_user.c"),
                false,
                projectName,
                projectRoot)
        };
        projectNode.Nodes.Add(libraryNode);
        projectNode.Nodes.Add(sourceNode);
        var manualPath = Path.Combine(projectRoot, ProjectManualFileName);
        if (File.Exists(manualPath))
        {
            projectNode.Nodes.Add(new TreeNode(ProjectManualFileName)
            {
                Tag = new SourceFileEntry(manualPath, true, projectName, projectRoot)
            });
        }
        sourceTreeView.Nodes.Add(projectNode);
        projectNode.Expand();
        return sourceNode;
    }

    private TreeNode AddUnavailableProjectTreeNode(ProjectReference project)
    {
        var health = projectCatalog.Inspect(project);
        var detail = health switch
        {
            ProjectHealth.MissingDirectory => "工程目录不可用 · 请重新定位",
            ProjectHealth.MissingSource => "入口文件缺失 · io_logic_user.c",
            _ => "工程身份不匹配 · 请检查目录"
        };
        var node = new TreeNode(project.Name + "（需处理）")
        {
            Tag = project,
            ForeColor = Color.FromArgb(166, 100, 45),
            ToolTipText = project.Path
        };
        node.Nodes.Add(new TreeNode(detail));
        sourceTreeView.Nodes.Add(node);
        node.Expand();
        return node;
    }

    private void RefreshProjectList()
    {
        if (!ConfirmLeaveCurrentEditor()) return;
        IReadOnlyList<ProjectReference> projects;
        try { projects = projectCatalog.Load(); }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"读取工程列表失败：{ex.Message}", "工程列表", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        var selectedId = currentProject?.Id;
        TreeNode? nextSelection = null;
        var previousPrompt = suppressProjectSwitchPrompt;
        suppressProjectSwitchPrompt = true;
        suppressProjectLoad = true;
        sourceTreeView.BeginUpdate();
        try
        {
            sourceTreeView.Nodes.Clear();
            foreach (var project in projects.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
            {
                var node = projectCatalog.Inspect(project) == ProjectHealth.Ready
                    ? AddProjectTreeNode(project)
                    : AddUnavailableProjectTreeNode(project);
                if (project.Id == selectedId) nextSelection = node;
                if (nextSelection is null && IsDefaultProject(project)) nextSelection = node;
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"刷新工程列表失败：{ex.Message}", "工程列表", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            sourceTreeView.EndUpdate();
            suppressProjectLoad = false;
            suppressProjectSwitchPrompt = previousPrompt;
        }

        if (nextSelection is not null) SelectProjectNodeWithoutPrompt(nextSelection);
        SetStatus("工程列表已刷新");
    }

    private void CheckForExternalProjectChanges()
    {
        if (isCheckingExternalChanges || IsDisposed || Disposing) return;
        isCheckingExternalChanges = true;
        try
        {
            var projects = projectCatalog.Load().OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ToArray();
            if (HasProjectTreeChanged(projects)) RebuildProjectTreeWithoutDiscardingEditor(projects);
            CheckForExternalSourceChange();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or JsonException)
        {
            SetStatus($"工程文件检查暂不可用：{ex.Message}");
        }
        finally
        {
            isCheckingExternalChanges = false;
        }
    }

    private bool HasProjectTreeChanged(IReadOnlyList<ProjectReference> projects)
    {
        if (sourceTreeView.Nodes.Count != projects.Count) return true;
        for (var index = 0; index < projects.Count; index++)
        {
            var project = projects[index];
            var node = sourceTreeView.Nodes[index];
            if (node.Tag is not ProjectReference displayed || displayed != project) return true;
            var ready = projectCatalog.Inspect(project) == ProjectHealth.Ready;
            var hasSourceNode = node.Nodes.Cast<TreeNode>().Any(child => child.Tag is SourceFileEntry { IsLibrary: false });
            if (ready != hasSourceNode) return true;
            if (ready && File.Exists(Path.Combine(project.Path, ProjectManualFileName))
                != node.Nodes.Cast<TreeNode>().Any(child => child.Text == ProjectManualFileName)) return true;
        }
        return false;
    }

    private void RebuildProjectTreeWithoutDiscardingEditor(IReadOnlyList<ProjectReference> projects)
    {
        var selected = sourceTreeView.SelectedNode;
        var selectedRoot = selected;
        while (selectedRoot?.Parent is not null) selectedRoot = selectedRoot.Parent;
        var selectedId = (selectedRoot?.Tag as ProjectReference)?.Id ?? currentProject?.Id;
        var reopenRecoveredSource = selectedRoot?.Tag is ProjectReference
            && !selectedRoot.Nodes.Cast<TreeNode>().Any(node => node.Tag is SourceFileEntry { IsLibrary: false })
            && codeEditor.ReadOnly && currentLoadedSourceText is null;
        var selectedFileName = selected?.Tag is SourceFileEntry file
            ? Path.GetFileName(file.FilePath)
            : !codeEditor.ReadOnly || reopenRecoveredSource ? "io_logic_user.c" : null;
        var previousPath = currentProject?.Path;
        TreeNode? recoveredSourceNode = null;

        var previousPrompt = suppressProjectSwitchPrompt;
        var previousLoad = suppressProjectLoad;
        suppressProjectSwitchPrompt = true;
        suppressProjectLoad = true;
        sourceTreeView.BeginUpdate();
        try
        {
            sourceTreeView.Nodes.Clear();
            TreeNode? nextSelection = null;
            foreach (var project in projects)
            {
                var source = projectCatalog.Inspect(project) == ProjectHealth.Ready
                    ? AddProjectTreeNode(project)
                    : AddUnavailableProjectTreeNode(project);
                if (project.Id != selectedId) continue;
                var root = source.Parent ?? source;
                nextSelection = selectedFileName is null ? root : root.Nodes.Cast<TreeNode>()
                    .FirstOrDefault(node => node.Tag is SourceFileEntry entry
                        && Path.GetFileName(entry.FilePath) == selectedFileName) ?? root;
                if (reopenRecoveredSource && nextSelection.Tag is SourceFileEntry { IsLibrary: false })
                    recoveredSourceNode = nextSelection;
                if (currentProject?.Id == project.Id)
                {
                    currentProject = project;
                    currentProjectName = project.Name;
                    currentUserSourcePath = Path.Combine(project.Path, "io_logic_user.c");
                }
            }
            sourceTreeView.SelectedNode = nextSelection;
        }
        finally
        {
            sourceTreeView.EndUpdate();
            suppressProjectLoad = previousLoad;
            suppressProjectSwitchPrompt = previousPrompt;
        }

        if (recoveredSourceNode is not null) LoadCProgramFromSelection();

        if (currentProject is ProjectReference active && active.Id == selectedId)
        {
            if (!string.Equals(previousPath, active.Path, StringComparison.OrdinalIgnoreCase))
            {
                if (!codeEditor.ReadOnly)
                    codeEditorTitle.Text = $"C 语言 IO 用户控制程序 · {active.Name} · 联合控制器 v2";
                SetStatus($"已自动找到工程新位置：{active.Path}");
            }
            else if (projectCatalog.Inspect(active) != ProjectHealth.Ready)
            {
                SetStatus("工程入口文件不可用，当前编辑内容已保留");
            }
            else
            {
                SetStatus("工程文件列表已自动更新");
            }
        }
    }

    private void CheckForExternalSourceChange()
    {
        if (contentTabs.SelectedIndex != 2 || codeEditor.ReadOnly || currentProject is not ProjectReference project
            || currentSourceHash is null || currentLoadedSourceText is null) return;

        if (projectCatalog.Inspect(project) != ProjectHealth.Ready)
        {
            if (!missingSourcePrompted && codeEditor.Text != currentLoadedSourceText)
            {
                missingSourcePrompted = true;
                if (MessageBox.Show(this,
                    "正在编辑的工程源码已在外部移动或改名，当前修改仍保留在编辑器中。是否先另存一份代码副本？\r\n原路径不会被自动重建。",
                    "工程文件已移动", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button1) == DialogResult.Yes) SaveEditorCopy();
            }
            return;
        }

        missingSourcePrompted = false;
        var diskHash = ComputeFileHash(currentUserSourcePath);
        if (diskHash == currentSourceHash)
        {
            dismissedExternalSourceHash = null;
            return;
        }
        if (diskHash == dismissedExternalSourceHash || isCompiling) return;

        var hasLocalChanges = codeEditor.Text != currentLoadedSourceText;
        var answer = externalSourceChangePrompt(hasLocalChanges);

        if (answer == DialogResult.Yes && hasLocalChanges && !SaveEditorCopy())
        {
            dismissedExternalSourceHash = diskHash;
            return;
        }
        if (answer == DialogResult.Yes || (hasLocalChanges && answer == DialogResult.No))
        {
            SetEditorText(File.ReadAllText(currentUserSourcePath));
            currentSourceHash = ComputeFileHash(currentUserSourcePath);
            currentLoadedSourceText = codeEditor.Text;
            dismissedExternalSourceHash = null;
            SetStatus("已加载外部修改的工程源码");
            return;
        }
        dismissedExternalSourceHash = diskHash;
        SetStatus("已保留编辑器内容；保存或编译前仍需处理外部更改");
    }

    private DialogResult ShowExternalSourceChangePrompt(bool hasLocalChanges) => hasLocalChanges
        ? MessageBox.Show(this,
            "工程源码已在上位机外部修改，编辑器中也有未保存内容。\r\n是：先另存当前编辑为副本，再加载外部版本。\r\n否：放弃当前编辑，加载外部版本。\r\n取消：暂时保留当前编辑。",
            "工程文件已更改", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button3)
        : MessageBox.Show(this, "工程源码已在上位机外部修改，是否加载磁盘上的新版本？",
            "工程文件已更改", MessageBoxButtons.YesNo, MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button1);

    private void EnsureProjectsRoot(string projectsRoot)
    {
        Directory.CreateDirectory(projectsRoot);

        var defaultProjectRoot = Path.Combine(projectsRoot, DefaultProjectName);
        var legacyProjectRoot = Path.Combine(projectsRoot, "DAQ_card");
        if (!Directory.Exists(defaultProjectRoot) && Directory.Exists(legacyProjectRoot))
        {
            Directory.Move(legacyProjectRoot, defaultProjectRoot);
            WriteProjectMetadata(defaultProjectRoot, DefaultProjectName);
        }
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
        EnsureProjectManual(defaultProjectRoot);
        EnsureProjectMetadata(defaultProjectRoot, DefaultProjectName);
    }

    private void EnsureProjectLibrary(string projectRoot)
    {
        var libraryDirectory = Path.Combine(projectRoot, "Library");
        Directory.CreateDirectory(libraryDirectory);
        var templateDirectory = Path.Combine(AppContext.BaseDirectory, "Library");
        foreach (var libraryFileName in LibraryFileNames)
        {
            var templatePath = Path.Combine(templateDirectory, libraryFileName);
            var projectLibraryPath = Path.Combine(libraryDirectory, libraryFileName);
            if (!File.Exists(templatePath) || string.Equals(Path.GetFullPath(templatePath), Path.GetFullPath(projectLibraryPath), StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var template = File.ReadAllText(templatePath);
            if (!File.Exists(projectLibraryPath) || !string.Equals(File.ReadAllText(projectLibraryPath), template, StringComparison.Ordinal))
            {
                File.WriteAllText(projectLibraryPath, template);
            }
        }

        var legacyLibraryPath = Path.Combine(libraryDirectory, "io_logic_library.c");
        if (File.Exists(legacyLibraryPath))
        {
            File.Delete(legacyLibraryPath);
        }
    }

    private static void EnsureProjectManual(string projectRoot)
    {
        var manualPath = Path.Combine(projectRoot, ProjectManualFileName);
        if (!File.Exists(manualPath))
        {
            var templatePath = Path.Combine(AppContext.BaseDirectory, "Docs", ProjectManualFileName);
            File.Copy(templatePath, manualPath);
        }
    }

    private static void EnsureProjectMetadata(string projectRoot, string projectName)
    {
        var metadataPath = Path.Combine(projectRoot, "project.json");
        if (File.Exists(metadataPath))
        {
            return;
        }

        WriteProjectMetadata(projectRoot, projectName);
    }

    private static void WriteProjectMetadata(string projectRoot, string projectName)
    {
        var metadataPath = Path.Combine(projectRoot, "project.json");
        File.WriteAllText(
            metadataPath,
            JsonSerializer.Serialize(new
            {
                projectName,
                sourceFile = "io_logic_user.c",
                manualFile = ProjectManualFileName,
                libraryFiles = LibraryFileNames.Select(name => $"Library/{name}").ToArray()
            }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private const string DefaultCProgram = IoLogicExamples.Default;

    private void SeedCProgram()
    {
        if (currentProject is not null && File.Exists(currentUserSourcePath)) LoadCProgramFromSelection();
        else SetEditorText(DefaultCProgram);
        UpdateCodeStatus();
        SetAllIndicators(0, 0);
    }

    private void LoadCProgramFromSelection()
    {
        var selected = sourceTreeView.SelectedNode;
        var projectNode = selected;
        while (projectNode?.Parent is not null) projectNode = projectNode.Parent;
        if (projectNode?.Tag is not ProjectReference project)
        {
            return;
        }

        dismissedExternalSourceHash = null;
        missingSourcePrompted = false;
        currentProject = project;
        currentProjectName = project.Name;
        currentUserSourcePath = Path.Combine(project.Path, "io_logic_user.c");
        if (projectCatalog.Inspect(project) != ProjectHealth.Ready)
        {
            codeEditor.ReadOnly = true;
            currentSourceHash = null;
            currentLoadedSourceText = null;
            codeEditorTitle.Text = $"工程不可用 · {project.Name}";
            SetEditorText($"工程位置：{project.Path}\n\n工程文件不可用。请右键工程选择“重新定位工程”，或检查 io_logic_user.c。\n");
            return;
        }

        if (selected?.Tag is not SourceFileEntry file) return;

        try
        {
            if (file.IsLibrary)
            {
                codeEditor.ReadOnly = true;
                codeEditor.BackColor = UiPalette.CodeReadOnlyBackground;
                codeEditorHost.BackColor = codeEditor.BackColor;
                var isManual = string.Equals(Path.GetFileName(file.FilePath), ProjectManualFileName, StringComparison.OrdinalIgnoreCase);
                codeEditorTitle.Text = isManual ? $"编程手册 · {file.ProjectName}（只读）" : "C 语言 IO 库文件（只读）";
                SetEditorText(File.Exists(file.FilePath)
                    ? File.ReadAllText(file.FilePath)
                    : isManual ? "编程手册文件不存在。" : "/* IO 库文件未部署，请检查程序安装目录。 */");
            }
            else
            {
                codeEditor.ReadOnly = false;
                codeEditor.BackColor = UiPalette.CodeBackground;
                codeEditorHost.BackColor = codeEditor.BackColor;
                codeEditorTitle.Text = $"C 语言 IO 用户控制程序 · {currentProjectName} · 联合控制器 v2";
                SetEditorText(File.ReadAllText(currentUserSourcePath));
                currentSourceHash = ComputeFileHash(currentUserSourcePath);
                currentLoadedSourceText = codeEditor.Text;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            codeEditor.ReadOnly = true;
            currentSourceHash = null;
            currentLoadedSourceText = null;
            codeEditorTitle.Text = $"无法读取工程 · {project.Name}";
            SetEditorText($"无法读取工程文件：{ex.Message}\n请检查文件权限或 OneDrive 状态。\n");
            SetStatus("工程文件暂时无法读取");
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
        if (!ConfirmLeaveCurrentEditor()) return;

        try
        {
            var sourcePath = Path.Combine(projectRoot, "io_logic_user.c");
            Directory.CreateDirectory(projectRoot);
            File.WriteAllText(sourcePath, DefaultCProgram);
            EnsureProjectLibrary(projectRoot);
            EnsureProjectManual(projectRoot);
            EnsureProjectMetadata(projectRoot, projectName);
            var project = projectCatalog.Register(projectRoot);
            var sourceNode = AddProjectTreeNode(project);
            SelectProjectNodeWithoutPrompt(sourceNode);

            currentProjectName = projectName;
            currentUserSourcePath = sourcePath;
            logicCompileLabel.Text = "C逻辑程序：新工程，未编译";
            logicCompileLabel.ForeColor = Color.FromArgb(96, 106, 118);
            SetStatus($"已创建工程：{projectName}");
            OnClientLog($"{DateTime.Now:HH:mm:ss.fff}  已创建 C IO 工程：{projectRoot}");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"新建工程失败：{ex.Message}\r\n已创建的文件不会被自动删除。", "新建工程", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OnSourceTreeNodeMouseClick(object? sender, TreeNodeMouseClickEventArgs e)
    {
        sourceTreeView.SelectedNode = e.Node;
    }

    private void OnSourceTreeKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.F2)
        {
            RenameSelectedProject();
            e.SuppressKeyPress = true;
            e.Handled = true;
            return;
        }

        if (e.KeyCode != Keys.Delete)
        {
            return;
        }

        RemoveSelectedProject();
        e.SuppressKeyPress = true;
        e.Handled = true;
    }

    private void RemoveSelectedProject()
    {
        var selected = sourceTreeView.SelectedNode;
        if (selected is null)
        {
            SetStatus("请先选择要移除的工程");
            return;
        }

        var projectNode = selected;
        while (projectNode.Parent is not null)
        {
            projectNode = projectNode.Parent;
        }

        if (projectNode.Tag is not ProjectReference project) return;
        var projectName = project.Name;
        if (IsDefaultProject(project))
        {
            MessageBox.Show(this, $"{DefaultProjectName} 是默认工程，不能从列表移除。", "移除工程", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var confirm = MessageBox.Show(
            this,
            $"从工程列表移除“{projectName}”？\r\n磁盘上的工程文件不会删除。",
            "从列表移除",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);
        if (confirm != DialogResult.Yes)
        {
            return;
        }
        if (!ConfirmLeaveCurrentEditor()) return;

        try
        {
            projectCatalog.Remove(project.Id);
            sourceTreeView.Nodes.Remove(projectNode);
            suppressProjectSwitchPrompt = true;
            try { SelectDefaultProjectSource(); }
            finally { suppressProjectSwitchPrompt = false; }
            SetStatus($"已从列表移除：{projectName}（磁盘文件保留）");
            OnClientLog($"{DateTime.Now:HH:mm:ss.fff}  已移除 C IO 工程引用：{project.Path}");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"移除失败：{ex.Message}", "移除工程", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void RelocateSelectedProject()
    {
        var node = sourceTreeView.SelectedNode;
        while (node?.Parent is not null) node = node.Parent;
        if (node?.Tag is not ProjectReference project) { SetStatus("请先选择工程"); return; }

        using var dialog = new FolderBrowserDialog
        {
            Description = $"选择“{project.Name}”的新位置（必须是原工程）",
            ShowNewFolderButton = false,
            SelectedPath = Directory.Exists(project.Path) ? project.Path : Path.GetDirectoryName(project.Path) ?? AppContext.BaseDirectory
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        if (!ConfirmLeaveCurrentEditor()) return;
        try
        {
            var allowLegacyBinding = false;
            if (projectCatalog.CanBindLegacyProject(dialog.SelectedPath))
            {
                allowLegacyBinding = MessageBox.Show(this,
                    "所选目录是没有工程 ID 的旧工程，无法自动确认它就是原工程。\r\n确定把它绑定到当前工程引用吗？此操作不会删除或覆盖源码。",
                    "确认旧工程身份", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) == DialogResult.Yes;
                if (!allowLegacyBinding) return;
            }
            if (!projectCatalog.TryRelocate(project.Id, dialog.SelectedPath, out var reason, allowLegacyBinding))
            {
                MessageBox.Show(this, reason, "重新定位失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var updated = projectCatalog.Load().Single(item => item.Id == project.Id);
            sourceTreeView.Nodes.Remove(node);
            var sourceNode = AddProjectTreeNode(updated);
            SelectProjectNodeWithoutPrompt(sourceNode);
            sourceNode.EnsureVisible();
            SetStatus($"工程已重新定位：{updated.Path}");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"重新定位失败：{ex.Message}", "重新定位工程", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void RenameSelectedProject()
    {
        var selected = sourceTreeView.SelectedNode;
        if (selected is null)
        {
            SetStatus("请先选择要重命名的工程");
            return;
        }

        var projectNode = selected;
        while (projectNode.Parent is not null)
        {
            projectNode = projectNode.Parent;
        }

        if (projectNode.Tag is not ProjectReference project) return;
        var oldProjectName = project.Name;
        if (IsDefaultProject(project))
        {
            MessageBox.Show(this, $"{DefaultProjectName} 是默认工程，不能重命名。", "重命名工程", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var projectRoot = project.Path;
        if (projectCatalog.Inspect(project) != ProjectHealth.Ready)
        {
            SetStatus("工程不可用，请先重新定位或修复入口文件");
            return;
        }

        var newProjectName = PromptForProjectName("重命名工程", oldProjectName, "确定");
        if (string.IsNullOrWhiteSpace(newProjectName)
            || string.Equals(oldProjectName, newProjectName, StringComparison.Ordinal))
        {
            return;
        }

        var parentDirectory = Path.GetDirectoryName(Path.GetFullPath(projectRoot));
        if (string.IsNullOrWhiteSpace(parentDirectory))
        {
            SetStatus("无法确定工程的上级目录");
            return;
        }

        var renamedRoot = Path.Combine(parentDirectory, newProjectName);
        if (Directory.Exists(renamedRoot) || File.Exists(renamedRoot))
        {
            MessageBox.Show(this, $"同级目录中已存在“{newProjectName}”。", "重命名工程", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (!ConfirmLeaveCurrentEditor()) return;

        var moved = false;
        try
        {
            Directory.Move(projectRoot, renamedRoot);
            moved = true;
            if (!projectCatalog.TryRelocate(project.Id, renamedRoot, out var reason))
                throw new InvalidOperationException(reason);
            moved = false;
            var updated = projectCatalog.Load().Single(item => item.Id == project.Id);

            projectNode.Text = newProjectName;
            projectNode.Tag = updated;
            UpdateProjectNodeEntries(projectNode, newProjectName, renamedRoot);
            currentProject = updated;
            currentProjectName = newProjectName;
            currentUserSourcePath = Path.Combine(renamedRoot, "io_logic_user.c");

            var sourceNode = FindUserSourceNode(renamedRoot);
            if (sourceNode is not null)
            {
                SelectProjectNodeWithoutPrompt(sourceNode);
                sourceNode.EnsureVisible();
            }

            codeEditorTitle.Text = $"C 语言 IO 用户控制程序 · {newProjectName}";
            SetStatus($"工程已重命名：{oldProjectName} → {newProjectName}");
            OnClientLog($"{DateTime.Now:HH:mm:ss.fff}  C IO 工程已重命名：{projectRoot} -> {renamedRoot}");
        }
        catch (Exception ex)
        {
            var message = ex.Message;
            if (moved && Directory.Exists(renamedRoot) && !Directory.Exists(projectRoot))
            {
                try { Directory.Move(renamedRoot, projectRoot); }
                catch (Exception rollbackError) { message += $"\r\n工程仍在新位置：{renamedRoot}\r\n恢复旧名称失败：{rollbackError.Message}"; }
            }
            MessageBox.Show(this, $"重命名失败：{message}", "重命名工程", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static void UpdateProjectNodeEntries(TreeNode projectNode, string projectName, string projectRoot)
    {
        foreach (TreeNode child in projectNode.Nodes)
        {
            if (child.Tag is SourceFileEntry entry)
            {
                child.Tag = new SourceFileEntry(
                    Path.Combine(projectRoot, Path.GetFileName(entry.FilePath)),
                    entry.IsLibrary,
                    projectName,
                    projectRoot);
            }

            foreach (TreeNode nestedChild in child.Nodes)
            {
                if (nestedChild.Tag is SourceFileEntry nestedEntry)
                {
                    nestedChild.Tag = new SourceFileEntry(
                        Path.Combine(projectRoot, "Library", Path.GetFileName(nestedEntry.FilePath)),
                        nestedEntry.IsLibrary,
                        projectName,
                        projectRoot);
                }
            }
        }
    }

    private void SelectDefaultProjectSource()
    {
        foreach (TreeNode project in sourceTreeView.Nodes)
        {
            if (project.Tag is not ProjectReference projectReference || !IsDefaultProject(projectReference))
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
        return PromptForProjectName("新建工程", "IO_Project", "创建");
    }

    private string? PromptForProjectName(string title, string initialName, string confirmText)
    {
        using var dialog = new Form
        {
            Text = title,
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MinimizeBox = false,
            MaximizeBox = false,
            ClientSize = new Size(360, 126),
            ShowInTaskbar = false
        };

        var label = new Label { Text = "工程名称：", AutoSize = true, Location = new Point(18, 20) };
        var nameInput = new TextBox { Text = initialName, Location = new Point(92, 16), Width = 238 };
        var okButton = new Button { Text = confirmText, DialogResult = DialogResult.OK, Location = new Point(178, 72), Width = 70 };
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
            MessageBox.Show(this, "工程名称不能为空，也不能包含 \\/:*?\"<>| 等字符。", title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
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

    private static string ComputeFileHash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private bool IsCurrentProjectReady()
    {
        if (currentProject is not ProjectReference project || projectCatalog.Inspect(project) != ProjectHealth.Ready)
        {
            SetStatus("工程文件不可用，请先重新定位或修复入口文件");
            return false;
        }

        return true;
    }

    private bool ConfirmLeaveCurrentEditor()
    {
        if (codeEditor.ReadOnly || currentLoadedSourceText is null || codeEditor.Text == currentLoadedSourceText)
            return true;

        var projectAvailable = currentProject is ProjectReference project && projectCatalog.Inspect(project) == ProjectHealth.Ready;
        var prompt = projectAvailable
            ? "当前代码尚未保存。\r\n是：保存后继续；否：放弃修改；取消：继续编辑。"
            : "当前工程文件不可用，代码只保存在编辑器中。\r\n是：另存为副本后继续；否：放弃修改；取消：继续编辑。";
        var result = MessageBox.Show(this, prompt,
            "未保存的修改", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button3);
        return result switch
        {
            DialogResult.Yes => projectAvailable ? TrySaveCProgram() : SaveEditorCopy(),
            DialogResult.No => true,
            _ => false
        };
    }

    private bool SaveEditorCopy()
    {
        using var dialog = new SaveFileDialog
        {
            FileName = currentProjectName + "_副本.c",
            Filter = "C 源文件 (*.c)|*.c|所有文件 (*.*)|*.*"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return false;
        try
        {
            if (!string.IsNullOrWhiteSpace(currentUserSourcePath)
                && string.Equals(Path.GetFullPath(dialog.FileName), Path.GetFullPath(currentUserSourcePath), StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show(this, "副本不能保存到当前工程源码路径，请选择其他位置。", "另存副本", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            File.WriteAllText(dialog.FileName, codeEditor.Text);
            SetStatus($"代码副本已保存：{dialog.FileName}（原工程未修改）");
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"另存副本失败：{ex.Message}", "另存副本", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
    }

    private void SelectProjectNodeWithoutPrompt(TreeNode node)
    {
        var previous = suppressProjectSwitchPrompt;
        suppressProjectSwitchPrompt = true;
        try { sourceTreeView.SelectedNode = node; }
        finally { suppressProjectSwitchPrompt = previous; }
    }

    private void SaveCProgram() => TrySaveCProgram();

    private bool TrySaveCProgram()
    {
        if (codeEditor.ReadOnly)
        {
            SetStatus("只读文件不可保存，请选择工程源文件");
            return false;
        }

        if (!IsCurrentProjectReady()) return false;

        try
        {
            var diskHash = ComputeFileHash(currentUserSourcePath);
            if (currentSourceHash is not null && diskHash != currentSourceHash)
            {
                var choice = MessageBox.Show(this,
                    "工程源码已在上位机外部修改。\r\n是：将当前编辑内容另存为副本；否：重新加载磁盘文件；取消：继续编辑。\r\n不会自动覆盖外部修改。",
                    "外部文件已变化", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button3);
                if (choice == DialogResult.Yes)
                {
                    using var dialog = new SaveFileDialog { FileName = currentProjectName + "_未合并.c", Filter = "C 源文件 (*.c)|*.c|所有文件 (*.*)|*.*" };
                    if (dialog.ShowDialog(this) != DialogResult.OK) return false;
                    File.WriteAllText(dialog.FileName, codeEditor.Text);
                    currentLoadedSourceText = codeEditor.Text;
                    SetStatus($"已另存副本：{dialog.FileName}（工程源码未覆盖）");
                    return true;
                }
                if (choice == DialogResult.No)
                {
                    SetEditorText(File.ReadAllText(currentUserSourcePath));
                    currentSourceHash = diskHash;
                    currentLoadedSourceText = codeEditor.Text;
                    SetStatus("已重新加载外部修改的源码");
                    return true;
                }
                return false;
            }

            File.WriteAllText(currentUserSourcePath, codeEditor.Text);
            currentSourceHash = ComputeFileHash(currentUserSourcePath);
            currentLoadedSourceText = codeEditor.Text;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"保存失败：{ex.Message}", "保存工程", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }

        logicCompileLabel.Text = "C逻辑程序：已保存";
        OnClientLog($"{DateTime.Now:HH:mm:ss.fff}  C逻辑程序已保存：{currentUserSourcePath}");
        return true;
    }

    private void ImportProject()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "选择包含 io_logic_user.c 的 C IO 工程文件夹",
            ShowNewFolderButton = false,
            SelectedPath = Directory.Exists(Path.GetDirectoryName(currentUserSourcePath))
                ? Path.GetDirectoryName(currentUserSourcePath)!
                : AppContext.BaseDirectory
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            var projectRoot = Path.GetFullPath(dialog.SelectedPath);
            var sourcePath = Path.Combine(projectRoot, "io_logic_user.c");
            if (!File.Exists(sourcePath))
            {
                MessageBox.Show(
                    this,
                    "所选文件夹不是有效工程。\r\n工程根目录必须包含 io_logic_user.c。",
                    "导入工程",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            var projectName = Path.GetFileName(Path.TrimEndingDirectorySeparator(projectRoot));
            if (string.IsNullOrWhiteSpace(projectName))
            {
                projectName = "C_IO_Project";
            }

            if (!ConfirmLeaveCurrentEditor()) return;
            EnsureProjectLibrary(projectRoot);
            EnsureProjectMetadata(projectRoot, projectName);
            var project = projectCatalog.Register(projectRoot);

            var sourceNode = FindUserSourceNode(projectRoot) ?? AddProjectTreeNode(project);
            currentProjectName = projectName;
            currentUserSourcePath = sourcePath;
            SelectProjectNodeWithoutPrompt(sourceNode);
            sourceNode.EnsureVisible();
            LoadCProgramFromSelection();

            logicCompileLabel.Text = "C逻辑程序：工程已导入，未编译";
            logicCompileLabel.ForeColor = Color.FromArgb(96, 106, 118);
            SetStatus($"已导入工程：{projectName}");
            OnClientLog($"{DateTime.Now:HH:mm:ss.fff}  已导入 C IO 工程：{projectRoot}");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"导入失败：{ex.Message}", "导入工程", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private TreeNode? FindUserSourceNode(string projectRoot)
    {
        var normalizedRoot = Path.GetFullPath(projectRoot);
        foreach (TreeNode project in sourceTreeView.Nodes)
        {
            foreach (TreeNode file in project.Nodes)
            {
                if (file.Tag is SourceFileEntry entry
                    && !entry.IsLibrary
                    && string.Equals(Path.GetFullPath(entry.ProjectRoot), normalizedRoot, StringComparison.OrdinalIgnoreCase))
                {
                    return file;
                }
            }
        }

        return null;
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

}
