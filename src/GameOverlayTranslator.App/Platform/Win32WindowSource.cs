using System.Diagnostics;
using System.Text;
using GameOverlayTranslator.App.Contracts;
using GameOverlayTranslator.App.Domain;

namespace GameOverlayTranslator.App.Platform;

public sealed class Win32WindowSource : IWindowSource
{
    public IReadOnlyList<CapturableWindow> ListWindows()
    {
        var windows = new List<CapturableWindow>();

        NativeMethods.EnumWindows((handle, _) =>
        {
            if (!NativeMethods.IsWindowVisible(handle) || NativeMethods.GetWindowTextLength(handle) == 0)
            {
                return true;
            }

            if (!NativeMethods.GetWindowRect(handle, out var rect) || rect.Width < 80 || rect.Height < 80)
            {
                return true;
            }

            var title = new StringBuilder(NativeMethods.GetWindowTextLength(handle) + 1);
            NativeMethods.GetWindowText(handle, title, title.Capacity);
            NativeMethods.GetWindowThreadProcessId(handle, out var processId);
            windows.Add(new CapturableWindow(handle, title.ToString(), GetProcessName(processId)));
            return true;
        }, nint.Zero);

        return windows.OrderBy(window => window.Title, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    private static string GetProcessName(uint processId)
    {
        try
        {
            return Process.GetProcessById((int)processId).ProcessName;
        }
        catch
        {
            return "unknown";
        }
    }
}
