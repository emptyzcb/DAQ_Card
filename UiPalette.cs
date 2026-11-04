using System.Drawing.Drawing2D;
using System.Runtime.CompilerServices;

namespace IndustrialFieldDataAcquisitionTerminal;

// Shared WinForms design tokens and control styling. Business state remains in each page.
//
// Visual language: calm light console.
//   * Cool near-neutral canvas, pure white cards, hairline borders, no heavy shadows.
//   * One low-saturation indigo accent; semantic colours stay muted so status never shouts.
//   * Generous spacing on an 8 px rhythm, 44 px+ row heights, clear type hierarchy.
internal static class UiPalette
{
    // ---------------------------------------------------------------- surfaces
    public static readonly Color Canvas = Color.FromArgb(0xF5, 0xF6, 0xF8);
    public static readonly Color CanvasSunken = Color.FromArgb(0xEE, 0xF0, 0xF4);
    public static readonly Color Surface = Color.White;
    public static readonly Color Sidebar = Color.FromArgb(0xFB, 0xFC, 0xFD);
    public static readonly Color SurfaceMuted = Color.FromArgb(0xF7, 0xF8, 0xFA);
    public static readonly Color SurfaceRaised = Color.FromArgb(0xFC, 0xFD, 0xFE);
    public static readonly Color RowHover = Color.FromArgb(0xF3, 0xF5, 0xF8);

    // ---------------------------------------------------------------- strokes
    public static readonly Color Border = Color.FromArgb(0xE6, 0xE9, 0xEE);
    public static readonly Color BorderSoft = Color.FromArgb(0xEF, 0xF1, 0xF5);
    public static readonly Color BorderStrong = Color.FromArgb(0xD5, 0xDA, 0xE3);

    // ---------------------------------------------------------------- ink
    public static readonly Color Ink = Color.FromArgb(0x16, 0x19, 0x1F);
    public static readonly Color InkSecondary = Color.FromArgb(0x3C, 0x43, 0x50);
    public static readonly Color Muted = Color.FromArgb(0x66, 0x70, 0x85);
    public static readonly Color Faint = Color.FromArgb(0x98, 0xA2, 0xB3);

    // ---------------------------------------------------------------- accent
    public static readonly Color Accent = Color.FromArgb(0x3E, 0x63, 0xDD);
    public static readonly Color AccentHover = Color.FromArgb(0x35, 0x57, 0xC9);
    public static readonly Color AccentPressed = Color.FromArgb(0x2C, 0x4B, 0xB4);
    public static readonly Color AccentSoft = Color.FromArgb(0xED, 0xF0, 0xFE);
    public static readonly Color AccentSoftHover = Color.FromArgb(0xE2, 0xE7, 0xFD);
    public static readonly Color AccentText = Color.FromArgb(0x2E, 0x4D, 0xC8);

    // Legacy names kept so every page keeps compiling against one palette.
    public static readonly Color Blue = Accent;
    public static readonly Color BlueSoft = AccentSoft;
    public static readonly Color Green = Color.FromArgb(0x1B, 0x83, 0x5A);
    public static readonly Color GreenSoft = Color.FromArgb(0xE7, 0xF6, 0xEF);
    public static readonly Color Red = Color.FromArgb(0xBE, 0x43, 0x3D);
    public static readonly Color RedSoft = Color.FromArgb(0xFC, 0xED, 0xEC);
    public static readonly Color Warning = Color.FromArgb(0xA9, 0x74, 0x14);
    public static readonly Color WarningSoft = Color.FromArgb(0xFB, 0xF3, 0xE4);

    // ---------------------------------------------------------------- code surfaces
    public static readonly Color CodeBackground = Color.FromArgb(0xFC, 0xFD, 0xFE);
    public static readonly Color CodeReadOnlyBackground = Color.FromArgb(0xF8, 0xFA, 0xFC);
    public static readonly Color CodeChrome = Color.FromArgb(0xF4, 0xF6, 0xFA);
    public static readonly Color CodeText = Color.FromArgb(0x27, 0x2E, 0x3B);
    public static readonly Color CodeMuted = Color.FromArgb(0x8A, 0x93, 0xA3);
    public static readonly Color CodeKeyword = Color.FromArgb(0x36, 0x5C, 0xC0);
    public static readonly Color CodeType = Color.FromArgb(0x0F, 0x76, 0x6E);
    public static readonly Color CodeIo = Color.FromArgb(0x1D, 0x6B, 0x9E);
    public static readonly Color CodeComment = Color.FromArgb(0x6E, 0x7C, 0x72);
    public static readonly Color CodeString = Color.FromArgb(0xA3, 0x5A, 0x3C);
    public static readonly Color CodeDirective = Color.FromArgb(0x7A, 0x54, 0x9E);
    public static readonly Color CodeNumber = Color.FromArgb(0x8C, 0x4C, 0x7F);
    public static readonly Color CodeFunction = Color.FromArgb(0x8A, 0x64, 0x22);
    public static readonly Color LogText = Color.FromArgb(0x4B, 0x55, 0x66);
    public static readonly Color LogTx = Color.FromArgb(0x18, 0x7A, 0x5C);
    public static readonly Color LogRx = Color.FromArgb(0x2F, 0x5B, 0xA8);
    public static readonly Color LogError = Color.FromArgb(0xBE, 0x43, 0x3D);

