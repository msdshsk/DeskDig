namespace DeskDig.Core;

/// <summary>画面上の矩形（物理ピクセル）。Placement や MonitorInfo の構成要素。</summary>
public readonly record struct Bounds(int Left, int Top, int Width, int Height)
{
    public int Right => Left + Width;
    public int Bottom => Top + Height;
    public ScreenPoint Center => new(Left + Width / 2, Top + Height / 2);

    public bool Contains(ScreenPoint point) =>
        point.X >= Left && point.X < Right && point.Y >= Top && point.Y < Bottom;

    public bool Intersects(Bounds other) =>
        other.Left < Right && other.Right > Left && other.Top < Bottom && other.Bottom > Top;

    /// <summary>矩形同士の最短距離の二乗。重なっていれば 0。</summary>
    public long DistanceSquaredTo(Bounds other)
    {
        long dx = Math.Max(0, Math.Max(other.Left - Right, Left - other.Right));
        long dy = Math.Max(0, Math.Max(other.Top - Bottom, Top - other.Bottom));
        return dx * dx + dy * dy;
    }

    public Bounds Intersect(Bounds other)
    {
        int l = Math.Max(Left, other.Left), t = Math.Max(Top, other.Top);
        int r = Math.Min(Right, other.Right), b = Math.Min(Bottom, other.Bottom);
        return r <= l || b <= t ? new Bounds(l, t, 0, 0) : new Bounds(l, t, r - l, b - t);
    }

    public bool IsEmpty => Width <= 0 || Height <= 0;
}

public readonly record struct ScreenPoint(int X, int Y);
