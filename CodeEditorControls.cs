using System.Diagnostics;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace IndustrialFieldDataAcquisitionTerminal;

internal sealed class CodeLineNumberGutter : Control
{
    private RichTextBox? editor;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public RichTextBox? Editor
    {
        get => editor;
        set
        {
            editor = value;
            Invalidate();
        }
    }

    public CodeLineNumberGutter()
    {
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw,
            true);
        BackColor = UiPalette.CodeChrome;
        ForeColor = UiPalette.CodeMuted;
        Font = new Font("Cascadia Mono", 10F, FontStyle.Regular);
        TabStop = false;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor);
        if (editor is null || editor.IsDisposed || !editor.IsHandleCreated)
        {
            return;
        }

        var firstLine = SendMessage(editor.Handle, 0x00CE, IntPtr.Zero, IntPtr.Zero).ToInt32();
        var lineHeight = Math.Max(1, TextRenderer.MeasureText(e.Graphics, "Ag", Font).Height);
        using var brush = new SolidBrush(ForeColor);
        using var format = new StringFormat
        {
            Alignment = StringAlignment.Far,
            LineAlignment = StringAlignment.Near
        };

        var lineCount = SendMessage(editor.Handle, 0x00BA, IntPtr.Zero, IntPtr.Zero).ToInt32();
        for (var line = firstLine; line < lineCount; line++)
        {
            var charIndex = editor.GetFirstCharIndexFromLine(line);
            if (charIndex < 0)
            {
                break;
            }

            var position = editor.GetPositionFromCharIndex(charIndex);
            var y = position.Y;
            if (y > ClientSize.Height)
            {
                break;
            }

            if (y + lineHeight >= 0)
            {
                e.Graphics.DrawString(
                    (line + 1).ToString(),
                    Font,
                    brush,
                    new RectangleF(0, y, ClientSize.Width - 8, lineHeight),
                    format);
            }
        }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int message, IntPtr wParam, IntPtr lParam);
}

internal sealed class CodeMinimap : Control
{
    private const int EmGetFirstVisibleLine = 0x00CE;
    private const int EmLineScroll = 0x00B6;
    private RichTextBox? editor;
    private Bitmap? contentBitmap;
    private bool contentDirty = true;
    private float lineStep = 1.5F;
    private int contentLineCount = 1;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public RichTextBox? Editor
    {
        get => editor;
        set
        {
            editor = value;
            InvalidateContent();
        }
    }

    public void MarkContentDirty() => contentDirty = true;

    public void InvalidateContent()
    {
        MarkContentDirty();
        Invalidate();
    }

