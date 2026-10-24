namespace DeskDig.Core;

/// <summary>OS 上の実在ウインドウを指す不透明な識別子。Palette が開いている間だけ有効。</summary>
public readonly record struct WindowId(long Value)
{
    public override string ToString() => $"W{Value:X}";
}

/// <summary>
/// Palette に並ぶウインドウ 1 件。OS の状態を映した読み取り専用の投影で、Palette を開くたびに作り直す。
/// </summary>
public sealed record WindowEntry(
    WindowId Id,
    string Title,
    string AppName,
    string ExecutablePath,
    bool IsOnCurrentDesktop,
    int StackingOrder,
    bool IsMinimized,
    string? AppUserModelId = null)
{
    /// <summary>既定の WindowMatcher（実行ファイルのみ）。Favorite / PlacementProfile 登録時の初期値。</summary>
    public WindowMatcher DefaultMatcher => WindowMatcher.ForExecutable(ExecutablePath);
}
