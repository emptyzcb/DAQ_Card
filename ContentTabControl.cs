namespace IndustrialFieldDataAcquisitionTerminal;

internal sealed class ContentTabControl : TabControl
{
    private const int TcmAdjustRect = 0x1328;

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == TcmAdjustRect && !DesignMode)
        {
            message.Result = (IntPtr)1;
            return;
        }

        base.WndProc(ref message);
    }
}
