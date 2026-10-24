namespace DeskDig.Core;

/// <summary>並び替え済みの WindowEntry 一覧。上から Favorite / 現在のデスクトップ / 他のデスクトップ。</summary>
public sealed record ArrangedWindowEntries(
    IReadOnlyList<WindowEntry> Favorites,
    IReadOnlyList<WindowEntry> CurrentDesktop,
    IReadOnlyList<WindowEntry> OtherDesktops)
{
    public static readonly ArrangedWindowEntries Empty = new([], [], []);

    /// <summary>画面上の並びと同じ順で平らにしたもの。キーボード選択や Launcher の振り替えに使う。</summary>
    public IEnumerable<WindowEntry> Flatten() => Favorites.Concat(CurrentDesktop).Concat(OtherDesktops);

    public int Count => Favorites.Count + CurrentDesktop.Count + OtherDesktops.Count;
}

/// <summary>Palette の WindowEntry 一覧の並び順規則。</summary>
public static class PaletteOrder
{
    public static ArrangedWindowEntries Arrange(
        IEnumerable<WindowEntry> entries,
        Favorites favorites,
        RecentActivation recent)
    {
        var all = entries.ToList();
        var favoriteSection = new List<WindowEntry>();
        var taken = new HashSet<WindowId>();

        foreach (var favorite in favorites.Items)
        {
            var matched = all.Where(e => favorite.Matcher.Matches(e) && !taken.Contains(e.Id))
                             .OrderBy(e => e, new RecentThenStacking(recent))
                             .ToList();
            foreach (var e in matched)
            {
                taken.Add(e.Id);
                favoriteSection.Add(e);
            }
        }

        var rest = all.Where(e => !taken.Contains(e.Id)).ToList();
        var comparer = new RecentThenStacking(recent);
        var current = rest.Where(e => e.IsOnCurrentDesktop).OrderBy(e => e, comparer).ToList();
        var other = rest.Where(e => !e.IsOnCurrentDesktop).OrderBy(e => e, comparer).ToList();
        return new ArrangedWindowEntries(favoriteSection, current, other);
    }

    /// <summary>RecentActivation の新しい順。記録が無いものは OS の重なり順（手前が先）。</summary>
    private sealed class RecentThenStacking(RecentActivation recent) : IComparer<WindowEntry>
    {
        public int Compare(WindowEntry? x, WindowEntry? y)
        {
            if (ReferenceEquals(x, y)) return 0;
            if (x is null) return 1;
            if (y is null) return -1;
            int? rx = recent.RankOf(x.Id), ry = recent.RankOf(y.Id);
            if (rx.HasValue && ry.HasValue) return rx.Value.CompareTo(ry.Value);
            if (rx.HasValue) return -1;
            if (ry.HasValue) return 1;
            return x.StackingOrder.CompareTo(y.StackingOrder);
        }
    }
}