    // ---------------------------------------------------------------- type
    private const string UiFamily = "Microsoft YaHei UI";
    public static readonly Font BodyFont = new(UiFamily, 10F);
    public static readonly Font BodyStrongFont = new(UiFamily, 10F, FontStyle.Bold);
    public static readonly Font MetaFont = new(UiFamily, 9F);
    public static readonly Font MetaStrongFont = new(UiFamily, 9F, FontStyle.Bold);
    public static readonly Font SectionFont = new(UiFamily, 12F, FontStyle.Bold);
    public static readonly Font PageTitleFont = new(UiFamily, 18F, FontStyle.Bold);
    public static readonly Font CardTitleFont = new(UiFamily, 11F, FontStyle.Bold);
    public static readonly Font NavFont = new(UiFamily, 10.5F);
    public static readonly Font NavigationFont = NavFont;
    public static readonly Font NavigationActiveFont = new(UiFamily, 10.5F, FontStyle.Bold);
    public static readonly Font NavigationCompactFont = NavFont;
    public static readonly Font NavigationCompactActiveFont = NavigationActiveFont;
    public static readonly Font MonoFont = new("Consolas", 10F);
    public static readonly Font MonoSmallFont = new("Consolas", 9F);
    public static readonly Font MetricFont = new("Consolas", 16F, FontStyle.Bold);

    // ---------------------------------------------------------------- metrics
    public const int ButtonCornerRadius = 8;
    public const int CardCornerRadius = 14;
    public const int GridRowHeight = 46;
    public const int GridHeaderHeight = 44;
    public const int SidebarWidth = 248;
    public const int ControlHeight = 36;
    public const int PagePadding = 24;
    public const int Space1 = 4;
    public const int Space2 = 8;
    public const int Space3 = 12;
    public const int Space4 = 16;
    public const int Space5 = 20;
    public const int Space6 = 24;
    public const int Space8 = 32;

    private static readonly ConditionalWeakTable<Control, object> RoundedControls = new();
    private static readonly ConditionalWeakTable<Button, ButtonState> ButtonStates = new();
    private sealed class ButtonState { public Color Back; public Color Fore; public bool Muted; }
    private static readonly ConditionalWeakTable<DataGridView, object> StyledGrids = new();

    /// <summary>Scales a design-time pixel value for the control's current DPI.</summary>
    public static int Scaled(Control control, float value) =>
        Math.Max(1, (int)Math.Round(value * control.DeviceDpi / 96f));

    // ---------------------------------------------------------------- buttons
    /// <summary>Secondary button: white card face with a hairline border.</summary>
    public static void StyleButton(Button button, bool primary = false) => StyleButton(button,
        primary ? ButtonEmphasis.Primary : ButtonEmphasis.Secondary);

    public static void StyleButton(Button button, ButtonEmphasis emphasis)
    {
        var (back, fore, border, hover, pressed) = emphasis switch
        {
            ButtonEmphasis.Primary => (Accent, Color.White, Accent, AccentHover, AccentPressed),
            ButtonEmphasis.Quiet => (Canvas, Muted, Color.Transparent, RowHover, CanvasSunken),
            ButtonEmphasis.Danger => (Surface, Red, BorderStrong, RedSoft, RedSoft),
            ButtonEmphasis.Ghost => (Color.Transparent, Ink, Color.Transparent, RowHover, CanvasSunken),
            _ => (Surface, Ink, BorderStrong, RowHover, CanvasSunken)
        };
        button.UseVisualStyleBackColor = false;
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = border == Color.Transparent ? 0 : 1;
        button.FlatAppearance.BorderColor = border;
        button.FlatAppearance.MouseOverBackColor = hover;
        button.FlatAppearance.MouseDownBackColor = pressed;
        button.BackColor = back;
        button.ForeColor = fore;
        button.Font = emphasis == ButtonEmphasis.Primary ? BodyStrongFont : BodyFont;
        button.Cursor = Cursors.Hand;
        button.TextAlign = ContentAlignment.MiddleCenter;
        if (!ButtonStates.TryGetValue(button, out var state))
        {
            state = new ButtonState();
            ButtonStates.Add(button, state);
            button.EnabledChanged += (_, _) => UpdateButtonEnabledState(button, state);
        }
        // StyleButton may be called again while disabled; remember the new intended style.
        state.Muted = false;
        UpdateButtonEnabledState(button, state);
        RoundControl(button, ButtonCornerRadius);
    }

