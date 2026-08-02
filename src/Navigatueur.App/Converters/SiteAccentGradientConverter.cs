using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace Navigatueur.App.Converters;

/// <summary>
/// Builds the chrome background's top-to-gray gradient from the active tab's
/// declared theme-color, if any. Transparent (no visible gradient, the flat
/// ChromeBackgroundBrush wash underneath just shows through) whenever there's
/// no site color, or the user has picked a custom background image — the two
/// looked messy layered together.
/// </summary>
public sealed class SiteAccentGradientConverter : IMultiValueConverter
{
    private static readonly Color GradientBottom = Color.FromRgb(0x2B, 0x2D, 0x31);

    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        var siteColorHex = values.Length > 0 ? values[0] as string : null;
        var customImagePath = values.Length > 1 ? values[1] as string : null;

        if (!string.IsNullOrEmpty(customImagePath) || string.IsNullOrEmpty(siteColorHex) || !TryParseColor(siteColorHex, out var color))
        {
            return Brushes.Transparent;
        }

        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0.5, 0),
            EndPoint = new Point(0.5, 1),
            GradientStops =
            {
                new GradientStop(Color.FromArgb(160, color.R, color.G, color.B), 0.0),
                new GradientStop(Color.FromArgb(0, GradientBottom.R, GradientBottom.G, GradientBottom.B), 1.0),
            },
        };
        brush.Freeze();
        return brush;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static bool TryParseColor(string text, out Color color)
    {
        text = text.Trim();

        try
        {
            if (ColorConverter.ConvertFromString(text) is Color parsed)
            {
                color = parsed;
                return true;
            }
        }
        catch (FormatException)
        {
            // Falls through to the rgb()/rgba() parser below.
        }

        if (text.StartsWith("rgb", StringComparison.OrdinalIgnoreCase))
        {
            var start = text.IndexOf('(');
            var end = text.IndexOf(')');
            if (start >= 0 && end > start)
            {
                var parts = text[(start + 1)..end].Split(',');
                if (parts.Length >= 3 &&
                    byte.TryParse(parts[0].Trim(), out var r) &&
                    byte.TryParse(parts[1].Trim(), out var g) &&
                    byte.TryParse(parts[2].Trim(), out var b))
                {
                    color = Color.FromRgb(r, g, b);
                    return true;
                }
            }
        }

        color = default;
        return false;
    }
}
