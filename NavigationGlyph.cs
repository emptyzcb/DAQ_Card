using System.Drawing.Drawing2D;

namespace IndustrialFieldDataAcquisitionTerminal;

internal enum NavigationGlyphKind
{
    Monitor,
    Rs485,
    Logic,
    Trends,
    Serial,
    Test,
    Devices,
    Can,
    Tools
}

/// <summary>Vector glyph painter shared by the sidebar and any other rail control.</summary>
internal static class NavigationGlyph
{
    /// <summary>Draws a navigation glyph inside the given bounds, scaled from a 20x20 grid.</summary>
    public static void Draw(Graphics graphics, NavigationGlyphKind kind, Rectangle bounds, Color color, float stroke)
    {
        if (bounds.Width < 6 || bounds.Height < 6) return;
        var state = graphics.Save();
        try
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.TranslateTransform(bounds.X, bounds.Y);
            graphics.ScaleTransform(bounds.Width / 20f, bounds.Height / 20f);
            using var pen = new Pen(color, stroke / (bounds.Width / 20f))
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
                LineJoin = LineJoin.Round
            };
            DrawGlyph(graphics, pen, kind);
        }
        finally
        {
            graphics.Restore(state);
        }
    }

    public static void Draw(Graphics graphics, NavigationGlyphKind kind, RectangleF bounds, Color color, float stroke) =>
        Draw(graphics, kind, Rectangle.Round(bounds), color, stroke);

    private static void DrawGlyph(Graphics graphics, Pen pen, NavigationGlyphKind kind)
    {
        switch (kind)
        {
            case NavigationGlyphKind.Monitor:
                graphics.DrawRectangle(pen, 1, 2, 18, 14);
                graphics.DrawLines(pen, new[] { new Point(3, 11), new Point(7, 11),
                    new Point(9, 6), new Point(12, 13), new Point(14, 9), new Point(17, 9) });
                graphics.DrawLine(pen, 7, 19, 13, 19);
                break;
            case NavigationGlyphKind.Rs485:
                graphics.DrawRectangle(pen, 2, 2, 6, 6);
                graphics.DrawRectangle(pen, 12, 12, 6, 6);
                graphics.DrawLine(pen, 8, 5, 15, 5);
                graphics.DrawLine(pen, 15, 5, 15, 12);
                graphics.DrawLine(pen, 2, 15, 12, 15);
                graphics.DrawLine(pen, 5, 8, 5, 15);
                break;
            case NavigationGlyphKind.Logic:
                graphics.DrawLines(pen, new[] { new Point(7, 3), new Point(2, 10), new Point(7, 17) });
                graphics.DrawLines(pen, new[] { new Point(13, 3), new Point(18, 10), new Point(13, 17) });
                graphics.DrawLine(pen, 11, 2, 9, 18);
                break;
            case NavigationGlyphKind.Trends:
                graphics.DrawLine(pen, 2, 17, 18, 17);
                graphics.DrawLine(pen, 2, 17, 2, 3);
                graphics.DrawLines(pen, new[] { new Point(4, 13), new Point(8, 9),
                    new Point(11, 12), new Point(16, 4), new Point(18, 6) });
                break;
            case NavigationGlyphKind.Serial:
                graphics.DrawRectangle(pen, 2, 5, 16, 10);
                graphics.DrawLine(pen, 5, 8, 15, 8);
                graphics.DrawLine(pen, 5, 12, 15, 12);
                using (var dot = new SolidBrush(pen.Color))
                {
                    graphics.FillEllipse(dot, 5, 8, 2, 2);
                    graphics.FillEllipse(dot, 13, 12, 2, 2);
                }
                break;
            case NavigationGlyphKind.Test:
                graphics.DrawEllipse(pen, 2, 2, 16, 16);
                graphics.DrawLines(pen, new[] { new Point(4, 11), new Point(8, 11),
                    new Point(10, 6), new Point(12, 14), new Point(15, 9), new Point(17, 9) });
                break;
            case NavigationGlyphKind.Tools:
                graphics.DrawEllipse(pen, 3, 3, 6, 6);
                graphics.DrawLine(pen, 8, 8, 17, 17);
                graphics.DrawEllipse(pen, 15, 15, 3, 3);
                break;
            case NavigationGlyphKind.Can:
                graphics.DrawRectangle(pen, 4, 4, 12, 12);
                graphics.DrawLine(pen, 1, 7, 4, 7); graphics.DrawLine(pen, 1, 13, 4, 13);
                graphics.DrawLine(pen, 16, 7, 19, 7); graphics.DrawLine(pen, 16, 13, 19, 13);
                graphics.DrawLine(pen, 7, 7, 13, 7); graphics.DrawLine(pen, 7, 10, 13, 10);
                graphics.DrawLine(pen, 7, 13, 13, 13);
                break;
            case NavigationGlyphKind.Devices:
                graphics.DrawRectangle(pen, 2, 3, 16, 6);
                graphics.DrawRectangle(pen, 2, 12, 16, 6);
                graphics.DrawLine(pen, 5, 6, 6, 6);
                graphics.DrawLine(pen, 5, 15, 6, 15);
                graphics.DrawLine(pen, 10, 6, 15, 6);
                graphics.DrawLine(pen, 10, 15, 15, 15);
                break;
        }
    }
}
