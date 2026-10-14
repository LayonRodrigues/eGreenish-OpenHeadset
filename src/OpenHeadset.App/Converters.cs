using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace OpenHeadset.App;

/// <summary>Verdadeiro quando o valor ligado é igual ao parâmetro. Usado para marcar o segmento selecionado.</summary>
public sealed class EqualsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not null && Equals(value, parameter);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Escolhe o ícone de bateria pelo nível: baixa (&lt; 20%), média (&lt; 60%) ou cheia. Sem leitura, mostra média.</summary>
public sealed class BatteryIconConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = value is int percent
            ? percent < 20 ? "Icon.BatteryLow" : percent < 60 ? "Icon.BatteryHalf" : "Icon.BatteryFull"
            : "Icon.BatteryHalf";
        return Application.Current.TryFindResource(key);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Verde quando conectado, âmbar caso contrário.</summary>
public sealed class StatusBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = value is true ? "OkBrush" : "WarnBrush";
        return Application.Current.TryFindResource(key) as Brush ?? Brushes.Gray;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
