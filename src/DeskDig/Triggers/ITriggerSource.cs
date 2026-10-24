using DeskDig.Core;

namespace DeskDig.Triggers;

/// <summary>Trigger を検出する差し替え可能な実装の共通契約。開始・停止・発火通知だけを持つ。</summary>
internal interface ITriggerSource : IDisposable
{
    string Name { get; }
    void Start();
    void Stop();
    /// <summary>発火位置（マウス座標、物理ピクセル）を渡す。呼び出しスレッドは任意。</summary>
    event Action<ScreenPoint>? Fired;
}
