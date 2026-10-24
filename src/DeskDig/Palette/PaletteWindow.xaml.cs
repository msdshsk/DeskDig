using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using DeskDig.Core;
using DeskDig.Core.Settings;
using DeskDig.Interop;
using static DeskDig.Interop.NativeMethods;

namespace DeskDig.Palette;

internal enum PaletteCloseReason { Activated, Launched, OutsideClick, Escape, TriggerToggle, Programmatic }

internal sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

public partial class PaletteWindow : Window
{
    private const double TileCellWidth = 248;   // 240 + Margin 4*2
    private const double LauncherCellWidth = 44; // 40 + Margin 2*2
    private const double FramePadding = 8 * 2 + 1 * 2 + 4 * 2;
    private const double ListWidth = 560;
    private const double PaletteHeight = 680;

    private readonly PaletteController _controller;
    private readonly PaletteViewModel _viewModel = new();
    private PaletteViewMode _mode = PaletteViewMode.Grid;
    private int _columns = 4;
    private bool _suppressDeactivate;
    private Point _dragStart;
    private PinnedAppItem? _dragCandidate;
    private nint _hwnd;

    internal PaletteWindow(PaletteController controller)
    {
        _controller = controller;
        InitializeComponent();
        DataContext = _viewModel;
        SourceInitialized += OnSourceInitialized;
        Deactivated += OnDeactivated;
        PreviewKeyDown += OnPreviewKeyDown;
    }

