using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace OpenHeadset.App;

/// <summary>
/// Desenha uma geometria da biblioteca de ícones (grade de 24x24) usando a cor de texto herdada,
/// de modo que o ícone acompanha o estado do botão em que está.
/// </summary>
public sealed class Icon : FrameworkElement
{
    private const double Grid = 24;
    private const double DefaultSize = 20;

    public static readonly DependencyProperty DataProperty = DependencyProperty.Register(
        nameof(Data), typeof(Geometry), typeof(Icon),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ForegroundProperty = TextElement.ForegroundProperty.AddOwner(
        typeof(Icon),
        new FrameworkPropertyMetadata(SystemColors.ControlTextBrush,
            FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.Inherits));

    public Geometry? Data
    {
        get => (Geometry?)GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    public Brush Foreground
    {
        get => (Brush)GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize) => new(DefaultSize, DefaultSize);

    protected override void OnRender(DrawingContext dc)
    {
        if (Data is null)
            return;

        var scale = Math.Min(ActualWidth, ActualHeight) / Grid;
        dc.PushTransform(new ScaleTransform(scale, scale));
        dc.DrawGeometry(Foreground, null, Data);
        dc.Pop();
    }
}
