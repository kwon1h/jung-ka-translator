using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using GameOverlayTranslator.App.Domain;
using GameOverlayTranslator.App.Platform;

namespace GameOverlayTranslator.App;

public partial class RegionSelectionWindow : Window
{
    private const double MinimumSize = 8;
    private const double ResizeMargin = 8;
    private readonly List<RegionItem> items = [];
    private readonly IReadOnlyList<CaptureRegion> initialIncluded;
    private readonly IReadOnlyList<CaptureRegion> initialExcluded;
    private readonly bool allowMultipleIncluded;
    private Point dragStart;
    private Rect dragStartRect;
    private RegionItem? selected;
    private DragMode dragMode;
    private ResizeEdges resizeEdges;

    public RegionSelectionWindow(
        CapturableWindow window,
        IReadOnlyList<CaptureRegion>? includedRegions = null,
        IReadOnlyList<CaptureRegion>? excludedRegions = null,
        bool allowMultipleIncluded = true)
    {
        InitializeComponent();
        initialIncluded = includedRegions ?? [];
        initialExcluded = excludedRegions ?? [];
        this.allowMultipleIncluded = allowMultipleIncluded;

        if (!WindowGeometry.TryGetClientScreenRect(window.Handle, out var rect))
        {
            throw new InvalidOperationException("선택한 창의 영역을 읽을 수 없습니다.");
        }

        var dpiScale = GetDpiScale(window.Handle);
        Left = rect.Left / dpiScale;
        Top = rect.Top / dpiScale;
        Width = rect.Width / dpiScale;
        Height = rect.Height / dpiScale;
        Loaded += OnLoaded;
    }

    public IReadOnlyList<CaptureRegion> Regions { get; private set; } = [];
    public IReadOnlyList<CaptureRegion> ExcludedRegions { get; private set; } = [];
    public CaptureRegion? Region => Regions.Count > 0 ? Regions[0] : null;

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        foreach (var region in initialIncluded)
        {
            AddItem(ToRect(region), false);
        }
        foreach (var region in initialExcluded)
        {
            AddItem(ToRect(region), true);
        }

