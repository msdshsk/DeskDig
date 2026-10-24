using DeskDig.Core;
using static DeskDig.Interop.NativeMethods;

namespace DeskDig.Interop;

/// <summary>モニタ構成の読み取り。座標はすべて物理ピクセル。</summary>
internal static class MonitorQuery
{
    public static IReadOnlyList<MonitorInfo> All()
    {
        var list = new List<MonitorInfo>();
        EnumDisplayMonitors(0, 0, (nint hMonitor, nint _, ref RECT _, nint _) =>
        {
            var info = FromHandle(hMonitor);
            if (info is not null) list.Add(info);
            return true;
        }, 0);
        return list;
    }

    public static MonitorInfo? AtPoint(ScreenPoint point)
    {
        var h = MonitorFromPoint(new POINT { X = point.X, Y = point.Y }, MONITOR_DEFAULTTONEAREST);
        return FromHandle(h);
    }

    public static MonitorInfo? OfWindow(nint hwnd) => FromHandle(MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST));

    public static MonitorInfo? FromHandle(nint hMonitor)
    {
        if (hMonitor == 0) return null;
        var mi = new MONITORINFOEX { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFOEX>() };
        if (!GetMonitorInfo(hMonitor, ref mi)) return null;
        double scale = 1.0;
        if (GetDpiForMonitor(hMonitor, MONITOR_DPI_TYPE.MDT_EFFECTIVE_DPI, out uint dpiX, out _) == 0 && dpiX > 0)
            scale = dpiX / 96.0;
        return new MonitorInfo(mi.szDevice, ToBounds(mi.rcMonitor), ToBounds(mi.rcWork), scale);
    }

    public static Bounds ToBounds(RECT r) => new(r.Left, r.Top, r.Width, r.Height);

    public static ScreenPoint CursorPosition()
    {
        GetCursorPos(out var p);
        return new ScreenPoint(p.X, p.Y);
    }
}
