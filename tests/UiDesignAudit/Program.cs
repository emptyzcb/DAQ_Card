using System.Reflection;
using System.Text;
using System.Windows.Forms;

// Design audit probe: renders the shell, then reports geometry, typography and colour
// contrast so a redesign can be checked quantitatively without eyeballing screenshots.
internal static class Program
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Length < 1)
        {
            Console.Error.WriteLine("usage: UiDesignAudit <app.dll> [output.txt] [width] [height]");
            return;
        }
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        var assembly = Assembly.LoadFrom(Path.GetFullPath(args[0]));
        var type = assembly.GetType("IndustrialFieldDataAcquisitionTerminal.MainForm", true)!;
        using var form = (Form)Activator.CreateInstance(type, nonPublic: true)!;
        form.Size = new Size(args.Length > 2 ? int.Parse(args[2]) : 1480, args.Length > 3 ? int.Parse(args[3]) : 940);
        form.Show();
        Application.DoEvents();
        type.GetField("selectedDeviceId", Private)!.SetValue(form, Guid.NewGuid());
        type.GetField("deviceWorkspaceOpen", Private)!.SetValue(form, true);
        var tabs = (TabControl)type.GetField("contentTabs", Private)!.GetValue(form)!;
        tabs.SelectedIndex = 0;
        Application.DoEvents();
        form.PerformLayout();
        Application.DoEvents();

        var report = new StringBuilder();
        var problems = new List<string>();
        report.AppendLine($"form size={form.Size} client={form.ClientSize} font={Describe(form.Font)}");
        report.AppendLine();
        report.AppendLine("=== SHELL TREE ===");
        Walk(form, report, problems, 0, form);

        report.AppendLine();
        report.AppendLine("=== METRICS ===");
        Section(report, "sidebar", "moduleNavigation");
        Section(report, "header", "connectionHeader");
        report.AppendLine($"grid rowTemplate={((DataGridView)type.GetField("dataGrid", Private)!.GetValue(form)!).RowTemplate.Height} " +
                          $"headerHeight={((DataGridView)type.GetField("dataGrid", Private)!.GetValue(form)!).ColumnHeadersHeight} " +
                          $"headerFont={Describe(((DataGridView)type.GetField("dataGrid", Private)!.GetValue(form)!).ColumnHeadersDefaultCellStyle.Font)}");
        foreach (var tileName in new[] { "kpiLink", "kpiInterval", "kpiAd", "kpiIo" })
        {
            var tile = (Control)type.GetField(tileName, Private)!.GetValue(form)!;
            report.AppendLine($"tile {tileName,-12} bounds={tile.Bounds} visible={tile.Visible}");
        }
        var buttons = ((Button[])type.GetField("navigationButtons", Private)!.GetValue(form)!)
            .Where(button => button is not null).ToArray();
        foreach (var button in buttons)
            report.AppendLine($"navitem '{button.Text}' bounds={button.Bounds} visible={button.Visible} font={Describe(button.Font)}");

        report.AppendLine();
        report.AppendLine("=== PAGE PROBLEMS ===");
        var pageIssues = new List<string>();
        for (var pageIndex = 0; pageIndex < tabs.TabCount; pageIndex++)
        {
            if (pageIndex < 4) type.GetField("deviceWorkspaceOpen", Private)!.SetValue(form, true);
            tabs.SelectedIndex = pageIndex;
            Application.DoEvents();
            form.PerformLayout();
            Application.DoEvents();
            var before = problems.Count;
            var scratch = new StringBuilder();
            Walk(tabs.TabPages[pageIndex], scratch, problems, 0, form);
            report.AppendLine($"page {pageIndex} '{tabs.TabPages[pageIndex].Text}': {problems.Count - before} problem(s)");
        }
        tabs.SelectedIndex = 0;
        Application.DoEvents();

        report.AppendLine();
        report.AppendLine("=== PROBLEMS ===");
        if (problems.Count == 0) report.AppendLine("none");
        foreach (var problem in problems) report.AppendLine(problem);

        var output = args.Length > 1 ? args[1] : Path.Combine(Environment.CurrentDirectory, "design-audit.txt");
        File.WriteAllText(output, report.ToString(), new UTF8Encoding(false));
        Console.WriteLine(report.ToString());
        Console.WriteLine($"written: {output}");
        form.Close();
    }

    private static void Section(StringBuilder report, string label, string name)
    {
        var field = FindByName(Application.OpenForms.Count > 0 ? Application.OpenForms[0] : null, name);
        if (field is null)
        {
            report.AppendLine($"{label}: not found");
            return;
        }
        report.AppendLine($"{label} '{name}' bounds={field.Bounds} back={Hex(field.BackColor)} font={Describe(field.Font)}");
    }

    private static Control? FindByName(Control? root, string name)
    {
        if (root is null) return null;
        if (root.Name == name) return root;
        foreach (Control child in root.Controls)
        {
            var found = FindByName(child, name);
            if (found is not null) return found;
        }
        return null;
    }

    private static void Walk(Control parent, StringBuilder report, List<string> problems, int depth, Form form)
    {
        foreach (Control control in parent.Controls)
        {
            if (!control.Visible && depth > 0) continue;
            var screen = control.PointToScreen(Point.Empty);
            var origin = form.PointToClient(screen);
            var bounds = new Rectangle(origin, control.Size);
            var text = control.Text.Replace('\r', ' ').Replace('\n', ' ');
            if (text.Length > 42) text = text[..42] + "…";
            report.AppendLine($"{new string(' ', depth * 2)}{control.GetType().Name,-22} {bounds.X,5},{bounds.Y,5} {bounds.Width,5}x{bounds.Height,-4} " +
                              $"'{text}' font={Describe(control.Font)} back={Hex(control.BackColor)} fore={Hex(control.ForeColor)}");
            Check(control, bounds, text, problems);
            if (depth < 9) Walk(control, report, problems, depth + 1, form);
        }
    }

    private static void Check(Control control, Rectangle bounds, string text, List<string> problems)
    {
        var isLabelOrButton = control is Label or Button or NumericUpDown or TextBox or ComboBox or DataGridView;
        if (!isLabelOrButton) return;
        if (text.Length == 0) return;
        if (bounds.Height < 24)
            problems.Append($"{control.GetType().Name} '{text}' is only {bounds.Height}px tall; " +
                            $"font {control.Font.Size}pt needs about {TextRenderer.MeasureText(text, control.Font).Height + 8}px.");
        if (control.Font.Size < 8.5f)
            problems.Add($"{control.GetType().Name} '{text}' uses {control.Font.Size}pt text, smaller than the 9pt floor.");
        var contrast = Contrast(control.ForeColor, EffectiveBack(control));
        if (contrast < 4.0 && control.Enabled)
            problems.Add($"{control.GetType().Name} '{text}' contrast {contrast:0.00}:1 below 4.0 ({Hex(control.ForeColor)} on {Hex(EffectiveBack(control))}).");
    }

    private static Color EffectiveBack(Control control)
    {
        for (Control? current = control; current is not null; current = current.Parent)
            if (current.BackColor.A == 255 && current.BackColor != Color.Transparent) return current.BackColor;
        return Color.White;
    }

    private static double Contrast(Color foreground, Color background)
    {
        double Lum(Color color)
        {
            static double Channel(int value)
            {
                var scaled = value / 255.0;
                return scaled <= 0.03928 ? scaled / 12.92 : Math.Pow((scaled + 0.055) / 1.055, 2.4);
            }
            return 0.2126 * Channel(color.R) + 0.7152 * Channel(color.G) + 0.0722 * Channel(color.B);
        }
        var a = Lum(foreground) + 0.05;
        var b = Lum(background) + 0.05;
        return a > b ? a / b : b / a;
    }

    private static string Describe(Font font) => $"{font.FontFamily.Name} {font.Size:0.#}pt{(font.Bold ? " bold" : string.Empty)}";

    private static string Hex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";
}
