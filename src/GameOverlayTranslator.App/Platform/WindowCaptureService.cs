using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using GameOverlayTranslator.App.Contracts;
using GameOverlayTranslator.App.Domain;

namespace GameOverlayTranslator.App.Platform;

public sealed class WindowCaptureService(bool requireTargetForeground = false) : ICaptureService, IDisposable
{
    private const int SourceCopy = 0x00CC0020;
    private readonly object captureLock = new();
    private nint memoryDc;
    private nint bitmap;
    private nint originalBitmap;
    private nint bufferSourceHandle;
    private int bufferWidth;
    private int bufferHeight;
    private bool disposed;

    public Task<CapturedFrame> CaptureAsync(CaptureTarget target, CaptureRegion region, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ct.ThrowIfCancellationRequested();

        if (!NativeMethods.IsWindow(target.Window.Handle))
        {
            throw new CaptureException("선택한 창이 종료되었습니다.");
        }

        if (NativeMethods.IsIconic(target.Window.Handle))
        {
            throw new CaptureException("최소화된 창은 캡처할 수 없습니다.");
        }

        if (requireTargetForeground && !IsTargetForeground(target.Window.Handle, NativeMethods.GetForegroundWindow()))
        {
            throw new CaptureDeferredException("게임 창이 활성화되면 번역을 자동으로 재개합니다.");
        }

        if (!WindowGeometry.TryGetClientScreenRect(target.Window.Handle, out var rect))
        {
            throw new CaptureException("창 크기를 읽을 수 없습니다.");
        }

        var crop = region.ToPixels(rect.Width, rect.Height);
        var captureSourceHandle = ResolveCaptureSourceHandle(target.Window.Handle);
        var windowDc = NativeMethods.GetDC(captureSourceHandle);
        if (windowDc == nint.Zero)
        {
            throw new CaptureException("게임 창의 화면을 읽을 수 없습니다.");
        }

        try
        {
            lock (captureLock)
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                EnsureCaptureBuffer(windowDc, captureSourceHandle, crop.Width, crop.Height);

                if (!NativeMethods.BitBlt(
                        memoryDc,
                        0,
                        0,
                        crop.Width,
                        crop.Height,
                        windowDc,
                        crop.X,
                        crop.Y,
                        SourceCopy))
                {
                    throw new CaptureException("창 프레임 캡처에 실패했습니다.");
                }

                var source = Imaging.CreateBitmapSourceFromHBitmap(
                    bitmap,
                    nint.Zero,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
                source.Freeze();
                return Task.FromResult(new CapturedFrame(source));
            }
        }
        finally
        {
            NativeMethods.ReleaseDC(captureSourceHandle, windowDc);
        }
    }

    public void Dispose()
    {
        lock (captureLock)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            ReleaseCaptureBuffer();
        }
    }

    internal static nint ResolveCaptureSourceHandle(nint targetHandle) => targetHandle;

    internal static bool IsGdiSelectionFailure(nint selectedObject) =>
        selectedObject == nint.Zero || selectedObject == NativeMethods.HgdiError;

    internal static bool IsTargetForeground(nint targetHandle, nint foregroundHandle) =>
        targetHandle != nint.Zero && targetHandle == foregroundHandle;

    internal static bool CanReuseCaptureBuffer(
        nint currentSourceHandle,
        int currentWidth,
        int currentHeight,
        nint nextSourceHandle,
        int nextWidth,
        int nextHeight) =>
        currentSourceHandle != nint.Zero
        && currentSourceHandle == nextSourceHandle
        && currentWidth == nextWidth
        && currentHeight == nextHeight;

    private void EnsureCaptureBuffer(nint windowDc, nint sourceHandle, int width, int height)
    {
        if (bitmap != nint.Zero
            && CanReuseCaptureBuffer(
                bufferSourceHandle,
                bufferWidth,
                bufferHeight,
                sourceHandle,
                width,
                height))
        {
            return;
        }

        if (memoryDc != nint.Zero
            && bufferSourceHandle != nint.Zero
            && bufferSourceHandle != sourceHandle)
        {
            ReleaseCaptureBuffer();
        }

        if (memoryDc == nint.Zero)
        {
            memoryDc = NativeMethods.CreateCompatibleDC(windowDc);
            if (memoryDc == nint.Zero)
            {
                throw new CaptureException("게임 창 캡처 버퍼를 만들 수 없습니다.");
            }
        }

        var nextBitmap = NativeMethods.CreateCompatibleBitmap(windowDc, width, height);
        if (nextBitmap == nint.Zero)
        {
            throw new CaptureException("게임 창 캡처 이미지를 만들 수 없습니다.");
        }

        var replacedBitmap = NativeMethods.SelectObject(memoryDc, nextBitmap);
        if (IsGdiSelectionFailure(replacedBitmap))
        {
            NativeMethods.DeleteObject(nextBitmap);
            throw new CaptureException("게임 창 캡처 이미지를 선택할 수 없습니다.");
        }

        if (bitmap == nint.Zero)
        {
            originalBitmap = replacedBitmap;
        }
        else
        {
            NativeMethods.DeleteObject(bitmap);
        }

        bitmap = nextBitmap;
        bufferSourceHandle = sourceHandle;
        bufferWidth = width;
        bufferHeight = height;
    }

    private void ReleaseCaptureBuffer()
    {
        if (memoryDc != nint.Zero)
        {
            if (bitmap != nint.Zero)
            {
                if (!IsGdiSelectionFailure(originalBitmap))
                {
                    NativeMethods.SelectObject(memoryDc, originalBitmap);
                }
                NativeMethods.DeleteObject(bitmap);
            }

            NativeMethods.DeleteDC(memoryDc);
        }

        memoryDc = nint.Zero;
        bitmap = nint.Zero;
        originalBitmap = nint.Zero;
        bufferSourceHandle = nint.Zero;
        bufferWidth = 0;
        bufferHeight = 0;
    }
}

public sealed class CaptureException(string message) : Exception(message);

public sealed class CaptureDeferredException(string message) : Exception(message);
