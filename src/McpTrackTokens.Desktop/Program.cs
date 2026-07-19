using System.Runtime.InteropServices;

namespace McpTrackTokens.Desktop;

internal static class Program
{
    private const string MutexName = "Local\\McpTrackTokens.Desktop.SingleInstance";
    private const string ActivateEventName = "Local\\McpTrackTokens.Desktop.Activate";

    [STAThread]
    private static void Main()
    {
        using var mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
        if (!createdNew)
        {
            try
            {
                using var activate = EventWaitHandle.OpenExisting(ActivateEventName);
                activate.Set();
            }
            catch
            {
                // First instance may still be starting; ignore.
            }

            return;
        }

        using var activateEvent = new EventWaitHandle(
            initialState: false,
            mode: EventResetMode.AutoReset,
            name: ActivateEventName);

        ApplicationConfiguration.Initialize();
        System.Windows.Forms.Application.EnableVisualStyles();
        System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);

        var options = DesktopOptions.Load();
        var form = new DashboardForm(options);

        RegisteredWaitHandle? waitHandle = null;
        waitHandle = ThreadPool.RegisterWaitForSingleObject(
            activateEvent,
            (_, _) =>
            {
                if (form.IsDisposed)
                {
                    return;
                }

                try
                {
                    form.BeginInvoke(BringToFront, form);
                }
                catch (ObjectDisposedException)
                {
                    // Form closed while activate was signaled.
                }
            },
            state: null,
            millisecondsTimeOutInterval: -1,
            executeOnlyOnce: false);

        try
        {
            System.Windows.Forms.Application.Run(form);
        }
        finally
        {
            waitHandle?.Unregister(null);
        }
    }

    private static void BringToFront(Form form)
    {
        if (form.WindowState == FormWindowState.Minimized)
        {
            form.WindowState = FormWindowState.Normal;
        }

        form.Show();
        form.Activate();
        form.BringToFront();

        // Brief top-most pulse helps when another process already called AllowSetForegroundWindow.
        form.TopMost = true;
        form.TopMost = false;
        SetForegroundWindow(form.Handle);
    }

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
}
