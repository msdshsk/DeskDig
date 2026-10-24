using DeskDig.Core;
using static DeskDig.Interop.NativeMethods;

namespace DeskDig.Interop;

/// <summary>DWM のライブサムネイル 1 件。宛先ウインドウのクライアント座標（物理ピクセル）で位置を更新する。</summary>
internal sealed class DwmThumbnail : IDisposable
{
    private nint _handle;
    private Bounds _lastDestination;
    private Bounds _lastSource;
    private bool _lastVisible;
    private bool _everUpdated;

    private DwmThumbnail(nint handle) => _handle = handle;

    public static DwmThumbnail? Register(nint destination, WindowId source)
    {
        if (DwmRegisterThumbnail(destination, (nint)source.Value, out nint handle) != 0 || handle == 0) return null;
        return new DwmThumbnail(handle);
    }

    public Bounds? SourceSize()
    {
        if (_handle == 0 || DwmQueryThumbnailSourceSize(_handle, out var size) != 0 || size.cx <= 0 || size.cy <= 0) return null;
        return new Bounds(0, 0, size.cx, size.cy);
    }

    public void Hide()
    {
        if (_handle == 0 || (_everUpdated && !_lastVisible)) return;
        var props = new DWM_THUMBNAIL_PROPERTIES { dwFlags = DWM_TNP_VISIBLE, fVisible = false };
        DwmUpdateThumbnailProperties(_handle, ref props);
        _lastVisible = false;
        _everUpdated = true;
    }

    public void Show(Bounds destination, Bounds source)
    {
        if (_handle == 0) return;
        if (_everUpdated && _lastVisible && destination == _lastDestination && source == _lastSource) return;
        var props = new DWM_THUMBNAIL_PROPERTIES
        {
            dwFlags = DWM_TNP_RECTDESTINATION | DWM_TNP_RECTSOURCE | DWM_TNP_VISIBLE | DWM_TNP_SOURCECLIENTAREAONLY | DWM_TNP_OPACITY,
            rcDestination = new RECT { Left = destination.Left, Top = destination.Top, Right = destination.Right, Bottom = destination.Bottom },
            rcSource = new RECT { Left = source.Left, Top = source.Top, Right = source.Right, Bottom = source.Bottom },
            fVisible = true,
            fSourceClientAreaOnly = false,
            opacity = 255,
        };
        DwmUpdateThumbnailProperties(_handle, ref props);
        _lastDestination = destination;
        _lastSource = source;
        _lastVisible = true;
        _everUpdated = true;
    }

    public void Dispose()
    {
        if (_handle != 0)
        {
            DwmUnregisterThumbnail(_handle);
            _handle = 0;
        }
    }
}
