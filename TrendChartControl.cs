using System.Drawing.Drawing2D;
using System.Globalization;

namespace IndustrialFieldDataAcquisitionTerminal;

internal sealed record TrendSeries(TrendChannel Channel, IReadOnlyList<TrendPoint> Points, Color Color);

internal sealed class TrendChartControl : Control
{
    private TrendChannel? channel;
    private IReadOnlyList<TrendSeries> series = Array.Empty<TrendSeries>();
    private TimeSpan window = TimeSpan.FromMinutes(2);
    private DateTime? sharedEnd;
    private DateTime? sharedLatest;
    private TimeSpan? sharedPlotWindow;
    private string? displayTitle;
    private readonly ToolTip pointTip = new();
    private string? lastTip;
    private int hoverX = -1;
    private bool panningValueAxis;
    private int panStartY;
    private (double Min, double Max) panStartRange;

    public IReadOnlyList<TrendPoint> Points { get; private set; } = Array.Empty<TrendPoint>();
    public (double Min, double Max)? ManualValueRange { get; private set; }
    public event Action<double>? TimeZoomRequested;
    public event EventHandler? ViewResetRequested;

    public TrendChartControl()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
        BackColor = Color.White;
        ForeColor = Color.FromArgb(43, 60, 75);
        Cursor = Cursors.Cross;
        TabStop = true;
        pointTip.SetToolTip(this, "图中滚轮缩放时间；右侧Y轴滚轮缩放数值、拖动平移；双击复位");
    }

    public void SetData(TrendChannel? channel, IReadOnlyList<TrendPoint> points, TimeSpan window)
    {
        SetSeries(channel is null ? Array.Empty<TrendSeries>() :
            new[] { new TrendSeries(channel, points, SeriesColor(channel)) }, window);
    }

    public void SetSeries(IReadOnlyList<TrendSeries> series, TimeSpan window)
        => SetSeries(series, window, null, null, null);

    public void SetSeries(IReadOnlyList<TrendSeries> series, TimeSpan window,
        DateTime? axisEnd, TimeSpan? axisWindow, string? title, DateTime? latestTimestamp = null)
    {
        var channelsChanged = this.series.Count != series.Count ||
            this.series.Where((entry, index) => entry.Channel.Key != series[index].Channel.Key).Any();
        this.series = series;
        channel = series.Count > 0 ? series[0].Channel : null;
        Points = series.Count > 0 ? series[0].Points : Array.Empty<TrendPoint>();
        this.window = window;
        sharedEnd = axisEnd == default ? null : axisEnd;
        sharedLatest = latestTimestamp == default ? null : latestTimestamp;
        sharedPlotWindow = axisWindow;
        displayTitle = title;
        if (channelsChanged)
        {
            lastTip = null;
            pointTip.Hide(this);
        }
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Color.White);
        var paneChart = displayTitle is not null;
        var compact = Height < 220;
        var plot = PlotBounds();
        if (plot.Width < 80 || plot.Height < 55) return;

        using var titleFont = new Font(Font.FontFamily, 9, FontStyle.Bold);
        if (!paneChart)
            TextRenderer.DrawText(g, channel is null ? "选择通道" :
                channel.Category == "电流回环" ? "电流回环  ·  mA" : $"{channel.Name}  ·  {channel.Unit}",
                titleFont, new Point(18, 8), Color.FromArgb(43, 58, 76));
        var end = sharedEnd ?? series.Where(entry => entry.Points.Count > 0)
            .Select(entry => entry.Points[^1].Timestamp).DefaultIfEmpty().Max();
        if (end == default)
        {
            TextRenderer.DrawText(g, "暂无采样数据", Font, plot, Color.FromArgb(132, 147, 159),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            return;
        }

        var plotWindow = PlotWindow();
        var start = end - plotWindow;
        var visible = VisibleSeries(start, end);
        var numeric = visible.SelectMany(entry => entry.points).Where(point => !double.IsNaN(point.Value)).ToArray();
        var digital = channel?.Unit == "0/1";
        var currentLoop = channel?.Unit == "mA";
        var (min, max) = ManualValueRange ?? AutoValueRange(numeric, digital, currentLoop);

        using var gridPen = new Pen(Color.FromArgb(210, 213, 220), 1) { DashStyle = DashStyle.Dash };
        using var axisPen = new Pen(Color.FromArgb(94, 104, 119), 1);
        using var axisFont = new Font(Font.FontFamily, 8);
        var verticalTicks = compact ? 4 : 6;
        var horizontalTicks = Width < 600 ? 6 : 8;
        var valueTick = (max - min) / verticalTicks;
        var valueFormat = valueTick < 0.001 ? "0.######" : valueTick < 0.1 ? "0.####" :
            valueTick < 1 ? "0.###" : currentLoop ? "0.0" : "0.###";
        var timeFormat = plotWindow.TotalSeconds < 1 ? "0.###" :
            plotWindow.TotalSeconds < 10 ? "0.##" : "0.#";
        var latest = sharedLatest ?? series.Where(entry => entry.Points.Count > 0)
            .Select(entry => entry.Points[^1].Timestamp).DefaultIfEmpty(end).Max();
        for (var tick = 0; tick <= verticalTicks; tick++)
        {
            var y = plot.Bottom - tick * plot.Height / verticalTicks;
            g.DrawLine(gridPen, plot.Left, y, plot.Right, y);
            var value = min + tick * (max - min) / verticalTicks;
            TextRenderer.DrawText(g, digital && ManualValueRange is null ? (tick == 0 ? "0" : tick == verticalTicks ? "1" : "") :
                value.ToString(valueFormat, CultureInfo.InvariantCulture), axisFont,
                new Rectangle(plot.Right + 8, (int)y - 10, 55, 20), Color.FromArgb(39, 53, 69),
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        }
        for (var tick = 0; tick <= horizontalTicks; tick++)
        {
            var x = plot.Left + tick * plot.Width / horizontalTicks;
            g.DrawLine(gridPen, x, plot.Top, x, plot.Bottom);
            var seconds = (start - latest).TotalSeconds + plotWindow.TotalSeconds * tick / horizontalTicks;
            TextRenderer.DrawText(g, seconds.ToString(timeFormat, CultureInfo.InvariantCulture) + " s", axisFont,
                new Rectangle((int)x - 32, plot.Bottom + 7, 66, 22), Color.FromArgb(39, 53, 69),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
        g.DrawLine(axisPen, plot.Left, plot.Bottom, plot.Right, plot.Bottom);
        if (visible.Any(entry => entry.points.Any(point => !point.IsValid)))
        {
            TextRenderer.DrawText(g, "读取未确认：虚线为原始读数", axisFont,
                new Rectangle(Math.Max(250, Width - 340), 13, 315, 23), Color.FromArgb(157, 96, 31),
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
        }
        if (numeric.Length == 0)
        {
            TextRenderer.DrawText(g, "暂无可绘制读数", Font, plot, Color.FromArgb(132, 147, 159),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            return;
        }

        g.SetClip(plot);
        foreach (var (entry, points) in visible)
        {
            using var linePen = new Pen(entry.Color, 1.8f) { LineJoin = LineJoin.Round };
            using var warningPen = new Pen(Color.FromArgb(185, 111, 39), 1.8f) { DashStyle = DashStyle.Dash };
            var previous = PointF.Empty;
            var hasPrevious = false;
            var previousValid = false;
            long previousSegment = 0;
            foreach (var point in points)
            {
                if (double.IsNaN(point.Value))
                {
                    hasPrevious = false;
                    continue;
                }
                var x = plot.Left + (float)Math.Clamp((point.Timestamp - start).TotalMilliseconds /
                    plotWindow.TotalMilliseconds, 0, 1) * plot.Width;
                var y = plot.Bottom - (float)((point.Value - min) / (max - min)) * plot.Height;
                var current = new PointF(x, y);
                if (hasPrevious && previousSegment == point.SegmentId)
                {
                    var segmentPen = previousValid && point.IsValid ? linePen : warningPen;
                    if (digital)
                    {
                        g.DrawLine(segmentPen, previous.X, previous.Y, current.X, previous.Y);
                        g.DrawLine(segmentPen, current.X, previous.Y, current.X, current.Y);
                    }
                    else g.DrawLine(segmentPen, previous, current);
                }
                previous = current;
                previousValid = point.IsValid;
                previousSegment = point.SegmentId;
                hasPrevious = true;
            }
            if (hasPrevious)
            {
                using var dot = new SolidBrush(previousValid ? entry.Color : Color.FromArgb(185, 111, 39));
                g.FillEllipse(dot, previous.X - 2.5f, previous.Y - 2.5f, 5, 5);
            }
        }
        if (hoverX >= plot.Left && hoverX <= plot.Right)
        {
            using var cursorPen = new Pen(Color.FromArgb(214, 0, 176), 1) { DashStyle = DashStyle.Dash };
            g.DrawLine(cursorPen, hoverX, plot.Top, hoverX, plot.Bottom);
        }
        g.ResetClip();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (panningValueAxis)
        {
            var offset = (e.Y - panStartY) * (panStartRange.Max - panStartRange.Min) /
                Math.Max(1, PlotBounds().Height);
            ManualValueRange = (panStartRange.Min + offset, panStartRange.Max + offset);
            Invalidate();
            return;
        }
        var plot = PlotBounds();
        Cursor = e.X > plot.Right && e.X < Width && e.Y >= plot.Top && e.Y <= plot.Bottom
            ? Cursors.SizeNS : Cursors.Cross;
        if (Points.Count == 0 || channel is null) return;
        var compact = Height < 220;
        var plotLeft = compact ? 62 : 72;
        var plotWidth = Math.Max(1, Width - (compact ? 130 : 150));
        if (e.X < plotLeft || e.X > plotLeft + plotWidth) return;
        if (Math.Abs(e.X - hoverX) > 2) { hoverX = e.X; Invalidate(); }
        var end = sharedEnd ?? series.Where(entry => entry.Points.Count > 0)
            .Select(entry => entry.Points[^1].Timestamp).DefaultIfEmpty().Max();
        var plotWindow = PlotWindow();
        var target = end - plotWindow + TimeSpan.FromTicks((long)(plotWindow.Ticks *
            (e.X - plotLeft) / (double)plotWidth));
        var lines = new List<string> { target.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture) };
        foreach (var entry in series)
        {
            var drawable = entry.Points.Where(point => !double.IsNaN(point.Value)).ToArray();
            if (drawable.Length == 0) continue;
            var before = drawable.LastOrDefault(point => point.Timestamp <= target);
            var after = drawable.FirstOrDefault(point => point.Timestamp >= target);
            if (before != default && after != default && before.SegmentId != after.SegmentId)
            {
                lines.Add($"{entry.Channel.Name}  采集间断");
                continue;
            }
            var nearest = drawable.MinBy(point => Math.Abs((point.Timestamp - target).Ticks));
            lines.Add($"{entry.Channel.Name}  {nearest.Value:0.###} {entry.Channel.Unit}" +
                (nearest.IsValid ? "" : "  (读取未确认)"));
        }
        if (lines.Count == 1) return;
        var message = string.Join(Environment.NewLine, lines);
        if (message == lastTip) return;
        lastTip = message;
        pointTip.SetToolTip(this, message);
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        Focus();
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        var plot = PlotBounds();
        if (e.Delta == 0 || plot.Width < 80 || plot.Height < 55 || series.All(entry => entry.Points.Count == 0)) return;
        var insideX = e.X >= plot.Left && e.X <= plot.Right;
        var insideY = e.Y >= plot.Top && e.Y <= plot.Bottom;
        var zoomTime = insideX && e.Y >= plot.Top && e.Y < Height;
        var zoomValue = insideY && e.X > plot.Right && e.X < Width;
        if (!zoomTime && !zoomValue) return;

        var timeFactor = Math.Pow(0.8, e.Delta / 120.0);
        if (zoomValue)
        {
            var end = sharedEnd ?? series.Where(entry => entry.Points.Count > 0)
                .Select(entry => entry.Points[^1].Timestamp).Max();
            var visible = VisibleSeries(end - PlotWindow(), end);
            var numeric = visible.SelectMany(entry => entry.points)
                .Where(point => !double.IsNaN(point.Value)).ToArray();
            var (min, max) = ManualValueRange ?? AutoValueRange(numeric, channel?.Unit == "0/1", channel?.Unit == "mA");
            var span = Math.Clamp((max - min) * Math.Pow(0.6, e.Delta / 120.0),
                0.000001, 1_000_000_000);
            var fraction = Math.Clamp((e.Y - plot.Top) / (double)plot.Height, 0, 1);
            var anchor = max - fraction * (max - min);
            if (numeric.Length > 0)
            {
                var nearest = numeric.MinBy(point => Math.Abs(point.Value - anchor));
                if (Math.Abs(nearest.Value - anchor) * plot.Height / (max - min) <= 12)
                    anchor = nearest.Value;
            }
            ManualValueRange = (anchor - (1 - fraction) * span, anchor + fraction * span);
        }
        if (zoomTime)
            TimeZoomRequested?.Invoke(timeFactor);
        pointTip.Hide(this);
        Invalidate();
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        if (!PlotBounds().Contains(e.Location)) return;
        ResetValueZoom();
        ViewResetRequested?.Invoke(this, EventArgs.Empty);
    }

    public void ResetValueZoom()
    {
        panningValueAxis = false;
        Capture = false;
        Cursor = Cursors.Cross;
        ManualValueRange = null;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        var plot = PlotBounds();
        if (e.Button != MouseButtons.Left || e.X <= plot.Right || e.X >= Width ||
            e.Y < plot.Top || e.Y > plot.Bottom || series.All(entry => entry.Points.Count == 0)) return;
        Focus();
        var end = sharedEnd ?? series.Where(entry => entry.Points.Count > 0)
            .Select(entry => entry.Points[^1].Timestamp).Max();
        var numeric = VisibleSeries(end - PlotWindow(), end).SelectMany(entry => entry.points)
            .Where(point => !double.IsNaN(point.Value)).ToArray();
        panStartRange = ManualValueRange ?? AutoValueRange(numeric, channel?.Unit == "0/1", channel?.Unit == "mA");
        panStartY = e.Y;
        panningValueAxis = true;
        Capture = true;
        Cursor = Cursors.SizeNS;
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (!panningValueAxis || e.Button != MouseButtons.Left) return;
        panningValueAxis = false;
        Capture = false;
        Cursor = Cursors.Cross;
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        hoverX = -1;
        pointTip.Hide(this);
        Invalidate();
    }

    private TimeSpan PlotWindow()
    {
        if (sharedPlotWindow.HasValue) return sharedPlotWindow.Value;
        if (Points.Count < 2) return TimeSpan.FromSeconds(Math.Min(10, window.TotalSeconds));
        var elapsed = Math.Max(0, (Points[^1].Timestamp - Points[0].Timestamp).TotalSeconds);
        return TimeSpan.FromSeconds(Math.Clamp(elapsed * 1.15, 10, window.TotalSeconds));
    }

    private Rectangle PlotBounds()
    {
        var compact = Height < 220;
        var paneChart = displayTitle is not null;
        return compact
            ? new Rectangle(62, paneChart ? 12 : 24, Math.Max(1, Width - 130),
                Math.Max(1, Height - (paneChart ? 50 : 62)))
            : new Rectangle(72, 34, Math.Max(1, Width - 150), Math.Max(1, Height - 90));
    }

    private (TrendSeries entry, TrendPoint[] points)[] VisibleSeries(DateTime start, DateTime end) =>
        series.Select(entry => (entry, entry.Points
            .Where(point => point.Timestamp >= start && point.Timestamp <= end).ToArray())).ToArray();

    private static (double Min, double Max) AutoValueRange(TrendPoint[] numeric, bool digital, bool currentLoop)
    {
        if (digital) return (-0.12, 1.12);
        if (currentLoop) return (0, 22);
        var min = numeric.Length == 0 ? 0 : numeric.Min(point => point.Value);
        var max = numeric.Length == 0 ? 1 : numeric.Max(point => point.Value);
        var margin = Math.Max((max - min) * 0.12, Math.Max(Math.Abs(max), 1) * 0.02);
        return (min - margin, max + margin);
    }

    private static readonly Color[] Palette =
    {
        Color.FromArgb(27, 73, 255), Color.FromArgb(0, 139, 76),
        Color.FromArgb(231, 91, 23), Color.FromArgb(126, 67, 180),
        Color.FromArgb(0, 137, 161), Color.FromArgb(176, 39, 99),
        Color.FromArgb(115, 111, 0), Color.FromArgb(167, 49, 49)
    };

    public static Color SeriesColor(TrendChannel channel)
    {
        if (channel.Key == "LOOP:SET") return Color.FromArgb(255, 0, 139);
        if (channel.Key == "LOOP:IN") return Color.FromArgb(27, 73, 255);
        uint hash = 2166136261;
        foreach (var character in channel.Key)
            hash = (hash ^ character) * 16777619;
        return Palette[(int)(hash % (uint)Palette.Length)];
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) pointTip.Dispose();
        base.Dispose(disposing);
    }
}