    private static void UpdateButtonEnabledState(Button button, ButtonState state)
    {
        var disabledBack = Color.FromArgb(0xF1, 0xF3, 0xF6);
        var disabledFore = Color.FromArgb(0xA3, 0xAB, 0xB9);
        if (!button.Enabled && !state.Muted)
        {
            state.Back = button.BackColor;
            state.Fore = button.ForeColor;
            state.Muted = true;
            button.BackColor = disabledBack;
            button.ForeColor = disabledFore;
            button.FlatAppearance.BorderColor = Border;
        }
        else if (button.Enabled && state.Muted)
        {
            if (button.BackColor == disabledBack) button.BackColor = state.Back;
            if (button.ForeColor == disabledFore) button.ForeColor = state.Fore;
            state.Muted = false;
        }
    }

    public static void RoundControl(Control control, int radius)
    {
        if (RoundedControls.TryGetValue(control, out _)) return;
        RoundedControls.Add(control, new object());
        void UpdateRegion()
        {
            if (control.IsDisposed || control.Width < 18 || control.Height < 18) return;
            using var path = RoundedPath(new Rectangle(0, 0, control.Width, control.Height), radius);
            var previous = control.Region;
            control.Region = new Region(path);
            previous?.Dispose();
        }
        control.SizeChanged += (_, _) => UpdateRegion();
        UpdateRegion();
    }

