using Microsoft.Win32;

namespace DeskDig.Interop;

/// <summary>Windows の「アプリモード」（個人用設定 > 色）を読む。Theme = System のときの実際の配色を決めるのに使う。</summary>
internal static class SystemThemeReader
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string ValueName = "AppsUseLightTheme";

    /// <summary>アプリモードが「ライト」か。値が無い場合は Windows の既定に合わせてライトとみなす。</summary>
    public static bool AppsUseLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            return key?.GetValue(ValueName) is not int value || value != 0;
        }
        catch (Exception ex)
        {
            Diagnostics.Log("AppsUseLightTheme read failed", ex);
            return true;
        }
    }
}
