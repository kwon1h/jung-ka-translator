namespace GameOverlayTranslator.App.Platform;

internal static class WindowGeometry
{
    public static bool TryGetClientScreenRect(nint hwnd, out NativeMethods.NativeRect rect)
    {
        rect = default;

        if (!NativeMethods.GetClientRect(hwnd, out var client) || client.Width < 2 || client.Height < 2)
        {
            return false;
        }

        var origin = new NativeMethods.NativePoint { X = client.Left, Y = client.Top };
        if (!NativeMethods.ClientToScreen(hwnd, ref origin))
        {
            return false;
        }

        rect = new NativeMethods.NativeRect
        {
            Left = origin.X,
            Top = origin.Y,
            Right = origin.X + client.Width,
            Bottom = origin.Y + client.Height
        };
        return true;
    }
}
