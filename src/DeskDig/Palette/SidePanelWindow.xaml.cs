using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using DeskDig.Core;
using DeskDig.Interop;
using static DeskDig.Interop.NativeMethods;

namespace DeskDig.Palette;

/// <summary>AlwaysShown のオン/オフをピン型の記号に変える。</summary>
internal sealed class AlwaysShownGlyphConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? "" /* Pinned */ : "" /* Pin */;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>
/// SidePanel（ToolsPanel / InfoPanel）。Palette と同じ高さで横に出る別ウインドウ。
/// WS_EX_NOACTIVATE にして、内側をクリックしても Palette からフォーカスを奪わない（Palette の Deactivated を起こさない）。
/// キー入力は Palette 側に留まるので、Esc の二段階は PaletteWindow が扱う。
/// </summary>
public partial class SidePanelWindow : Window
{
    private const double PanelWidth = 320;
    private const int GapPx = 8;

    private readonly PaletteController _controller;
    private readonly SidePanelViewModel _viewModel;
    private nint _hwnd;
    private bool _syncing;

    internal SidePanelWindow(PaletteController controller, SidePanelViewModel viewModel, Window owner)
    {
        _controller = controller;
        _viewModel = viewModel;
        InitializeComponent();
        DataContext = _viewModel;
        Owner = owner;
        SourceInitialized += OnSourceInitialized;
    }

    internal nint Handle => _hwnd;

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _hwnd = new WindowInteropHelper(this).Handle;
        long exStyle = GetWindowLongPtr(_hwnd, GWL_EXSTYLE);
        SetWindowLongPtr(_hwnd, GWL_EXSTYLE, (nint)(exStyle | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE));
        int corner = DWMWCP_ROUND;
        DwmSetWindowAttribute(_hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));
    }

    /// <summary>Palette の物理矩形の横に、同じ高さで表示する。Palette は動かさない。</summary>
    internal void ShowBeside(nint paletteHwnd)
    {
        if (!GetWindowRect(paletteHwnd, out var rect)) return;
        var palette = MonitorQuery.ToBounds(rect);
        var monitor = MonitorQuery.OfWindow(paletteHwnd);
        double scale = monitor?.Scale ?? 1.0;
        var work = monitor?.WorkArea ?? palette;
        var bounds = SidePanelPlacement.Resolve(palette, (int)Math.Round(PanelWidth * scale), GapPx, work);

        new WindowInteropHelper(this).EnsureHandle();
        double currentScale = _hwnd != 0 ? GetDpiForWindow(_hwnd) / 96.0 : scale;
        if (currentScale <= 0) currentScale = scale;
        Left = bounds.Left / currentScale;
        Top = bounds.Top / currentScale;
        Width = bounds.Width / currentScale;
        Height = bounds.Height / currentScale;
        Show();
        SetWindowPos(_hwnd, HWND_TOPMOST, bounds.Left, bounds.Top, bounds.Width, bounds.Height, SWP_SHOWWINDOW | SWP_NOACTIVATE);
        Dispatcher.BeginInvoke(() =>
        {
            if (IsVisible) SetWindowPos(_hwnd, HWND_TOPMOST, bounds.Left, bounds.Top, bounds.Width, bounds.Height, SWP_NOACTIVATE);
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    internal void HidePanel()
    {
        if (IsVisible) Hide();
    }

    /// <summary>デバイス側から来た値を UI に写す間、スライダーやトグルの変更イベントをデバイスへ書き戻さない。</summary>
    internal void Sync(Action apply)
    {
        _syncing = true;
        try
        {
            apply();
        }
        finally
        {
            _syncing = false;
        }
    }

    // ---- ToolsPanel ----

    private void AlwaysShown_Changed(object sender, RoutedEventArgs e)
    {
        if (_syncing) return;
        _controller.SetAlwaysShown(_viewModel.AlwaysShown);
    }

    private void Shortcut_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is SettingsShortcutItem item) _controller.OpenSettingsShortcut(item.Shortcut);
    }

    private void Device_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is AudioDeviceItem item)
        {
            _controller.SetDefaultAudioDevice(item.Id);
            e.Handled = true;
        }
    }

    private void Volume_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_syncing || !IsLoaded) return;
        _controller.SetAudioVolume(e.NewValue / 100.0);
    }

    private void Mute_Changed(object sender, RoutedEventArgs e)
    {
        if (_syncing || !IsLoaded) return;
        _controller.SetAudioMuted(_viewModel.IsMuted);
    }

    private void SoundFallback_Click(object sender, RoutedEventArgs e) => _controller.OpenSoundSettingsFallback();

    // ---- InfoPanel ----

    private void CopySummary_Click(object sender, RoutedEventArgs e) => _controller.CopySystemSummary();

    private void OpenSettings_Click(object sender, RoutedEventArgs e) => _controller.OpenSettings();
}
