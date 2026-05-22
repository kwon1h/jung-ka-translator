using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace GameOverlayTranslator.App;

public sealed class OutlinedTextBlock : Control
{
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(nameof(Text), typeof(string), typeof(OutlinedTextBlock), new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsRender));

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        var typeface = new Typeface(FontFamily, FontStyle, FontWeight, FontStretch);
        var text = new FormattedText(Text, System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, typeface, FontSize, Brushes.Black, VisualTreeHelper.GetDpi(this).PixelsPerDip)
        {
            MaxTextWidth = Math.Max(0, ActualWidth),
            Trimming = TextTrimming.CharacterEllipsis
        };
        var geometry = text.BuildGeometry(new Point(0, 0));
        drawingContext.DrawGeometry(Brushes.Black, new Pen(Brushes.White, 3), geometry);
    }
}
