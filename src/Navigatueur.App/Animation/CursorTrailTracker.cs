using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace Navigatueur.App.Animation;

/// <summary>
/// Draws the accent-colored cursor trail as a continuous fading line: each
/// mouse-move connects a short Line segment to the previous point (touching
/// end-to-end), so it reads as one smooth streak rather than a string of
/// separate dots. A gap longer than <see cref="MaxGapMilliseconds"/> since the
/// last point (re-entering the canvas at a different spot, or a throttled
/// pause) starts a fresh stroke instead of drawing one long spurious line
/// across the gap. One instance per canvas — MainWindow's chrome trail and
/// each BrowserTabView's in-page trail each need their own, independent state.
/// </summary>
public sealed class CursorTrailTracker
{
    private const double SpawnIntervalMilliseconds = 12;
    private const double MaxGapMilliseconds = 120;
    private const double SegmentLifetimeMilliseconds = 260;
    private const double StrokeThickness = 3.5;

    private readonly Canvas _canvas;
    private Point? _lastPoint;
    private DateTime _lastMoveTime = DateTime.MinValue;
    private DateTime _lastSpawnTime = DateTime.MinValue;

    public CursorTrailTracker(Canvas canvas)
    {
        _canvas = canvas;
    }

    public void OnMove(Point position)
    {
        var now = DateTime.UtcNow;
        if ((now - _lastSpawnTime).TotalMilliseconds < SpawnIntervalMilliseconds)
        {
            return;
        }

        _lastSpawnTime = now;

        if (_lastPoint is { } previous && (now - _lastMoveTime).TotalMilliseconds <= MaxGapMilliseconds)
        {
            DrawSegment(previous, position);
        }

        _lastPoint = position;
        _lastMoveTime = now;
    }

    private void DrawSegment(Point from, Point to)
    {
        var accentColor = Application.Current.Resources["AccentBrush"] is SolidColorBrush accentBrush
            ? accentBrush.Color
            : Colors.White;

        var line = new Line
        {
            X1 = from.X,
            Y1 = from.Y,
            X2 = to.X,
            Y2 = to.Y,
            Stroke = new SolidColorBrush(accentColor),
            StrokeThickness = StrokeThickness,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            IsHitTestVisible = false,
            Opacity = 0.7,
        };
        _canvas.Children.Add(line);

        var fade = new DoubleAnimation(0.7, 0, TimeSpan.FromMilliseconds(SegmentLifetimeMilliseconds))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn },
        };
        fade.Completed += (_, _) => _canvas.Children.Remove(line);
        line.BeginAnimation(UIElement.OpacityProperty, fade);
    }
}
