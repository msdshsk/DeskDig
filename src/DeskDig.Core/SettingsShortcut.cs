using DeskDig.Core.Settings;

namespace DeskDig.Core;

/// <summary>
/// Windows の設定ページへ一発で移動する項目。行き先は OS が定める URI（ms-settings:）またはコントロールパネル項目。
/// ユーザーが登録するものではなく、<see cref="SettingsShortcutCatalog"/> の候補から表示するものを選ぶ。
/// </summary>
/// <param name="Id">設定の識別子。</param>
/// <param name="Name">表示名。</param>
/// <param name="Destination">シェルに渡す行き先。</param>
/// <param name="Glyph">Segoe Fluent Icons / Segoe MDL2 Assets の記号。</param>
/// <param name="Keywords">絞り込みで名前のほかに一致させる別名。</param>
public sealed record SettingsShortcut(string Id, string Name, string Destination, string Glyph, IReadOnlyList<string> Keywords)
{
    public bool Matches(EntryFilter filter) =>
        filter.IsEmpty || filter.Matches(Name, string.Empty) || Keywords.Any(k => filter.Matches(k, string.Empty));
}

/// <summary>SettingsShortcut の候補一覧と、設定で有効にされたものの抽出。</summary>
public static class SettingsShortcutCatalog
{
    private sealed record Candidate(SettingsShortcut Shortcut, Func<PaletteSettings, bool> IsEnabled);

    private static readonly IReadOnlyList<Candidate> Candidates =
    [
        new(new("about", "バージョン情報", "ms-settings:about", "", ["about", "version", "システム情報", "PC 情報", "Windows のバージョン"]), s => s.ShortcutAbout),
        new(new("sound", "サウンド", "ms-settings:sound", "", ["sound", "audio", "volume", "音量", "音声", "オーディオ", "スピーカー", "マイク", "ミュート", "イヤホン"]), s => s.ShortcutSound),
        new(new("network", "ネットワークとインターネット", "ms-settings:network", "", ["network", "internet", "LAN", "イーサネット", "VPN", "プロキシ"]), s => s.ShortcutNetwork),
        new(new("wifi", "Wi-Fi", "ms-settings:network-wifi", "", ["wifi", "wireless", "無線", "ワイヤレス"]), s => s.ShortcutWifi),
        new(new("display", "ディスプレイ", "ms-settings:display", "", ["display", "monitor", "画面", "モニタ", "解像度", "拡大率", "DPI"]), s => s.ShortcutDisplay),
        new(new("bluetooth", "Bluetooth とデバイス", "ms-settings:bluetooth", "", ["bluetooth", "device", "デバイス", "ペアリング"]), s => s.ShortcutBluetooth),
        new(new("sound-dialog", "サウンド（従来のダイアログ）", "mmsys.cpl", "", ["sound", "playback", "recording", "再生デバイス", "録音デバイス", "既定のデバイス"]), s => s.ShortcutSoundDialog),
    ];

    public static IReadOnlyList<SettingsShortcut> All => Candidates.Select(c => c.Shortcut).ToList();

    public static IReadOnlyList<SettingsShortcut> Enabled(PaletteSettings settings) =>
        Candidates.Where(c => c.IsEnabled(settings)).Select(c => c.Shortcut).ToList();
}
