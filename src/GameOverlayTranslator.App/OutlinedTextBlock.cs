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

    protected override Size MeasureOverride(Size constraint)
    {
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
            text.MaxTextWidth = Math.Max(0, constraint.Width);
            text.Trimming = TextTrimming.CharacterEllipsis;
        }
        
        double offset = StrokeThickness * 2;
        double width = double.IsFinite(constraint.Width) ? Math.Min(constraint.Width, text.Width + offset) : text.Width + offset;
        double height = double.IsFinite(constraint.Height) ? Math.Min(constraint.Height, text.Height + offset) : text.Height + offset;
        return new Size(width, height);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        var typeface = new Typeface(FontFamily, FontStyle, FontWeight, FontStretch);
        var text = new FormattedText(Text ?? string.Empty, System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, typeface, FontSize, Fill, VisualTreeHelper.GetDpi(this).PixelsPerDip);

        if (double.IsFinite(ActualWidth) && ActualWidth > 0)
        {
            text.MaxTextWidth = ActualWidth;
            text.Trimming = TextTrimming.CharacterEllipsis;
        }

        var geometry = text.BuildGeometry(new Point(0, 0));
        drawingContext.DrawGeometry(Fill, new Pen(Stroke, StrokeThickness), geometry);
    }
}
