using Microsoft.Win32;

namespace DeskDig.Interop;

/// <summary>
/// Windows サインイン時の自動起動。HKCU の Run キーに exe のパスを登録する。
/// Windows の「設定 > アプリ > スタートアップ」でオフにされると Run キーは残ったまま
/// StartupApproved に無効フラグが立つので、有効化時はそのフラグも消し、状態の読み取りでは両方を見る。
/// </summary>
internal static class AutoStartRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string ValueName = "DeskDig";

    /// <summary>登録を書き換える。有効化時は現在の exe のパスで上書きするので、フォルダを移動した後の修復にもなる。</summary>
    public static void Apply(bool enabled)
    {
        using var run = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
        if (enabled)
        {
            var exe = Environment.ProcessPath;
            if (exe is null) return;
            run.SetValue(ValueName, $"\"{exe}\"");
            // Windows 側でオフにされていた場合、そのフラグを消さないと再び起動しない
            using var approved = Registry.CurrentUser.OpenSubKey(ApprovedKey, writable: true);
            approved?.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        else
        {
            run.DeleteValue(ValueName, throwOnMissingValue: false);
            using var approved = Registry.CurrentUser.OpenSubKey(ApprovedKey, writable: true);
            approved?.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }

    /// <summary>実際にサインイン時に起動する状態かどうか。Run キーがあり、かつ Windows 側で無効化されていないこと。</summary>
    public static bool IsEffectivelyEnabled()
    {
        using var run = Registry.CurrentUser.OpenSubKey(RunKey);
        if (run?.GetValue(ValueName) is not string) return false;

        using var approved = Registry.CurrentUser.OpenSubKey(ApprovedKey);
        if (approved?.GetValue(ValueName) is byte[] { Length: > 0 } flags)
        {
            // 先頭バイト: 0x02 = 有効、0x03 = 無効（Windows の内部形式）
            return (flags[0] & 0x01) == 0;
        }
        return true;
    }

    /// <summary>Run キーが現在の exe を指しているか。別の場所に移動した後の検出に使う。</summary>
    public static bool PointsToCurrentExecutable()
    {
        using var run = Registry.CurrentUser.OpenSubKey(RunKey);
        var registered = run?.GetValue(ValueName) as string;
        var exe = Environment.ProcessPath;
        if (registered is null || exe is null) return false;
        return string.Equals(registered.Trim('"'), exe, StringComparison.OrdinalIgnoreCase);
    }
}
