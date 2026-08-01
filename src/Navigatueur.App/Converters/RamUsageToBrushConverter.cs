using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Navigatueur.App.Converters;

public sealed class RamUsageToBrushConverter : IValueConverter
{
    private static readonly Brush NormalBrush = new SolidColorBrush(Color.FromRgb(0x8A, 0x8F, 0x98));
    private static readonly Brush WarningBrush = new SolidColorBrush(Color.FromRgb(0xE0, 0xA5, 0x2A));
    private static readonly Brush CriticalBrush = new SolidColorBrush(Color.FromRgb(0xE0, 0x4F, 0x4F));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var megabytes = value is long l ? l : 0;

        return megabytes switch
        {
            >= 1000 => CriticalBrush,
            >= 800 => WarningBrush,
            _ => NormalBrush,
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
