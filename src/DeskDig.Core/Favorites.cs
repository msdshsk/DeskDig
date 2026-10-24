namespace DeskDig.Core;

/// <summary>Palette 内で上位固定した WindowEntry。WindowMatcher を識別子とする。</summary>
public sealed record Favorite(WindowMatcher Matcher);

/// <summary>Favorite の集まり。登録順を保ち、同じ WindowMatcher は 1 件のみ。</summary>
public sealed class Favorites
{
    private readonly List<Favorite> _items = new();

    public Favorites() { }

    public Favorites(IEnumerable<Favorite> items)
    {
        foreach (var item in items) Add(item.Matcher);
    }

    public IReadOnlyList<Favorite> Items => _items;

    public bool Add(WindowMatcher matcher)
    {
        if (Contains(matcher)) return false;
        _items.Add(new Favorite(matcher));
        return true;
    }

    public bool Remove(WindowMatcher matcher) => _items.RemoveAll(f => f.Matcher.Equals(matcher)) > 0;

    public bool Contains(WindowMatcher matcher) => _items.Any(f => f.Matcher.Equals(matcher));

    /// <summary>この WindowEntry を Favorite 段に出す根拠となる Favorite（登録順で最初のもの）。</summary>
    public Favorite? FavoriteFor(WindowEntry entry) => _items.FirstOrDefault(f => f.Matcher.Matches(entry));

    public bool IsFavorite(WindowEntry entry) => FavoriteFor(entry) is not null;
}
