using System.Runtime.InteropServices;
using DeskDig.Core;
using static DeskDig.Interop.NativeMethods;

namespace DeskDig.Interop;

/// <summary>
/// 低レベルマウスフック。専用スレッドのメッセージループ上で動き、コールバックは即座に返す。
/// ボタン押下だけを通知する（移動やホイールは扱わない）。
/// </summary>
internal sealed class LowLevelMouseHook : IDisposable
{
    private readonly LowLevelMouseProc _callback; // GC に回収されないよう保持
    private Thread? _thread;
    private uint _threadId;
    private nint _hook;
    private readonly ManualResetEventSlim _started = new();

    /// <summary>左ボタン押下。座標（物理ピクセル）と押下時刻（ミリ秒）を渡す。</summary>
    public event Action<ScreenPoint, uint>? LeftButtonDown;

    /// <summary>左・右・中・拡張ボタンのいずれかの押下。座標（物理ピクセル）を渡す。</summary>
    public event Action<ScreenPoint>? AnyButtonDown;

    public LowLevelMouseHook()
    {
        _callback = HookProc;
    }

    public void Start()
    {
        if (_thread is not null) return;
        _thread = new Thread(MessageLoop) { IsBackground = true, Name = "DeskDig.MouseHook" };
        _thread.Start();
        _started.Wait(TimeSpan.FromSeconds(2));
    }

    public void Stop()
    {
        if (_thread is null) return;
        PostThreadMessage(_threadId, WM_QUIT, 0, 0);
        _thread.Join(TimeSpan.FromSeconds(2));
        _thread = null;
    }

    private void MessageLoop()
    {
        _threadId = GetCurrentThreadId();
        _hook = SetWindowsHookEx(WH_MOUSE_LL, _callback, GetModuleHandle(null), 0);
        if (_hook == 0) Diagnostics.Log($"Mouse hook failed: error {Marshal.GetLastWin32Error()}");
        _started.Set();
        while (GetMessage(out var msg, 0, 0, 0) > 0)
        {
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }
        if (_hook != 0)
        {
            UnhookWindowsHookEx(_hook);
            _hook = 0;
        }
    }

    private nint HookProc(int nCode, nint wParam, nint lParam)
    {
        if (nCode == 0 && wParam is WM_LBUTTONDOWN or WM_RBUTTONDOWN or WM_MBUTTONDOWN or WM_XBUTTONDOWN)
        {
            var data = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
            var point = new ScreenPoint(data.pt.X, data.pt.Y);
            uint time = data.time;

            var anyHandler = AnyButtonDown;
            if (anyHandler is not null)
                ThreadPool.UnsafeQueueUserWorkItem(_ => anyHandler(point), null);

            var leftHandler = LeftButtonDown;
            if (wParam == WM_LBUTTONDOWN && leftHandler is not null)
                ThreadPool.UnsafeQueueUserWorkItem(_ => leftHandler(point, time), null);
        }
        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    public void Dispose() => Stop();
}
