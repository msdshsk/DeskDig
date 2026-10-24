using System.Windows.Input;
using DeskDig.Core;
using DeskDig.Interop;
using static DeskDig.Interop.NativeMethods;

namespace DeskDig.Triggers;

/// <summary>"Ctrl+Alt+Space" のような文字列で表されたホットキー。</summary>
internal sealed record HotkeyGesture(uint Modifiers, uint VirtualKey, string Display)
{
    public static bool TryParse(string? text, out HotkeyGesture gesture)
    {
        gesture = null!;
        if (string.IsNullOrWhiteSpace(text)) return false;
        uint modifiers = 0;
        Key key = Key.None;
        foreach (var raw in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (raw.ToLowerInvariant())
            {
                case "ctrl" or "control": modifiers |= MOD_CONTROL; break;
                case "alt": modifiers |= MOD_ALT; break;
                case "shift": modifiers |= MOD_SHIFT; break;
                case "win" or "windows": modifiers |= MOD_WIN; break;
                default:
                    if (key != Key.None) return false;
                    if (!Enum.TryParse(raw, ignoreCase: true, out key)) return false;
                    break;
            }
        }
        if (key == Key.None) return false;
        uint vk = (uint)KeyInterop.VirtualKeyFromKey(key);
        if (vk == 0) return false;
        gesture = new HotkeyGesture(modifiers, vk, text.Trim());
        return true;
    }
}

/// <summary>グローバルホットキー。発火位置は現在のマウス座標。</summary>
internal sealed class HotkeyTriggerSource : ITriggerSource
{
    private const int HotkeyId = 0x5750; // 'WP'
    private readonly MessageWindow _window;
    private readonly HotkeyGesture _gesture;
    private bool _registered;

    public string Name => "Hotkey";

    public event Action<ScreenPoint>? Fired;

    public HotkeyTriggerSource(MessageWindow window, HotkeyGesture gesture)
    {
        _window = window;
        _gesture = gesture;
        _window.MessageReceived += OnMessage;
    }

    public bool RegistrationFailed { get; private set; }

    public void Start()
    {
        if (_registered) return;
        _registered = RegisterHotKey(_window.Handle, HotkeyId, _gesture.Modifiers | MOD_NOREPEAT, _gesture.VirtualKey);
        RegistrationFailed = !_registered;
    }

    public void Stop()
    {
        if (!_registered) return;
        UnregisterHotKey(_window.Handle, HotkeyId);
        _registered = false;
    }

    private void OnMessage(int msg, nint wParam, nint lParam)
    {
        if (msg == WM_HOTKEY && (int)wParam == HotkeyId) Fired?.Invoke(MonitorQuery.CursorPosition());
    }

    public void Dispose()
    {
        Stop();
        _window.MessageReceived -= OnMessage;
    }
}
