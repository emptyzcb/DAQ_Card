using System.Reflection;

internal static class Program
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    [STAThread]
    private static void Main(string[] args)
    {
        Application.EnableVisualStyles();
        var assembly = Assembly.Load("IndustrialFieldDataAcquisitionTerminal");
        var type = assembly.GetType("IndustrialFieldDataAcquisitionTerminal.MainForm", true)!;
        using var form = (Form)Activator.CreateInstance(type, nonPublic: true)!;
        T Field<T>(string name) => (T)type.GetField(name, Private)!.GetValue(form)!;
        void Invoke(string name, params object[] values) => type.GetMethod(name, Private)!.Invoke(form, values);
        void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
        void Capture(string name)
        {
            Application.DoEvents();
            if (args.Length == 0) return;
            Directory.CreateDirectory(args[0]);
            using var bitmap = new Bitmap(form.Width, form.Height);
            form.DrawToBitmap(bitmap, new Rectangle(0, 0, form.Width, form.Height));
            bitmap.Save(Path.Combine(args[0], name + ".png"));
        }
        form.Show();
        Application.DoEvents();
        var tabs = Field<TabControl>("contentTabs");
        var global = Field<FlowLayoutPanel>("globalNavigation");
        var deviceNav = Field<FlowLayoutPanel>("deviceNavigation");
        Check(tabs.SelectedIndex == 6 && global.Visible && !deviceNav.Visible, "Startup must be the product list.");
        Check(!Field<Button>("connectButton").Visible, "Board connection controls must not appear on the global home.");
        tabs.SelectedIndex = 0;
        Check(tabs.SelectedIndex == 6, "Board modules must require a selected device.");

        var profileType = assembly.GetType("IndustrialFieldDataAcquisitionTerminal.DeviceProfile", true)!;
        var idA = Guid.NewGuid(); var idB = Guid.NewGuid();
        object Profile(Guid id, string name) => Activator.CreateInstance(profileType, id, name, "DAQ-800",
            "192.168.1.10", 5000, (byte)1, 250, "测试档案")!;
        var profileA = Profile(idA, "DAQ-800 · 测试设备 A");
        var profileB = Profile(idB, "DAQ-800 · 测试设备 B");
        var profiles = Array.CreateInstance(profileType, 2);
        profiles.SetValue(profileA, 0); profiles.SetValue(profileB, 1);
        var snapshotType = assembly.GetType("IndustrialFieldDataAcquisitionTerminal.DeviceCatalogSnapshot", true)!;
        var snapshot = Activator.CreateInstance(snapshotType, profiles, "probe")!;
        type.GetField("deviceSnapshot", Private)!.SetValue(form, snapshot);
        Invoke("RefreshDeviceGrid");
        var pathMethod = type.GetMethod("DeviceSensorConfigurationPath", BindingFlags.Static | BindingFlags.NonPublic)!;
        var paths = new[] { idA, idB }.Select(id => (string)pathMethod.Invoke(null, new object[] { id })!).ToArray();
        try
        {
            Capture("products");
            Invoke("OpenDeviceWorkspace", profileA);
            Application.DoEvents();
            Check(tabs.SelectedIndex == 0 && global.Visible && deviceNav.Visible,
                $"General tools must remain visible after entering a device. tab={tabs.SelectedIndex} global={global.Visible} device={deviceNav.Visible} info={Field<Label>("deviceInfo").Text}");
            Check(Field<Button>("deviceHomeButton").Visible, "The device list must stay reachable from the sidebar while a device is open.");
            Check(Field<Button>("connectButton").Visible, "Selected device connection entry missing.");
            Check(!(bool)Field<object>("client").GetType().GetProperty("IsOpen")!.GetValue(Field<object>("client"))!,
                "Opening the device must not connect to hardware.");
            Check(Field<Label>("workspaceTitle").Text.Contains("DAQ-800"), "Product model missing.");
            var sensors = Field<DataGridView>("rs485Grid");
            sensors.Rows[0].Cells["slave"].Value = "7";
            Field<TextBox>("serialSendInput").Text = "serial tool preserved";
            Capture("device");
            tabs.SelectedIndex = 1;
            Application.DoEvents();
            var originalStatus = sensors.Rows[0].Cells["state"].Value;
            sensors.Rows[0].Cells["state"].Value = "正常";
            sensors.CurrentCell = sensors.Rows[0].Cells["slave"];
            Check(sensors.BeginEdit(true), "RS485 address must remain editable after theming.");
            Check(sensors.EndEdit(), "RS485 address edit did not finish.");
            Capture("rs485-table");
            sensors.Rows[0].Cells["state"].Value = originalStatus;
            tabs.SelectedIndex = 2; Capture("editor");
            tabs.SelectedIndex = 3; Capture("trend");
            tabs.SelectedIndex = 4; Capture("serial");
            tabs.SelectedIndex = 4;
            Application.DoEvents();
            Check(global.Visible && deviceNav.Visible && !Field<Button>("connectButton").Visible,
                "Serial must preserve the device module navigation while hiding board connection controls.");
            Check(Field<Guid?>("selectedDeviceId") == idA, "Opening a tool lost the selected device.");
            tabs.SelectedIndex = 5;
            Application.DoEvents();
            Check(global.Visible && deviceNav.Visible, "Virtual RS485 must not hide device modules.");
            tabs.SelectedIndex = 7;
            Application.DoEvents();
            Check(global.Visible && deviceNav.Visible && tabs.SelectedIndex == 7, "CAN tool must preserve the device workspace.");
            Field<Button[]>("navigationButtons")[2].PerformClick();
            Application.DoEvents();
            Check(tabs.SelectedIndex == 2 && global.Visible && deviceNav.Visible, "Device logic must be directly accessible from a general tool.");
            tabs.SelectedIndex = 5;
            Capture("tools");
            tabs.SelectedIndex = 8; Capture("toolbox");
            tabs.SelectedIndex = 7; Capture("can");
            type.GetField("isConnecting", Private)!.SetValue(form, true);
            Invoke("OpenDeviceWorkspace", profileB);
            Check(Field<Guid?>("selectedDeviceId") == idA, "Pending connections must block device switches.");
            type.GetField("isConnecting", Private)!.SetValue(form, false);
            Invoke("OpenDeviceWorkspace", profileB);
            Check(sensors.Rows[0].Cells["slave"].Value?.ToString() == "1", "Device B inherited device A sensor configuration.");
            sensors.Rows[0].Cells["slave"].Value = "9";
            Invoke("OpenDeviceWorkspace", profileA);
            Check(sensors.Rows[0].Cells["slave"].Value?.ToString() == "7", "Device A sensor configuration was not restored.");
            Check(Field<TextBox>("serialSendInput").Text == "serial tool preserved", "Device switches must preserve general tools.");
            Field<Button>("deviceManagerButton").PerformClick();
            Application.DoEvents();
            Check(tabs.SelectedIndex == 6 && global.Visible && !deviceNav.Visible, "Return to product home failed.");
            Check(Field<Button>("deviceHomeButton").Visible, "My Devices must return on the home page.");
            var pickerType = assembly.GetType("IndustrialFieldDataAcquisitionTerminal.DeviceModelPickerDialog", true)!;
            using (var picker = (Form)Activator.CreateInstance(pickerType, nonPublic: true)!)
            {
                picker.Show(form);
                Application.DoEvents();
                var next = (Button)pickerType.GetField("nextButton", Private)!.GetValue(picker)!;
                Check(!next.Enabled && pickerType.GetProperty("SelectedModel")!.GetValue(picker) is null,
                    "Creation must require an explicit product selection.");
                ((Button)pickerType.GetField("selectDeviceButton", Private)!.GetValue(picker)!).PerformClick();
                Check(next.Enabled && pickerType.GetProperty("SelectedModel")!.GetValue(picker)?.ToString() == "DAQ-800",
                    "Selecting the product must enable the settings step.");
                if (args.Length > 0)
                {
                    using var bitmap = new Bitmap(picker.Width, picker.Height);
                    picker.DrawToBitmap(bitmap, new Rectangle(0, 0, picker.Width, picker.Height));
                    bitmap.Save(Path.Combine(args[0], "product-picker.png"));
                }
                picker.Close();
            }
            var dialogType = assembly.GetType("IndustrialFieldDataAcquisitionTerminal.DeviceProfileDialog", true)!;
            using (var editor = (Form)Activator.CreateInstance(dialogType, new object?[] { null })!)
            {
                editor.Show(form);
                Application.DoEvents();
                Check(dialogType.GetProperty("Profile")!.GetValue(editor) is null,
                    "Opening device settings must not create a profile.");
                if (args.Length > 0)
                {
                    using var bitmap = new Bitmap(editor.Width, editor.Height);
                    editor.DrawToBitmap(bitmap, new Rectangle(0, 0, editor.Width, editor.Height));
                    bitmap.Save(Path.Combine(args[0], "device-settings.png"));
                }
                editor.Close();
                Check(dialogType.GetProperty("Profile")!.GetValue(editor) is null,
                    "Cancelling device settings must not create a profile.");
            }
            Console.WriteLine("Product hierarchy, offline entry, switch guard, isolated RS485 configuration and general tool preservation passed.");
        }
        finally
        {
            foreach (var path in paths)
            {
                var folder = Path.GetFullPath(Path.GetDirectoryName(path)!);
                var expectedParent = Path.GetFullPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "IndustrialFieldDataAcquisitionTerminal", "Devices"));
                if (Path.GetDirectoryName(folder) != expectedParent ||
                    (Path.GetFileName(folder) != idA.ToString("N") && Path.GetFileName(folder) != idB.ToString("N")))
                    throw new Exception("Unsafe test cleanup path.");
                if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
            }
        }
    }
}
