using System.Collections.ObjectModel;
using DeskDig.Core;

namespace DeskDig.Palette;

/// <summary>SidePanel の種類。同時に開くのは一方だけ。</summary>
internal enum SidePanelKind { None, Tools, Info }

/// <summary>ToolsPanel 上段の SettingsShortcut 1 件。</summary>
internal sealed class SettingsShortcutItem(SettingsShortcut shortcut)
{
    public SettingsShortcut Shortcut { get; } = shortcut;
    public string Name => Shortcut.Name;
    public string Glyph => Shortcut.Glyph;
}

/// <summary>AudioControl の出力デバイス 1 件。</summary>
internal sealed class AudioDeviceItem(string id, string name, bool isDefault) : ObservableObject
{
    private bool _isDefault = isDefault;
    public string Id { get; } = id;
    public string Name { get; } = name;
    public bool IsDefault { get => _isDefault; set => Set(ref _isDefault, value); }
}

/// <summary>SystemSummary の 1 行。</summary>
internal sealed record SummaryLine(string Label, string Value);

/// <summary>SidePanel（ToolsPanel / InfoPanel）の表示状態。Palette とは別に、開いている間だけ更新される。</summary>
internal sealed class SidePanelViewModel : ObservableObject
{
    private SidePanelKind _kind = SidePanelKind.None;
    private bool _alwaysShown;
    private double _volume;
    private bool _isMuted;
    private bool _hasDefaultDevice;
    private bool _showSoundFallback;
    private string _hotkeyText = string.Empty;
    private string _triggerText = string.Empty;
    private SystemSummary? _summary;

    public SidePanelViewModel()
    {
        foreach (var shortcut in SettingsShortcutCatalog.All) Shortcuts.Add(new SettingsShortcutItem(shortcut));
    }

    public SidePanelKind Kind
    {
        get => _kind;
        set
        {
            if (!Set(ref _kind, value)) return;
            Raise(nameof(IsTools));
            Raise(nameof(IsInfo));
            Raise(nameof(Title));
        }
    }

    public bool IsTools => Kind == SidePanelKind.Tools;
    public bool IsInfo => Kind == SidePanelKind.Info;
    public string Title => Kind == SidePanelKind.Info ? "InfoPanel" : "ToolsPanel";

    // ---- ToolsPanel ----

    public ObservableCollection<SettingsShortcutItem> Shortcuts { get; } = new();

    /// <summary>AlwaysShown。ピン型ボタンと双方向に結び、変更は Controller が設定へ保存する。</summary>
    public bool AlwaysShown { get => _alwaysShown; set => Set(ref _alwaysShown, value); }

    public ObservableCollection<AudioDeviceItem> Devices { get; } = new();
    public bool HasDevices => Devices.Count > 0;

    /// <summary>0..100。スライダーに結ぶ。</summary>
    public double Volume { get => _volume; set => Set(ref _volume, value); }
    public bool IsMuted
    {
        get => _isMuted;
        set { if (Set(ref _isMuted, value)) Raise(nameof(MuteGlyph)); }
    }
    public string MuteGlyph => IsMuted ? "" : "";
    public bool HasDefaultDevice { get => _hasDefaultDevice; set => Set(ref _hasDefaultDevice, value); }

    /// <summary>既定の切替に失敗したあと、ms-settings:sound への退避ボタンを出す。</summary>
    public bool ShowSoundFallback { get => _showSoundFallback; set => Set(ref _showSoundFallback, value); }

    /// <summary>デバイス一覧と音量を差し替える。UI スレッドで呼ぶ。</summary>
    public void SetAudioState(IReadOnlyList<(string Id, string Name, bool IsDefault)> devices, double? volume, bool? muted)
    {
        Devices.Clear();
        foreach (var d in devices) Devices.Add(new AudioDeviceItem(d.Id, d.Name, d.IsDefault));
        Raise(nameof(HasDevices));
        HasDefaultDevice = volume is not null;
        Volume = volume ?? 0;
        IsMuted = muted ?? false;
    }

    // ---- InfoPanel ----

    public ObservableCollection<SummaryLine> SummaryLines { get; } = new();
    public SystemSummary? Summary
    {
        get => _summary;
        set
        {
            _summary = value;
            SummaryLines.Clear();
            if (value is not null)
            {
                foreach (var (label, text) in value.Lines()) SummaryLines.Add(new SummaryLine(label, text));
            }
            Raise(nameof(Summary));
        }
    }

    public string HotkeyText { get => _hotkeyText; set => Set(ref _hotkeyText, value); }
    public string TriggerText { get => _triggerText; set => Set(ref _triggerText, value); }
}
