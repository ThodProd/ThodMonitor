using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace ThodMonitor.Converters;

/// <summary>
/// Сравнивает значение с ConverterParameter. Для RadioButton.IsChecked (двусторонне) и Visibility (односторонне).
/// </summary>
public sealed class EqualsConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var eq = string.Equals(System.Convert.ToString(value, CultureInfo.InvariantCulture),
                               parameter?.ToString(), StringComparison.Ordinal);
        return targetType == typeof(Visibility) ? (eq ? Visibility.Visible : Visibility.Collapsed) : eq;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true && parameter != null
            ? System.Convert.ChangeType(parameter.ToString()!, targetType, CultureInfo.InvariantCulture)
            : Binding.DoNothing;
}
