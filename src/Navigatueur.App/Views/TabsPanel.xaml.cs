using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using Navigatueur.App.Models;
using Navigatueur.App.Services;
using Navigatueur.App.Shell;
using Navigatueur.App.ViewModels;
using Navigatueur.Core.Settings;

namespace Navigatueur.App.Views;

/// <summary>
/// The tab column: open tabs and groups, drag-and-drop reordering, new tab,
/// saved groups. Like the toolbar it never decides its own visibility, but
/// reports when it must stay open (<see cref="IsHeld"/>): during a drag, a
/// resize, a rename, or while one of its menus is open.
/// </summary>
public partial class TabsPanel : UserControl, IShellPanel
{
    /// <summary>Where a dragged tab would land relative to the row under the cursor: "Before", "After", "Into" (group with it), or null.</summary>
    public static readonly DependencyProperty DropPositionProperty = DependencyProperty.RegisterAttached(
        "DropPosition", typeof(string), typeof(TabsPanel), new PropertyMetadata(null));

    public static readonly DependencyProperty IndicatorAlignmentProperty = DependencyProperty.Register(
        nameof(IndicatorAlignment), typeof(HorizontalAlignment), typeof(TabsPanel), new PropertyMetadata(HorizontalAlignment.Left));

    public static readonly DependencyProperty IndicatorMarginProperty = DependencyProperty.Register(
        nameof(IndicatorMargin), typeof(Thickness), typeof(TabsPanel), new PropertyMetadata(new Thickness(-6, 0, 0, 0)));

    private readonly IShellCommands _shell;
    private Point? _dragStart;
    private BrowserTabViewModel? _dragSource;
    private bool _isDragging;
    private bool _isResizing;
    private ContextMenu? _lastContextMenu;
    private (bool Floating, bool Effects, PanelSide Side, bool Dark)? _appliedChrome;

    public TabsPanel(IShellCommands shell)
    {
        InitializeComponent();
        _shell = shell;
        ContextMenuOpening += (_, e) => _lastContextMenu = (e.Source as FrameworkElement)?.ContextMenu;
        Body.Width = AppServices.Interface.Current.SidebarWidth;

        // Subscribed only while on screen, so a closed window's panel isn't kept alive by the app-wide service.
        Loaded += (_, _) =>
        {
            AppServices.Interface.Changed += OnInterfaceChanged;
            Body.Width = AppServices.Interface.Current.SidebarWidth;
        };
        Unloaded += (_, _) => AppServices.Interface.Changed -= OnInterfaceChanged;
    }

    private void OnInterfaceChanged(object? sender, EventArgs e) => Body.Width = AppServices.Interface.Current.SidebarWidth;

    public static string? GetDropPosition(DependencyObject element) => (string?)element.GetValue(DropPositionProperty);

    public static void SetDropPosition(DependencyObject element, string? value) => element.SetValue(DropPositionProperty, value);

    /// <summary>The active-tab marker sits on the column's outer edge — left or right depending on the side.</summary>
    public HorizontalAlignment IndicatorAlignment
    {
        get => (HorizontalAlignment)GetValue(IndicatorAlignmentProperty);
        set => SetValue(IndicatorAlignmentProperty, value);
    }

    public Thickness IndicatorMargin
    {
        get => (Thickness)GetValue(IndicatorMarginProperty);
        set => SetValue(IndicatorMarginProperty, value);
    }

    private MainWindowViewModel? ViewModel => DataContext as MainWindowViewModel;

    /// <summary>
    /// Focus only holds the column open while the keyboard is driving it
    /// (Ctrl+Maj+A then arrows); a row that merely got focus from a click
    /// mustn't pin it on screen.
    /// </summary>
    public bool IsHeld =>
        (IsKeyboardFocusWithin && InputManager.Current.MostRecentInputDevice is KeyboardDevice)
        || _isDragging
        || _isResizing
        || _lastContextMenu is { IsOpen: true }
        || SavedGroupsMenu.IsOpen
        || (ViewModel?.TabManager.Groups.Any(g => g.IsEditingName || g.IsPickingIcon) ?? false);

