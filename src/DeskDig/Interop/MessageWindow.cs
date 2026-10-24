using System.Windows.Interop;

namespace DeskDig.Interop;

/// <summary>ホットキーや多重起動通知を受け取るための不可視ウインドウ。UI スレッド上に作る。</summary>
internal sealed class MessageWindow : IDisposable
{
    private readonly HwndSource _source;

    public event Action<int, nint, nint>? MessageReceived;

    public MessageWindow()
    {
        var parameters = new HwndSourceParameters("DeskDig.MessageWindow")
        {
            Width = 0,
            Height = 0,
            PositionX = 0,
            PositionY = 0,
            WindowStyle = 0,
            ExtendedWindowStyle = (int)NativeMethods.WS_EX_TOOLWINDOW,
        };
        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);
    }

    public nint Handle => _source.Handle;

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        MessageReceived?.Invoke(msg, wParam, lParam);
        return 0;
    }

    public void Dispose() => _source.Dispose();
}
