using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using Navigatueur.Core.Tabs;

namespace Navigatueur.App.Converters;

/// <summary>A tab group's icon key → its glyph geometry ("GroupGlyph" + key in Icons.xaml), or null for a plain folder.</summary>
public sealed class GroupGlyphConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = GroupIcons.Normalize(value as string);
        return key.Length == 0 ? null : Application.Current?.TryFindResource("GroupGlyph" + key) as Geometry;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
