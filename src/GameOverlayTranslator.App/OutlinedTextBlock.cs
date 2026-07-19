using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace GameOverlayTranslator.App;

public sealed class OutlinedTextBlock : Control
{
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(nameof(Text), typeof(string), typeof(OutlinedTextBlock), new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FillProperty =
        DependencyProperty.Register(nameof(Fill), typeof(Brush), typeof(OutlinedTextBlock), new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeProperty =
        DependencyProperty.Register(nameof(Stroke), typeof(Brush), typeof(OutlinedTextBlock), new FrameworkPropertyMetadata(Brushes.Black, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeThicknessProperty =
        DependencyProperty.Register(nameof(StrokeThickness), typeof(double), typeof(OutlinedTextBlock), new FrameworkPropertyMetadata(3.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MaxLineCountProperty =
        DependencyProperty.Register(
            nameof(MaxLineCount),
            typeof(int),
            typeof(OutlinedTextBlock),
            new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public Brush Fill
    {
        get => (Brush)GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    public Brush Stroke
    {
        get => (Brush)GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public double StrokeThickness
    {
        get => (double)GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    public int MaxLineCount
    {
        get => (int)GetValue(MaxLineCountProperty);
        set => SetValue(MaxLineCountProperty, value);
    }

    protected override Size MeasureOverride(Size constraint)
    {
        if (string.IsNullOrWhiteSpace(Text))
        {
            return new Size(0, 0);
        }

        var typeface = new Typeface(FontFamily, FontStyle, FontWeight, FontStretch);
        var text = new FormattedText(
            Text ?? string.Empty,
            System.Globalization.CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            typeface,
            FontSize,
            Fill,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);

        if (double.IsFinite(constraint.Width))
        {
            text.MaxTextWidth = Math.Max(1, constraint.Width);
            text.Trimming = TextTrimming.CharacterEllipsis;
        }
        if (MaxLineCount > 0)
        {
            text.MaxLineCount = MaxLineCount;
        }
        
        double offset = StrokeThickness * 2;
        double width = double.IsFinite(constraint.Width) ? Math.Min(constraint.Width, text.Width + offset) : text.Width + offset;
        double height = double.IsFinite(constraint.Height) ? Math.Min(constraint.Height, text.Height + offset) : text.Height + offset;
        return new Size(width, height);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        if (string.IsNullOrWhiteSpace(Text) || ActualWidth < 1 || ActualHeight < 1)
        {
            return;
        }

        var typeface = new Typeface(FontFamily, FontStyle, FontWeight, FontStretch);
        var text = new FormattedText(Text ?? string.Empty, System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, typeface, FontSize, Fill, VisualTreeHelper.GetDpi(this).PixelsPerDip);

        if (double.IsFinite(ActualWidth) && ActualWidth > 0)
        {
            text.MaxTextWidth = Math.Max(1, ActualWidth);
            text.Trimming = TextTrimming.CharacterEllipsis;
        }
        if (MaxLineCount > 0)
        {
            text.MaxLineCount = MaxLineCount;
        }

        var geometry = text.BuildGeometry(new Point(0, 0));
        var bounds = geometry.Bounds;

        if (bounds.Width > 0 && bounds.Height > 0)
        {
            // geometry의 실제 시각적 높이 중심을 컨트롤의 세로 중심에 맞춤
            double offsetY = (ActualHeight / 2.0) - (bounds.Y + bounds.Height / 2.0);
            
            // X축은 원래 기준을 유지하되, Y축 방향으로만 평행이동 적용
            var transform = new TranslateTransform(0, offsetY);
            geometry.Transform = transform;
        }

        drawingContext.DrawGeometry(Fill, new Pen(Stroke, StrokeThickness), geometry);
    }
}
