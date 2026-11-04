using IndustrialFieldDataAcquisitionTerminal;
using System.Net;
using System.Net.Sockets;
using System.Reflection;

internal static class Program
{
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Equal(byte[] a, byte[] b, string message) => Check(a.SequenceEqual(b), message + " actual=" + Convert.ToHexString(a));
    private static async Task<byte[]> Read(NetworkStream stream, int count) { var bytes = new byte[count]; await stream.ReadExactlyAsync(bytes).AsTask().WaitAsync(TimeSpan.FromSeconds(3)); return bytes; }
    private static async Task NetTests()
    {
        foreach (var little in new[] { false, true }) foreach (var words in new[] { false, true }) foreach (var pair in new[] { ("Int16", "-12345"), ("UInt16", "65535"), ("Int32", "-87654321"), ("UInt32", "4000000000"), ("Float32", "1.5"), ("Int64", "-9876543210"), ("UInt64", "18446744073709551615"), ("Float64", "-3.25") })
        { var bytes = DataConversion.Encode(pair.Item2, pair.Item1, little, words); Check(DataConversion.Decode(bytes, pair.Item1, little, words) == pair.Item2, "conversion " + pair); }
        Equal(DataConversion.Encode("1", "Float32", false, false), new byte[] { 0x3f, 0x80, 0, 0 }, "float big endian");
        Equal(DataConversion.Encode("1", "Float32", false, true), new byte[] { 0, 0, 0x3f, 0x80 }, "word swap");
        Equal(DataConversion.Encode("1", "Float32", true, false), new byte[] { 0, 0, 0x80, 0x3f }, "little endian");
        try { DataConversion.ParseHex("123"); throw new Exception("odd hex accepted"); } catch (ArgumentException) { }
        var unit = new ModbusTcpUnit(1, 0, new ushort[] { 0x1234, 0x5678 }, new ushort[] { 2, 3 }, new bool[] { true, false }, new bool[] { false, true });
        Equal(unit.Process(new byte[] { 1, 0, 0, 0, 2 }), new byte[] { 1, 1, 1 }, "coils");
        Equal(unit.Process(new byte[] { 2, 0, 0, 0, 2 }), new byte[] { 2, 1, 2 }, "discrete");
        Equal(unit.Process(new byte[] { 4, 0, 0, 0, 1 }), new byte[] { 4, 2, 0, 2 }, "input");
        Equal(unit.Process(new byte[] { 3, 0, 2, 0, 1 }), new byte[] { 0x83, 2 }, "address exception");
        Equal(unit.Process(new byte[] { 0x55, 0, 0, 0, 1 }), new byte[] { 0xd5, 1 }, "function exception");
        Equal(unit.Process(new byte[] { 5, 0, 0, 0xff, 0 }), new byte[] { 5, 0, 0, 0xff, 0 }, "write coil");
        Equal(unit.Process(new byte[] { 6, 0, 0, 0, 4 }), new byte[] { 6, 0, 0, 0, 4 }, "write register");
        Equal(unit.Process(new byte[] { 15, 0, 0, 0, 2, 1, 2 }), new byte[] { 15, 0, 0, 0, 2 }, "multi coils");
        Equal(unit.Process(new byte[] { 16, 0, 0, 0, 2, 4, 0, 7, 0, 8 }), new byte[] { 16, 0, 0, 0, 2 }, "multi registers");
        using var simulator = new ModbusTcpSimulator(new[] { unit, new ModbusTcpUnit(2, 0, new ushort[] { 0xabcd }, Array.Empty<ushort>(), Array.Empty<bool>(), Array.Empty<bool>()) }); simulator.Start(IPAddress.Loopback, 0);
        using var tcp = new TcpClient(); await tcp.ConnectAsync(IPAddress.Loopback, simulator.LocalEndpoint!.Port); using var stream = tcp.GetStream();
        var request = new byte[] { 0x12, 0x34, 0, 0, 0, 6, 1, 3, 0, 0, 0, 2 }; await stream.WriteAsync(request.AsMemory(0, 3)); await stream.WriteAsync(request.AsMemory(3));
        Equal(await Read(stream, 13), new byte[] { 0x12, 0x34, 0, 0, 0, 7, 1, 3, 4, 0, 7, 0, 8 }, "fragmented modbus");
        request[1] = 0x35; request[6] = 2; request[11] = 1; await stream.WriteAsync(request); Equal(await Read(stream, 11), new byte[] { 0x12, 0x35, 0, 0, 0, 5, 2, 3, 2, 0xab, 0xcd }, "unit2");
        using var network = new NetworkToolService(); var received = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        network.Event += e => { if (e.Kind == "RX") received.TrySetResult(e.Data); };
        await network.StartAsync(NetworkMode.TcpServer, IPAddress.Loopback, 0, "127.0.0.1", 1, CancellationToken.None);
        using var other = new TcpClient(); await other.ConnectAsync(IPAddress.Loopback, network.LocalEndpoint!.Port); var otherStream = other.GetStream();
        await otherStream.WriteAsync(new byte[] { 1, 2, 3 }); Equal(await received.Task.WaitAsync(TimeSpan.FromSeconds(3)), new byte[] { 1, 2, 3 }, "tcp rx");
        await network.SendAsync(new byte[] { 4, 5 }, "127.0.0.1", 1, CancellationToken.None); Equal(await Read(otherStream, 2), new byte[] { 4, 5 }, "tcp tx"); network.Stop();
        received = new(TaskCreationOptions.RunContinuationsAsynchronously); var externalListener = new TcpListener(IPAddress.Loopback, 0); externalListener.Start();
        await network.StartAsync(NetworkMode.TcpClient, IPAddress.Loopback, 0, "127.0.0.1", ((IPEndPoint)externalListener.LocalEndpoint).Port, CancellationToken.None);
        using (var accepted = await externalListener.AcceptTcpClientAsync()) { await accepted.GetStream().WriteAsync(new byte[] { 0x42 }); Equal(await received.Task.WaitAsync(TimeSpan.FromSeconds(3)), new byte[] { 0x42 }, "tcp client rx"); await network.SendAsync(new byte[] { 0x24 }, "127.0.0.1", 1, CancellationToken.None); Equal(await Read(accepted.GetStream(), 1), new byte[] { 0x24 }, "tcp client tx"); }
        network.Stop(); externalListener.Stop();
        received = new(TaskCreationOptions.RunContinuationsAsynchronously); using var udpPeer = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        await network.StartAsync(NetworkMode.Udp, IPAddress.Loopback, 0, "127.0.0.1", 1, CancellationToken.None);
        await udpPeer.SendAsync(new byte[] { 6, 7 }, network.LocalEndpoint!); Equal(await received.Task.WaitAsync(TimeSpan.FromSeconds(3)), new byte[] { 6, 7 }, "udp rx");
        await network.SendAsync(new byte[] { 8, 9 }, "127.0.0.1", ((IPEndPoint)udpPeer.Client.LocalEndPoint!).Port, CancellationToken.None);
        Equal((await udpPeer.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(3))).Buffer, new byte[] { 8, 9 }, "udp tx"); network.Stop();
        var diagnostics = await NetworkDiagnostics.CheckAsync("127.0.0.1", simulator.LocalEndpoint!.Port, CancellationToken.None); Check(diagnostics.Contains("连接成功"), "diagnostics TCP");
        Console.WriteLine("Conversion, Modbus TCP, TCP/UDP and network diagnostics passed.");
    }
    [STAThread] private static void Main(string[] args)
    {
        if (args.Contains("--stress")) { StressUi(); return; }
        NetTests().GetAwaiter().GetResult(); Application.EnableVisualStyles(); using var form = new MainForm(); form.Show(); Application.DoEvents();
        var type = typeof(MainForm); var flags = BindingFlags.NonPublic | BindingFlags.Instance;
        var main = (TabControl)type.GetField("contentTabs", flags)!.GetValue(form)!; main.SelectedIndex = 8;
        var tabs = (TabControl)type.GetField("toolboxTabs", flags)!.GetValue(form)!; Check(main.SelectedIndex == 8 && tabs.TabCount == 4, "toolbox navigation");
        if (args.Length > 0) { Directory.CreateDirectory(args[0]); for (var i = 0; i < 4; i++) { tabs.SelectedIndex = i; Application.DoEvents(); using var bitmap = new Bitmap(form.Width, form.Height); form.DrawToBitmap(bitmap, new Rectangle(0, 0, form.Width, form.Height)); bitmap.Save(Path.Combine(args[0], $"toolbox-{i}.png")); } }
        if (args.Length > 0) { form.Size = new Size(1100, 700); tabs.SelectedIndex = 3; Application.DoEvents(); using var bitmap = new Bitmap(form.Width, form.Height); form.DrawToBitmap(bitmap, new Rectangle(0, 0, form.Width, form.Height)); bitmap.Save(Path.Combine(args[0], "toolbox-narrow.png")); }
        // The same board data before and after a stop must render as two segments.
        var chart = new TrendChartControl { Size = new Size(800, 300) };
        var when = DateTime.Now;
        var channel = new TrendChannel("test", "analog", "Signal", "V");
        TrendPoint[] ChartPoints(long after) => new[] { new TrendPoint(when, 0, true, 1), new TrendPoint(when.AddSeconds(1), 10, true, 1), new TrendPoint(when.AddSeconds(7), 10, true, after), new TrendPoint(when.AddSeconds(8), 0, true, after) };
        int MidBlue(long after)
        {
            chart.SetSeries(new[] { new TrendSeries(channel, ChartPoints(after), Color.Blue) }, TimeSpan.FromSeconds(8), when.AddSeconds(8), TimeSpan.FromSeconds(8), "Signal", when.AddSeconds(8));
            using var bmp = new Bitmap(chart.Width, chart.Height); chart.DrawToBitmap(bmp, chart.ClientRectangle);
            int count = 0; var x = 72 + (800 - 150) / 2;
            for (int y = 34; y < 244; y++) { var pixel = bmp.GetPixel(x, y); if (pixel.B > 180 && pixel.R < 100 && pixel.G < 120) count++; }
            return count;
        }
        Check(MidBlue(1) > 0 && MidBlue(2) == 0, "chart must not bridge a stopped acquisition"); chart.Dispose();
        var holding = new ushort[2048]; holding[0] = 8; holding[0x10] = 42;
        using var fakeBoard = new ModbusTcpSimulator(new[] { new ModbusTcpUnit(1, 0, holding, Array.Empty<ushort>(), Array.Empty<bool>(), Array.Empty<bool>()) });
        fakeBoard.Start(IPAddress.Loopback, 0);
        var client = (ModbusTcpClient)type.GetField("client", flags)!.GetValue(form)!;
        client.OpenAsync("127.0.0.1", fakeBoard.LocalEndpoint!.Port, 1, CancellationToken.None).GetAwaiter().GetResult();
        var recorder = (TrendRecorder)type.GetField("trendRecorder", flags)!.GetValue(form)!;
        var startPoll = type.GetMethod("StartPolling", flags)!; var stopPoll = type.GetMethod("StopPolling", flags)!;
        void WaitFor(Func<bool> check, string error)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            while (!check() && watch.ElapsedMilliseconds < 5000) { Application.DoEvents(); Thread.Sleep(10); }
            Check(check(), error);
        }
        startPoll.Invoke(form, null); WaitFor(() => recorder.GetPoints("AD:0").Count >= 4, "polling did not produce samples");
        var beforePause = recorder.GetPoints("AD:0");
        var periods = beforePause.Zip(beforePause.Skip(1), (a, b) => (b.Timestamp - a.Timestamp).TotalMilliseconds).ToArray();
        Check(periods.All(x => x > 150 && x < 500), "polling interval unexpectedly unstable");
        stopPoll.Invoke(form, null); Application.DoEvents(); var countAtStop = recorder.GetPoints("AD:0").Count;
        var pauseWatch = System.Diagnostics.Stopwatch.StartNew(); while (pauseWatch.ElapsedMilliseconds < 500) { Application.DoEvents(); Thread.Sleep(10); }
        Check(recorder.GetPoints("AD:0").Count == countAtStop, "samples were committed after stop");
        startPoll.Invoke(form, null); WaitFor(() => recorder.GetPoints("AD:0").Count > countAtStop, "polling did not resume");
        var afterPause = recorder.GetPoints("AD:0");
        Check(afterPause[countAtStop - 1].SegmentId != afterPause[countAtStop].SegmentId, "restart failed to break series");
        stopPoll.Invoke(form, null);
        ((NumericUpDown)type.GetField("pollIntervalInput", flags)!.GetValue(form)!).Value = 100;
        var initialCount = recorder.SnapshotCount;
        startPoll.Invoke(form, null); WaitFor(() => recorder.SnapshotCount >= initialCount + 12, "100 ms polling lost snapshots or stalled");
        stopPoll.Invoke(form, null);
        Check(recorder.GetPoints("AD:0").Count >= initialCount + 12, "display throttling lost recorded samples");
        var rxLog = (RichTextBox)type.GetField("logBox", flags)!.GetValue(form)!;
        WaitFor(() => rxLog.Text.Contains("  RX  "), "communication log did not update");
        client.Close();
        Console.WriteLine("Acquisition start/stop, 100 ms recording, real intervals and chart discontinuity passed.");
        // Exercise the real virtual serial pair without changing the running user's connections.
        if (System.IO.Ports.SerialPort.GetPortNames().Contains("COM1") && System.IO.Ports.SerialPort.GetPortNames().Contains("COM2"))
        {
            main.SelectedIndex = 5; Application.DoEvents();
            ((TabControl)type.GetField("simulationTabs", flags)!.GetValue(form)!).SelectedIndex = 0;
            Application.DoEvents();
            if (args.Length > 0) { using var bmp = new Bitmap(form.Width, form.Height); form.DrawToBitmap(bmp, new Rectangle(0, 0, form.Width, form.Height)); bmp.Save(Path.Combine(args[0], "simulation-settings.png")); }
            var general = (SerialConnection)type.GetField("serialConnection", flags)!.GetValue(form)!;
            var sim = (SerialConnection)type.GetField("simulationConnection", flags)!.GetValue(form)!;
            var selected = (ComboBox)type.GetField("simulationPortInput", flags)!.GetValue(form)!;
            var setBaud = (ComboBox)type.GetField("simulationBaudInput", flags)!.GetValue(form)!;
            selected.SelectedItem = "COM1"; setBaud.Text = "115200";
            var replies = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
            general.Received += bytes => replies.TrySetResult(bytes);
            general.Open(new SerialConnectionOptions("COM2", 115200, 8, System.IO.Ports.Parity.None, System.IO.Ports.StopBits.One));
            Task ToggleSim() => (Task)type.GetMethod("ToggleSimulationAsync", flags)!.Invoke(form, null)!;
            var start = ToggleSim(); WaitFor(() => start.IsCompleted, "simulation port did not open"); start.GetAwaiter().GetResult();
            Check(sim.IsOpen && (bool)type.GetField("simulationRunning", flags)!.GetValue(form)!, "simulation not active");
            var request = new byte[] { 1, 3, 0, 0, 0, 1, 0x84, 0x0A }; general.Send(request);
            try { WaitFor(() => replies.Task.IsCompleted, "virtual slave did not reply over serial"); }
            catch { Console.WriteLine("Simulation status: " + ((Label)type.GetField("simulationStatusLabel", flags)!.GetValue(form)!).Text); Console.WriteLine("Simulation log: " + ((RichTextBox)type.GetField("simulationLogBox", flags)!.GetValue(form)!).Text); throw; }
            var response = replies.Task.GetAwaiter().GetResult();
            Check(response.Length == 7 && response[0] == 1 && response[1] == 3 && response[2] == 2 && response[4] == 1 && ModbusRtuSlaveSimulator.HasValidCrc(response), "serial simulation response invalid");
            var closeGeneral = (Task)type.GetMethod("ToggleSerialConnectionAsync", flags)!.Invoke(form, null)!;
            WaitFor(() => closeGeneral.IsCompleted, "general serial did not close"); closeGeneral.GetAwaiter().GetResult();
            Check(sim.IsOpen && (bool)type.GetField("simulationRunning", flags)!.GetValue(form)!, "closing general port stopped simulation");
            var stop = ToggleSim(); WaitFor(() => stop.IsCompleted, "simulation did not stop"); stop.GetAwaiter().GetResult();
            Check(!sim.IsOpen, "simulation serial not released");
            general.Open(new SerialConnectionOptions("COM2", 115200, 8, System.IO.Ports.Parity.None, System.IO.Ports.StopBits.One));
            selected.SelectedItem = "COM2";
            var conflict = ToggleSim(); WaitFor(() => conflict.IsCompleted, "port conflict did not complete"); conflict.GetAwaiter().GetResult();
            Check(!sim.IsOpen && general.IsOpen && ((Label)type.GetField("simulationStatusLabel", flags)!.GetValue(form)!).Text.Contains("已被串口工具使用"), "port conflict disturbed existing connection");
            general.Close();
            Console.WriteLine("Independent COM1/COM2 virtual Modbus request, conflict and lifecycle passed.");
        }
        form.Close(); Console.WriteLine("Toolbox navigation and four pages passed.");
    }

    private static void StressUi()
    {
        Application.EnableVisualStyles();
        using var form = new MainForm();
        var type = typeof(MainForm); const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
        var holding = new ushort[2048]; holding[0] = 8; holding[0x10] = 42;
        using var board = new ModbusTcpSimulator(new[] { new ModbusTcpUnit(1, 0, holding, Array.Empty<ushort>(), Array.Empty<bool>(), Array.Empty<bool>()) });
        board.Start(IPAddress.Loopback, 0);
        var client = (ModbusTcpClient)type.GetField("client", flags)!.GetValue(form)!;
        var recorder = (TrendRecorder)type.GetField("trendRecorder", flags)!.GetValue(form)!;
        var tabs = (TabControl)type.GetField("contentTabs", flags)!.GetValue(form)!;
        var start = type.GetMethod("StartPolling", flags)!; var stop = type.GetMethod("StopPolling", flags)!;
        var navigate = type.GetMethod("HostNavigate", flags)!;
        var selected = type.GetField("selectedDeviceId", flags)!;
        var active = type.GetField("deviceWorkspaceOpen", flags)!;
        var originalSelected = selected.GetValue(form); var originalActive = active.GetValue(form);
        Exception? failure = null;
        var count = 0; long maxSwitchMs = 0;
        var clock = new System.Diagnostics.Stopwatch();
        var pages = new[] { 0, 1, 2, 3, 4, 8 };
        using var timer = new System.Windows.Forms.Timer { Interval = 200 };
        timer.Tick += (_, _) =>
        {
            try
            {
                var tick = System.Diagnostics.Stopwatch.StartNew();
                var page = pages[count++ % pages.Length];
                navigate.Invoke(form, new object[] { page });
                maxSwitchMs = Math.Max(maxSwitchMs, tick.ElapsedMilliseconds);
                if (clock.ElapsedMilliseconds < 8000) return;
                timer.Stop(); stop.Invoke(form, null);
                var snapshots = recorder.SnapshotCount;
                Check(snapshots >= 45, $"100 ms polling stalled during page switches: {snapshots}");
                Check(count >= 12, $"UI timer starved during page switches: {count}");
                var latest = new AcquisitionSnapshot
                {
                    Timestamp = DateTime.Now, Status = 8, DigitalInputs = 1,
                    Ad7606Raw = new short[] { 123, 0, 0, 0, 0, 0, 0, 0 },
                    Rs485Sensors = new[] { new Rs485SensorTelemetry(0, 1, 4, 1, true, true, true, 0, 10, new ushort[] { 42 }) }
                };
                tabs.SelectedIndex = 8;
                type.GetMethod("UpdateSnapshot", flags)!.Invoke(form, new object[] { latest });
                tabs.SelectedIndex = 0;
                var grid = (DataGridView)type.GetField("dataGrid", flags)!.GetValue(form)!;
                var rows = (Dictionary<string, int>)type.GetField("rowIndexes", flags)!.GetValue(form)!;
                Check(grid.Rows[rows["ad0"]].Cells[2].Value?.ToString() == "123", "monitor latest value after switch");
                tabs.SelectedIndex = 2;
                var indicators = (Label[])type.GetField("inputIndicators", flags)!.GetValue(form)!;
                Check(indicators[0].Text.Contains("开"), "logic latest value after switch");
                tabs.SelectedIndex = 1;
                var rs = (DataGridView)type.GetField("rs485Grid", flags)!.GetValue(form)!;
                Check(rs.Rows[0].Cells["state"].Value?.ToString() == "正常" &&
                      rs.Rows[0].Cells["values"].Value?.ToString()?.Contains("42") == true, "RS485 latest value after switch");
                type.GetMethod("MarkRs485TelemetryUnavailable", flags)!.Invoke(form, null);
                tabs.SelectedIndex = 8; tabs.SelectedIndex = 1;
                Check(rs.Rows[0].Cells["state"].Value?.ToString() == "状态读取失败", "stale RS485 value restored after error");
                Console.WriteLine($"100 ms polling with page switches: {snapshots} snapshots, {count} switches, max synchronous switch {maxSwitchMs} ms.");
            }
            catch (Exception ex) { failure = ex; }
            if (!timer.Enabled || failure is not null)
            {
                timer.Stop();
                selected.SetValue(form, originalSelected); active.SetValue(form, originalActive);
                client.Close(); form.Close();
            }
        };
        form.Shown += (_, _) =>
        {
            client.OpenAsync("127.0.0.1", board.LocalEndpoint!.Port, 1, CancellationToken.None).GetAwaiter().GetResult();
            ((NumericUpDown)type.GetField("pollIntervalInput", flags)!.GetValue(form)!).Value = 100;
            selected.SetValue(form, Guid.NewGuid()); active.SetValue(form, true);
            start.Invoke(form, null);
            clock.Start(); timer.Start();
        };
        _ = Task.Run(async () => { await Task.Delay(TimeSpan.FromSeconds(35)); if (timer.Enabled) Environment.Exit(3); });
        Application.Run(form);
        if (failure is not null) throw failure;
    }
}