    public void ApplyChrome(bool floating, bool effects, PanelSide side, double shadowSize)
    {
        var state = (floating, effects, side, AppServices.Theme.IsDark);
        if (_appliedChrome == state)
        {
            return;
        }

        _appliedChrome = state;
        var left = side == PanelSide.Left;

        Body.HorizontalAlignment = left ? HorizontalAlignment.Left : HorizontalAlignment.Right;
        Body.BorderThickness = left ? new Thickness(0, 0, 1, 0) : new Thickness(1, 0, 0, 0);
        ResizeGrip.HorizontalAlignment = left ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        IndicatorAlignment = left ? HorizontalAlignment.Left : HorizontalAlignment.Right;
        IndicatorMargin = left ? new Thickness(-6, 0, 0, 0) : new Thickness(0, 0, -6, 0);

        if (floating && effects)
        {
            Body.CornerRadius = left ? new CornerRadius(0, 14, 14, 0) : new CornerRadius(14, 0, 0, 14);
            Body.Effect = new DropShadowEffect
            {
                BlurRadius = shadowSize,
                ShadowDepth = 3,
                Direction = left ? 0 : 180,
                Color = Colors.Black,
                Opacity = AppServices.Theme.IsDark ? 0.5 : 0.16,
            };
        }
        else
        {
            Body.CornerRadius = new CornerRadius(0);
            Body.Effect = null;
        }
    }

    public void PlaceWallpaper(Rect window)
    {
        // Relative to the layer, which sits inside Body (offset by any shadow margin).
        var origin = WallpaperLayer.IsLoaded ? WallpaperLayer.TranslatePoint(default, this) : default;
        Canvas.SetLeft(Wallpaper, window.X - origin.X);
        Canvas.SetTop(Wallpaper, window.Y - origin.Y);
        Wallpaper.Width = window.Width;
        Wallpaper.Height = window.Height;
    }

    /// <summary>Moves keyboard focus onto the active tab's row (Ctrl+Shift+A), so arrows/Enter/Delete work straight away.</summary>
    public void FocusActiveTab()
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            var active = ViewModel?.ActiveTab;
            if (active is null || TabsList.ItemContainerGenerator.ContainerFromItem(active) is not ContentPresenter container)
            {
                TabsList.Focus();
                return;
            }

