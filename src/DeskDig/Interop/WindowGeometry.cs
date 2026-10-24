using DeskDig.Core;
using static DeskDig.Interop.NativeMethods;

namespace DeskDig.Interop;

/// <summary>ウインドウの Placement の読み取りと適用。見えない余白（DWM 拡張フレーム）を補正する。</summary>
internal static class WindowGeometry
{
    /// <summary>現在の見た目の矩形と所属モニタを Placement として読む。</summary>
    public static Placement? Read(WindowId id)
    {
        nint hwnd = (nint)id.Value;
        if (!IsWindow(hwnd)) return null;
        var visible = VisibleBounds(hwnd);
        var monitor = MonitorQuery.OfWindow(hwnd);
        if (monitor is null || visible.IsEmpty) return null;
        try
        {
            return new Placement(visible, monitor.Id);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>通常状態に戻したうえで Placement を適用する。サイズが拒否された場合でも位置は適用される。</summary>
    public static void Apply(WindowId id, Placement placement, IReadOnlyList<MonitorInfo> monitors)
    {
        nint hwnd = (nint)id.Value;
        if (!IsWindow(hwnd)) return;
        if (IsIconic(hwnd) || IsZoomed(hwnd)) ShowWindow(hwnd, SW_RESTORE);

        var resolved = placement.ResolveAgainst(monitors);
        var insets = FrameInsetsOf(hwnd);
        var target = resolved.ToWindowBounds(insets);
        SetWindowPos(hwnd, 0, target.Left, target.Top, target.Width, target.Height, SWP_NOZORDER | SWP_NOACTIVATE | SWP_NOOWNERZORDER);
    }

    public static Bounds VisibleBounds(nint hwnd)
    {
        if (DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out RECT frame, System.Runtime.InteropServices.Marshal.SizeOf<RECT>()) == 0
            && frame.Width > 0 && frame.Height > 0)
            return MonitorQuery.ToBounds(frame);
        GetWindowRect(hwnd, out var rect);
        return MonitorQuery.ToBounds(rect);
    }

    public static FrameInsets FrameInsetsOf(nint hwnd)
    {
        if (!GetWindowRect(hwnd, out var window)) return FrameInsets.None;
        if (DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out RECT frame, System.Runtime.InteropServices.Marshal.SizeOf<RECT>()) != 0)
            return FrameInsets.None;
        return new FrameInsets(
            Math.Max(0, frame.Left - window.Left),
            Math.Max(0, frame.Top - window.Top),
            Math.Max(0, window.Right - frame.Right),
            Math.Max(0, window.Bottom - frame.Bottom));
    }
}
