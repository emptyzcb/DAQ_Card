using System.Drawing.Drawing2D;

namespace IndustrialFieldDataAcquisitionTerminal;

internal sealed class GradientHeaderPanel : Panel
{
    private static readonly ColorBlend Colors = new()
    {
        Positions = new[] { 0f, 0.18f, 0.30f, 0.54f, 0.78f, 1f },
        Colors = new[]
        {
            Color.FromArgb(26, 156, 210),
            Color.FromArgb(99, 210, 226),
            Color.FromArgb(59, 189, 245),
            Color.FromArgb(128, 209, 255),
            Color.FromArgb(116, 185, 236),
            Color.FromArgb(54, 197, 204)
        }
    };

    public GradientHeaderPanel()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        if (ClientSize.Width < 2 || ClientSize.Height < 1) return;
        using var brush = new LinearGradientBrush(ClientRectangle, Colors.Colors[0], Colors.Colors[^1], 0f)
        {
            InterpolationColors = Colors
        };
        e.Graphics.FillRectangle(brush, ClientRectangle);
    }
}