    public CodeMinimap()
    {
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw,
            true);
        BackColor = UiPalette.CodeChrome;
        Cursor = Cursors.Hand;
        TabStop = false;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor);
        if (editor is null || editor.IsDisposed || !editor.IsHandleCreated)
        {
            return;
        }

        if (contentDirty || contentBitmap is null || contentBitmap.Size != ClientSize)
        {
            RenderContent();
        }
        if (contentBitmap is not null) e.Graphics.DrawImageUnscaled(contentBitmap, Point.Empty);

        var firstVisibleLine = SendMessage(editor.Handle, EmGetFirstVisibleLine, IntPtr.Zero, IntPtr.Zero).ToInt32();
        var editorLineHeight = Math.Max(1, TextRenderer.MeasureText("Ag", editor.Font).Height);
        var visibleLines = Math.Max(1, editor.ClientSize.Height / editorLineHeight);
        var viewportTop = Math.Clamp((int)(firstVisibleLine * lineStep), 0, Math.Max(0, ClientSize.Height - 1));
        var viewportHeight = Math.Clamp((int)Math.Ceiling(visibleLines * lineStep), 8, Math.Max(8, ClientSize.Height - viewportTop));
        using var viewportBrush = new SolidBrush(Color.FromArgb(65, 56, 142, 213));
        using var viewportPen = new Pen(Color.FromArgb(87, 144, 203), 1F);
        var viewport = new Rectangle(1, viewportTop, Math.Max(1, ClientSize.Width - 3), viewportHeight);
        e.Graphics.FillRectangle(viewportBrush, viewport);
        e.Graphics.DrawRectangle(viewportPen, viewport);
    }

    private void RenderContent()
    {
        contentBitmap?.Dispose();
        contentBitmap = null;
        contentDirty = false;
        if (editor is null || ClientSize.Width <= 0 || ClientSize.Height <= 0) return;

        contentBitmap = new Bitmap(ClientSize.Width, ClientSize.Height);
        using var graphics = Graphics.FromImage(contentBitmap);
        graphics.Clear(BackColor);

        var lines = editor.Lines;
        contentLineCount = Math.Max(1, lines.Length);
        lineStep = Math.Clamp(ClientSize.Height / (float)contentLineCount, 1.5F, 5F);
        var fontSize = Math.Clamp(lineStep * 0.84F, 1.5F, 4F);
        using var minimapFont = new Font("Cascadia Mono", fontSize, FontStyle.Regular, GraphicsUnit.Pixel);
        using var codeBrush = new SolidBrush(UiPalette.CodeMuted);
        using var keywordBrush = new SolidBrush(UiPalette.CodeKeyword);
        using var ioBrush = new SolidBrush(UiPalette.CodeIo);
        using var commentBrush = new SolidBrush(UiPalette.CodeComment);
        using var textFormat = new StringFormat(StringFormat.GenericTypographic)
        {
            FormatFlags = StringFormatFlags.NoWrap | StringFormatFlags.MeasureTrailingSpaces,
            Trimming = StringTrimming.None
        };
        var characterWidth = Math.Max(1F, graphics.MeasureString("M", minimapFont, PointF.Empty, textFormat).Width);

        for (var index = 0; index < lines.Length; index++)
        {
            var y = index * lineStep;
            if (y >= ClientSize.Height)
            {
                break;
            }

            var line = lines[index];
            if (line.Length == 0)
            {
                continue;
            }

            graphics.DrawString(line, minimapFont, codeBrush, new PointF(3F, y), textFormat);
            foreach (Match token in Regex.Matches(line, @"//.*|[A-Za-z_][A-Za-z0-9_]*"))
            {
                Brush? tokenBrush = token.Value.StartsWith("//", StringComparison.Ordinal)
                    ? commentBrush
                    : MainForm.IsMinimapKeyword(token.Value)
                        ? keywordBrush
                        : MainForm.IsMinimapIoSymbol(token.Value)
                            ? ioBrush
                            : null;
                if (tokenBrush is null)
                {
                    continue;
                }

                graphics.DrawString(
                    token.Value,
                    minimapFont,
                    tokenBrush,
                    new PointF(3F + (token.Index * characterWidth), y),
                    textFormat);
            }
        }
    }

    protected override void OnResize(EventArgs e)
    {
        MarkContentDirty();
        base.OnResize(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) contentBitmap?.Dispose();
        base.Dispose(disposing);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Left)
        {
            ScrollEditorTo(e.Y);
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if ((e.Button & MouseButtons.Left) != 0)
        {
            ScrollEditorTo(e.Y);
        }
    }

    private void ScrollEditorTo(int y)
    {
        if (editor is null || !editor.IsHandleCreated || contentLineCount == 0)
        {
            return;
        }

        var ratio = Math.Clamp(y / (double)Math.Max(1, ClientSize.Height), 0D, 1D);
        var editorLineHeight = Math.Max(1, TextRenderer.MeasureText("Ag", editor.Font).Height);
        var visibleLines = Math.Max(1, editor.ClientSize.Height / editorLineHeight);
        var targetFirstLine = (int)Math.Round(ratio * Math.Max(0, contentLineCount - visibleLines));
        var currentFirstLine = SendMessage(editor.Handle, EmGetFirstVisibleLine, IntPtr.Zero, IntPtr.Zero).ToInt32();
        SendMessage(editor.Handle, EmLineScroll, IntPtr.Zero, new IntPtr(targetFirstLine - currentFirstLine));
        editor.Focus();
        Invalidate();
    }

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int message, IntPtr wParam, IntPtr lParam);
}
