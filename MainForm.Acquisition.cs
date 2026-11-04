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
    private async void OnConnectClicked(object? sender, EventArgs e)
    {
        if (!client.IsOpen && !isConnecting) verifiedEngineeringRs485 = null;
        if (isConnecting)
        {
            userCancelledConnect = true;
            connectCancellation?.Cancel();
            connectButton.Enabled = false;
            SetStatus("正在取消连接...");
            return;
        }
        if (client.IsOpen)
        {
            Disconnect();
            return;
        }

        var host = hostInput.Text.Trim();
        var port = (int)ethernetPortInput.Value;
        var unitId = (byte)slaveAddressInput.Value;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        connectCancellation = timeout;
        userCancelledConnect = false;
        isConnecting = true;
        UpdateDeviceConnectionState();
        connectButton.Text = "取消连接";
        hostInput.Enabled = false;
        ethernetPortInput.Enabled = false;
        slaveAddressInput.Enabled = false;
        SetStatus("正在连接以太网...");
        try
        {
            await openConnection(host, port, unitId, timeout.Token);
            timeout.Token.ThrowIfCancellationRequested();
            if (IsDisposed || Disposing)
            {
                client.Close();
                return;
            }
            firmwareStatsSupported = true;
            PrepareTrendForDevice(host, port, unitId);
            Interlocked.Increment(ref connectionEpoch);
            SetConnectedUi(true);
            SetStatus("已连接，正在探测以太网链路...");
            ProbeLinkAsync();
        }
        catch (OperationCanceledException)
        {
            client.Close();
            if (IsDisposed || Disposing) return;
            var message = userCancelledConnect ? "连接已取消" : "连接超时，请检查网线、电脑网卡和板卡 IP";
            SetStatus(message);
            OnClientLog($"{DateTime.Now:HH:mm:ss.fff}  ERROR  {message}");
        }
        catch (Exception ex)
        {
            client.Close();
            if (IsDisposed || Disposing) return;
            SetStatus("连接失败");
            OnClientLog($"{DateTime.Now:HH:mm:ss.fff}  ERROR  连接失败：{ex.Message}");
        }
        finally
        {
            if (ReferenceEquals(connectCancellation, timeout)) connectCancellation = null;
            isConnecting = false;
            if (!IsDisposed && !Disposing)
            {
                connectButton.Enabled = true;
                SetConnectedUi(client.IsOpen);
            }
        }
    }

    private void Disconnect()
    {
        verifiedEngineeringRs485 = null;
        Interlocked.Increment(ref connectionEpoch);
        StopPolling();
        client.Close();
        latestSnapshotForDisplay = null;
        latestRs485ForDisplay = null;
        SetConnectedUi(false);
        SetStatus("未连接");
        OnClientLog($"{DateTime.Now:HH:mm:ss.fff}  以太网已断开");
    }

    private void StartPolling()
    {
        if (pollTask is not null)
        {
            return;
        }

        Interlocked.Increment(ref trendSegment);
        Interlocked.Exchange(ref lastPollStartTimestamp, 0);
        Interlocked.Exchange(ref lastOnlineStatusSecond, 0);
        lastSnapshotDisplayTimestamp = 0;
        pollCancellation = new CancellationTokenSource();
        var token = pollCancellation.Token;
        pollTask = Task.Run(() => PollLoopAsync(token), token);
        pollToggleButton.Text = "停止轮询";
        pollToggleButton.BackColor = UiPalette.Red;
        pollToggleButton.ForeColor = Color.White;
        pollToggleButton.FlatStyle = FlatStyle.Flat;
        pollToggleButton.FlatAppearance.BorderSize = 0;
    }

    private void StopPolling()
    {
        Interlocked.Increment(ref trendSegment);
        Interlocked.Exchange(ref lastPollStartTimestamp, 0);
        samplingTimingLabel.Text = "实际间隔：--";
        pollCancellation?.Cancel();
        pollCancellation?.Dispose();
        pollCancellation = null;
        pollTask = null;
        if (pendingSnapshotDisplay is { } finalSnapshot) ApplySnapshotDisplay(finalSnapshot);
        if (!trendPaused && contentTabs.SelectedTab?.Text == "趋势记录") RefreshTrendChart();
        pollToggleButton.Text = "开始轮询";
        UiPalette.StyleButton(pollToggleButton);
    }

    private void OnPollToggleClicked(object? sender, EventArgs e)
    {
        if (!client.IsOpen)
        {
            SetStatus("请先连接以太网");
            return;
        }

        if (pollTask is not null)
        {
            StopPolling();
            SetStatus("已连接，轮询已停止");
        }
        else
        {
            StartPolling();
            SetStatus("已连接，正在轮询");
        }
    }

    private void ProbeLinkAsync()
    {
        _ = Task.Run(() =>
        {
            if (!client.IsOpen)
            {
                return;
            }

            try
            {
                pollGate.Wait();
                ushort[] probe;
                try
                {
                    probe = client.ReadHoldingRegisters(0x0000, 1);
                }
                finally
                {
                    pollGate.Release();
                }

                if (!client.IsOpen)
                {
                    return;
                }

                SetStatus("已连接，以太网对接成功");
                OnClientLog($"{DateTime.Now:HH:mm:ss.fff}  以太网对接成功：Unit ID={client.UnitId}，状态寄存器=0x{probe[0]:X4}");
            }
            catch (Exception ex)
            {
                if (!client.IsOpen)
                {
                    return;
                }

                SetStatus("已连接，以太网对接失败");
                OnClientLog($"{DateTime.Now:HH:mm:ss.fff}  ERROR  以太网对接失败：{ex.Message}");
            }
        });
    }

    private async Task PollLoopAsync(CancellationToken token)
    {
        var nextStart = Stopwatch.GetTimestamp();
        try
        {
            while (!token.IsCancellationRequested)
            {
                await PollOnceAsync(token);
                var intervalTicks = Math.Max(1L, (long)(pollIntervalMs * (double)Stopwatch.Frequency / 1000));
                nextStart += intervalTicks;
                var now = Stopwatch.GetTimestamp();
                if (nextStart <= now) nextStart += ((now - nextStart) / intervalTicks + 1) * intervalTicks;
                await Task.Delay(TimeSpan.FromSeconds((nextStart - now) / (double)Stopwatch.Frequency), token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }

    private async Task PollOnceAsync(CancellationToken token)
    {
        var pollEpoch = Volatile.Read(ref connectionEpoch);
        if (!client.IsOpen || token.IsCancellationRequested)
        {
            return;
        }

        await pollGate.WaitAsync(token);
        var pollSegment = Volatile.Read(ref trendSegment);
        try
        {
            var startedTicks = Stopwatch.GetTimestamp();
            var priorTicks = Interlocked.Exchange(ref lastPollStartTimestamp, startedTicks);
            double? actualInterval = priorTicks == 0 ? null : (startedTicks - priorTicks) * 1000.0 / Stopwatch.Frequency;
            var pollStartedAt = DateTime.Now;
            var status = client.ReadHoldingRegisters(0x0000, 3);
            var statusObservedAt = DateTime.Now;
            var ad7606 = client.ReadHoldingRegisters(0x0010, 8);
            var adObservedAt = DateTime.Now;
            var ioController = client.ReadHoldingRegisters(0x0077, 5);
            var outputObservedAt = DateTime.Now;
            ushort[]? linkStats = null;
            IReadOnlyList<Rs485SensorTelemetry>? rs485Sensors = null;
            DateTime? rs485ObservedAt = null;

            if (firmwareStatsSupported)
            {
                try
                {
                    linkStats = client.ReadHoldingRegisters(0x0060, 4);
                }
                catch (Exception ex)
                {
                    firmwareStatsSupported = false;
                    OnClientLog($"{DateTime.Now:HH:mm:ss.fff}  ERROR  链路统计寄存器(0x0060)读取失败，已停用：{ex.Message}");
                }
            }

            if (DateTime.UtcNow >= nextRs485TelemetryPollUtc)
            {
                try
                {
                    rs485Sensors = rs485Protocol.ReadTelemetry();
                    rs485ObservedAt = DateTime.Now;
                    nextRs485TelemetryPollUtc = DateTime.UtcNow.AddSeconds(1);
                }
                catch (Exception ex)
                {
                    nextRs485TelemetryPollUtc = DateTime.UtcNow.AddSeconds(5);
                    Ui(MarkRs485TelemetryUnavailable);
                    OnClientLog($"{DateTime.Now:HH:mm:ss.fff}  ERROR  RS485 传感器数据读取失败：{ex.Message}");
                }
            }

            var snapshot = new AcquisitionSnapshot
            {
                Timestamp = DateTime.Now,
                SegmentId = pollSegment,
                ActualPollIntervalMs = actualInterval,
                PollStartedAt = pollStartedAt,
                DigitalInputsObservedAt = statusObservedAt,
                Ad7606ObservedAt = adObservedAt,
                AnalogOutputsObservedAt = outputObservedAt,
                Rs485ObservedAt = rs485ObservedAt,
                Status = status[0],
                DigitalInputs = status[1],
                DigitalOutputs = status[2],
                IoControllerState = ioController[0],
                IoControllerFlags = ioController[2],
                AnalogVoltageMv = ioController[3],
                AnalogCurrentUa = ioController[4],
                Ad7606Raw = ad7606.Select(value => unchecked((short)value)).ToArray(),
                LinkStats = linkStats,
                Rs485Sensors = rs485Sensors
            };

            Ui(() => { if (!token.IsCancellationRequested && pollSegment == Volatile.Read(ref trendSegment) && pollEpoch == Volatile.Read(ref connectionEpoch) && client.IsOpen) UpdateSnapshot(snapshot); });
            var second = snapshot.Timestamp.Ticks / TimeSpan.TicksPerSecond;
            if (Interlocked.Exchange(ref lastOnlineStatusSecond, second) != second)
                SetStatus($"在线 · 最后响应 {snapshot.Timestamp:HH:mm:ss}");
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Interlocked.CompareExchange(ref trendSegment, pollSegment + 1, pollSegment);
            Interlocked.Exchange(ref lastPollStartTimestamp, 0);
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
            SetStatus("请先连接以太网");
            return;
        }

        if (pollTask is null) { Interlocked.Increment(ref trendSegment); Interlocked.Exchange(ref lastPollStartTimestamp, 0); }
        _ = Task.Run(() => PollOnceAsync(CancellationToken.None));
    }

    private void UpdateSnapshot(AcquisitionSnapshot snapshot)
    {
        // Record every received snapshot; limit expensive live-control repaint work to 5 Hz.
        RecordTrendSnapshot(snapshot);
        latestSnapshotForDisplay = snapshot;
        if (snapshot.Rs485Sensors is not null) latestRs485ForDisplay = snapshot.Rs485Sensors;
        pendingSnapshotDisplay = snapshot;
        var nowTicks = Stopwatch.GetTimestamp();
        if (snapshot.Rs485Sensors is null && lastSnapshotDisplayTimestamp != 0 &&
            (nowTicks - lastSnapshotDisplayTimestamp) * 1000.0 / Stopwatch.Frequency < 200) return;
        ApplySnapshotDisplay(snapshot);
    }

    private void ApplySnapshotDisplay(AcquisitionSnapshot snapshot, bool forcePageRefresh = false)
    {
        pendingSnapshotDisplay = null;
        lastSnapshotDisplayTimestamp = Stopwatch.GetTimestamp();
        samplingTimingLabel.Text = snapshot.ActualPollIntervalMs is { } interval ? $"实际间隔：{interval:0} ms" : "实际间隔：--";
        if (contentTabs.SelectedIndex == 2)
        {
            analogVoltageIndicator.Text = $"U1  {snapshot.AnalogVoltageMv / 1000.0:0.000} V";
            analogCurrentIndicator.Text = $"I1  {snapshot.AnalogCurrentUa / 1000.0:0.000} mA";
            SetAllIndicators(snapshot.DigitalInputs, snapshot.DigitalOutputs);
        }
        if (contentTabs.SelectedIndex == 1 && latestRs485ForDisplay is not null &&
            (forcePageRefresh || snapshot.Rs485Sensors is not null))
            UpdateRs485Telemetry(latestRs485ForDisplay);
        if (contentTabs.SelectedIndex != 0) return;
        UpdateMonitorTiles(snapshot);
        SetValue("digitalInputs", $"0x{snapshot.DigitalInputs:X4}");
        SetValue("digitalOutputs", $"0x{snapshot.DigitalOutputs:X4}");
        SetValue("ioControllerState", DescribeIoControllerState(snapshot.IoControllerState));
        SetValue("ioControllerFlags", $"0x{snapshot.IoControllerFlags:X4}");
        SetValue("analogVoltage", $"{snapshot.AnalogVoltageMv} mV ({snapshot.AnalogVoltageMv / 1000.0:0.000} V)");
        SetValue("analogCurrent", $"{snapshot.AnalogCurrentUa} uA ({snapshot.AnalogCurrentUa / 1000.0:0.000} mA)");
        SetValue("status", $"0x{snapshot.Status:X4} ({DescribeStatus(snapshot.Status)})");

        for (var index = 0; index < snapshot.Ad7606Raw.Length; index++)
        {
            var raw = snapshot.Ad7606Raw[index];
            var config = currentEngineering.FirstOrDefault(c => c.Source == $"AD:{index}");
            var valid = (snapshot.Status & 0x0008) != 0;
            SetValue($"ad{index}", config is null ? raw.ToString() : valid
                ? $"{EngineeringChannel.Format(config.Convert(raw))} {config.Unit}  (原始 {raw})"
                : $"原始 {raw} · 数据未确认");
            var nameCell = dataGrid.Rows[rowIndexes[$"ad{index}"]].Cells[0];
            var channelName = config is null ? $"AD7606 CH{index + 1}" : $"CH{index + 1} · {config.Name}";
            if (!string.Equals(nameCell.Value?.ToString(), channelName, StringComparison.Ordinal)) nameCell.Value = channelName;
        }

        lastPollLabel.Text = $"最近接收：{snapshot.Timestamp:yyyy-MM-dd HH:mm:ss.fff}";

        if (snapshot.LinkStats is { Length: 4 } stats)
        {
            SetValue("statRx", stats[0].ToString());
            SetValue("statBad", stats[1].ToString());
            SetValue("statDrop", stats[2].ToString());
            SetValue("statTx", stats[3].ToString());
        }
        else
        {
            SetValue("statRx", "N/A");
            SetValue("statBad", "N/A");
            SetValue("statDrop", "N/A");
            SetValue("statTx", "N/A");
        }
    }

    private static string DescribeStatus(ushort status)
    {
        var flags = new List<string>();
        if ((status & 0x0002) != 0) flags.Add("AD7606");
        if ((status & 0x0008) != 0) flags.Add("AD读取OK");
        return flags.Count == 0 ? "无就绪标志" : string.Join(", ", flags);
    }

    private static string DescribeIoControllerState(ushort state)
    {
        var runState = (state >> 8) & 0x00FF;
        var externalMode = (state >> 4) & 0x000F;
        var currentMode = state & 0x000F;
        var runText = runState switch
        {
            0 => "停止",
            1 => "运行",
            2 => "结束",
            _ => $"未知{runState}"
        };
        return $"{runText} · 当前 M{currentMode} · 外部 M{externalMode} (0x{state:X4})";
    }

    private void SetValue(string key, string value)
    {
        if (rowIndexes.TryGetValue(key, out var row))
        {
            var cell = dataGrid.Rows[row].Cells[2];
            if (!string.Equals(cell.Value?.ToString(), value, StringComparison.Ordinal)) cell.Value = value;
        }
    }

    private void UpdateMonitorTiles(AcquisitionSnapshot snapshot)
    {
        var ready = (snapshot.Status & 0x0008) != 0;
        kpiLink.Value = ready ? "正常" : "待确认";
        kpiLink.ValueColor = ready ? UiPalette.Green : UiPalette.Warning;
        kpiLink.Hint = $"状态字 0x{snapshot.Status:X4}";
        kpiInterval.Value = snapshot.ActualPollIntervalMs is { } interval ? $"{interval:0} ms" : "--";
        kpiInterval.ValueColor = UiPalette.Ink;
        kpiInterval.Hint = $"目标 {pollIntervalInput.Value:0} ms";
        var channels = snapshot.Ad7606Raw.Length;
        kpiAd.Value = ready ? $"{channels}/{channels}" : $"0/{channels}";
        kpiAd.ValueColor = ready ? UiPalette.Ink : UiPalette.Faint;
        kpiAd.Hint = ready ? "采样数据已确认" : "AD7606 未就绪";
        kpiIo.Value = $"IN {CountBits(snapshot.DigitalInputs)} · OUT {CountBits(snapshot.DigitalOutputs)}";
        kpiIo.ValueColor = UiPalette.Ink;
        kpiIo.Hint = $"输入 0x{snapshot.DigitalInputs:X4} · 输出 0x{snapshot.DigitalOutputs:X4}";
    }

    private void ResetMonitorTiles()
    {
        kpiLink.Value = "--";
        kpiLink.ValueColor = UiPalette.Faint;
        kpiLink.Hint = "等待连接板卡";
        kpiInterval.Value = "--";
        kpiInterval.ValueColor = UiPalette.Faint;
        kpiAd.Value = "0/8";
        kpiAd.ValueColor = UiPalette.Faint;
        kpiIo.Value = "IN 0 · OUT 0";
        kpiIo.ValueColor = UiPalette.Faint;
        kpiIo.Hint = "输入位图 / 输出位图";
    }

    private static int CountBits(ushort value)
    {
        var count = 0;
        for (var bit = 0; bit < 16; bit++) if ((value & (1 << bit)) != 0) count++;
        return count;
    }

    private void SetConnectedUi(bool connected)
    {
        UpdateConnectionSummary();
        hostInput.Enabled = !connected;
        ethernetPortInput.Enabled = !connected;
        slaveAddressInput.Enabled = !connected;
        refreshPortsButton.Enabled = !connected;
        connectButton.Text = connected ? "断开以太网" : "连接以太网";
        connectButton.BackColor = connected ? UiPalette.Red : UiPalette.Blue;
        pollIntervalInput.Enabled = connected;
        pollNowButton.Enabled = connected;
        pollToggleButton.Enabled = connected;
        if (!connected)
        {
            pollToggleButton.Text = "开始轮询";
            UiPalette.StyleButton(pollToggleButton);
            ResetMonitorTiles();
        }

        rs485UploadButton.Enabled = connected;
        rs485ReadButton.Enabled = connected;
        downloadLogicButton.Enabled = connected;
        UpdateDeviceConnectionState();
    }

}
