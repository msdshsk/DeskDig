namespace DeskDig.Core;

/// <summary>ウインドウの見える外周（DWM 拡張フレーム境界）と OS 上のウインドウ矩形との差。</summary>
public readonly record struct FrameInsets(int Left, int Top, int Right, int Bottom)
{
    public static readonly FrameInsets None = new(0, 0, 0, 0);
}

/// <summary>
/// ウインドウ 1 件の位置・サイズ・所属モニタの組。Area は見た目の矩形（物理ピクセル）。
/// 負または 0 のサイズと空のモニタ参照は構成できない。
/// </summary>
public sealed record Placement
{
    public Bounds Area { get; }
    public string MonitorId { get; }

    public Placement(Bounds area, string monitorId)
    {
        if (area.Width <= 0 || area.Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(area), "Placement のサイズは正でなければならない。");
        if (string.IsNullOrWhiteSpace(monitorId))
            throw new ArgumentException("Placement はモニタを参照しなければならない。", nameof(monitorId));
        Area = area;
        MonitorId = monitorId;
    }

    /// <summary>記録時のモニタが無ければ最寄りのモニタの作業領域に収まるよう補正する。</summary>
    public Placement ResolveAgainst(IReadOnlyList<MonitorInfo> monitors)
    {
        if (monitors.Count == 0) return this;
        foreach (var monitor in monitors)
        {
            if (string.Equals(monitor.Id, MonitorId, StringComparison.OrdinalIgnoreCase)) return this;
        }
        var nearest = MonitorInfo.Nearest(monitors, Area)!;
        return FitInto(nearest);
    }

    /// <summary>指定モニタの作業領域内に収まるようサイズを縮め位置をずらす。</summary>
    public Placement FitInto(MonitorInfo monitor)
    {
        var work = monitor.WorkArea;
        int width = Math.Min(Area.Width, work.Width);
        int height = Math.Min(Area.Height, work.Height);
        int left = Math.Clamp(Area.Left, work.Left, Math.Max(work.Left, work.Right - width));
        int top = Math.Clamp(Area.Top, work.Top, Math.Max(work.Top, work.Bottom - height));
        return new Placement(new Bounds(left, top, width, height), monitor.Id);
    }

    /// <summary>見た目の矩形から OS に渡すウインドウ矩形へ（見えない余白ぶん外へ広げる）。</summary>
    public Bounds ToWindowBounds(FrameInsets insets) => new(
        Area.Left - insets.Left,
        Area.Top - insets.Top,
        Area.Width + insets.Left + insets.Right,
        Area.Height + insets.Top + insets.Bottom);
}
