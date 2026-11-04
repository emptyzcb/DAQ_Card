using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace IndustrialFieldDataAcquisitionTerminal;

/// <summary>
/// A rounded white surface with a hairline border. Children are laid out normally,
/// so it can host any existing page content without clipping it.
/// </summary>
internal class CardPanel : Panel
{
    private int radius = UiPalette.CardCornerRadius;
    private Color fill = UiPalette.Surface;
    private Color stroke = UiPalette.Border;

    public CardPanel()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Radius
    {
        get => radius;
        set { radius = value; Invalidate(); }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color FillColor
    {
        get => fill;
        set { fill = value; Invalidate(); }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color StrokeColor
    {
        get => stroke;
        set { stroke = value; Invalidate(); }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var graphics = e.Graphics;
        var parentColor = Parent?.BackColor ?? UiPalette.Canvas;
        using (var background = new SolidBrush(parentColor)) graphics.FillRectangle(background, ClientRectangle);
        if (Width <= 2 || Height <= 2) return;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = UiPalette.RoundedPath(bounds, Math.Max(1, UiPalette.Scaled(this, radius)));
        using var fillBrush = new SolidBrush(fill);
        graphics.FillPath(fillBrush, path);
        if (stroke.A > 0)
        {
            using var pen = new Pen(stroke);
            graphics.DrawPath(pen, path);
        }
    }
}

/// <summary>Owner-drawn sidebar entry: glyph, label and a soft active pill.</summary>
internal sealed class NavItem : Button
{
    private bool hover;
    private bool active;
    private NavigationGlyphKind glyph = NavigationGlyphKind.Monitor;

    public NavItem()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        FlatAppearance.MouseOverBackColor = Color.Transparent;
        FlatAppearance.MouseDownBackColor = Color.Transparent;
        BackColor = UiPalette.Sidebar;
        ForeColor = UiPalette.InkSecondary;
        Font = UiPalette.NavFont;
        Cursor = Cursors.Hand;
        TextAlign = ContentAlignment.MiddleLeft;
        UseVisualStyleBackColor = false;
        TabStop = false;
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public NavigationGlyphKind Glyph
    {
        get => glyph;
        set { glyph = value; Invalidate(); }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Active
    {
        get => active;
        set
        {
            if (active == value) return;
            active = value;
            Invalidate();
        }
    }

    /// <summary>Optional right-aligned short marker, for example a live dot count.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Badge { get; set; } = string.Empty;

    protected override void OnMouseEnter(EventArgs e)
    {
        hover = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        hover = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var graphics = e.Graphics;
        var dpi = DeviceDpi / 96f;
        using (var background = new SolidBrush(BackColor)) graphics.FillRectangle(background, ClientRectangle);
        if (Width <= 4 || Height <= 4) return;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

        var pill = new Rectangle(0, 0, Width - 1, Height - 1);
        if (active || hover)
        {
            using var path = UiPalette.RoundedPath(pill, (int)Math.Round(10 * dpi));
            using var fill = new SolidBrush(active ? UiPalette.AccentSoft : UiPalette.RowHover);
            graphics.FillPath(fill, path);
        }
        if (active)
        {
            var bar = new Rectangle((int)Math.Round(3 * dpi), (Height - (int)Math.Round(18 * dpi)) / 2,
                Math.Max(2, (int)Math.Round(3 * dpi)), (int)Math.Round(18 * dpi));
            using var barPath = UiPalette.RoundedPath(bar, bar.Width);
            using var barBrush = new SolidBrush(UiPalette.Accent);
            graphics.FillPath(barBrush, barPath);
        }

        var iconSize = (int)Math.Round(19 * dpi);
        var iconLeft = (int)Math.Round(14 * dpi);
        var iconBounds = new Rectangle(iconLeft, (Height - iconSize) / 2, iconSize, iconSize);
        NavigationGlyph.Draw(graphics, glyph, iconBounds, active ? UiPalette.Accent : UiPalette.Muted, active ? 1.7f : 1.55f);

        var textLeft = iconLeft + iconSize + (int)Math.Round(12 * dpi);
        var textBounds = new Rectangle(textLeft, 0, Math.Max(1, Width - textLeft - (int)Math.Round(10 * dpi)), Height);
        var font = active ? UiPalette.NavigationActiveFont : UiPalette.NavigationFont;
        TextRenderer.DrawText(graphics, Text, font, textBounds,
            active ? UiPalette.AccentText : UiPalette.InkSecondary,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
    }
}

/// <summary>A rounded status chip with a leading dot. Used for connection and health state.</summary>
internal sealed class StatusPill : Control
{
    private string text = string.Empty;
    private Color tone = UiPalette.Muted;
    private Color toneSoft = UiPalette.SurfaceMuted;

    public StatusPill()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = UiPalette.Surface;
        Font = UiPalette.MetaFont;
    }

    public void SetState(string caption, Color foreground, Color background)
    {
        caption ??= string.Empty;
        if (text == caption && tone == foreground && toneSoft == background) return;
        text = caption;
        tone = foreground;
        toneSoft = background;
        Invalidate();
    }

    protected override void OnTextChanged(EventArgs e)
    {
        text = Text ?? string.Empty;
        base.OnTextChanged(e);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        if (Parent is { } parent)
        {
            using var background = new SolidBrush(parent.BackColor);
            graphics.FillRectangle(background, ClientRectangle);
        }
        if (Width <= 4 || Height <= 4) return;
        var dpi = DeviceDpi / 96f;
        using var path = UiPalette.RoundedPath(new Rectangle(0, 0, Width - 1, Height - 1), Height / 2);
        using var fill = new SolidBrush(toneSoft);
        graphics.FillPath(fill, path);
        var dot = Math.Max(3, (int)Math.Round(6 * dpi));
        using var dotBrush = new SolidBrush(tone);
        graphics.FillEllipse(dotBrush, (int)Math.Round(11 * dpi), (Height - dot) / 2, dot, dot);
        var textBounds = new Rectangle((int)Math.Round(11 * dpi) + dot + (int)Math.Round(7 * dpi), 0,
            Math.Max(1, Width - (int)Math.Round(11 * dpi) - dot - (int)Math.Round(14 * dpi)), Height);
        TextRenderer.DrawText(graphics, text, Font, textBounds, tone,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
    }
}

/// <summary>A quiet metric tile: caption, large value and an optional hint.</summary>
internal sealed class StatTile : Control
{
    private string caption = string.Empty;
    private string value = "--";
    private string hint = string.Empty;
    private Color accent = UiPalette.Ink;

    public StatTile()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = UiPalette.Canvas;
        Margin = Padding.Empty;
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Caption
    {
        get => caption;
        set { var next = value ?? string.Empty; if (caption == next) return; caption = next; Invalidate(); }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Value
    {
        get => value;
        set { var next = value ?? "--"; if (this.value == next) return; this.value = next; Invalidate(); }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Hint
    {
        get => hint;
        set { var next = value ?? string.Empty; if (hint == next) return; hint = next; Invalidate(); }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color ValueColor
    {
        get => accent;
        set { if (accent == value) return; accent = value; Invalidate(); }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var graphics = e.Graphics;
        var dpi = DeviceDpi / 96f;
        using (var background = new SolidBrush(Parent?.BackColor ?? UiPalette.Canvas))
            graphics.FillRectangle(background, ClientRectangle);
        if (Width <= 6 || Height <= 6) return;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using (var path = UiPalette.RoundedPath(new Rectangle(0, 0, Width - 1, Height - 1), (int)Math.Round(12 * dpi)))
        {
            using var fill = new SolidBrush(UiPalette.Surface);
            graphics.FillPath(fill, path);
            using var pen = new Pen(UiPalette.Border);
            graphics.DrawPath(pen, path);
        }
        var left = (int)Math.Round(16 * dpi);
        var width = Width - left - (int)Math.Round(12 * dpi);
        if (width <= 8) return;
        var captionHeight = (int)Math.Round(18 * dpi);
        TextRenderer.DrawText(graphics, caption, UiPalette.MetaFont, new Rectangle(left, (int)Math.Round(12 * dpi), width, captionHeight),
            UiPalette.Muted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        var valueHeight = (int)Math.Round(28 * dpi);
        var valueTop = (int)Math.Round(12 * dpi) + captionHeight + (int)Math.Round(2 * dpi);
        TextRenderer.DrawText(graphics, value, UiPalette.MetricFont, new Rectangle(left, valueTop, width, valueHeight),
            accent, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        if (hint.Length == 0) return;
        var hintTop = valueTop + valueHeight;
        var hintHeight = Height - hintTop - (int)Math.Round(10 * dpi);
        if (hintHeight < 12) return;
        TextRenderer.DrawText(graphics, hint, UiPalette.MetaFont, new Rectangle(left, hintTop, width, hintHeight),
            UiPalette.Faint, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
    }
}
