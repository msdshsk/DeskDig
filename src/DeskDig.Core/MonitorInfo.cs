namespace DeskDig.Core;

/// <summary>モニタ 1 台。Id は OS が与えるデバイス名。</summary>
public sealed record MonitorInfo(string Id, Bounds Bounds, Bounds WorkArea, double Scale = 1.0)
{
    public static MonitorInfo? Nearest(IReadOnlyList<MonitorInfo> monitors, Bounds target)
    {
        MonitorInfo? best = null;
        long bestDistance = long.MaxValue;
        foreach (var monitor in monitors)
        {
            long d = monitor.WorkArea.DistanceSquaredTo(target);
            if (d < bestDistance)
            {
                bestDistance = d;
                best = monitor;
            }
        }
        return best;
    }

    public static MonitorInfo? Containing(IReadOnlyList<MonitorInfo> monitors, ScreenPoint point)
    {
        foreach (var monitor in monitors)
        {
            if (monitor.Bounds.Contains(point)) return monitor;
        }
        return monitors.Count == 0 ? null : monitors[0];
    }
}
