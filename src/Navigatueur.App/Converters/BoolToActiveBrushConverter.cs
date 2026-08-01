using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace Navigatueur.App.Converters;

/// <summary>
/// Looks up "SurfaceBrush" fresh on every conversion (rather than caching it
/// once) so it stays correct after <see cref="Navigatueur.App.Services.ThemeService"/>
/// replaces that resource entry on a theme change.
/// </summary>
public sealed class BoolToActiveBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Application.Current.Resources["SurfaceBrush"] : Brushes.Transparent;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
