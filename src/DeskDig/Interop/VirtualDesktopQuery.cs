namespace DeskDig.Interop;

/// <summary>仮想デスクトップは公開 API（IVirtualDesktopManager）の範囲でのみ扱う（ADR-0002）。</summary>
internal sealed class VirtualDesktopQuery
{
    private readonly IVirtualDesktopManager? _manager;

    public VirtualDesktopQuery()
    {
        try
        {
            _manager = (IVirtualDesktopManager)new VirtualDesktopManagerClass();
        }
        catch
        {
            _manager = null;
        }
    }

    /// <summary>判定できない場合は「現在のデスクトップにある」と見なす。</summary>
    public bool IsOnCurrentDesktop(nint hwnd)
    {
        if (_manager is null) return true;
        try
        {
            return _manager.IsWindowOnCurrentVirtualDesktop(hwnd, out bool onCurrent) != 0 || onCurrent;
        }
        catch
        {
            return true;
        }
    }
}
