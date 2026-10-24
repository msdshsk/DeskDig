namespace DeskDig.Core;

/// <summary>Palette の表示位置。発火位置の近傍に置き、同じモニタの作業領域内に収める。</summary>
public static class PalettePositioning
{
    public static Bounds Resolve(ScreenPoint anchor, int width, int height, Bounds workArea)
    {
        int w = Math.Min(width, workArea.Width);
        int h = Math.Min(height, workArea.Height);
        int left = Math.Clamp(anchor.X, workArea.Left, Math.Max(workArea.Left, workArea.Right - w));
        int top = Math.Clamp(anchor.Y, workArea.Top, Math.Max(workArea.Top, workArea.Bottom - h));
        return new Bounds(left, top, w, h);
    }
}
