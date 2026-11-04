using IndustrialFieldDataAcquisitionTerminal;
using System.Reflection;
using System.Text.Json;

internal static class Program
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static void Check(bool valid, string message) { if (!valid) throw new Exception(message); }
    private static void Reject(Action action, string message)
    {
        try { action(); } catch (Exception ex) when (ex is InvalidDataException or IOException or ArgumentException or InvalidOperationException) { return; }
        throw new Exception(message);
    }
    [STAThread]
    private static void Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        var root = Path.Combine(Path.GetTempPath(), "workbench-smoke-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var a = new DeviceProfile(Guid.NewGuid(), "压力采集 A", "DAQ-800", "192.168.1.10", 5000, 1, 250, "中文备注");
        var b = new DeviceProfile(Guid.NewGuid(), "温度采集 B", "DAQ-800", "192.168.1.11", 5001, 2, 500, "第二台");
        var document = new WorkbenchDocument(1, "测试工程", new()
        {
            new(a, new() { new("压力", true, 5, 3, 16, 2, 800) }),
            new(b, new() { new("温度", true, 9, 4, 32, 1, 1000) }, new() {
                new("AD:1", "入口压力", "MPa", 100, 500, 0, 1),
                new(EngineeringChannel.RsSource(0, 9, 4, 32, 0), "液温", "℃", 0, 1000, 0, 100) })
        }, b.Id, true, 3, new(new() { "AD:1", "AO:I", "RS:0:9:04:0" }, 2, 15, "raw"),
            new("控制程序", new() { ["io_logic_user.c"] = IoLogicExamples.Default + "\n// portable 编码\n", ["Library/custom.h"] = "// custom\n", ["编程手册.txt"] = "手册\n" }), false);
        var path = Path.Combine(root, "实验.daqproj");
        var revision = WorkbenchProject.Save(path, document, null);
        var loaded = WorkbenchProject.Load(path);
        Check(loaded.Revision == revision && JsonSerializer.Serialize(loaded.Document) == JsonSerializer.Serialize(document), "roundtrip mismatch");
        var firstCopy = WorkbenchProject.MaterializeCode(document.Code, root);
        var secondCopy = WorkbenchProject.MaterializeCode(document.Code, root);
        Check(firstCopy != secondCopy && File.ReadAllText(Path.Combine(secondCopy, "编程手册.txt")) == "手册\n", "portable source copy mismatch");
        var changed = document with { Name = "另一次保存" };
        var revision2 = WorkbenchProject.Save(path, changed, revision);
        Check(File.Exists(path + ".bak") && WorkbenchProject.Load(path + ".bak").Revision == revision, "backup missing");
        Reject(() => WorkbenchProject.Save(path, document, revision), "external revision conflict was overwritten");
        Check(WorkbenchProject.Load(path).Revision == revision2, "conflict corrupted file");
        Reject(() => WorkbenchProject.Validate(document with { Version = 99 }), "future version accepted");
        WorkbenchProject.Validate(document with { PageIndex = 9 });
        Reject(() => WorkbenchProject.Validate(document with { PageIndex = 10 }), "unknown page accepted");
        Reject(() => WorkbenchProject.Validate(document with { ActiveDeviceId = Guid.NewGuid() }), "missing device accepted");
        Reject(() => WorkbenchProject.Validate(document with { Devices = new() { document.Devices[0], document.Devices[0] } }), "duplicate devices accepted");
        foreach (var invalid in new[] { "../outside.c", "C:/bad.c", "Library/../../bad.h", "Library/CON.h", "Library/evil.exe", "Library\\bad.h" })
        {
            var files = new Dictionary<string,string>(document.Code.Files) { [invalid] = "bad" };
            Reject(() => WorkbenchProject.Validate(document with { Code = document.Code with { Files = files } }), "unsafe path accepted: " + invalid);
        }
        File.WriteAllText(Path.Combine(root, "broken.daqproj"), "{broken");
        try { WorkbenchProject.Load(Path.Combine(root, "broken.daqproj")); throw new Exception("broken JSON accepted"); } catch (JsonException) { }
        revision = WorkbenchProject.Save(path, document, revision2);
        Console.WriteLine("Storage: portable roundtrip, independent copies, backup, stale-write conflict, version/path/schema rejection passed.");

        typeof(MainForm).GetField("workbenchStorageRoot", Private | BindingFlags.Static)!.SetValue(null, Path.Combine(root, "app-state"));
        using var form = new MainForm();
        var type = typeof(MainForm);
        T Field<T>(string name) => (T)type.GetField(name, Private)!.GetValue(form)!;
        void Set(string name, object? value) => type.GetField(name, Private)!.SetValue(form, value);
        object? Invoke(string name, params object?[] values)
        {
            try { return type.GetMethod(name, Private)!.Invoke(form, values); }
            catch (TargetInvocationException ex) { throw ex.InnerException!; }
        }
        form.Show();
        var localPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "IndustrialFieldDataAcquisitionTerminal", "devices.json");
        var localBefore = File.Exists(localPath) ? File.ReadAllText(localPath) : null;
        Invoke("ApplyWorkbench", document, path, revision);
        Application.DoEvents();
        Check(Field<Guid?>("selectedDeviceId") == b.Id, "active device not restored");
        var scales = Field<List<EngineeringChannel>>("currentEngineering");
        Check(scales.Count == 2 && Math.Abs(scales[0].Convert(300) - .5) < 1e-9, "engineering settings not restored");
        var activeEngineering = (Dictionary<string, EngineeringChannel>)Invoke("EffectiveEngineeringChannels")!;
        Check(activeEngineering.ContainsKey("AD:1") && !activeEngineering.Keys.Any(x => x.StartsWith("RS:")), "RS conversion enabled without verified board configuration");
        Set("verifiedEngineeringRs485", document.Devices[1].Sensors.ToList());
        activeEngineering = (Dictionary<string, EngineeringChannel>)Invoke("EffectiveEngineeringChannels")!;
        Check(activeEngineering.ContainsKey("RS:0:9:04:0"), "verified RS conversion not enabled");
        var originalGrid = Field<DataGridView>("rs485Grid");
        originalGrid.Rows[0].Cells["start"].Value = "0x0021";
        activeEngineering = (Dictionary<string, EngineeringChannel>)Invoke("EffectiveEngineeringChannels")!;
        Check(!activeEngineering.ContainsKey("RS:0:9:04:0"), "changed register applied stale calibration");
        originalGrid.Rows[0].Cells["start"].Value = "0x0020";
        Check(Field<TextBox>("hostInput").Text == b.Host && Field<NumericUpDown>("ethernetPortInput").Value == b.Port, "endpoint not restored");
        Check(!Field<ModbusTcpClient>("client").IsOpen && !Field<bool>("isConnecting"), "opening project connected hardware");
        Check(Field<TabControl>("contentTabs").SelectedIndex == 3, "page not restored");
        Check(Field<RichTextBox>("codeEditor").Text.Replace("\r\n", "\n") == document.Code.Files["io_logic_user.c"].Replace("\r\n", "\n"), "code not restored: " + Field<RichTextBox>("codeEditor").Text[..Math.Min(80, Field<RichTextBox>("codeEditor").Text.Length)]);
        Check(Field<HashSet<string>>("selectedTrendKeys").SetEquals(document.Trend.Channels), "trend selection lost before first sample");
        Check(Field<ComboBox>("trendWindowInput").SelectedIndex == 2, "time window not restored");
        var sensorGrid = Field<DataGridView>("rs485Grid");
        Check(Convert.ToInt32(sensorGrid.Rows[0].Cells["slave"].Value) == 9, "B sensors not restored");
        Invoke("OpenDeviceWorkspace", a);
        sensorGrid.Rows[0].Cells["slave"].Value = 7;
        Field<TextBox>("hostInput").Text = "192.168.1.99";
        Invoke("OpenDeviceWorkspace", b);
        Check(Convert.ToInt32(sensorGrid.Rows[0].Cells["slave"].Value) == 9, "sensor tables leaked between devices");
        var editor = Field<RichTextBox>("codeEditor");
        editor.AppendText("\n// unsaved buffer included\n");
        var copySource = Field<string>("currentUserSourcePath");
        var diskBefore = File.ReadAllText(copySource);
        Invoke("SaveWorkbenchTo", path);
        loaded = WorkbenchProject.Load(path);
        Check(loaded.Document.Code.Files["io_logic_user.c"].Contains("unsaved buffer included"), "unsaved editor buffer excluded");
        Check(loaded.Document.Code.Files["Library/custom.h"] == "// custom\n", "supporting source omitted when resaving");
        Check(File.ReadAllText(copySource) == diskBefore, "saving archive overwrote original source");
        Check(loaded.Document.Devices.Single(d => d.Profile.Id == a.Id).Sensors[0].SlaveAddress == 7, "inactive device changes not saved");
        Check(loaded.Document.Devices.Single(d => d.Profile.Id == b.Id).Engineering?.Count == 2, "engineering settings not persisted");
        Check(loaded.Document.Devices.Single(d => d.Profile.Id == a.Id).Profile.Host == "192.168.1.99", "inactive endpoint edit lost");
        var sourceTree = Field<TreeView>("sourceTreeView");
        var manualNode = sourceTree.Nodes[0].Nodes.Cast<TreeNode>().Single(n => n.Text == "编程手册.txt");
        Invoke("SelectProjectNodeWithoutPrompt", manualNode);
        Invoke("LoadCProgramFromSelection");
        Check(Field<RichTextBox>("codeEditor").ReadOnly, "manual not selected");
        Invoke("SaveWorkbenchTo", path);
        loaded = WorkbenchProject.Load(path);
        Check(loaded.Document.Code.Files["io_logic_user.c"].Contains("unsaved buffer included"), "saving from manual reverted archived code");
        var sourceNode = sourceTree.Nodes[0].Nodes.Cast<TreeNode>().Single(n => n.Text == "io_logic_user.c");
        Invoke("SelectProjectNodeWithoutPrompt", sourceNode);
        Invoke("LoadCProgramFromSelection");
        Check(Field<RichTextBox>("codeEditor").Text.Contains("unsaved buffer included"), "returning to source reverted saved code");
        Invoke("ApplyWorkbench", loaded.Document, path, loaded.Revision);
        Application.DoEvents();
        Check(Field<RichTextBox>("codeEditor").Text.Contains("unsaved buffer included"), "reopen lost buffer");
        Check(!Field<bool>("workbenchDirty"), "restored project starts dirty");
        var editedProfiles = Field<DeviceCatalogSnapshot>("deviceSnapshot").Devices.Select(p => p.Id == b.Id ? p with { Host = "192.168.1.77" } : p).ToArray();
        Invoke("SaveDevices", (object)editedProfiles);
        Check(Field<TextBox>("hostInput").Text == "192.168.1.77", "profile editor changes were not reflected in active endpoint");
        Invoke("SaveWorkbenchTo", path);
        Check(WorkbenchProject.Load(path).Document.Devices.Single(d => d.Profile.Id == b.Id).Profile.Host == "192.168.1.77", "profile edit was overwritten by stale connection fields");
        Field<RichTextBox>("codeEditor").AppendText("\n// reopen after save prompt\n");
        Set("workbenchSavePrompt", new Func<DialogResult>(() => DialogResult.Yes));
        Invoke("OpenWorkbench", path);
        Check(Field<RichTextBox>("codeEditor").Text.Contains("reopen after save prompt"), "reopen restored pre-prompt version");
        Check(Field<string>("workbenchRevision") == WorkbenchProject.Load(path).Revision, "reopen retained stale revision");
        Set("isConnecting", true);
        Reject(() => Invoke("ApplyWorkbench", document, path, revision), "connecting switch was not blocked");
        Set("isConnecting", false);
        Check((File.Exists(localPath) ? File.ReadAllText(localPath) : null) == localBefore, "local device catalog overwritten");
        if (args.Length > 0)
        {
            Directory.CreateDirectory(args[0]);
            form.Size = new Size(1440, 900);
            Application.DoEvents();
            using var bitmap = new Bitmap(form.Width, form.Height);
            form.DrawToBitmap(bitmap, new Rectangle(0, 0, form.Width, form.Height));
            bitmap.Save(Path.Combine(args[0], "workbench-open.png"));
            using var configDialog = new EngineeringDialog(Field<List<EngineeringChannel>>("currentEngineering"), document.Devices[1].Sensors);
            configDialog.Show(form);
            Application.DoEvents();
            using var dialogBitmap = new Bitmap(configDialog.Width, configDialog.Height);
            configDialog.DrawToBitmap(dialogBitmap, new Rectangle(0, 0, configDialog.Width, configDialog.Height));
            dialogBitmap.Save(Path.Combine(args[0], "engineering-dialog.png"));
            configDialog.Close();
        }
        Invoke("CloseWorkbench");
        Check(Field<object?>("workbenchSensors") is null && Field<TabControl>("contentTabs").SelectedIndex == 6, "return to local environment failed");
        form.Close();
        Console.WriteLine("UI: offline restore, selected page/device, isolated RS485 edits, unsaved code archive, reopen, trend selection and local catalog preservation passed.");
        Console.WriteLine("Test artifacts: " + root);
    }
}
