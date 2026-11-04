namespace IndustrialFieldDataAcquisitionTerminal;

internal sealed partial class MainForm
{
    private async Task ToggleCanTraceAsync()
    {
        if (canTraceFinishing) return;
        if (canTrace is not null) { await StopCanTraceAsync("已停止记录"); return; }
        using var dialog = new SaveFileDialog { Title = "开始连续记录 CAN 报文", Filter = "CSV 文件 (*.csv)|*.csv",
            FileName = $"CAN记录_{DateTime.Now:yyyyMMdd_HHmmss}.csv" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            canTrace = new CanTraceRecorder(dialog.FileName);
            canTracePath = dialog.FileName; canTraceCount = 0;
            canTraceButton.Text = "停止记录";
            canTraceStatus.Text = "正在记录全部收发帧";
            canTraceStatus.ForeColor = UiPalette.Green;
        }
        catch (Exception ex) { canTraceStatus.Text = "无法开始记录：" + ex.Message; canTraceStatus.ForeColor = UiPalette.Red; }
    }
    private async Task StopCanTraceAsync(string reason)
    {
        var recorder = canTrace;
        if (recorder is null || canTraceFinishing) return;
        canTraceFinishing = true; canTraceButton.Enabled = false; canTraceStatus.Text = "正在写完记录…";
        try
        {
            await recorder.FinishAsync();
            if (!IsDisposed && !Disposing)
            {
                canTraceStatus.Text = $"{reason} · {canTraceCount} 帧";
                canTraceStatus.ForeColor = UiPalette.Muted;
                canTraceStatus.Tag = canTracePath;
            }
        }
        catch (Exception ex) { if (!IsDisposed && !Disposing) { canTraceStatus.Text = "记录不完整：" + ex.Message; canTraceStatus.ForeColor = UiPalette.Red; } }
        finally
        {
            canTrace = null; canTraceFinishing = false;
            if (!IsDisposed && !Disposing) { canTraceButton.Enabled = true; canTraceButton.Text = "开始记录"; }
        }
    }
}
