using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Navigatueur.App.Converters;

/// <summary>Picks the maximize-vs-restore glyph for the title bar's maximize/restore button based on the window's current state.</summary>
public sealed class WindowStateToIconConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is WindowState.Maximized
            ? Application.Current.Resources["IconRestore"]
            : Application.Current.Resources["IconMaximize"];

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
