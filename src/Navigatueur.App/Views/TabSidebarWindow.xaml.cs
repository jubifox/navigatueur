using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using Navigatueur.App.Animation;
using Navigatueur.App.Services;
using Navigatueur.App.ViewModels;

namespace Navigatueur.App.Views;

/// <summary>
/// The vertical tab strip, as a genuinely separate top-level window (owned by
/// MainWindow) instead of an element inside it — see the XAML comment for why.
/// Compact by default, expands on hover unless pinned (Zen-browser-style).
/// MainWindow keeps this window's position/size synced to its own; everything
/// about the hover/pin/width-animation behavior is self-contained here.
/// </summary>
public partial class TabSidebarWindow : Window
{
    // Also read by MainWindow to size the content area's left inset, so the
    // page sits flush against the sidebar in its docked (collapsed or pinned)
    // state instead of being permanently covered by it.
    internal const double CollapsedWidth = 52;
    internal const double ExpandedWidth = 220;

    private readonly MainWindowViewModel _viewModel;
    private Point? _tabDragStart;
    private BrowserTabViewModel? _tabDragSource;

    public TabSidebarWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();

        _viewModel = viewModel;
        DataContext = viewModel;
        Width = CollapsedWidth;

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    /// <summary>
    /// Sizes/positions the shared chrome background image so the slice visible
    /// in this narrow window lines up pixel-for-pixel with what MainWindow
    /// renders at the same screen position. MainWindow calls this whenever it
    /// repositions the sidebar (see RepositionTabSidebar) — <paramref name="virtualWidth"/>/
    /// <paramref name="virtualHeight"/> are MainWindow's own ActualWidth/ActualHeight
    /// (the box the image actually stretches to fill over there, not this
    /// window's much narrower one), and <paramref name="offsetY"/> is how far
    /// down MainWindow's content area starts (title bar + toolbar height).
    /// </summary>
    public void SyncBackgroundGeometry(double virtualWidth, double virtualHeight, double offsetY)
    {
        BackgroundImage.Width = virtualWidth;
        BackgroundImage.Height = virtualHeight;
        Canvas.SetTop(BackgroundImage, -offsetY);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.IsSidebarPinned))
        {
            AnimateWidth(_viewModel.IsSidebarPinned ? ExpandedWidth : CollapsedWidth);
        }
    }

    private void OnMouseEnter(object sender, MouseEventArgs e)
    {
        if (_viewModel.IsSidebarPinned)
        {
            return;
        }

        _viewModel.IsSidebarExpanded = true;
        AnimateWidth(ExpandedWidth);
    }

    private void OnMouseLeave(object sender, MouseEventArgs e)
    {
        if (_viewModel.IsSidebarPinned)
        {
            return;
        }

        _viewModel.IsSidebarExpanded = false;
        AnimateWidth(CollapsedWidth);
    }

    private void AnimateWidth(double toPixels)
    {
        var animation = new DoubleAnimation
        {
            To = toPixels,
            Duration = TimeSpan.FromMilliseconds(280),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut },
            FillBehavior = FillBehavior.Stop,
        };
        animation.Completed += (_, _) => Width = toPixels;
        BeginAnimation(WidthProperty, animation);
    }

    private CursorTrailTracker? _cursorTrailTracker;

    /// <summary>Same accent-colored trail as MainWindow's own chrome — was previously never drawn here at all.</summary>
    private void OnPreviewMouseMoveForTrail(object sender, MouseEventArgs e)
    {
        if (!AppServices.Theme.IsCursorTrailEnabled)
        {
            return;
        }

        _cursorTrailTracker ??= new CursorTrailTracker(CursorTrailCanvas);
        _cursorTrailTracker.OnMove(e.GetPosition(CursorTrailCanvas));
    }

    private void OnGroupNameEditPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        // Stops the click from bubbling into the group header Button, which would otherwise toggle collapse instead of placing the caret.
        e.Handled = true;
        ((TextBox)sender).Focus();
    }

    private void OnGroupNameEditKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && (sender as FrameworkElement)?.DataContext is Models.TabGroup group)
        {
            group.IsEditingName = false;
        }
    }

    private void OnGroupNameEditLostFocus(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is Models.TabGroup group)
        {
            group.IsEditingName = false;
        }
    }

    private void OnGroupNameEditIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not TextBox { IsVisible: true } textBox)
        {
            return;
        }

        // The context menu that triggered "Renommer..." restores focus to its
        // placement target (the group's Button) as it closes — if that lands
        // after a synchronous Focus() call here, it steals focus right back
        // and LostFocus immediately reverts the rename. Deferring past that
        // restoration (ApplicationIdle) makes ours win instead.
        Dispatcher.BeginInvoke(new Action(() =>
        {
            textBox.Focus();
            textBox.SelectAll();
        }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    }

    private void OnTabPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _tabDragStart = e.GetPosition(null);
        _tabDragSource = (sender as FrameworkElement)?.DataContext as BrowserTabViewModel;
    }

    private void OnTabPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _tabDragStart is not { } start || _tabDragSource is null)
        {
            return;
        }

        var current = e.GetPosition(null);
        if (Math.Abs(current.X - start.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(current.Y - start.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        DragDrop.DoDragDrop((DependencyObject)sender, _tabDragSource, DragDropEffects.Move);
        _tabDragStart = null;
        _tabDragSource = null;
    }

    private void OnTabDrop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(typeof(BrowserTabViewModel)))
        {
            return;
        }

        var source = (BrowserTabViewModel)e.Data.GetData(typeof(BrowserTabViewModel))!;
        if (sender is not FrameworkElement { DataContext: BrowserTabViewModel target } targetElement || source == target)
        {
            return;
        }

        // Dropping on the middle band of the target row groups the two tabs (Chrome/Edge-style).
        // Dropping in the top quarter inserts before the target, the bottom quarter after it —
        // previously any edge drop always inserted before, so there was no way to place a tab
        // as the last item, or precisely after a specific tab rather than before the next one.
        var dropY = e.GetPosition(targetElement).Y;
        var isCenterDrop = targetElement.ActualHeight > 0
            && dropY > targetElement.ActualHeight * 0.25
            && dropY < targetElement.ActualHeight * 0.75;

        if (isCenterDrop)
        {
            AppServices.TabManager.GroupTabs(source, target);
        }
        else
        {
            var insertAfter = targetElement.ActualHeight > 0 && dropY >= targetElement.ActualHeight * 0.75;
            AppServices.TabManager.ReorderTab(source, target, insertAfter);
        }
    }
}
