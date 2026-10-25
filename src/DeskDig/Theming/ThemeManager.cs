using System.Windows;
using DeskDig.Core.Settings;
using DeskDig.Interop;

namespace DeskDig.Theming;

/// <summary>
/// Theme の適用。App のリソース辞書のうち Themes/Dark.xaml または Themes/Light.xaml を差し替える。
/// XAML 側は DynamicResource で参照しているので、開いているウインドウにも再起動なしで反映される。
/// UI スレッドから呼ぶ。
/// </summary>
internal static class ThemeManager
{
    private static Theme _preference = Theme.System;
    private static Theme? _applied;

    /// <summary>設定で選ばれている Theme（System を含む）。</summary>
    public static Theme Preference => _preference;

    /// <summary>実際に描いている配色。Dark か Light。</summary>
    public static Theme Effective => _applied ?? Theme.Dark;

    /// <summary>実際の配色が変わった。DWM の枠色など辞書の外にある要素を合わせるために使う。</summary>
    public static event Action<Theme>? EffectiveChanged;

    /// <summary>設定の値を適用する。起動時と、設定の変更時に呼ぶ。</summary>
    public static void Apply(Theme preference)
    {
        _preference = preference;
        Refresh();
    }

    /// <summary>Windows のアプリモードが変わった（WM_SETTINGCHANGE "ImmersiveColorSet"）。System のときだけ追従する。</summary>
    public static void OnSystemAppModeChanged()
    {
        if (_preference == Theme.System) Refresh();
    }

    private static void Refresh()
    {
        var effective = ThemeResolution.Resolve(_preference, SystemThemeReader.AppsUseLightTheme());
        if (_applied == effective) return;
        if (Application.Current is not { } app) return;

        var dictionaries = app.Resources.MergedDictionaries;
        var fresh = new ResourceDictionary { Source = UriFor(effective) };
        int index = IndexOfThemeDictionary(dictionaries);
        if (index >= 0) dictionaries[index] = fresh;
        else dictionaries.Insert(0, fresh);

        _applied = effective;
        Diagnostics.Log($"Theme applied: {effective} (setting: {_preference})");
        EffectiveChanged?.Invoke(effective);
    }

    private static int IndexOfThemeDictionary(IList<ResourceDictionary> dictionaries)
    {
        for (int i = 0; i < dictionaries.Count; i++)
        {
            if (dictionaries[i].Source is { OriginalString: var s } && IsThemeDictionary(s)) return i;
        }
        return -1;
    }

    private static bool IsThemeDictionary(string source) =>
        source.EndsWith($"Themes/{Theme.Dark}.xaml", StringComparison.OrdinalIgnoreCase) ||
        source.EndsWith($"Themes/{Theme.Light}.xaml", StringComparison.OrdinalIgnoreCase);

    private static Uri UriFor(Theme effective) => new($"pack://application:,,,/Themes/{effective}.xaml", UriKind.Absolute);
}