            container.BringIntoView();
            if (VisualTreeHelper.GetChildrenCount(container) > 0 && VisualTreeHelper.GetChild(container, 0) is Button row)
            {
                row.Focus();
            }
        }), DispatcherPriority.Background);
    }

    private void OnCommandClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string commandId })
        {
            _shell.Execute(commandId);
        }
    }

    private void OnSavedGroupsClick(object sender, RoutedEventArgs e)
    {
        SavedGroupsMenu.PlacementTarget = SavedGroupsButton;
        SavedGroupsMenu.Placement = PlacementMode.Top;
        SavedGroupsMenu.IsOpen = true;
    }

    /// <summary>After picking a tab with the mouse, keyboard focus goes to its page (scrolling, typing), not to the row.</summary>
    private void OnTabRowClick(object sender, RoutedEventArgs e)
    {
        if (InputManager.Current.MostRecentInputDevice is MouseDevice)
        {
            Dispatcher.BeginInvoke(new Action(_shell.FocusPage), DispatcherPriority.Input);
        }
    }

    // ------------------------------------------------------------ keyboard

    private void OnTabPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not BrowserTabViewModel tab || ViewModel is null)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Delete:
                ViewModel.TabManager.CloseTab(tab);
                FocusActiveTab();
                e.Handled = true;
                break;

            case Key.Enter:
            case Key.Space:
                ViewModel.TabManager.ActivateTab(tab);
                _shell.FocusPage();
                _shell.Execute(ShellCommandIds.HideTabs);
                e.Handled = true;
                break;

            case Key.Escape:
                _shell.FocusPage();
                _shell.Execute(ShellCommandIds.HideTabs);
                e.Handled = true;
                break;
        }
    }

    /// <summary>Middle-click closes a tab, like in every browser.</summary>
    private void OnTabMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Middle &&
            (sender as FrameworkElement)?.DataContext is BrowserTabViewModel tab &&
            ViewModel is { } viewModel)
        {
            viewModel.TabManager.CloseTab(tab);
            e.Handled = true;
        }
    }

    // ------------------------------------------------------------- resize

    private void OnResizeStarted(object sender, DragStartedEventArgs e) => _isResizing = true;

    private void OnResizeDelta(object sender, DragDeltaEventArgs e)
    {
        var growsRight = AppServices.Interface.SidebarSide == PanelSide.Left;
        var change = growsRight ? e.HorizontalChange : -e.HorizontalChange;
        AppServices.Interface.Preview(s => s.SidebarWidth += change);
    }

    private void OnResizeCompleted(object sender, DragCompletedEventArgs e)
    {
        _isResizing = false;
        AppServices.Interface.Update(_ => { });
    }

    // -------------------------------------------------------- group rename

    private void OnGroupNameEditPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        // Keeps the click from reaching the group header button, which would toggle collapse instead of placing the caret.
        e.Handled = true;
        ((TextBox)sender).Focus();
    }

    private void OnGroupNameEditKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Enter or Key.Escape && (sender as FrameworkElement)?.DataContext is TabGroup group)
        {
            group.IsEditingName = false;
            e.Handled = true;
        }
    }

    private void OnGroupNameEditLostFocus(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is TabGroup group)
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

        // The context menu that triggered "Renommer…" hands focus back to its
        // target as it closes; deferring past that keeps the rename box focused.
        Dispatcher.BeginInvoke(new Action(() =>
        {
            textBox.Focus();
            textBox.SelectAll();
        }), DispatcherPriority.ApplicationIdle);
    }

    // ------------------------------------------------------- drag and drop

    private void OnTabPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(this);
        _dragSource = (sender as FrameworkElement)?.DataContext as BrowserTabViewModel;
    }

    private void OnTabPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragStart is not { } start || _dragSource is null)
        {
            return;
        }

        var current = e.GetPosition(this);
        if (Math.Abs(current.X - start.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(current.Y - start.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        var source = _dragSource;
        _dragStart = null;
        _dragSource = null;
        _isDragging = true;
        try
        {
            DragDrop.DoDragDrop((DependencyObject)sender, source, DragDropEffects.Move);
        }
        finally
        {
            _isDragging = false;
        }
    }

    /// <summary>
    /// Top quarter of a row: insert before it; bottom quarter: after it;
    /// the middle: group the two tabs together (Chrome/Edge-style).
    /// </summary>
    private static string ResolveDropPosition(FrameworkElement row, DragEventArgs e)
    {
        var y = e.GetPosition(row).Y;
        var height = row.ActualHeight;
        if (height <= 0 || y < height * 0.25)
        {
            return "Before";
        }

        return y >= height * 0.75 ? "After" : "Into";
    }

    private void OnTabDragOver(object sender, DragEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: BrowserTabViewModel target } row)
        {
            RefuseDrop(e);
            return;
        }

        string? position = null;
        if (e.Data.GetData(typeof(BrowserTabViewModel)) is BrowserTabViewModel source && source != target)
        {
            position = ResolveDropPosition(row, e);
        }
        else if (e.Data.GetData(typeof(TabGroup)) is TabGroup group)
        {
            position = ResolveGroupDropOnTab(group, target);
        }

        if (position is null)
        {
            SetDropPosition(row, null);
            RefuseDrop(e);
            return;
        }

        SetDropPosition(row, position);
        e.Effects = DragDropEffects.Move;
        e.Handled = true;
    }

    private static void RefuseDrop(DragEventArgs e)
    {
        e.Effects = DragDropEffects.None;
        e.Handled = true;
    }

    /// <summary>
    /// A dragged group can land after another group by being dropped on that
    /// group's last tab — the drop line then sits right where it will go.
    /// Anywhere else among the tabs would be ambiguous, so it's refused.
    /// </summary>
    private string? ResolveGroupDropOnTab(TabGroup dragged, BrowserTabViewModel target)
    {
        if (ViewModel is not { } viewModel || target.IsPinned || target.GroupId is not { } groupId || groupId == dragged.Id)
        {
            return null;
        }

        var lastMember = viewModel.TabManager.Tabs.LastOrDefault(t => !t.IsPinned && t.GroupId == groupId);
        return lastMember == target ? "After" : null;
    }

    // ------------------------------------------------------- group dragging

    private Point? _groupDragStart;
    private TabGroup? _groupDragSource;

    private void OnGroupPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _groupDragStart = e.GetPosition(this);
        _groupDragSource = (sender as FrameworkElement)?.DataContext as TabGroup;
    }

    private void OnGroupPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _groupDragStart is not { } start || _groupDragSource is null ||
            _groupDragSource.IsEditingName)
        {
            return;
        }

        var current = e.GetPosition(this);
        if (Math.Abs(current.X - start.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(current.Y - start.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        var source = _groupDragSource;
        _groupDragStart = null;
        _groupDragSource = null;
        _isDragging = true;
        try
        {
            DragDrop.DoDragDrop((DependencyObject)sender, new DataObject(typeof(TabGroup), source), DragDropEffects.Move);
        }
        finally
        {
            _isDragging = false;
        }
    }

    /// <summary>
    /// Over a group header: a dragged tab joins the group ("Into"); a dragged
    /// group goes before it — or after it, in the lower half of a collapsed
    /// group (an expanded one shows its tabs below: drop on its last tab instead).
    /// </summary>
    private string? ResolveDropOnGroupHeader(FrameworkElement header, TabGroup target, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(BrowserTabViewModel)) is BrowserTabViewModel tab)
        {
            return tab.GroupId == target.Id ? null : "Into";
        }

        if (e.Data.GetData(typeof(TabGroup)) is TabGroup group && group != target)
        {
            var lowerHalf = header.ActualHeight > 0 && e.GetPosition(header).Y >= header.ActualHeight / 2;
            return target.IsCollapsed && lowerHalf ? "After" : "Before";
        }

        return null;
    }

    private void OnGroupDragOver(object sender, DragEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: TabGroup target } header ||
            ResolveDropOnGroupHeader(header, target, e) is not { } position)
        {
            if (sender is DependencyObject element)
            {
                SetDropPosition(element, null);
            }

            RefuseDrop(e);
            return;
        }

        SetDropPosition(header, position);
        e.Effects = DragDropEffects.Move;
        e.Handled = true;
    }

    private void OnGroupDrop(object sender, DragEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: TabGroup target } header || ViewModel is not { } viewModel)
        {
            return;
        }

        var position = ResolveDropOnGroupHeader(header, target, e);
        SetDropPosition(header, null);

        if (position == "Into" && e.Data.GetData(typeof(BrowserTabViewModel)) is BrowserTabViewModel tab)
        {
            viewModel.TabManager.MoveTabToGroup(tab, target);
        }
        else if (position is "Before" or "After" && e.Data.GetData(typeof(TabGroup)) is TabGroup group)
        {
            viewModel.TabManager.MoveGroup(group, target, after: position == "After");
        }

        e.Handled = true;
    }

    private void OnTabDragLeave(object sender, DragEventArgs e)
    {
        if (sender is DependencyObject row)
        {
            SetDropPosition(row, null);
        }
    }

    private void OnTabDrop(object sender, DragEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: BrowserTabViewModel groupTarget } groupRow &&
            e.Data.GetData(typeof(TabGroup)) is TabGroup draggedGroup && ViewModel is { } groupViewModel)
        {
            SetDropPosition(groupRow, null);
            if (ResolveGroupDropOnTab(draggedGroup, groupTarget) is not null &&
                groupViewModel.TabManager.Groups.FirstOrDefault(g => g.Id == groupTarget.GroupId) is { } targetGroup)
            {
                groupViewModel.TabManager.MoveGroup(draggedGroup, targetGroup, after: true);
            }

            e.Handled = true;
            return;
        }

        if (sender is not FrameworkElement { DataContext: BrowserTabViewModel target } row ||
            e.Data.GetData(typeof(BrowserTabViewModel)) is not BrowserTabViewModel source ||
            source == target || ViewModel is not { } viewModel)
        {
            return;
        }

        var position = ResolveDropPosition(row, e);
        SetDropPosition(row, null);

        // This window's own tab manager — a secondary window must never reorder the primary window's tabs.
        if (position == "Into")
        {
            viewModel.TabManager.GroupTabs(source, target);
        }
        else
        {
            viewModel.TabManager.ReorderTab(source, target, insertAfter: position == "After");
        }

        e.Handled = true;
    }
}