    internal PaletteViewModel ViewModel => _viewModel;
    internal nint Handle => _hwnd;

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _hwnd = new WindowInteropHelper(this).Handle;
        long exStyle = GetWindowLongPtr(_hwnd, GWL_EXSTYLE);
        SetWindowLongPtr(_hwnd, GWL_EXSTYLE, (nint)(exStyle | WS_EX_TOOLWINDOW)); // Alt+Tab に出さない
        int corner = DWMWCP_ROUND;
        DwmSetWindowAttribute(_hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));
    }

    /// <summary>設定に従って表示形式を適用する。</summary>
    internal void ApplyViewMode(PaletteViewMode mode, int columns)
    {
        _mode = mode;
        _columns = Math.Max(1, columns);
        var template = (DataTemplate)FindResource(mode == PaletteViewMode.Grid ? "GridTile" : "ListRow");
        var panel = (ItemsPanelTemplate)FindResource(mode == PaletteViewMode.Grid ? "GridPanel" : "ListPanel");
        foreach (var list in new[] { FavoriteList, CurrentList, OtherList })
        {
            list.ItemTemplate = template;
            list.ItemsPanel = panel;
        }
    }

    /// <summary>DIP 単位の希望サイズ。</summary>
    internal Size DesiredPaletteSize() => _mode == PaletteViewMode.Grid
        ? new Size(_columns * TileCellWidth + FramePadding + 18, PaletteHeight)
        : new Size(ListWidth, PaletteHeight);

    /// <summary>発火位置の近傍、同じモニタ内に収めて表示し、前面に出す。</summary>
    internal void ShowAt(ScreenPoint anchor)
    {
        var monitor = MonitorQuery.AtPoint(anchor);
        var size = DesiredPaletteSize();
        double scale = monitor?.Scale ?? 1.0;
        var work = monitor?.WorkArea ?? new Bounds(anchor.X, anchor.Y, (int)size.Width, (int)size.Height);
        var bounds = PalettePositioning.Resolve(anchor, (int)Math.Round(size.Width * scale), (int)Math.Round(size.Height * scale), work);

        // WPF は Left/Top をウインドウの現在の DPI で換算するので、その DPI 相当の DIP を与えてから表示する
        double currentScale = _hwnd != 0 ? GetDpiForWindow(_hwnd) / 96.0 : scale;
        if (currentScale <= 0) currentScale = scale;
        Left = bounds.Left / currentScale;
        Top = bounds.Top / currentScale;
        Width = size.Width;
        Height = size.Height;
        Show();
        SetWindowPos(_hwnd, HWND_TOPMOST, bounds.Left, bounds.Top, bounds.Width, bounds.Height, SWP_SHOWWINDOW | SWP_NOACTIVATE);
        // DPI が変わった場合に備えてもう一度（WPF が WM_DPICHANGED でサイズを変えるため）
        Dispatcher.BeginInvoke(() =>
        {
            if (IsVisible) SetWindowPos(_hwnd, HWND_TOPMOST, bounds.Left, bounds.Top, bounds.Width, bounds.Height, SWP_NOACTIVATE);
        }, System.Windows.Threading.DispatcherPriority.Loaded);

        ForegroundAccess.BringToForeground(_hwnd);
        Activate();
        EntryScroller.ScrollToTop();
        FilterBox.Focus();
        Keyboard.Focus(FilterBox);
    }

    internal void HidePalette()
    {
        _suppressDeactivate = true;
        try
        {
            Hide();
            _viewModel.Clear();
        }
        finally
        {
            _suppressDeactivate = false;
        }
    }

    private void OnDeactivated(object? sender, EventArgs e)
    {
        if (_suppressDeactivate || !IsVisible) return;
        _controller.HidePalette(PaletteCloseReason.OutsideClick);
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                _controller.HidePalette(PaletteCloseReason.Escape);
                e.Handled = true;
                break;
            case Key.Enter:
                ActivateSelection();
                e.Handled = true;
                break;
            case Key.Tab:
                _viewModel.ToggleRegion();
                e.Handled = true;
                break;
            case Key.Left: Move(-1, 0); e.Handled = true; break;
            case Key.Right: Move(1, 0); e.Handled = true; break;
            case Key.Up: Move(0, -1); e.Handled = true; break;
            case Key.Down: Move(0, 1); e.Handled = true; break;
        }
    }

    private void Move(int dx, int dy)
    {
        int entryColumns = _mode == PaletteViewMode.Grid ? Math.Max(1, (int)(FavoriteList.ActualWidth / TileCellWidth)) : 1;
        if (_mode == PaletteViewMode.List && dx != 0)
        {
            dy = dx; // 縦リストでは左右も上下として扱う
            dx = 0;
        }
        int launcherColumns = Math.Max(1, (int)(LauncherList.ActualWidth / LauncherCellWidth));
        _viewModel.MoveSelection(dx, dy, entryColumns, launcherColumns);
        ScrollSelectionIntoView();
    }

    private void ScrollSelectionIntoView()
    {
        var selected = _viewModel.SelectedEntry;
        if (selected is null) return;
        foreach (var list in new[] { FavoriteList, CurrentList, OtherList })
        {
            if (list.ItemContainerGenerator.ContainerFromItem(selected) is FrameworkElement element)
            {
                element.BringIntoView();
                return;
            }
        }
    }

    private void ActivateSelection()
    {
        if (_viewModel.Region == SelectionRegion.Launcher)
        {
            var app = _viewModel.SelectedPinnedApp ?? _viewModel.LauncherItems.FirstOrDefault();
            if (app is not null) _controller.LaunchOrActivate(app.App, _viewModel.VisibleEntries);
            return;
        }
        var entry = _viewModel.SelectedEntry ?? _viewModel.VisibleEntryItems().FirstOrDefault();
        if (entry is not null) _controller.ActivateEntry(entry.Entry);
        else if (_viewModel.LauncherItems.FirstOrDefault() is { } first) _controller.LaunchOrActivate(first.App, _viewModel.VisibleEntries);
    }

    // ---- WindowEntry ----

    private static WindowEntryItem? EntryOf(object sender) => (sender as FrameworkElement)?.DataContext as WindowEntryItem;

    private void Entry_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (EntryOf(sender) is { } item)
        {
            _controller.ActivateEntry(item.Entry);
            e.Handled = true;
        }
    }

    private void Entry_MouseEnter(object sender, MouseEventArgs e)
    {
        if (EntryOf(sender) is { } item) _viewModel.SelectEntry(item);
    }

    private void Entry_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (EntryOf(sender) is { } item)
        {
            _viewModel.SelectEntry(item);
            _controller.RefreshEntryState(item);
        }
    }

    private void ToggleFavorite_Click(object sender, RoutedEventArgs e)
    {
        if (EntryOf(sender) is { } item) _controller.ToggleFavorite(item);
    }

    private void RecordPlacement_Click(object sender, RoutedEventArgs e)
    {
        if (EntryOf(sender) is { } item) _controller.RecordPlacement(item);
    }

    private void RemovePlacement_Click(object sender, RoutedEventArgs e)
    {
        if (EntryOf(sender) is { } item) _controller.RemovePlacement(item);
    }

    // ---- Launcher ----

    private static PinnedAppItem? AppOf(object sender) => (sender as FrameworkElement)?.DataContext as PinnedAppItem;

    private void PinnedApp_MouseEnter(object sender, MouseEventArgs e)
    {
        if (AppOf(sender) is { } item) _viewModel.SelectApp(item);
    }

    private void PinnedApp_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(this);
        _dragCandidate = AppOf(sender);
    }

    private void PinnedApp_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragCandidate is null || e.LeftButton != MouseButtonState.Pressed) return;
        var delta = e.GetPosition(this) - _dragStart;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        var moving = _dragCandidate;
        _dragCandidate = null;
        _suppressDeactivate = true;
        try
        {
            DragDrop.DoDragDrop((DependencyObject)sender, new DataObject(typeof(PinnedAppItem), moving), DragDropEffects.Move);
        }
        finally
        {
            _suppressDeactivate = false;
        }
    }

    private void PinnedApp_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        bool wasClick = _dragCandidate is not null;
        _dragCandidate = null;
        if (wasClick && AppOf(sender) is { } item)
        {
            _controller.LaunchOrActivate(item.App, _viewModel.VisibleEntries);
            e.Handled = true;
        }
    }

    private void PinnedApp_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(typeof(PinnedAppItem)) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void PinnedApp_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(PinnedAppItem)) is PinnedAppItem moving && AppOf(sender) is { } target)
        {
            _controller.MovePinnedApp(moving.App.Id, target.App.Id);
            e.Handled = true;
        }
    }
}
