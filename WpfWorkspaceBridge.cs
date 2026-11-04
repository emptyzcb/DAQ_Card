using System.Windows.Forms;

namespace IndustrialFieldDataAcquisitionTerminal;

// A narrow interop boundary keeps the existing workspace and its lifecycle intact.
public sealed class WpfWorkspaceBridge
{
    private readonly MainForm workspace;

    public WpfWorkspaceBridge()
    {
        workspace = new MainForm { TopLevel = false, FormBorderStyle = FormBorderStyle.None, Dock = DockStyle.Fill };
        workspace.PrepareForWpfHost();
        workspace.WorkspaceViewChanged += (_, _) => StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public Form View => workspace;
    public int SelectedPage => workspace.HostSelectedPage;
    public bool HasDeviceWorkspace => workspace.HostHasDeviceWorkspace;
    public string DeviceName => workspace.HostDeviceName;
    public event EventHandler? StateChanged;
    public void Navigate(int page) => workspace.HostNavigate(page);

    // Close() invokes the original save prompts and cleanup. A cancelled close leaves the form alive.
    public bool TryClose()
    {
        if (!workspace.IsDisposed) workspace.Close();
        return workspace.IsDisposed;
    }
}

internal sealed partial class MainForm
{
    internal event EventHandler? WorkspaceViewChanged;
    internal int HostSelectedPage => contentTabs.SelectedIndex;
    internal bool HostHasDeviceWorkspace => deviceWorkspaceOpen && selectedDeviceId is not null;
    internal string HostDeviceName => deviceNavigationTitle.Text;

    internal void PrepareForWpfHost()
    {
        var navigation = Controls.Find("moduleNavigation", true).FirstOrDefault();
        if (navigation?.Parent is TableLayoutPanel shell)
        {
            navigation.Visible = false;
            shell.ColumnStyles[0].Width = 0;
        }
        contentTabs.SelectedIndexChanged += (_, _) => WorkspaceViewChanged?.Invoke(this, EventArgs.Empty);
        TextChanged += (_, _) => WorkspaceViewChanged?.Invoke(this, EventArgs.Empty);
    }

    internal void HostNavigate(int page)
    {
        if (page < 0 || page >= contentTabs.TabPages.Count || contentTabs.SelectedIndex == page) return;
        var redraw = contentTabs.IsHandleCreated;
        if (redraw) SendMessage(contentTabs.Handle, WM_SETREDRAW, IntPtr.Zero, IntPtr.Zero);
        workspaceSplit.SuspendLayout();
        contentTabs.SuspendLayout();
        try { contentTabs.SelectedIndex = page; }
        finally
        {
            try
            {
                contentTabs.ResumeLayout(true);
                workspaceSplit.ResumeLayout(true);
            }
            finally
            {
                if (redraw && !contentTabs.IsDisposed)
                {
                    SendMessage(contentTabs.Handle, WM_SETREDRAW, new IntPtr(1), IntPtr.Zero);
                    contentTabs.Invalidate(true);
                }
            }
        }
    }

}
