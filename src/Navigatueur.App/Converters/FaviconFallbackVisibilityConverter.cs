using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Navigatueur.App.Converters;

/// <summary>Shows the generic globe in a tab row only when there's neither a favicon nor a loading spinner to show instead.</summary>
public sealed class FaviconFallbackVisibilityConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        var hasFavicon = values.Length > 0 && values[0] is not null && values[0] != DependencyProperty.UnsetValue;
        var isLoading = values.Length > 1 && values[1] is true;
        return hasFavicon || isLoading ? Visibility.Collapsed : Visibility.Visible;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
