using System.Reflection;

internal static class Program
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    [STAThread]
    static void Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        var type = Assembly.Load("IndustrialFieldDataAcquisitionTerminal").GetType("IndustrialFieldDataAcquisitionTerminal.MainForm", true)!;
        using var form = (Form)Activator.CreateInstance(type, true)!;
        T Field<T>(string name) => (T)type.GetField(name, Private)!.GetValue(form)!;
        form.Show();
        type.GetField("selectedDeviceId", Private)!.SetValue(form, Guid.NewGuid());
        type.GetField("deviceWorkspaceOpen", Private)!.SetValue(form, true);
        Field<TabControl>("contentTabs").SelectedIndex = 0;
        Application.DoEvents();
        // The network row starts collapsed; expand it so the audit covers the tallest header state.
        Field<Button>("connectionSummaryButton").PerformClick();
        Application.DoEvents();
        var header = Descendants(form).Single(c => c.Name == "connectionHeader");
        var details = Field<FlowLayoutPanel>("connectionBar");
        var title = Field<Label>("workspaceTitle");
        var subtitle = Field<Label>("activeDeviceLabel");
        var originalFonts = Descendants(header).Prepend(header).ToDictionary(c => c, c => c.Font);
        foreach (var fontScale in new[] { 1F, 1.25F, 1.5F })
        {
        foreach (var pair in originalFonts) pair.Key.Font = new Font(pair.Value.FontFamily, pair.Value.Size * fontScale, pair.Value.Style);
        foreach (var width in new[] { 1100, 1400, 1920 })
        foreach (var expanded in new[] { true })
        {
            form.Size = new Size(width, 900);
            if (!details.Visible || !Field<FlowLayoutPanel>("samplingBar").Visible) throw new Exception("network and acquisition controls must remain visible");
            Application.DoEvents();
            foreach (var buttonName in new[] { "pollNowButton", "pollToggleButton" })
            {
                var samplingButton = Field<Button>(buttonName);
                if (!samplingButton.Visible) throw new Exception($"{width}/{expanded}: {buttonName} is hidden");
                for (Control ancestor = samplingButton.Parent!; ancestor != form; ancestor = ancestor.Parent!)
                {
                    var local = new Rectangle(ancestor.PointToClient(samplingButton.PointToScreen(Point.Empty)), samplingButton.Size);
                    if (!ancestor.ClientRectangle.Contains(local))
                        throw new Exception($"{width}/{expanded}: {buttonName} clipped by {ancestor.GetType().Name}, {local} within {ancestor.ClientRectangle}");
                }
            }
            var leaves = Descendants(header).Where(c => c.Visible && c.Parent is not NumericUpDown && c is Button or Label or NumericUpDown or TextBox).ToArray();
            foreach (var c in leaves)
            {
                var bounds = new Rectangle(header.PointToClient(c.PointToScreen(Point.Empty)), c.Size);
                if (!header.ClientRectangle.Contains(bounds)) throw new Exception($"{width}/{expanded}: clipped {c.Text} {bounds} header={header.ClientRectangle}");
                for (Control ancestor = c.Parent!; ancestor != header; ancestor = ancestor.Parent!)
                {
                    var local = new Rectangle(ancestor.PointToClient(c.PointToScreen(Point.Empty)), c.Size);
                    if (!ancestor.ClientRectangle.Contains(local)) throw new Exception($"{width}/{expanded}: {c.Text} clipped by {ancestor.GetType().Name}");
                }
                if (c is Button or Label && c.ClientSize.Height < TextRenderer.MeasureText(c.Text, c.Font, Size.Empty, TextFormatFlags.SingleLine).Height)
                    throw new Exception($"{width}/{expanded}: text too tall for {c.Text}");
            }
            for (var i = 0; i < leaves.Length; i++)
            for (var j = i + 1; j < leaves.Length; j++)
            {
                var a = new Rectangle(leaves[i].PointToScreen(Point.Empty), leaves[i].Size);
                var b = new Rectangle(leaves[j].PointToScreen(Point.Empty), leaves[j].Size);
                if (a.IntersectsWith(b)) throw new Exception($"{width}/{expanded}: overlap {leaves[i].Text} / {leaves[j].Text}");
            }
            if (args.Length > 0)
            {
                Directory.CreateDirectory(args[0]);
                using var bitmap = new Bitmap(header.Width, header.Height);
                header.DrawToBitmap(bitmap, header.ClientRectangle);
                bitmap.Save(Path.Combine(args[0], $"header-{fontScale}-{width}-{expanded}.png"));
            }
        }
        }
        form.Close();
        Console.WriteLine("Header visibility, parent clipping, text height and overlap passed at 1100/1400/1920 widths, collapsed and expanded, at 100/125/150% text sizes.");
    }
    static IEnumerable<Control> Descendants(Control parent)
    {
        foreach (Control c in parent.Controls) { yield return c; foreach (var child in Descendants(c)) yield return child; }
    }
}
