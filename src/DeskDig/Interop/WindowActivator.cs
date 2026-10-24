using DeskDig.Core;
using static DeskDig.Interop.NativeMethods;

namespace DeskDig.Interop;

/// <summary>ウインドウを前面に出し入力可能にする。Placement があれば同じ一連の動作の中で適用する。</summary>
internal static class WindowActivator
{
    public static bool Activate(WindowId id, Placement? placement, IReadOnlyList<MonitorInfo> monitors)
    {
        nint hwnd = (nint)id.Value;
        if (!IsWindow(hwnd)) return false;

        if (placement is not null)
            WindowGeometry.Apply(id, placement, monitors);
        else if (IsIconic(hwnd))
            ShowWindow(hwnd, SW_RESTORE);

        return ForegroundAccess.BringToForeground(hwnd);
    }
}

/// <summary>SetForegroundWindow の前面化制限を越えるための補助。</summary>
internal static class ForegroundAccess
{
    public static bool BringToForeground(nint hwnd)
    {
        if (SetForegroundWindow(hwnd) && GetForegroundWindow() == hwnd) return true;

        // 入力スレッドを前面スレッドに一時的に接続して権限を得る
        uint foregroundThread = GetWindowThreadProcessId(GetForegroundWindow(), out _);
        uint current = GetCurrentThreadId();
        bool attached = foregroundThread != 0 && foregroundThread != current && AttachThreadInput(current, foregroundThread, true);
        try
        {
            BringWindowToTop(hwnd);
            SetForegroundWindow(hwnd);
        }
        finally
        {
            if (attached) AttachThreadInput(current, foregroundThread, false);
        }
        if (GetForegroundWindow() == hwnd) return true;

        // 最後の手段: Alt キーを打って前面化制限を解除する
        keybd_event(VK_MENU, 0, KEYEVENTF_EXTENDEDKEY, 0);
        keybd_event(VK_MENU, 0, KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP, 0);
        SetForegroundWindow(hwnd);
        return GetForegroundWindow() == hwnd;
    }
}
