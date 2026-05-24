using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Shapes;
using GameOverlayTranslator.App.Domain;
using GameOverlayTranslator.App.Platform;

namespace GameOverlayTranslator.App;

public partial class RegionSelectionWindow : Window
{
    private Point? start;

    public RegionSelectionWindow(CapturableWindow window)
    {
        InitializeComponent();
        if (!WindowGeometry.TryGetClientScreenRect(window.Handle, out var rect))
        {
            throw new InvalidOperationException("선택한 창의 영역을 읽을 수 없습니다.");
        }

        var dpiScale = GetDpiScale(window.Handle);
        Left = rect.Left / dpiScale;
        Top = rect.Top / dpiScale;
        Width = rect.Width / dpiScale;
        Height = rect.Height / dpiScale;
        Loaded += (_, _) =>
        {
            SelectionCanvas.Focus();
            Activate();
        };
    }

    public CaptureRegion? Region { get; private set; }

    private void BeginSelection(object sender, MouseButtonEventArgs e)
    {
        start = e.GetPosition(SelectionCanvas);
        SelectionCanvas.CaptureMouse();
        SelectionRectangle.Visibility = Visibility.Visible;
        SetRectangle(SelectionRectangle, new Rect(start.Value, start.Value));
        e.Handled = true;
    }

    private void ResizeSelection(object sender, MouseEventArgs e)
    {
        if (start is not { } origin || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }
        SetRectangle(SelectionRectangle, new Rect(origin, e.GetPosition(SelectionCanvas)));
    }

    private void CompleteSelection(object sender, MouseButtonEventArgs e)
    {
        if (start is not { } origin)
        {
            return;
        }

        SelectionCanvas.ReleaseMouseCapture();
        var selection = new Rect(origin, e.GetPosition(SelectionCanvas));
        if (selection.Width < 8 || selection.Height < 8)
        {
            SelectionRectangle.Visibility = Visibility.Collapsed;
            start = null;
            return;
        }

        Region = CaptureRegion.FromPixels(selection, new Size(ActualWidth, ActualHeight));
        DialogResult = true;
        e.Handled = true;
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            DialogResult = false;
        }
    }

    private static void SetRectangle(Rectangle rectangle, Rect rect)
    {
        Canvas.SetLeft(rectangle, rect.Left);
        Canvas.SetTop(rectangle, rect.Top);
        rectangle.Width = rect.Width;
        rectangle.Height = rect.Height;
    }

    private static double GetDpiScale(nint handle)
    {
        try
        {
            return Math.Max(1, NativeMethods.GetDpiForWindow(handle) / 96d);
        }
        catch
        {
            return 1;
        }
    }
}
