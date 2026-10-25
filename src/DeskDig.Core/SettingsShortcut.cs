namespace DeskDig.Core;

/// <summary>
/// Windows の設定ページや従来のダイアログへ一発で移動するボタン。ToolsPanel の上段に並ぶ。
/// 候補は <see cref="SettingsShortcutCatalog"/> に固定で、ユーザーが登録したり設定で出し入れしたりするものではない。
/// </summary>
/// <param name="Id">識別子。</param>
/// <param name="Name">表示名。</param>
/// <param name="Glyph">Segoe Fluent Icons / Segoe MDL2 Assets の記号。</param>
/// <param name="FileName">シェルに渡す行き先（ms-settings: の URI、.cpl、実行ファイル）。</param>
/// <param name="Arguments">行き先に渡す引数。無ければ null。</param>
public sealed record SettingsShortcut(string Id, string Name, string Glyph, string FileName, string? Arguments = null)
{
    /// <summary>ログや表示に使う 1 行表現。</summary>
    public string Destination => Arguments is null ? FileName : $"{FileName} {Arguments}";
}

/// <summary>SettingsShortcut の固定の一覧。表示順は spec §4.8 の表に従う。</summary>
public static class SettingsShortcutCatalog
{
    public static IReadOnlyList<SettingsShortcut> All { get; } =
    [
        new("environment", "環境変数", "", "rundll32.exe", "sysdm.cpl,EditEnvironmentVariables"),
        new("sound-dialog", "サウンド（従来のダイアログ）", "", "mmsys.cpl"),
        new("network", "ネットワークとインターネット", "", "ms-settings:network"),
        new("bluetooth", "Bluetooth とデバイス", "", "ms-settings:bluetooth"),
        new("display", "ディスプレイ", "", "ms-settings:display"),
        new("about", "バージョン情報", "", "ms-settings:about"),
    ];
}
