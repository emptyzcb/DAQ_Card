namespace IndustrialFieldDataAcquisitionTerminal;

internal sealed partial class MainForm
{
    private void StopAllCanPlans(string reason)
    {
        if (canScheduler.RunningIds.Count == 0) return;
        canScheduler.StopAll();
        canStatusLabel.Text = reason;
        if (canPlanStatus is not null && !canPlanStatus.IsDisposed) canPlanStatus.Text = reason;
        RefreshCanPlanGrid();
    }
    private void TickCanPlans()
    {
        if (canScheduler.RunningIds.Count == 0) return;
        if (!canAdapter.IsOpen || canAdapter.IsListenOnly || (canAdapter.BusStatusCode() & (0x10u | 0x200u | 0x04000000u)) != 0)
        { StopAllCanPlans("CAN 连接、监听模式或总线状态不允许发送，周期发送已停止。"); return; }
        try
        {
            foreach (var plan in canScheduler.Due(Environment.TickCount64)) QueueCanFrame(plan.Frame);
        }
        catch (Exception ex) { StopAllCanPlans("周期发送失败并已停止：" + ex.Message); }
    }
    private void RefreshCanPlanGrid()
    {
        if (canPlanGrid is null || canPlanGrid.IsDisposed) return;
        var selected = canPlanGrid.CurrentRow?.Tag is Guid id ? id : Guid.Empty;
        canPlanGrid.Rows.Clear();
        foreach (var plan in canTemplates)
        {
            var f = plan.Frame;
            var row = canPlanGrid.Rows.Add(plan.Name, f.Id.ToString("X"), CanType(f), f.Dlc,
                string.Join(" ", f.Data.Select(b => b.ToString("X2"))), plan.PeriodMs,
                canScheduler.IsRunning(plan.Id) ? "周期发送中" : "已停止");
            canPlanGrid.Rows[row].Tag = plan.Id;
            if (plan.Id == selected) canPlanGrid.CurrentCell = canPlanGrid.Rows[row].Cells[0];
        }
    }
    private CanSendTemplate? SelectedCanPlan() => canPlanGrid?.CurrentRow?.Tag is Guid id ? canTemplates.FirstOrDefault(t => t.Id == id) : null;
    private void ShowCanPlans()
    {
        if (canPlanForm is not null && !canPlanForm.IsDisposed) { canPlanForm.Activate(); return; }
        var dialog = new Form { Text = "CAN 发送计划", StartPosition = FormStartPosition.CenterParent,
            MinimumSize = new Size(960, 500), Size = new Size(1120, 620), Font = Font, BackColor = UiPalette.Surface };
        canPlanForm = dialog;
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(12) };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, WrapContents = false };
        var fileToolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, WrapContents = false };
        var name = new TextBox { Width = 115, PlaceholderText = "计划名称", Margin = new Padding(4, 5, 4, 0) };
        var period = new NumericUpDown { Minimum = 100, Maximum = 3600000, Value = 1000, Width = 88, Margin = new Padding(4, 5, 4, 0) };
        toolbar.Controls.Add(new Label { Text = "名称", AutoSize = true, Margin = new Padding(4, 8, 0, 0) }); toolbar.Controls.Add(name);
        toolbar.Controls.Add(new Label { Text = "周期 ms", AutoSize = true, Margin = new Padding(4, 8, 0, 0) }); toolbar.Controls.Add(period);
        void AddButton(string text, int width, Action action, bool fileRow = false)
        {
            var button = new Button { Text = text, Width = width, Height = 30, Margin = new Padding(2, 2, 4, 0) };
            UiPalette.StyleButton(button); button.Click += (_, _) =>
            {
                try { action(); RefreshCanPlanGrid(); }
                catch (Exception ex) { if (canPlanStatus is not null) canPlanStatus.Text = ex.Message; }
            };
            (fileRow ? fileToolbar : toolbar).Controls.Add(button);
        }
        AddButton("添加当前帧", 100, () =>
        {
            var template = new CanSendTemplate(Guid.NewGuid(), string.IsNullOrWhiteSpace(name.Text) ? $"计划 {canTemplates.Count + 1}" : name.Text.Trim(),
                CanFrameFromInputs(), (int)period.Value);
            CanSendScheduler.Validate(canTemplates.Append(template).ToArray()); canTemplates.Add(template);
            canPlanStatus!.Text = "已添加；周期发送需选中后手动启动。";
        });
        AddButton("替换选中", 88, () =>
        {
            var selected = SelectedCanPlan() ?? throw new InvalidOperationException("请先选择发送计划。");
            if (canScheduler.IsRunning(selected.Id)) throw new InvalidOperationException("请先停止该计划再修改。");
            var updated = selected with { Name = string.IsNullOrWhiteSpace(name.Text) ? selected.Name : name.Text.Trim(), Frame = CanFrameFromInputs(), PeriodMs = (int)period.Value };
            CanSendScheduler.Validate(new[] { updated }); canTemplates[canTemplates.IndexOf(selected)] = updated;
        });
        AddButton("启动选中", 88, () =>
        {
            var selected = SelectedCanPlan() ?? throw new InvalidOperationException("请先选择发送计划。");
            if (!canAdapter.IsOpen || canAdapter.IsListenOnly) throw new InvalidOperationException("请先连接正常模式 CAN；只监听模式不允许发送。");
            if ((canAdapter.BusStatusCode() & (0x10u | 0x200u | 0x04000000u)) != 0) throw new InvalidOperationException("总线状态不允许启动周期发送。");
            canScheduler.Start(selected, Environment.TickCount64); canPlanStatus!.Text = "已启动，第一帧将在一个周期后发送；定时精度受 Windows 负载影响。";
        });
        AddButton("停止选中", 88, () => { var selected = SelectedCanPlan() ?? throw new InvalidOperationException("请先选择发送计划。"); canScheduler.Stop(selected.Id); });
        AddButton("删除选中", 88, () => { var selected = SelectedCanPlan() ?? throw new InvalidOperationException("请先选择发送计划。"); canScheduler.Stop(selected.Id); canTemplates.Remove(selected); });
        AddButton("保存列表", 88, () =>
        {
            using var file = new SaveFileDialog { Filter = "CAN 发送列表 (*.canlist)|*.canlist", DefaultExt = "canlist", AddExtension = true };
            if (file.ShowDialog(dialog) == DialogResult.OK) { CanSendScheduler.Save(file.FileName, canTemplates); canPlanStatus!.Text = "已保存发送列表。"; }
        }, fileRow: true);
        AddButton("打开列表", 88, () =>
        {
            using var file = new OpenFileDialog { Filter = "CAN 发送列表 (*.canlist)|*.canlist" };
            if (file.ShowDialog(dialog) != DialogResult.OK) return;
            var loaded = CanSendScheduler.Load(file.FileName); // Validate before changing the current list.
            canScheduler.StopAll(); canTemplates.Clear(); canTemplates.AddRange(loaded);
            canPlanStatus!.Text = "已打开；所有周期计划处于停止状态。";
        }, fileRow: true);
        root.Controls.Add(toolbar, 0, 0); root.Controls.Add(fileToolbar, 0, 1);
        canPlanGrid = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false,
            AllowUserToDeleteRows = false, RowHeadersVisible = false, MultiSelect = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            BackgroundColor = UiPalette.Surface, BorderStyle = BorderStyle.None };
        ConfigureOperationalGridStyle(canPlanGrid);
        foreach (var (title, width) in new[] { ("名称", 140), ("ID (HEX)", 100), ("类型", 100), ("DLC", 55), ("数据 (HEX)", 210), ("周期 ms", 90), ("状态", 110) })
            canPlanGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = title, Width = width });
        canPlanGrid.CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex < 0 || canPlanGrid.Rows[e.RowIndex].Tag is not Guid id) return;
            var item = canTemplates.FirstOrDefault(x => x.Id == id); if (item is null) return;
            name.Text = item.Name; period.Value = item.PeriodMs;
            canFrameTypeInput.SelectedIndex = item.Frame.Extended ? 1 : 0; canIdInput.Text = item.Frame.Id.ToString("X");
            canRemoteInput.Checked = item.Frame.Remote; canRemoteDlcInput.Value = item.Frame.Dlc;
            canDataInput.Text = string.Join(" ", item.Frame.Data.Select(b => b.ToString("X2")));
            canPlanStatus!.Text = "已载入主界面输入框，可修改后替换或添加。";
        };
        root.Controls.Add(canPlanGrid, 0, 2);
        canPlanStatus = new Label { Dock = DockStyle.Fill, ForeColor = UiPalette.Muted,
            Text = "从主界面输入框添加帧；关闭窗口不停止运行中的计划，断开 CAN 时自动停止。" };
        root.Controls.Add(canPlanStatus, 0, 3); dialog.Controls.Add(root);
        UiPalette.ApplyWorkbenchTheme(dialog);
        dialog.FormClosed += (_, _) => { canPlanForm = null; canPlanGrid = null; canPlanStatus = null; };
        RefreshCanPlanGrid(); dialog.Show(this);
    }
}
