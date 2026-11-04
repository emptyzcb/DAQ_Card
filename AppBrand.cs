using System.Drawing.Drawing2D;

namespace IndustrialFieldDataAcquisitionTerminal;

internal static class AppBrand
{
    public static Bitmap CreateBitmap(int size)
    {
        var bitmap = new Bitmap(size, size);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(Color.Transparent);
        graphics.ScaleTransform(size / 64f, size / 64f);

        using var shape = RoundedSquare(2, 2, 60, 60, 13);
        using var background = new LinearGradientBrush(new Point(2, 2), new Point(62, 62),
            Color.FromArgb(25, 113, 231), Color.FromArgb(19, 168, 191));
        graphics.FillPath(background, shape);
        using var rail = new Pen(Color.FromArgb(120, 235, 248), 1.8f);
        graphics.DrawLine(rail, 12, 16, 35, 16);
        graphics.DrawLine(rail, 12, 48, 52, 48);
        using var wave = new Pen(Color.White, 4.2f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        };
        graphics.DrawLines(wave, new[]
        {
            new Point(12, 36), new Point(23, 36), new Point(28, 24),
            new Point(35, 44), new Point(41, 31), new Point(52, 31)
        });
        using var marker = new SolidBrush(Color.FromArgb(174, 255, 229));
        graphics.FillEllipse(marker, 47, 11, 7, 7);
        return bitmap;
    }

    private static GraphicsPath RoundedSquare(float x, float y, float width, float height, float radius)
    {
        var path = new GraphicsPath();
        var diameter = radius * 2;
        path.AddArc(x, y, diameter, diameter, 180, 90);
        path.AddArc(x + width - diameter, y, diameter, diameter, 270, 90);
        path.AddArc(x + width - diameter, y + height - diameter, diameter, diameter, 0, 90);
        path.AddArc(x, y + height - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}
