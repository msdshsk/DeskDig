namespace DeskDig.Core;

/// <summary>Palette 経由で Activate した順序の記録。実行中のみ有効で永続化しない。</summary>
public sealed class RecentActivation
{
    private readonly List<WindowId> _newestFirst = new();

    public IReadOnlyList<WindowId> NewestFirst => _newestFirst;

    public void Record(WindowId id)
    {
        _newestFirst.Remove(id);
        _newestFirst.Insert(0, id);
    }

    /// <summary>0 が最新。記録が無ければ null。</summary>
    public int? RankOf(WindowId id)
    {
        int index = _newestFirst.IndexOf(id);
        return index < 0 ? null : index;
    }

    public void Forget(WindowId id) => _newestFirst.Remove(id);
}
