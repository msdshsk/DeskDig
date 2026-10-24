using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using DeskDig.Core;
using DeskDig.Core.Settings;
using DeskDig.Interop;
using DeskDig.Palette;
using DeskDig.Persistence;
using DeskDig.Settings;
using DeskDig.Tray;
using DeskDig.Triggers;

namespace DeskDig;

/// <summary>Trigger・Palette・永続状態・常駐を結ぶ。UI スレッド上で動く。</summary>
internal sealed class PaletteController : IDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly StateStore _store;
    private readonly VirtualDesktopQuery _desktops = new();
    private readonly ProcessInfo _processes = new();
    private readonly WindowEnumerator _enumerator;
    private readonly IconProvider _icons = new();
    private readonly List<ITriggerSource> _triggers = new();
    private readonly MessageWindow _messageWindow;
    private readonly uint _showMessage;
    private readonly OutsideClickMonitor _outsideClicks = new();
    private PaletteWindow? _window;
    private SettingsWindow? _settingsWindow;
    private TrayIcon? _tray;
    private DateTime _lastOutsideClose = DateTime.MinValue;
    private IReadOnlyList<PinnedApp> _pinnedApps = [];

    public PaletteSettings Settings { get; private set; }
    public Favorites Favorites { get; private set; }
    public PlacementProfiles PlacementProfiles { get; private set; }
    public RecentActivation Recent { get; } = new();

    public PaletteController(Dispatcher dispatcher, StateStore store, MessageWindow messageWindow, uint showMessage)
    {
        _dispatcher = dispatcher;
        _store = store;
        _messageWindow = messageWindow;
        _showMessage = showMessage;
        _enumerator = new WindowEnumerator(_desktops, _processes);
        Settings = store.LoadSettings();
        // 自動起動の実態はレジストリにある（Windows の設定画面やインストーラーからも変わる）ので、起動時にそちらへ合わせる
        Settings.AutoStart = AutoStartRegistration.IsEffectivelyEnabled();
        Favorites = store.LoadFavorites();
        PlacementProfiles = store.LoadPlacementProfiles();
        _messageWindow.MessageReceived += OnMessage;
    }

    public void Start()
    {
        _tray = new TrayIcon();
        _tray.OpenSettingsRequested += OpenSettings;
        _tray.ExitRequested += Exit;
        _outsideClicks.ClickedOutside += () => _dispatcher.BeginInvoke(() => HidePalette(PaletteCloseReason.OutsideClick));
        _outsideClicks.Start();
        RepairAutoStartPath();
        StartTriggers();
        _dispatcher.BeginInvoke(WarmUp, DispatcherPriority.ApplicationIdle);
    }

    /// <summary>自動起動がオンのまま exe の場所が変わっていたら（ポータブル版の移動など）、登録を現在の場所で書き直す。</summary>
    private void RepairAutoStartPath()
    {
        if (!Settings.AutoStart || AutoStartRegistration.PointsToCurrentExecutable()) return;
        try
        {
            AutoStartRegistration.Apply(true);
            Diagnostics.Log("Auto-start registration repaired to current executable path");
        }
        catch (Exception ex)
        {
            Diagnostics.Log("Auto-start repair failed", ex);
        }
    }

    /// <summary>初回表示の遅延を避けるため、ウインドウ生成とアイコン読み込みを前倒しする。</summary>
    private void WarmUp()
    {
        var sw = Stopwatch.StartNew();
        _window ??= new PaletteWindow(this);
        new System.Windows.Interop.WindowInteropHelper(_window).EnsureHandle();
        _pinnedApps = PinnedAppOrder.Arrange(_store.LoadPinnedAppOrder(), PinnedAppReader.ReadAll());
        foreach (var app in _pinnedApps) _icons.ForPinnedApp(PinnedAppReader.ShortcutPathOf(app), 32);
        foreach (var entry in _enumerator.Enumerate(new HashSet<nint>())) _icons.ForEntry(entry, 32);
        Diagnostics.Log($"Warm-up done in {sw.ElapsedMilliseconds} ms");
    }

    private void OnMessage(int msg, nint wParam, nint lParam)
    {
        if ((uint)msg == _showMessage) Toggle(MonitorQuery.CursorPosition());
    }

    // ---- Trigger ----

    private void StartTriggers()
    {
        StopTriggers();
        if (Settings.DesktopDoubleClickEnabled) _triggers.Add(new DesktopDoubleClickTriggerSource());
        if (Settings.HotkeyEnabled)
        {
            if (HotkeyGesture.TryParse(Settings.Hotkey, out var gesture)) _triggers.Add(new HotkeyTriggerSource(_messageWindow, gesture));
            else _tray?.ShowBalloon("DeskDig", $"ホットキー \"{Settings.Hotkey}\" を解釈できません。");
        }
        foreach (var trigger in _triggers)
        {
            trigger.Fired += OnTriggerFired;
            trigger.Start();
            if (trigger is HotkeyTriggerSource { RegistrationFailed: true })
                _tray?.ShowBalloon("DeskDig", $"ホットキー \"{Settings.Hotkey}\" を登録できません（他のアプリが使用中）。");
        }
    }

    private void StopTriggers()
    {
        foreach (var trigger in _triggers)
        {
            trigger.Fired -= OnTriggerFired;
            trigger.Dispose();
        }
        _triggers.Clear();
    }

    private void OnTriggerFired(ScreenPoint point)
    {
        Diagnostics.Log($"Trigger fired at ({point.X},{point.Y})");
        _dispatcher.BeginInvoke(() => Toggle(point));
    }

    // ---- Palette の表示・終了 ----

    public void Toggle(ScreenPoint anchor)
    {
        if (_window is { IsVisible: true })
        {
            HidePalette(PaletteCloseReason.TriggerToggle);
            return;
        }
        // 外クリックで閉じた直後のダブルクリック 2 打目で再び開かないようにする
        if ((DateTime.UtcNow - _lastOutsideClose).TotalMilliseconds < NativeMethods.GetDoubleClickTime()) return;
        ShowPalette(anchor);
    }

    public void ShowPalette(ScreenPoint anchor)
    {
        var sw = Stopwatch.StartNew();
        _window ??= new PaletteWindow(this);
        _window.ApplyViewMode(Settings.ViewMode, Settings.GridColumns);

        var excluded = new HashSet<nint>();
        if (_window.Handle != 0) excluded.Add(_window.Handle);
        if (_settingsWindow is not null) excluded.Add(new System.Windows.Interop.WindowInteropHelper(_settingsWindow).Handle);

        var entries = _enumerator.Enumerate(excluded);
        var arranged = PaletteOrder.Arrange(entries, Favorites, Recent);
        var items = arranged.Flatten().Select(e => new WindowEntryItem(e, _icons.ForEntry(e, 32), e.IsMinimized ? _icons.ForEntry(e, 96) : null)
        {
            IsFavorite = Favorites.IsFavorite(e),
            HasPlacement = PlacementProfiles.HasProfileFor(e),
        }).ToList();

        _pinnedApps = PinnedAppOrder.Arrange(_store.LoadPinnedAppOrder(), PinnedAppReader.ReadAll());
        var apps = _pinnedApps.Select(a => new PinnedAppItem(a, _icons.ForPinnedApp(PinnedAppReader.ShortcutPathOf(a), 32))).ToList();

        _window.ViewModel.Load(items, arranged, apps);
        _window.ShowAt(anchor);
        _outsideClicks.Arm();
        Diagnostics.Log($"Palette shown at ({anchor.X},{anchor.Y}) in {sw.ElapsedMilliseconds} ms ({entries.Count} windows, {apps.Count} pinned apps)");
    }

    public void HidePalette(PaletteCloseReason reason)
    {
        if (_window is null || !_window.IsVisible) return;
        Diagnostics.Log($"Palette hidden ({reason})");
        if (reason == PaletteCloseReason.OutsideClick) _lastOutsideClose = DateTime.UtcNow;
        _outsideClicks.Disarm();
        _window.HidePalette();
    }

    // ---- Activate / Launcher ----

    public void ActivateEntry(WindowEntry entry)
    {
        var profile = PlacementProfiles.FindFor(entry);
        bool ok = WindowActivator.Activate(entry.Id, profile?.Placement, MonitorQuery.All());
        if (ok) Recent.Record(entry.Id);
        HidePalette(PaletteCloseReason.Activated);
    }

    public void LaunchOrActivate(PinnedApp app, ArrangedWindowEntries visible)
    {
        var existing = Launcher.ResolveExistingWindow(app, visible);
        if (existing is not null)
        {
            ActivateEntry(existing);
            return;
        }
        try
        {
            PinnedAppReader.Launch(app);
        }
        catch (Exception ex)
        {
            _tray?.ShowBalloon("DeskDig", $"{app.Name} を起動できません: {ex.Message}");
        }
        HidePalette(PaletteCloseReason.Launched);
    }

    public void MovePinnedApp(string movingId, string targetId)
    {
        _pinnedApps = PinnedAppOrder.Move(_pinnedApps, movingId, targetId);
        _store.SavePinnedAppOrder(_pinnedApps.Select(a => a.Id));
        _window?.ViewModel.ReorderLauncher(_pinnedApps);
    }

    // ---- Favorite / Placement ----

    public void RefreshEntryState(WindowEntryItem item)
    {
        item.IsFavorite = Favorites.IsFavorite(item.Entry);
        item.HasPlacement = PlacementProfiles.HasProfileFor(item.Entry);
    }

    public void ToggleFavorite(WindowEntryItem item)
    {
        var existing = Favorites.FavoriteFor(item.Entry);
        if (existing is not null) Favorites.Remove(existing.Matcher);
        else Favorites.Add(item.Entry.DefaultMatcher);
        _store.SaveFavorites(Favorites);
        RefreshEntryState(item);
        RearrangeOpenPalette();
    }

    public void RecordPlacement(WindowEntryItem item)
    {
        var placement = WindowGeometry.Read(item.Entry.Id);
        if (placement is null) return;
        // 既にタイトル条件付きの規則が一致していればそれを上書き、無ければ実行ファイルのみの規則に記録する
        var matcher = PlacementProfiles.FindFor(item.Entry)?.Matcher ?? item.Entry.DefaultMatcher;
        PlacementProfiles.Record(matcher, placement);
        _store.SavePlacementProfiles(PlacementProfiles);
        RefreshEntryState(item);
    }

    public void RemovePlacement(WindowEntryItem item)
    {
        var profile = PlacementProfiles.FindFor(item.Entry);
        if (profile is null) return;
        PlacementProfiles.Remove(profile.Matcher);
        _store.SavePlacementProfiles(PlacementProfiles);
        RefreshEntryState(item);
    }

    private void RearrangeOpenPalette()
    {
        if (_window is not { IsVisible: true }) return;
        var vm = _window.ViewModel;
        var items = vm.VisibleEntryItems().Concat(AllLoadedItems(vm)).DistinctBy(i => i.Entry.Id).ToList();
        var arranged = PaletteOrder.Arrange(items.Select(i => i.Entry), Favorites, Recent);
        var apps = vm.LauncherItems.ToList();
        var filter = vm.FilterText;
        vm.Load(items, arranged, _pinnedApps.Select(a => apps.FirstOrDefault(x => x.App.Id == a.Id) ?? new PinnedAppItem(a, _icons.ForPinnedApp(PinnedAppReader.ShortcutPathOf(a), 32))));
        vm.FilterText = filter;
    }

    private static IEnumerable<WindowEntryItem> AllLoadedItems(PaletteViewModel vm) => vm.VisibleEntryItems();

    // ---- 設定 ----

    public void OpenSettings()
    {
        HidePalette(PaletteCloseReason.Programmatic);
        if (_settingsWindow is { IsLoaded: true })
        {
            _settingsWindow.Activate();
            return;
        }
        _settingsWindow = new SettingsWindow(this);
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    public void ApplySettings(PaletteSettings settings, PlacementProfiles profiles, Favorites favorites)
    {
        Settings = settings;
        PlacementProfiles = profiles;
        Favorites = favorites;
        _store.SaveSettings(settings);
        _store.SavePlacementProfiles(profiles);
        _store.SaveFavorites(favorites);
        try
        {
            AutoStartRegistration.Apply(settings.AutoStart);
        }
        catch (Exception ex)
        {
            _tray?.ShowBalloon("DeskDig", $"自動起動の設定に失敗しました: {ex.Message}");
        }
        StartTriggers();
    }

    public void Exit()
    {
        Dispose();
        Application.Current.Shutdown();
    }

    public void Dispose()
    {
        StopTriggers();
        _outsideClicks.Dispose();
        _messageWindow.MessageReceived -= OnMessage;
        _window?.HidePalette();
        _window?.Close();
        _window = null;
        _tray?.Dispose();
        _tray = null;
    }
}
