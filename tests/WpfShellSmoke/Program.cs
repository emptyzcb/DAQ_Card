using System;
using System.Drawing;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using DeviceWorkbench.Wpf;
using IndustrialFieldDataAcquisitionTerminal;

internal static class Program
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static void Check(bool value, string error) { if (!value) throw new Exception(error); }
    [STAThread]
    private static void Main(string[] args)
    {
        System.Windows.Forms.Application.EnableVisualStyles();
        System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);
        var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        application.Resources.MergedDictionaries.Add(new ResourceDictionary
            { Source = new Uri("/DeviceWorkbench.Wpf;component/Resources/Theme.xaml", UriKind.Relative) });
        var window = new MainWindow();
        application.MainWindow = window;
        window.Show();
        void Pump() => application.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        Pump();
        var bridge = (WpfWorkspaceBridge)typeof(MainWindow).GetField("bridge", Private)!.GetValue(window)!;
        Check(bridge.SelectedPage == 6 && !bridge.HasDeviceWorkspace, "Initial device page");
        var home = (Button)window.FindName("HomeButton");
        Check((string?)home.Tag == "Selected", "Initial WPF navigation selection");
        var serial = (Button)window.FindName("SerialButton");
        serial.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
        Check(bridge.SelectedPage == 4 && (string?)serial.Tag == "Selected", "WPF navigation click");
        home.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
        var errorButton = (Button)window.FindName("ErrorAnalysisButton");
        errorButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
        Check(bridge.SelectedPage == 9 && (string?)errorButton.Tag == "Selected", "independent error analysis navigation");
        var tabControl = (System.Windows.Forms.TabControl)bridge.View.GetType().GetField("toolboxTabs", Private)!.GetValue(bridge.View)!;
        Check(tabControl.TabCount == 4 && ((System.Windows.Forms.Label)bridge.View.GetType().GetField("workspaceTitle", Private)!.GetValue(bridge.View)!).Text == "数据误差分析",
            "independent page title and four communication tools");
        home.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
        bridge.Navigate(0);
        Check(bridge.SelectedPage == 6, "Device modules must require a device");
        foreach (var page in new[] { 4, 5, 7, 8, 6 })
        {
            bridge.Navigate(page); Pump();
            Check(bridge.SelectedPage == page, "Navigation page " + page);
        }
        var legacyType = bridge.View.GetType();
        errorButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
        var inputs = (System.Collections.IList)legacyType.GetField("errorInputs", Private)!.GetValue(bridge.View)!;
        var rowType = legacyType.GetNestedType("ErrorInput", BindingFlags.NonPublic)!;
        inputs[0] = Activator.CreateInstance(rowType, "10", "11")!;
        inputs[1] = Activator.CreateInstance(rowType, "20", "18")!;
        var calculation = (System.Threading.Tasks.Task)legacyType.GetMethod("CalculateErrorsAsync", Private)!.Invoke(bridge.View, null)!;
        var calculationTime = System.Diagnostics.Stopwatch.StartNew();
        while (!calculation.IsCompleted && calculationTime.ElapsedMilliseconds < 10000) { Pump(); System.Threading.Thread.Sleep(10); }
        Check(calculation.IsCompletedSuccessfully, "error analysis calculation completed");
        var metrics = (System.Windows.Forms.DataGridView)legacyType.GetField("errorMetricGrid", Private)!.GetValue(bridge.View)!;
        Check(metrics.Rows.Count >= 20 && Convert.ToString(metrics.Rows[0].Cells[1].Value) == "2", "error analysis results displayed");
        var errorGrid = (System.Windows.Forms.DataGridView)legacyType.GetField("errorInputGrid", Private)!.GetValue(bridge.View)!;
        errorGrid.Rows[0].Cells["measured"].Value = "12";
        Check(!(bool)((System.Windows.Forms.Button)legacyType.GetField("errorExport", Private)!.GetValue(bridge.View)!).Enabled,
            "editing must invalidate previous results");
        var mappingType = legacyType.Assembly.GetType("IndustrialFieldDataAcquisitionTerminal.ErrorCsvMappingDialog", true)!;
        using (var mapping = (System.Windows.Forms.Form)Activator.CreateInstance(mappingType,
            new object[] { new System.Collections.Generic.List<string[]> { new[] { "测量值" }, new[] { "1.2" } } })!)
        {
            Check((int)mappingType.GetProperty("ReferenceColumn")!.GetValue(mapping)! == -1 &&
                  (int)mappingType.GetProperty("MeasuredColumn")!.GetValue(mapping)! == 0,
                "one-column CSV defaults to a constant reference");
        }
        home.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
        errorButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
        Check(Convert.ToString(errorGrid.Rows[0].Cells["measured"].Value) == "12",
            "error inputs must survive navigation");
        home.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
        var productType = legacyType.Assembly.GetType("IndustrialFieldDataAcquisitionTerminal.DeviceProfile", true)!;
        var profile = Activator.CreateInstance(productType, Guid.NewGuid(), "WPF 测试设备", "DAQ-800",
            "192.168.1.10", 5000, (byte)1, 250, "测试")!;
        legacyType.GetMethod("OpenDeviceWorkspace", Private)!.Invoke(bridge.View, new[] { profile });
        Pump();
        Check(bridge.HasDeviceWorkspace && bridge.SelectedPage == 0, "Open device workspace");
        Check(((StackPanel)window.FindName("DeviceSection")).Visibility == Visibility.Visible, "WPF device navigation");
        foreach (var page in new[] { 1, 2, 3, 4, 5, 7, 8, 0 })
        {
            bridge.Navigate(page); Pump();
            Check(bridge.SelectedPage == page && bridge.HasDeviceWorkspace, "Device page " + page);
        }
        var dialogType = legacyType.Assembly.GetType("IndustrialFieldDataAcquisitionTerminal.DeviceModelPickerDialog", true)!;
        using (var dialog = (System.Windows.Forms.Form)Activator.CreateInstance(dialogType, nonPublic: true)!)
        {
            dialog.Shown += (_, _) => dialog.BeginInvoke(new Action(dialog.Close));
            dialog.ShowDialog(bridge.View);
        }
        // Cancelling an unsaved-work prompt must keep both windows and their resources alive.
        var sensorField = legacyType.GetField("workbenchSensors", Private)!;
        var originalSensors = sensorField.GetValue(bridge.View);
        sensorField.SetValue(bridge.View, Activator.CreateInstance(sensorField.FieldType));
        legacyType.GetField("workbenchDirty", Private)!.SetValue(bridge.View, true);
        legacyType.GetField("workbenchSavePrompt", Private)!.SetValue(bridge.View,
            (Func<System.Windows.Forms.DialogResult>)(() => System.Windows.Forms.DialogResult.Cancel));
        window.Close(); Pump();
        Check(!bridge.View.IsDisposed && window.IsVisible, "Cancel must keep the workspace open");
        sensorField.SetValue(bridge.View, originalSensors);
        if (args.Length > 0)
        {
            Pump();
            using var bitmap = new Bitmap((int)window.ActualWidth, (int)window.ActualHeight);
            using var graphics = Graphics.FromImage(bitmap);
            var point = window.PointToScreen(new System.Windows.Point(0, 0));
            graphics.CopyFromScreen((int)point.X, (int)point.Y, 0, 0, bitmap.Size);
            bitmap.Save(args[0]);
        }
        window.Close(); Pump();
        Check(bridge.View.IsDisposed && !window.IsVisible, "Closing must dispose the workspace");
        application.Shutdown();
        Console.WriteLine("WPF host navigation, device guard, tool pages and workspace lifecycle passed.");
    }
}
