using System.Windows;
using System.Windows.Media.Animation;

namespace Navigatueur.App.Animation;

/// <summary>
/// WPF has no built-in animation for GridLength (DoubleAnimation only targets
/// double properties), so animating a ColumnDefinition's Width — used for the
/// hover-driven auto-expand/collapse tab sidebar — needs this small custom
/// AnimationTimeline. Standard, well-known technique for this exact gap.
/// </summary>
public sealed class GridLengthAnimation : AnimationTimeline
{
    public override Type TargetPropertyType => typeof(GridLength);

    public GridLength From { get; set; }

    public GridLength To { get; set; }

    public IEasingFunction? EasingFunction { get; set; }

    public override object GetCurrentValue(object defaultOriginValue, object defaultDestinationValue, AnimationClock animationClock)
    {
        var progress = animationClock.CurrentProgress ?? 0;
        if (EasingFunction is not null)
        {
            progress = EasingFunction.Ease(progress);
        }

        var fromValue = From.Value;
        var toValue = To.Value;
        return new GridLength(fromValue + (toValue - fromValue) * progress, GridUnitType.Pixel);
    }

    protected override Freezable CreateInstanceCore() => new GridLengthAnimation();
}
