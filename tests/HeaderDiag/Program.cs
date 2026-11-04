using System.Reflection;
using System.Windows.Forms;

// Reproduces the strict header overflow check and reports the exact clipping ancestor.
internal static class Program
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    [STAThread]
    private static void Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        var assembly = Assembly.LoadFrom(Path.GetFullPath(args[0]));
        var type = assembly.GetType("IndustrialFieldDataAcquisitionTerminal.MainForm", true)!;
        using var form = (Form)Activator.CreateInstance(type, nonPublic: true)!;
        T Field<T>(string name) => (T)type.GetField(name, Private)!.GetValue(form)!;
        form.Show();
        type.GetField("selectedDeviceId", Private)!.SetValue(form, Guid.NewGuid());
        type.GetField("deviceWorkspaceOpen", Private)!.SetValue(form, true);
        Field<TabControl>("contentTabs").SelectedIndex = 0;
        Application.DoEvents();
        Field<Button>("connectionSummaryButton").PerformClick();
        Application.DoEvents();

        var header = Descendants(form).Single(control => control.Name == "connectionHeader");
        var originalFonts = Descendants(header).Prepend(header).ToDictionary(control => control, control => control.Font);
        var scale = args.Length > 1 ? float.Parse(args[1]) : 1.5f;
        var width = args.Length > 2 ? int.Parse(args[2]) : 1100;
        foreach (var pair in originalFonts)
            pair.Key.Font = new Font(pair.Value.FontFamily, pair.Value.Size * scale, pair.Value.Style);

        form.Size = new Size(width, 900);
        Application.DoEvents();
        for (var pass = 0; pass < 3; pass++)
        {
            form.PerformLayout();
            Application.DoEvents();
        }

        Console.WriteLine($"header bounds={header.Bounds} client={header.ClientRectangle} scale={scale}");
        var stackPanel = Descendants(header).OfType<TableLayoutPanel>().First();
        var overview = (Control)type.GetField("samplingBar", Private)!.GetValue(form)!;
        foreach (var probe in new Control[] { stackPanel, overview })
        {
            var w = probe.Parent?.ClientSize.Width ?? 744;
            Console.WriteLine($"probe {probe.GetType().Name} actual={probe.Size} pref={probe.PreferredSize} " +
                              $"gps(0)={probe.GetPreferredSize(new Size(w, 0))} gps(big)={probe.GetPreferredSize(new Size(w, 1 << 20))} gps(empty)={probe.GetPreferredSize(Size.Empty)}");
        }
        var connection = (Control)type.GetField("connectionBar", Private)!.GetValue(form)!;
        Console.WriteLine($"connectionBar actual={connection.Size} pref={connection.PreferredSize} gps(big)={connection.GetPreferredSize(new Size(744, 1 << 20))} visible={connection.Visible}");
        Dump(header, 0);
        Console.WriteLine($"header type={header.GetType().Name} pref={header.PreferredSize} gps(empty)={header.GetPreferredSize(Size.Empty)} gps(744)={header.GetPreferredSize(new Size(744, 0))} dock={header.Dock}");
        var innerProp = header.GetType().GetProperty("Inner");
        var inner = innerProp?.GetValue(header) as Control;
        Console.WriteLine($"inner={(inner?.GetType().Name ?? "null")} innerPref={inner?.PreferredSize} innerGps={inner?.GetPreferredSize(new Size(744, 1 << 20))}");
        foreach (var tl in Descendants(header).OfType<TableLayoutPanel>())
        {
            var rows = string.Join(",", tl.RowStyles.Cast<RowStyle>().Select(style => $"{style.SizeType}:{style.Height:0}"));
            var cols = string.Join(",", tl.ColumnStyles.Cast<ColumnStyle>().Select(style => $"{style.SizeType}:{style.Width:0}"));
            Console.WriteLine($"TLP size={tl.Size} dock={tl.Dock} autosize={tl.AutoSize} rows=[{rows}] cols=[{cols}]");
            foreach (Control child in tl.Controls)
                Console.WriteLine($"    child {child.GetType().Name,-18} bounds={child.Bounds} dock={child.Dock} autosize={child.AutoSize} margin={child.Margin}");
        }
        Console.WriteLine("--- text height ---");
        foreach (var control in Descendants(header).Where(c => c.Visible && c is Button or Label).OfType<Control>())
        {
            var need = TextRenderer.MeasureText(control.Text, control.Font, Size.Empty, TextFormatFlags.SingleLine).Height;
            var state = control.ClientSize.Height < need ? "TOO TALL" : "ok";
            Console.WriteLine($"{state,-9} h={control.ClientSize.Height,3} need={need,3} font={control.Font.Size,5:0.#} '{control.Text}'");
        }
        Console.WriteLine("--- clipping ---");
        foreach (var control in Descendants(header).Where(c => c.Visible && c is Button or Label or NumericUpDown or TextBox))
        {
            var bounds = new Rectangle(header.PointToClient(control.PointToScreen(Point.Empty)), control.Size);
            if (!header.ClientRectangle.Contains(bounds))
                Console.WriteLine($"OUTSIDE header: '{control.Text}' {bounds}");
            for (var ancestor = control.Parent; ancestor is not null && ancestor != header; ancestor = ancestor.Parent)
            {
                var local = new Rectangle(ancestor.PointToClient(control.PointToScreen(Point.Empty)), control.Size);
                if (!ancestor.ClientRectangle.Contains(local))
                    Console.WriteLine($"'{control.Text}' {local} clipped by {ancestor.GetType().Name} {ancestor.ClientRectangle} (parent={ancestor.Parent?.GetType().Name})");
            }
        }
        form.Close();
    }

    private static void Dump(Control parent, int depth)
    {
        foreach (Control control in parent.Controls)
        {
            if (!control.Visible) continue;
            var text = control.Text.Length > 26 ? control.Text[..26] + "…" : control.Text;
            Console.WriteLine($"{new string(' ', depth * 2)}{control.GetType().Name,-18} {control.Bounds.X,5},{control.Bounds.Y,-5} {control.Bounds.Width,5}x{control.Bounds.Height,-4} " +
                              $"pref={control.PreferredSize.Width}x{control.PreferredSize.Height} max={control.MaximumSize.Width} '{text}'");
            if (depth < 6) Dump(control, depth + 1);
        }
    }

    private static IEnumerable<Control> Descendants(Control parent)
    {
        foreach (Control control in parent.Controls)
        {
            yield return control;
            foreach (var child in Descendants(control)) yield return child;
        }
    }
}
