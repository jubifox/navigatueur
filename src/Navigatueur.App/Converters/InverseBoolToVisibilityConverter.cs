using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Navigatueur.App.Converters;

/// <summary>Collapsed when true, Visible when false — e.g. the reload button, which gives way to Stop while a page loads.</summary>
public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
