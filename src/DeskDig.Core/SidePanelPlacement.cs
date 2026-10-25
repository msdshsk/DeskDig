namespace DeskDig.Core;

/// <summary>SidePanel の表示位置。Palette と同じ高さで右に置き、右に収まらなければ左に置く。Palette は動かさない。</summary>
public static class SidePanelPlacement
{
    /// <param name="palette">Palette の矩形（物理ピクセル）。</param>
    /// <param name="width">SidePanel の幅（物理ピクセル）。</param>
    /// <param name="gap">Palette との隙間（物理ピクセル）。</param>
    /// <param name="workArea">Palette があるモニタの作業領域。</param>
    public static Bounds Resolve(Bounds palette, int width, int gap, Bounds workArea)
    {
        int w = Math.Max(1, Math.Min(width, workArea.Width));
        int right = palette.Right + gap;
        int left = right + w <= workArea.Right
            ? right
            : palette.Left - gap - w;
        // 左にも収まらない（極端に狭いモニタ）場合は作業領域の内側に押し込む
        left = Math.Clamp(left, workArea.Left, Math.Max(workArea.Left, workArea.Right - w));
        return new Bounds(left, palette.Top, w, palette.Height);
    }
}
