using System.Reflection;
using System.Runtime.InteropServices;
using IndustrialFieldDataAcquisitionTerminal;

static class Program
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate uint GetUInt();
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate uint GetByte(uint index);
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    static void Reject(Action action) { try { action(); } catch (ArgumentException) { return; } throw new Exception("invalid input was accepted"); }
    [STAThread] static void Main(string[] args)
    {
        Check(CanInterpreter.ParseId("0x7FF", false) == 2047 && CanInterpreter.ParseId("1FFFFFFF", true) == 0x1FFFFFFF, "ID parsing");
        Reject(() => CanInterpreter.ParseId("800", false)); Reject(() => CanInterpreter.ParseId("20000000", true));
        Reject(() => CanInterpreter.ParseHex("01 2")); Reject(() => CanInterpreter.ParseHex("00 00 00 00 00 00 00 00 00"));
        Check(CanInterpreter.ParseHex("01 02 A0,FF").SequenceEqual(new byte[] { 1, 2, 160, 255 }), "hex data parsing");
        var data = new CanFrame(0x123, false, false, 3, new byte[] { 1, 2, 255 }, DeviceTimestampUs: 123456);
        Check(CanInterpreter.Explain(data).Contains("小端 U16=513") && CanInterpreter.Explain(data).Contains("大端 U16=258"), "byte explanation");
        Check(CanInterpreter.Explain(new(0x123, false, true, 8, Array.Empty<byte>())).Contains("不携带数据"), "remote explanation");
        Check(CanInterpreter.Explain(new(0, false, false, 0, Array.Empty<byte>(), 0x80)).Contains("状态消息"), "status explanation");
        Reject(() => new CanFrame(0x800, false, false, 0, Array.Empty<byte>()).ValidateForTransmit());
        Reject(() => new CanFrame(1, false, true, 1, new byte[] { 1 }).ValidateForTransmit());
        var nativeMessage = typeof(PcanAdapter).GetNestedType("NativeMessage", BindingFlags.NonPublic)!;
        var nativeTimestamp = typeof(PcanAdapter).GetNestedType("NativeTimestamp", BindingFlags.NonPublic)!;
        Check(Marshal.SizeOf(nativeMessage) == 16 && Marshal.OffsetOf(nativeMessage, "Data").ToInt32() == 6 && Marshal.SizeOf(nativeTimestamp) == 8, "native ABI mismatch");
        using (var actual = new PcanAdapter())
        {
            var channels = actual.ListChannels();
            Console.WriteLine("PCAN-Basic native DLL loaded; detected USB channels: " + channels.Count);
        }
        if (args.Length > 1)
        {
            var native = NativeLibrary.Load(args[1]);
            try
            {
                T Get<T>(string export) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(native, export));
                using var adapter = new PcanAdapter(args[1]);
                Check(adapter.ListChannels().Single().Handle == 0x51, "native channel enumeration");
                try { adapter.Open(0x51, 0x011C); throw new Exception("bitrate mismatch accepted"); }
                catch (InvalidOperationException) { }
                Check(Get<GetUInt>("Fixture_IsOpen")() == 0 && !adapter.IsOpen, "bitrate warning did not close native channel");
                adapter.Open(0x51, 0x001C);
                var received = adapter.Read()!;
                Check(received.Id == 0x123 && received.Data.SequenceEqual(new byte[] { 1, 0xA5, 0xFF }) && received.DeviceTimestampUs == 1234567, "native read payload and timestamp");
                Check(adapter.Read() is null && adapter.BusStatus() == "OK", "native receive queue empty and status");
                Get<GetUInt>("Fixture_InjectStatus")();
                var warning = adapter.Read()!;
                Check(warning.DriverStatus == 4 && (warning.DriverFlags & 0x80) != 0 && CanInterpreter.Explain(warning).Contains("0x4"), "native warning status frame retained");
                adapter.Write(new CanFrame(0x1ABCDE, true, false, 3, new byte[] { 0x10, 0x20, 0x30 }));
                Check(Get<GetUInt>("Fixture_LastId")() == 0x1ABCDE && Get<GetUInt>("Fixture_LastFlags")() == 2 &&
                    Get<GetUInt>("Fixture_LastDlc")() == 3 && Get<GetByte>("Fixture_LastByte")(2) == 0x30, "native write frame ABI");
                adapter.Close();
                Check(Get<GetUInt>("Fixture_IsOpen")() == 0, "native close");
                adapter.Open(0x51, 0x001C, true);
                Check(adapter.IsListenOnly, "listen-only verification");
                try { adapter.Write(data); throw new Exception("listen-only accepted send"); } catch (InvalidOperationException) { }
                adapter.Close();
                Console.WriteLine("Native fixture: discovery, bitrate warning, receive, timestamp and transmit ABI passed.");
            }
            finally { NativeLibrary.Free(native); }
        }
        var template = new CanSendTemplate(Guid.NewGuid(), "心跳", data with { DeviceTimestampUs = null }, 100);
        var scheduler = new CanSendScheduler();
        scheduler.Start(template, 1000);
        Check(scheduler.Due(1099).Count == 0 && scheduler.Due(1100).Count == 1, "periodic first send timing");
        Check(scheduler.Due(2100).Count == 1 && scheduler.Due(2100).Count == 0 && scheduler.Due(2199).Count == 0, "missed deadlines must not burst");
        scheduler.StopAll(); Check(scheduler.Due(3000).Count == 0, "periodic stop");
        Reject(() => new CanFrame(0x800, false, false, 0, Array.Empty<byte>()).ValidateForTransmit());
        var planPath = Path.Combine(Path.GetTempPath(), "can-test-" + Guid.NewGuid().ToString("N") + ".canlist");
        try { CanSendScheduler.Save(planPath, new[] { template }); Check(CanSendScheduler.Load(planPath).Single().Frame.Id == data.Id, "plan save/load"); }
        finally { File.Delete(planPath); }
        var tracePath = Path.Combine(Path.GetTempPath(), "can-trace-" + Guid.NewGuid().ToString("N") + ".csv");
        try
        {
            var trace = new CanTraceRecorder(tracePath);
            for (int i = 0; i < 5000; i++) Check(trace.Append(new DateTime(2026, 1, 2, 3, 4, 5), "RX", data), "trace enqueue");
            trace.FinishAsync().GetAwaiter().GetResult();
            var lines = File.ReadAllLines(tracePath);
            Check(lines.Length == 5001 && lines[1].Contains(",RX,123,STD_DATA,3,0102FF,123456,0"), "trace flush and payload");
        }
        finally { File.Delete(tracePath); }
        Application.EnableVisualStyles();
        using var form = new MainForm();
        var fake = new FakeCan();
        typeof(MainForm).GetField("canAdapter", Private)!.SetValue(form, fake);
        T Field<T>(string name) => (T)typeof(MainForm).GetField(name, Private)!.GetValue(form)!;
        void SetField(string name, object value) => typeof(MainForm).GetField(name, Private)!.SetValue(form, value);
        void Invoke(string name, params object[] values) => typeof(MainForm).GetMethod(name, Private)!.Invoke(form, values);
        form.Show(); Application.DoEvents();
        var tabs = Field<TabControl>("contentTabs"); tabs.SelectedIndex = 7; Application.DoEvents();
        Check(Field<ComboBox>("canBaudInput").Text == "500 kbit/s", "default baud");
        Check(Field<ComboBox>("canChannelInput").Items.Count == 1, "channel refresh");
        Invoke("ToggleCanConnection");
        Check(fake.IsOpen && fake.Bitrate == 0x001C && fake.Sent.Count == 0, "open must not transmit");
        Field<System.Windows.Forms.Timer>("canTimer").Stop();
        fake.Incoming.Enqueue(data);
        fake.Incoming.Enqueue(data with { DeviceTimestampUs = 1123456 });
        Invoke("DrainCanFrames");
        var grid = Field<DataGridView>("canGrid");
        Check(grid.Rows.Count == 1 && Convert.ToString(grid.Rows[0].Cells["direction"].Value) == "RX" &&
            Convert.ToString(grid.Rows[0].Cells["count"].Value) == "2" && Convert.ToString(grid.Rows[0].Cells["cycle"].Value) == "1000", "ID summary, cycle and count");
        fake.Incoming.Enqueue(data with { Id = 0x124 });
        Invoke("DrainCanFrames");
        Field<CheckBox>("canFilterEnabled").Checked = true;
        Field<TextBox>("canFilterFrom").Text = "123"; Field<TextBox>("canFilterTo").Text = "123";
        Invoke("ApplyCanFilter");
        Check(grid.Rows.Count == 1 && Convert.ToString(grid.Rows[0].Cells["id"].Value) == "123", "display filter");
        Field<CheckBox>("canFilterEnabled").Checked = false; Invoke("ApplyCanFilter");
        Check(grid.Rows.Count == 2, "filter must retain hidden data");
        Invoke("SendCanFrame");
        Check(fake.Sent.Count == 1 && fake.Sent[0].Id == 0x321 && fake.Sent[0].Data.Length == 8, "manual transmit");
        Field<CheckBox>("canRemoteInput").Checked = true;
        Field<NumericUpDown>("canRemoteDlcInput").Value = 4;
        Invoke("SendCanFrame");
        Check(fake.Sent.Last().Remote && fake.Sent.Last().Dlc == 4 && fake.Sent.Last().Data.Length == 0, "remote transmit");
        Check(Field<DataGridView>("canTransmitGrid").Rows.Count == 2, "transmit history");
        var uiScheduler = Field<CanSendScheduler>("canScheduler");
        uiScheduler.Start(template, Environment.TickCount64 - 150);
        Invoke("TickCanPlans");
        Check(fake.Sent.Count == 3, "periodic UI send");
        fake.StatusCode = 0x10;
        Invoke("TickCanPlans");
        Check(uiScheduler.RunningIds.Count == 0 && fake.Sent.Count == 3, "bus-off must stop periodic sending");
        fake.StatusCode = 0;
        for (var i = 0; i < 3002; i++) Invoke("AddCanRow", "RX", data);
        Field<ComboBox>("canViewInput").SelectedIndex = 1;
        Invoke("RefreshCanDisplay");
        Check(grid.Rows.Count == 3000 && Field<long>("canDiscardedCount") == 8, "bounded rows and discarded counter");
        Check(Field<Label>("canCountLabel").Text.Contains("保留 3000/3000") && Field<Label>("canCountLabel").Text.Contains("记录 8"), "display counters");
        Invoke("CloseCanConnection"); Check(!fake.IsOpen, "channel close");
        Field<CheckBox>("canListenOnly").Checked = true;
        Invoke("ToggleCanConnection"); Field<System.Windows.Forms.Timer>("canTimer").Stop();
        Check(fake.IsListenOnly && !Field<Button>("canSendButton").Enabled, "listen-only UI must disable transmission");
        uiScheduler.Start(template, Environment.TickCount64 - 150);
        Invoke("TickCanPlans");
        Check(uiScheduler.RunningIds.Count == 0 && fake.Sent.Count == 3, "listen-only must stop periodic sending");
        Invoke("CloseCanConnection");
        if (args.Length > 0)
        {
            Directory.CreateDirectory(args[0]); Field<ComboBox>("canViewInput").SelectedIndex = 0; form.Size = new Size(1440, 900); Application.DoEvents();
            using var bitmap = new Bitmap(form.Width, form.Height);
            form.DrawToBitmap(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
            bitmap.Save(Path.Combine(args[0], "can-page.png"));
            Invoke("ShowCanPlans"); Application.DoEvents();
            var plans = Field<Form>("canPlanForm");
            using var planBitmap = new Bitmap(plans.Width, plans.Height);
            plans.DrawToBitmap(planBitmap, new Rectangle(0, 0, plans.Width, plans.Height));
            planBitmap.Save(Path.Combine(args[0], "can-plans.png"));
            plans.Close();
            form.Size = new Size(1100, 680); Application.DoEvents();
            using var smallBitmap = new Bitmap(form.Width, form.Height);
            form.DrawToBitmap(smallBitmap, new Rectangle(0, 0, form.Width, form.Height));
            smallBitmap.Save(Path.Combine(args[0], "can-small.png"));
        }
        var closeTracePath = Path.Combine(Path.GetTempPath(), "can-close-" + Guid.NewGuid().ToString("N") + ".csv");
        SetField("canTrace", new CanTraceRecorder(closeTracePath));
        for (int i = 0; i < 1000; i++) Invoke("AddCanRow", "RX", data);
        form.Close();
        try { Check(File.ReadAllLines(closeTracePath).Length == 1001, "closing must flush trace queue"); }
        finally { File.Delete(closeTracePath); }
        Console.WriteLine("CAN validation, decoding, native ABI, offline discovery, simulated RX/TX and bounded UI passed.");
    }
}
internal sealed class FakeCan : ICanAdapter
{
    public bool IsOpen { get; private set; }
    public ushort Bitrate { get; private set; }
    public uint StatusCode { get; set; }
    public bool IsListenOnly { get; private set; }
    public readonly Queue<CanFrame> Incoming = new();
    public readonly List<CanFrame> Sent = new();
    public IReadOnlyList<CanChannel> ListChannels() => new[] { new CanChannel(0x51, "PCAN-USB 1 (mock)", 1) };
    public void Open(ushort handle, ushort bitrate, bool listenOnly = false) { if (handle != 0x51) throw new Exception("wrong channel"); IsOpen = true; Bitrate = bitrate; IsListenOnly = listenOnly; }
    public CanFrame? Read() => Incoming.TryDequeue(out var frame) ? frame : null;
    public void Write(CanFrame frame) { if (IsListenOnly) throw new InvalidOperationException("listen only"); frame.ValidateForTransmit(); Sent.Add(frame); }
    public string BusStatus() => "OK";
    public uint BusStatusCode() => StatusCode;
    public void Close() => IsOpen = false;
    public void Dispose() => Close();
}