    public static GraphicsPath RoundedPath(Rectangle bounds, int radius)
    {
        var result = new GraphicsPath();
        var diameter = Math.Max(2, Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height)));
        result.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        result.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        result.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        result.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        result.CloseFigure();
        return result;
    }

    // ---------------------------------------------------------------- theme walk
    public static void ApplyWorkbenchTheme(Control root)
    {
        foreach (Control control in root.Controls)
        {
            if (control is Button button && button.FlatStyle == FlatStyle.Flat) RoundControl(button, ButtonCornerRadius);
            if (control is ComboBox combo) combo.FlatStyle = FlatStyle.Flat;
            if (control is TextBox text && !text.Multiline && text.BorderStyle != BorderStyle.None)
                text.BorderStyle = BorderStyle.FixedSingle;
            if (control is NumericUpDown number && number.BorderStyle != BorderStyle.None)
                number.BorderStyle = BorderStyle.FixedSingle;
            if (control is DataGridView grid) ApplyGrid(grid);
            if (control is ToolStrip strip)
            {
                strip.Renderer = new ToolStripProfessionalRenderer(new WorkbenchColorTable()) { RoundedEdges = false };
                strip.ForeColor = Ink;
            }
            ApplyWorkbenchTheme(control);
        }
    }

    public static void ApplyGrid(DataGridView grid)
    {
        if (StyledGrids.TryGetValue(grid, out _)) return;
        StyledGrids.Add(grid, new object());
        grid.BackgroundColor = Surface;
        grid.BorderStyle = BorderStyle.None;
        grid.EnableHeadersVisualStyles = false;
        grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
        grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        grid.ColumnHeadersHeight = GridHeaderHeight;
        grid.RowTemplate.Height = GridRowHeight;
        if (!grid.VirtualMode && grid.AutoSizeRowsMode == DataGridViewAutoSizeRowsMode.None && grid.Rows.Count < 500)
            foreach (DataGridViewRow row in grid.Rows) row.Height = GridRowHeight;
        grid.GridColor = BorderSoft;
        grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        grid.ColumnHeadersDefaultCellStyle.BackColor = Surface;
        grid.ColumnHeadersDefaultCellStyle.ForeColor = Muted;
        grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Surface;
        grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = Muted;
        grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(14, 0, 8, 0);
        grid.ColumnHeadersDefaultCellStyle.Font = MetaStrongFont;
        grid.DefaultCellStyle.BackColor = Surface;
        grid.DefaultCellStyle.ForeColor = Ink;
        grid.DefaultCellStyle.Padding = new Padding(13, 0, 8, 0);
        grid.DefaultCellStyle.SelectionBackColor = AccentSoft;
        grid.DefaultCellStyle.SelectionForeColor = Ink;
        grid.AlternatingRowsDefaultCellStyle.BackColor = Surface;
        grid.AlternatingRowsDefaultCellStyle.ForeColor = Ink;
        grid.AlternatingRowsDefaultCellStyle.SelectionBackColor = AccentSoft;
        grid.AlternatingRowsDefaultCellStyle.SelectionForeColor = Ink;
        // Repaint only the rows under the pointer; keep selection and cell editing intact.
        var hoverRow = -1;
        grid.CellMouseEnter += (_, e) =>
        {
            if (e.RowIndex == hoverRow) return;
            var old = hoverRow; hoverRow = e.RowIndex;
            if (old >= 0 && old < grid.RowCount) grid.InvalidateRow(old);
            if (hoverRow >= 0 && hoverRow < grid.RowCount) grid.InvalidateRow(hoverRow);
        };
        grid.MouseLeave += (_, _) =>
        {
            var old = hoverRow; hoverRow = -1;
            if (old >= 0 && old < grid.RowCount) grid.InvalidateRow(old);
        };
        grid.RowPrePaint += (_, e) =>
        {
            if (e.RowIndex == hoverRow && (e.State & DataGridViewElementStates.Selected) == 0 && e.InheritedRowStyle is not null)
                e.InheritedRowStyle.BackColor = RowHover;
        };
        if (grid.Columns.Contains("state")) grid.CellPainting += PaintStatusCell;
    }

    private static void PaintStatusCell(object? sender, DataGridViewCellPaintingEventArgs e)
    {
        if (sender is not DataGridView grid || e.Graphics is not { } graphics || e.RowIndex < 0 || e.ColumnIndex < 0 ||
            grid.Columns[e.ColumnIndex].Name != "state" || e.FormattedValue is not string text || string.IsNullOrWhiteSpace(text)) return;
        var success = text.StartsWith("正常", StringComparison.Ordinal);
        var error = text.Contains("异常", StringComparison.Ordinal) || text.Contains("失败", StringComparison.Ordinal);
        var warning = text.Contains("等待", StringComparison.Ordinal);
        var foreground = success ? Green : error ? Red : warning ? Warning : Muted;
        var background = success ? GreenSoft : error ? RedSoft : warning ? WarningSoft : SurfaceMuted;
        e.Paint(e.ClipBounds, DataGridViewPaintParts.All & ~DataGridViewPaintParts.ContentForeground);
        var font = e.CellStyle?.Font ?? BodyFont;
        var width = Math.Min(e.CellBounds.Width - 14, TextRenderer.MeasureText(text, font).Width + 20);
        var height = Math.Min(26, e.CellBounds.Height - 10);
        if (width > 12 && height > 6)
        {
            var rect = new Rectangle(e.CellBounds.Left + 12, e.CellBounds.Top + (e.CellBounds.Height - height) / 2, width, height);
            using var path = RoundedPath(rect, height / 2);
            using var fill = new SolidBrush(background);
            var smoothing = graphics.SmoothingMode; graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.FillPath(fill, path); graphics.SmoothingMode = smoothing;
            TextRenderer.DrawText(graphics, text, font, rect, foreground,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        }
        e.Handled = true;
    }

    private sealed class WorkbenchColorTable : ProfessionalColorTable
    {
        public override Color ToolStripGradientBegin => Surface;
        public override Color ToolStripGradientMiddle => Surface;
        public override Color ToolStripGradientEnd => Surface;
        public override Color MenuItemSelected => AccentSoft;
        public override Color MenuItemBorder => Border;
        public override Color MenuBorder => Border;
        public override Color MenuStripGradientBegin => Surface;
        public override Color MenuStripGradientEnd => Surface;
        public override Color ButtonSelectedHighlight => AccentSoft;
        public override Color ButtonSelectedHighlightBorder => AccentSoft;
        public override Color SeparatorDark => Border;
        public override Color SeparatorLight => Surface;
        public override Color ImageMarginGradientBegin => Surface;
        public override Color ImageMarginGradientMiddle => Surface;
        public override Color ImageMarginGradientEnd => Surface;
    }
}

internal enum ButtonEmphasis
{
    Secondary,
    Primary,
    Quiet,
    Danger,
    Ghost
}
