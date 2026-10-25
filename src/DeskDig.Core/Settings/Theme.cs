using System.ComponentModel;

namespace DeskDig.Core.Settings;

/// <summary>
/// Theme。Palette・SidePanel・設定画面の配色の組。
/// System は Windows の「アプリモード」（個人用設定 > 色）に追従する。
/// </summary>
public enum Theme
{
    [Description("Dark")] Dark,
    [Description("Light")] Light,
    [Description("System（Windows に合わせる）")] System,
}

/// <summary>Theme の選択値から、実際に描く配色（Dark か Light）を決める。</summary>
public static class ThemeResolution
{
    /// <param name="preference">設定で選ばれた Theme。</param>
    /// <param name="systemUsesLightTheme">Windows のアプリモードが「ライト」か。</param>
    /// <returns>Dark または Light。System は返さない。</returns>
    public static Theme Resolve(Theme preference, bool systemUsesLightTheme) => preference switch
    {
        Theme.System => systemUsesLightTheme ? Theme.Light : Theme.Dark,
        _ => preference,
    };
}
