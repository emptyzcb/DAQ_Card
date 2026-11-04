using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using IndustrialFieldDataAcquisitionTerminal;

namespace DeviceWorkbench.Wpf;

public partial class MainWindow : Window
{
    private readonly WpfWorkspaceBridge bridge;

    public MainWindow()
    {
        InitializeComponent();
        bridge = new WpfWorkspaceBridge();
        bridge.StateChanged += (_, _) => RefreshNavigation();
        WorkspaceHost.Child = bridge.View;
        bridge.View.Show();
        Loaded += (_, _) => RefreshNavigation();
        RefreshNavigation();
    }

    private void NavigationClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { CommandParameter: string value }) return;
        if (int.TryParse(value, out var page)) bridge.Navigate(page);
    }

    private void RefreshNavigation()
    {
        var visibility = bridge.HasDeviceWorkspace ? Visibility.Visible : Visibility.Collapsed;
        if (DeviceSection.Visibility != visibility) DeviceSection.Visibility = visibility;
        if (DeviceNameLabel.Text != bridge.DeviceName) DeviceNameLabel.Text = bridge.DeviceName;
        foreach (var button in Descendants<Button>(this))
            if (button.CommandParameter is string value)
            {
                var selected = int.TryParse(value, out var page) && page == bridge.SelectedPage;
                var tag = selected ? "Selected" : null;
                if (!Equals(button.Tag, tag)) button.Tag = tag;
            }
    }

    private static System.Collections.Generic.IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T item) yield return item;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!bridge.TryClose()) e.Cancel = true;
        base.OnClosing(e);
    }
}
