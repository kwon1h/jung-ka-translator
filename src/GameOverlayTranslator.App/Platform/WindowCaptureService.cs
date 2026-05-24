using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using GameOverlayTranslator.App.Contracts;
using GameOverlayTranslator.App.Domain;

namespace GameOverlayTranslator.App.Platform;

public sealed class WindowCaptureService : ICaptureService
{
    public Task<CapturedFrame> CaptureAsync(CaptureTarget target, CaptureRegion region, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (!NativeMethods.IsWindow(target.Window.Handle))
        {
            throw new CaptureException("선택한 창이 종료되었습니다.");
        }

        if (NativeMethods.IsIconic(target.Window.Handle))
        {
            throw new CaptureException("최소화된 창은 캡처할 수 없습니다.");
        }

        if (!WindowGeometry.TryGetClientScreenRect(target.Window.Handle, out var rect))
        {
            throw new CaptureException("창 크기를 읽을 수 없습니다.");
        }

        var crop = region.ToPixels(rect.Width, rect.Height);
        var screenDc = NativeMethods.GetDC(nint.Zero);
        var memoryDc = NativeMethods.CreateCompatibleDC(screenDc);
        var bitmap = NativeMethods.CreateCompatibleBitmap(screenDc, crop.Width, crop.Height);
        var oldBitmap = NativeMethods.SelectObject(memoryDc, bitmap);
        try
        {
            const int SourceCopy = 0x00CC0020;
            if (!NativeMethods.BitBlt(memoryDc, 0, 0, crop.Width, crop.Height, screenDc, rect.Left + crop.X, rect.Top + crop.Y, SourceCopy))
            {
                throw new CaptureException("창 프레임 캡처에 실패했습니다.");
            }
            var source = Imaging.CreateBitmapSourceFromHBitmap(bitmap, nint.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return Task.FromResult(new CapturedFrame(source));
        }
        finally
        {
            NativeMethods.SelectObject(memoryDc, oldBitmap);
            NativeMethods.DeleteObject(bitmap);
            NativeMethods.DeleteDC(memoryDc);
            NativeMethods.ReleaseDC(nint.Zero, screenDc);
        }
    }
}

public sealed class CaptureException(string message) : Exception(message);