        SelectionCanvas.Focus();
        Activate();
    }

    private void BeginSelection(object sender, MouseButtonEventArgs e)
    {
        dragStart = e.GetPosition(SelectionCanvas);
        var isExclude = e.ChangedButton == MouseButton.Right;
        var hit = isExclude ? null : FindItem(e.OriginalSource as DependencyObject);

        if (hit is not null)
        {
            Select(hit);
            dragStartRect = GetRectangle(hit.Shape);
            resizeEdges = FindResizeEdges(dragStart, dragStartRect);
            dragMode = resizeEdges == ResizeEdges.None ? DragMode.Move : DragMode.Resize;
        }
        else
        {
            if (!isExclude && !allowMultipleIncluded)
            {
                foreach (var old in items.Where(item => !item.IsExcluded).ToArray())
                {
                    RemoveItem(old);
                }
            }

            selected = AddItem(new Rect(dragStart, dragStart), isExclude);
            dragStartRect = GetRectangle(selected.Shape);
            dragMode = isExclude ? DragMode.CreateExclude : DragMode.CreateInclude;
        }

        SelectionCanvas.CaptureMouse();
        e.Handled = true;
    }

    private void UpdateSelection(object sender, MouseEventArgs e)
    {
        if (selected is null || dragMode == DragMode.None)
        {
            return;
        }

        var point = e.GetPosition(SelectionCanvas);
        var next = dragMode switch
        {
            DragMode.CreateInclude or DragMode.CreateExclude => new Rect(dragStart, point),
            DragMode.Move => MoveRectangle(dragStartRect, point - dragStart),
            DragMode.Resize => ResizeRectangle(dragStartRect, point - dragStart, resizeEdges),
            _ => dragStartRect
        };
        SetRectangle(selected.Shape, Clamp(next));
    }

    private void CompleteSelection(object sender, MouseButtonEventArgs e)
    {
        if (selected is null || dragMode == DragMode.None)
        {
            return;
        }

        SelectionCanvas.ReleaseMouseCapture();
        var rect = GetRectangle(selected.Shape);
        if (rect.Width < MinimumSize || rect.Height < MinimumSize)
        {
            RemoveItem(selected);
        }
        dragMode = DragMode.None;
        e.Handled = true;
    }

    private RegionItem AddItem(Rect rect, bool isExcluded)
    {
        var shape = new Rectangle
        {
            Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(isExcluded ? "#44EF4444" : "#4435C3A7")),
            Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString(isExcluded ? "#EF4444" : "#35C3A7")),
            StrokeThickness = 2,
            Cursor = Cursors.SizeAll
        };
        var item = new RegionItem(shape, isExcluded);
        shape.Tag = item;
        items.Add(item);
        SelectionCanvas.Children.Add(shape);
        SetRectangle(shape, rect);
        Select(item);
        return item;
    }

    private void Select(RegionItem item)
    {
        if (selected is not null)
        {
            selected.Shape.StrokeThickness = 2;
        }
        selected = item;
        selected.Shape.StrokeThickness = 4;
    }

    private RegionItem? FindItem(DependencyObject? source)
    {
        while (source is not null && source != SelectionCanvas)
        {
            if (source is Rectangle { Tag: RegionItem item })
            {
                return item;
            }
            source = VisualTreeHelper.GetParent(source);
        }
        return null;
    }

    private void RemoveItem(RegionItem item)
    {
        SelectionCanvas.Children.Remove(item.Shape);
        items.Remove(item);
        if (selected == item)
        {
            selected = null;
        }
    }

    private void SaveEditing(object sender, RoutedEventArgs e) => CompleteDialog();
    private void CancelEditing(object sender, RoutedEventArgs e) => DialogResult = false;

    private void ClearAllRegions(object sender, RoutedEventArgs e)
    {
        foreach (var item in items.ToArray())
        {
            RemoveItem(item);
        }
        SelectionCanvas.Focus();
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            DialogResult = false;
        }
        else if (e.Key == Key.Enter)
        {
            CompleteDialog();
        }
        else if (e.Key is Key.Delete or Key.Back && selected is not null)
        {
            RemoveItem(selected);
            e.Handled = true;
        }
    }

    private void CompleteDialog()
    {
        var size = new Size(ActualWidth, ActualHeight);
        Regions = items.Where(item => !item.IsExcluded)
            .Select(item => CaptureRegion.FromPixels(GetRectangle(item.Shape), size)).ToArray();
        ExcludedRegions = items.Where(item => item.IsExcluded)
            .Select(item => CaptureRegion.FromPixels(GetRectangle(item.Shape), size)).ToArray();
        DialogResult = true;
    }

    private Rect ToRect(CaptureRegion region) =>
        new(region.X * ActualWidth, region.Y * ActualHeight, region.Width * ActualWidth, region.Height * ActualHeight);

    private Rect Clamp(Rect rect)
    {
        var left = Math.Clamp(rect.Left, 0, Math.Max(0, ActualWidth - 1));
        var top = Math.Clamp(rect.Top, 0, Math.Max(0, ActualHeight - 1));
        var right = Math.Clamp(rect.Right, left, ActualWidth);
        var bottom = Math.Clamp(rect.Bottom, top, ActualHeight);
        return new Rect(new Point(left, top), new Point(right, bottom));
    }

    private Rect MoveRectangle(Rect rect, Vector delta)
    {
        var x = Math.Clamp(rect.X + delta.X, 0, Math.Max(0, ActualWidth - rect.Width));
        var y = Math.Clamp(rect.Y + delta.Y, 0, Math.Max(0, ActualHeight - rect.Height));
        return new Rect(x, y, rect.Width, rect.Height);
    }

    private static Rect ResizeRectangle(Rect rect, Vector delta, ResizeEdges edges)
    {
        var left = edges.HasFlag(ResizeEdges.Left) ? rect.Left + delta.X : rect.Left;
        var right = edges.HasFlag(ResizeEdges.Right) ? rect.Right + delta.X : rect.Right;
        var top = edges.HasFlag(ResizeEdges.Top) ? rect.Top + delta.Y : rect.Top;
        var bottom = edges.HasFlag(ResizeEdges.Bottom) ? rect.Bottom + delta.Y : rect.Bottom;

        if (right - left < MinimumSize)
        {
            if (edges.HasFlag(ResizeEdges.Left)) left = right - MinimumSize;
            else right = left + MinimumSize;
        }
        if (bottom - top < MinimumSize)
        {
            if (edges.HasFlag(ResizeEdges.Top)) top = bottom - MinimumSize;
            else bottom = top + MinimumSize;
        }
        return new Rect(new Point(left, top), new Point(right, bottom));
    }

    private static ResizeEdges FindResizeEdges(Point point, Rect rect)
    {
        var edges = ResizeEdges.None;
        if (Math.Abs(point.X - rect.Left) <= ResizeMargin) edges |= ResizeEdges.Left;
        else if (Math.Abs(point.X - rect.Right) <= ResizeMargin) edges |= ResizeEdges.Right;
        if (Math.Abs(point.Y - rect.Top) <= ResizeMargin) edges |= ResizeEdges.Top;
        else if (Math.Abs(point.Y - rect.Bottom) <= ResizeMargin) edges |= ResizeEdges.Bottom;
        return edges;
    }

    private static Rect GetRectangle(Rectangle rectangle) => new(
        Canvas.GetLeft(rectangle), Canvas.GetTop(rectangle), rectangle.Width, rectangle.Height);

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

    private sealed record RegionItem(Rectangle Shape, bool IsExcluded);
    private enum DragMode { None, CreateInclude, CreateExclude, Move, Resize }

    [Flags]
    private enum ResizeEdges { None = 0, Left = 1, Right = 2, Top = 4, Bottom = 8 }
}
