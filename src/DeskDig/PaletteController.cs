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
    private readonly SidePanelViewModel _sidePanelViewModel = new();
    private PaletteWindow? _window;
    private SidePanelWindow? _sidePanel;
    private SettingsWindow? _settingsWindow;
    private TrayIcon? _tray;
    private AudioDeviceService? _audio;
    private bool _audioRefreshQueued;
    private string? _lastGpu;
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
        _pinnedApps = LoadPinnedApps();
        foreach (var app in _pinnedApps) _icons.ForPinnedApp(app, 32);
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
        if (_sidePanel is { Handle: not 0 }) excluded.Add(_sidePanel.Handle);
        if (_settingsWindow is not null) excluded.Add(new System.Windows.Interop.WindowInteropHelper(_settingsWindow).Handle);

        var entries = _enumerator.Enumerate(excluded);
        var arranged = PaletteOrder.Arrange(entries, Favorites, Recent);
        var items = arranged.Flatten().Select(e => new WindowEntryItem(e, _icons.ForEntry(e, 32), e.IsMinimized ? _icons.ForEntry(e, 96) : null)
        {
            IsFavorite = Favorites.IsFavorite(e),
            HasPlacement = PlacementProfiles.HasProfileFor(e),
        }).ToList();

        _pinnedApps = LoadPinnedApps();
        var apps = _pinnedApps.Select(a => new PinnedAppItem(a, _icons.ForPinnedApp(a, 32))).ToList();

        _window.ViewModel.Load(items, arranged, apps);
        _window.ShowAt(anchor);
        // AlwaysShown なら ToolsPanel も同時に開く。そうでなければ閉じた状態から始まる
        if (Settings.AlwaysShown) OpenSidePanel(SidePanelKind.Tools);
        _outsideClicks.Arm();
        Diagnostics.Log($"Palette shown at ({anchor.X},{anchor.Y}) in {sw.ElapsedMilliseconds} ms ({entries.Count} windows, {apps.Count} pinned apps)");
    }

    public void HidePalette(PaletteCloseReason reason)
    {
        if (_window is null || !_window.IsVisible) return;
        Diagnostics.Log($"Palette hidden ({reason})");
        if (reason == PaletteCloseReason.OutsideClick) _lastOutsideClose = DateTime.UtcNow;
        _outsideClicks.Disarm();
        CloseSidePanel(); // Palette を閉じると SidePanel も閉じる
        _window.HidePalette();
    }

    // ---- SidePanel（ToolsPanel / InfoPanel）----

    public bool IsSidePanelOpen => _sidePanel is { IsVisible: true };

    /// <summary>同じ種類なら閉じ、違う種類なら切り替える。</summary>
    public void ToggleSidePanel(SidePanelKind kind)
    {
        if (IsSidePanelOpen && _sidePanelViewModel.Kind == kind) CloseSidePanel();
        else OpenSidePanel(kind);
    }

    public void OpenSidePanel(SidePanelKind kind)
    {
        if (_window is not { IsVisible: true } || kind == SidePanelKind.None) return;
        _sidePanel ??= new SidePanelWindow(this, _sidePanelViewModel, _window);
        _sidePanelViewModel.Kind = kind;
        if (kind == SidePanelKind.Tools)
        {
            _sidePanel.Sync(() => _sidePanelViewModel.AlwaysShown = Settings.AlwaysShown);
            _sidePanelViewModel.ShowSoundFallback = false;
            RefreshAudio();
        }
        else
        {
            LoadSystemSummary();
            _sidePanelViewModel.HotkeyText = Settings.HotkeyEnabled ? Settings.Hotkey : $"{Settings.Hotkey}（無効）";
            _sidePanelViewModel.TriggerText =
                $"DesktopDoubleClick: {(Settings.DesktopDoubleClickEnabled ? "有効" : "無効")}\nHotkey: {(Settings.HotkeyEnabled ? "有効" : "無効")}";
        }
        _sidePanel.ShowBeside(_window.Handle);
        _window.ViewModel.SidePanel = kind;
        Diagnostics.Log($"SidePanel opened ({kind})");
    }

    public void CloseSidePanel()
    {
        if (!IsSidePanelOpen) return;
        _sidePanel!.HidePanel();
        _sidePanelViewModel.Kind = SidePanelKind.None;
        if (_window is not null) _window.ViewModel.SidePanel = SidePanelKind.None;
    }

    /// <summary>ToolsPanel のピン型ボタン。設定として保存し、設定画面の行とも同じ値になる。</summary>
    public void SetAlwaysShown(bool value)
    {
        if (Settings.AlwaysShown == value) return;
        Settings.AlwaysShown = value;
        _store.SaveSettings(Settings);
        Diagnostics.Log($"AlwaysShown = {value}");
    }

    // ---- AudioControl ----

    private AudioDeviceService? Audio
    {
        get
        {
            if (_audio is not null) return _audio;
            try
            {
                _audio = new AudioDeviceService();
                _audio.DevicesChanged += OnAudioDevicesChanged;
            }
            catch (Exception ex)
            {
                Diagnostics.Log("AudioDeviceService init failed", ex);
            }
            return _audio;
        }
    }

    /// <summary>COM のワーカースレッドから来る。UI スレッドへ渡し、連続する通知は 1 回の更新にまとめる。</summary>
    private void OnAudioDevicesChanged()
    {
        if (_audioRefreshQueued) return;
        _audioRefreshQueued = true;
        _dispatcher.BeginInvoke(() =>
        {
            _audioRefreshQueued = false;
            if (IsSidePanelOpen && _sidePanelViewModel.Kind == SidePanelKind.Tools) RefreshAudio();
        });
    }

    private void RefreshAudio()
    {
        if (_sidePanel is null) return;
        var audio = Audio;
        _sidePanel.Sync(() =>
        {
            RefreshAudioSection(audio, EDataFlow.Render, _sidePanelViewModel.Output);
            RefreshAudioSection(audio, EDataFlow.Capture, _sidePanelViewModel.Input);
        });
    }

    private static void RefreshAudioSection(AudioDeviceService? audio, EDataFlow flow, AudioSectionViewModel section)
    {
        IReadOnlyList<(string, string, bool)> devices = [];
        AudioVolumeState? volume = null;
        if (audio is not null)
        {
            try
            {
                devices = audio.GetDevices(flow).Select(d => (d.Id, d.Name, d.IsDefault)).ToList();
                volume = audio.GetDefaultVolume(flow);
            }
            catch (Exception ex)
            {
                Diagnostics.Log($"Audio refresh failed ({flow})", ex);
            }
        }
        section.SetState(devices, volume is null ? null : volume.Level * 100.0, volume?.IsMuted);
    }

    /// <summary>既定のデバイス（出力・入力のどちらでも）を切り替える。Palette は閉じない。失敗したら通知し、ms-settings:sound への退避を出す。</summary>
    public void SetDefaultAudioDevice(string deviceId)
    {
        try
        {
            Audio?.SetDefaultDevice(deviceId);
            Diagnostics.Log($"Default audio device set to {deviceId}");
            RefreshAudio();
        }
        catch (Exception ex)
        {
            Diagnostics.Log("Default audio device switch failed", ex);
            _sidePanelViewModel.ShowSoundFallback = true;
            _tray?.ShowBalloon("DeskDig", "デバイスの切替に失敗しました。Windows のサウンド設定から切り替えてください。");
        }
    }

    public void SetAudioVolume(EDataFlow flow, double level)
    {
        try
        {
            Audio?.SetDefaultVolume(flow, (float)level);
        }
        catch (Exception ex)
        {
            Diagnostics.Log($"Volume change failed ({flow})", ex);
        }
    }

    public void SetAudioMuted(EDataFlow flow, bool muted)
    {
        try
        {
            Audio?.SetDefaultMuted(flow, muted);
        }
        catch (Exception ex)
        {
            Diagnostics.Log($"Mute change failed ({flow})", ex);
        }
    }

    /// <summary>既定の切替に失敗したときの退避。OS のサウンド設定を開き、Palette を閉じる。</summary>
    public void OpenSoundSettingsFallback()
    {
        try
        {
            Process.Start(new ProcessStartInfo("ms-settings:sound") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Diagnostics.Log("ms-settings:sound failed", ex);
        }
        HidePalette(PaletteCloseReason.Launched);
    }

    // ---- SystemSummary ----

    /// <summary>即時に取れる項目を先に出し、GPU はバックグラウンドで取得して後から埋める。</summary>
    private void LoadSystemSummary()
    {
        var quick = SystemSummaryReader.ReadQuick() with { Gpu = _lastGpu };
        _sidePanelViewModel.Summary = quick;
        Task.Run(SystemSummaryReader.ReadGpu).ContinueWith(t =>
        {
            _lastGpu = t.IsCompletedSuccessfully ? t.Result : SystemSummary.Unknown;
            if (_sidePanelViewModel.Summary is { } current) _sidePanelViewModel.Summary = current.WithGpu(_lastGpu);
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    public void CopySystemSummary()
    {
        var summary = _sidePanelViewModel.Summary;
        if (summary is null) return;
        try
        {
            Clipboard.SetDataObject(summary.ToPlainText(), copy: true);
        }
        catch (Exception ex)
        {
            Diagnostics.Log("Clipboard copy failed", ex);
            _tray?.ShowBalloon("DeskDig", "クリップボードへコピーできませんでした。");
        }
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

    /// <summary>SettingsShortcut の行き先（ms-settings: 等）をシェルで開き、Palette を閉じる。</summary>
    public void OpenSettingsShortcut(SettingsShortcut shortcut)
    {
        try
        {
            Process.Start(new ProcessStartInfo(shortcut.FileName, shortcut.Arguments ?? string.Empty) { UseShellExecute = true });
            Diagnostics.Log($"Opened settings shortcut {shortcut.Id} ({shortcut.Destination})");
        }
        catch (Exception ex)
        {
            Diagnostics.Log($"Settings shortcut {shortcut.Id} failed", ex);
            _tray?.ShowBalloon("DeskDig", $"{shortcut.Name} を開けません: {ex.Message}");
        }
        HidePalette(PaletteCloseReason.Launched);
    }

    /// <summary>Launcher のドラッグ並べ替えが有効か。タスクバーと同じ順に従う設定では無効。</summary>
    public bool LauncherReorderEnabled => Settings.LauncherOrder == LauncherOrderMode.Custom;

    /// <summary>タスクバーの固定状態を読み、設定に応じてタスクバー順のまま、または保存した独自の順に並べる。</summary>
    private IReadOnlyList<PinnedApp> LoadPinnedApps()
    {
        var current = PinnedAppReader.ReadAll();
        return LauncherReorderEnabled ? PinnedAppOrder.Arrange(_store.LoadPinnedAppOrder(), current) : current;
    }

    public void MovePinnedApp(string movingId, string targetId)
    {
        if (!LauncherReorderEnabled) return;
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
        var sidePanel = vm.SidePanel;
        vm.Load(items, arranged, _pinnedApps.Select(a => apps.FirstOrDefault(x => x.App.Id == a.Id) ?? new PinnedAppItem(a, _icons.ForPinnedApp(a, 32))));
        vm.FilterText = filter;
        vm.SidePanel = sidePanel;
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
        if (_audio is not null)
        {
            _audio.DevicesChanged -= OnAudioDevicesChanged;
            _audio.Dispose();
            _audio = null;
        }
        _sidePanel?.HidePanel();
        _sidePanel?.Close();
        _sidePanel = null;
        _window?.HidePalette();
        _window?.Close();
        _window = null;
        _tray?.Dispose();
        _tray = null;
    }
}
