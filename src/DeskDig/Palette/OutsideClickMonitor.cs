using DeskDig.Core;
using DeskDig.Interop;
using static DeskDig.Interop.NativeMethods;

namespace DeskDig.Palette;

/// <summary>
/// Palette 表示中に、Palette の外でマウスボタンが押されたことを検出する。
/// WPF の Deactivated はフォーカスの状態（絞り込み欄にキャレットがある等）によって発火しないことがあるため、
/// 低レベルマウスフックで補い、どの状態でも外クリックで閉じられるようにする。
/// </summary>
internal sealed class OutsideClickMonitor : IDisposable
{
    private readonly LowLevelMouseHook _hook = new();
    private volatile bool _armed;

    /// <summary>Palette の外でボタンが押された。呼び出しスレッドはスレッドプール。</summary>
    public event Action? ClickedOutside;

    public OutsideClickMonitor()
    {
        _hook.AnyButtonDown += OnButtonDown;
    }

    public void Start() => _hook.Start();

    /// <summary>Palette を表示した直後に呼ぶ。以後、外クリックを通知する。</summary>
    public void Arm() => _armed = true;

    /// <summary>Palette を隠す前に呼ぶ。以後、通知しない。</summary>
    public void Disarm() => _armed = false;

    private void OnButtonDown(ScreenPoint point)
    {
        if (!_armed) return;
        if (IsOwnedByThisProcess(point)) return; // Palette 本体・コンテキストメニュー・ツールチップ上のクリックは「外」ではない
        ClickedOutside?.Invoke();
    }

    private static bool IsOwnedByThisProcess(ScreenPoint point)
    {
        nint hwnd = WindowFromPoint(new POINT { X = point.X, Y = point.Y });
        if (hwnd == 0) return false;
        GetWindowThreadProcessId(hwnd, out uint processId);
        return processId == (uint)Environment.ProcessId;
    }

    public void Dispose()
    {
        _armed = false;
        _hook.AnyButtonDown -= OnButtonDown;
        _hook.Dispose();
    }
}
