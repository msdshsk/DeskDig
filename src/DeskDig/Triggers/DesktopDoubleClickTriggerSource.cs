using DeskDig.Core;
using DeskDig.Interop;
using static DeskDig.Interop.NativeMethods;

namespace DeskDig.Triggers;

/// <summary>デスクトップ余白のダブルクリック。アイコン上は発火しない。</summary>
internal sealed class DesktopDoubleClickTriggerSource : ITriggerSource
{
    private readonly LowLevelMouseHook _hook = new();
    private readonly object _gate = new();
    private ScreenPoint _lastPoint;
    private uint _lastTime;
    private bool _running;

    public string Name => "DesktopDoubleClick";

    public event Action<ScreenPoint>? Fired;

    public DesktopDoubleClickTriggerSource()
    {
        _hook.LeftButtonDown += OnLeftButtonDown;
    }

    public void Start()
    {
        if (_running) return;
        _running = true;
        _hook.Start();
    }

    public void Stop()
    {
        if (!_running) return;
        _running = false;
        _hook.Stop();
    }

    /// <summary>フック本体ではなくスレッドプール上で呼ばれる。ここで二重クリック判定とデスクトップ判定を行う。</summary>
    private void OnLeftButtonDown(ScreenPoint point, uint time)
    {
        bool isDoubleClick;
        lock (_gate)
        {
            uint interval = GetDoubleClickTime();
            int dx = GetSystemMetrics(SM_CXDOUBLECLK) / 2, dy = GetSystemMetrics(SM_CYDOUBLECLK) / 2;
            isDoubleClick = _lastTime != 0
                && time - _lastTime <= interval
                && Math.Abs(point.X - _lastPoint.X) <= dx
                && Math.Abs(point.Y - _lastPoint.Y) <= dy;
            if (isDoubleClick)
            {
                _lastTime = 0; // 3 連打を 2 回のダブルクリックと数えない
            }
            else
            {
                _lastTime = time;
                _lastPoint = point;
            }
        }
        if (!isDoubleClick || !_running) return;
        bool blank = DesktopHitTest.IsDesktopBlank(point, out string detail);
        Diagnostics.Log($"DoubleClick at ({point.X},{point.Y}) => {(blank ? "desktop blank" : "not blank")} [{detail}]");
        if (blank) Fired?.Invoke(point);
    }

    public void Dispose()
    {
        Stop();
        _hook.Dispose();
    }
}
