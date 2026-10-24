namespace DeskDig.Core;

/// <summary>WindowMatcher に紐づけて記録した Placement。同じ WindowMatcher の PlacementProfile は 1 件のみ。</summary>
public sealed record PlacementProfile(WindowMatcher Matcher, Placement Placement);

/// <summary>PlacementProfile の集まり。</summary>
public sealed class PlacementProfiles
{
    private readonly List<PlacementProfile> _items = new();

    public PlacementProfiles() { }

    public PlacementProfiles(IEnumerable<PlacementProfile> items)
    {
        foreach (var item in items) Record(item.Matcher, item.Placement);
    }

    public IReadOnlyList<PlacementProfile> Items => _items;

    /// <summary>記録。同じ WindowMatcher の既存 PlacementProfile は上書き。</summary>
    public void Record(WindowMatcher matcher, Placement placement)
    {
        int index = _items.FindIndex(p => p.Matcher.Equals(matcher));
        var profile = new PlacementProfile(matcher, placement);
        if (index >= 0) _items[index] = profile;
        else _items.Add(profile);
    }

    public bool Remove(WindowMatcher matcher) => _items.RemoveAll(p => p.Matcher.Equals(matcher)) > 0;

    /// <summary>
    /// WindowMatcher を差し替える（設定画面でのタイトル条件編集）。差し替え先が既存と衝突すれば既存を置き換える。
    /// </summary>
    public bool ReplaceMatcher(WindowMatcher current, WindowMatcher replacement)
    {
        int index = _items.FindIndex(p => p.Matcher.Equals(current));
        if (index < 0) return false;
        var placement = _items[index].Placement;
        _items.RemoveAt(index);
        _items.RemoveAll(p => p.Matcher.Equals(replacement));
        _items.Insert(Math.Min(index, _items.Count), new PlacementProfile(replacement, placement));
        return true;
    }

    /// <summary>WindowEntry に一致する PlacementProfile。タイトル条件付きを優先する。</summary>
    public PlacementProfile? FindFor(WindowEntry entry)
    {
        PlacementProfile? best = null;
        foreach (var profile in _items)
        {
            if (!profile.Matcher.Matches(entry)) continue;
            if (best is null || profile.Matcher.IsMoreSpecificThan(best.Matcher)) best = profile;
        }
        return best;
    }

    /// <summary>右クリック「配置を解除」の対象。その WindowEntry に現に適用される PlacementProfile。</summary>
    public bool HasProfileFor(WindowEntry entry) => FindFor(entry) is not null;
}
