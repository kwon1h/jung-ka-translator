using System.Runtime.InteropServices;
using System.Text;

namespace GameOverlayTranslator.App.Platform;

internal static class NativeMethods
{
    internal delegate bool EnumWindowsProc(nint hwnd, nint lParam);
    internal delegate void WinEventProc(
        nint hook,
        uint eventType,
        nint hwnd,
        int objectId,
        int childId,
        uint eventThread,
        uint eventTime);

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public int Width => Right - Left;
        public int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativePoint
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    internal static extern bool EnumWindows(EnumWindowsProc callback, nint lParam);

    [DllImport("user32.dll")]
    internal static extern bool IsWindowVisible(nint hwnd);

    [DllImport("user32.dll")]
    internal static extern bool IsIconic(nint hwnd);

    [DllImport("user32.dll")]
    internal static extern bool IsWindow(nint hwnd);

    [DllImport("user32.dll")]
    internal static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    internal static extern bool SetForegroundWindow(nint hwnd);

    [DllImport("user32.dll")]
    internal static extern bool BringWindowToTop(nint hwnd);

    [DllImport("user32.dll")]
    internal static extern nint GetWindow(nint hwnd, uint command);

    [DllImport("user32.dll")]
    internal static extern bool SetWindowPos(nint hwnd, nint insertAfter, int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll")]
    internal static extern nint SetWinEventHook(
        uint eventMin,
        uint eventMax,
        nint eventHookModule,
        WinEventProc eventProc,
        uint processId,
        uint threadId,
        uint flags);

    [DllImport("user32.dll")]
    internal static extern bool UnhookWinEvent(nint eventHook);

    [DllImport("user32.dll")]
    internal static extern nint SetActiveWindow(nint hwnd);

    [DllImport("user32.dll")]
    internal static extern nint SetFocus(nint hwnd);

    [DllImport("user32.dll")]
    internal static extern bool ShowWindow(nint hwnd, int command);

    [DllImport("user32.dll")]
    internal static extern bool AttachThreadInput(uint sourceThreadId, uint targetThreadId, bool attach);

    [DllImport("kernel32.dll")]
    internal static extern uint GetCurrentThreadId();

    internal static readonly nint HwndTop = nint.Zero;
    internal static readonly nint HwndTopmost = new(-1);
    internal static readonly nint HgdiError = new(-1);
    internal const uint GwHwndPrev = 3;
    internal const uint SwpNoSize = 0x0001;
    internal const uint SwpNoMove = 0x0002;
    internal const uint SwpNoActivate = 0x0010;
    internal const uint SwpShowWindow = 0x0040;
    internal const uint EventSystemForeground = 0x0003;
    internal const uint EventObjectReorder = 0x8004;
    internal const uint EventObjectLocationChange = 0x800B;
    internal const uint WinEventOutOfContext = 0x0000;
    internal const uint WinEventSkipOwnProcess = 0x0002;
    internal const int ObjIdWindow = 0;
    internal const int SwMinimize = 6;
    internal const int SwShowNoActivate = 4;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int GetWindowText(nint hwnd, StringBuilder text, int maxLength);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int GetWindowTextLength(nint hwnd);

    [DllImport("user32.dll")]
    internal static extern uint GetWindowThreadProcessId(nint hwnd, out uint processId);

    [DllImport("user32.dll")]
    internal static extern bool GetWindowRect(nint hwnd, out NativeRect rect);

    [DllImport("user32.dll")]
    internal static extern bool GetClientRect(nint hwnd, out NativeRect rect);

    [DllImport("user32.dll")]
    internal static extern bool ClientToScreen(nint hwnd, ref NativePoint point);

    [DllImport("user32.dll")]
    internal static extern uint GetDpiForWindow(nint hwnd);

    [DllImport("gdi32.dll")]
    internal static extern bool DeleteObject(nint handle);

    [DllImport("user32.dll")]
    internal static extern nint GetDC(nint hwnd);

    [DllImport("user32.dll")]
    internal static extern int ReleaseDC(nint hwnd, nint dc);

    [DllImport("gdi32.dll")]
    internal static extern nint CreateCompatibleDC(nint dc);

    [DllImport("gdi32.dll")]
    internal static extern nint CreateCompatibleBitmap(nint dc, int width, int height);

    [DllImport("gdi32.dll")]
    internal static extern nint SelectObject(nint dc, nint handle);

    [DllImport("gdi32.dll")]
    internal static extern bool BitBlt(nint destinationDc, int x, int y, int width, int height, nint sourceDc, int sourceX, int sourceY, int rasterOperation);

    [DllImport("gdi32.dll")]
    internal static extern bool DeleteDC(nint dc);

    internal const int GWL_EXSTYLE = -20;
    internal const int WS_EX_TOPMOST = 0x00000008;
    internal const int WS_EX_TRANSPARENT = 0x00000020;
    internal const int WS_EX_NOACTIVATE = 0x08000000;

    [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
    internal static extern int GetWindowLong(nint hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
    internal static extern int SetWindowLong(nint hwnd, int index, int newLong);

    internal const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool SetWindowDisplayAffinity(nint hwnd, uint affinity);
}
