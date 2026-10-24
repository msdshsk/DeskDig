namespace DeskDig.Core;

/// <summary>文字入力による絞り込み。アプリ名・タイトルの部分一致（大小無視）。空文字は全件一致。</summary>
public sealed record EntryFilter(string Text)
{
    public static readonly EntryFilter None = new(string.Empty);

    public bool IsEmpty => string.IsNullOrWhiteSpace(Text);

    public bool Matches(WindowEntry entry) => Matches(entry.AppName, entry.Title);

    public bool Matches(PinnedApp app) => Matches(app.Name, string.Empty);

    public bool Matches(string appName, string title)
    {
        if (IsEmpty) return true;
        var needle = Text.Trim();
        return appName.Contains(needle, StringComparison.OrdinalIgnoreCase)
            || title.Contains(needle, StringComparison.OrdinalIgnoreCase);
    }

    public ArrangedWindowEntries Apply(ArrangedWindowEntries arranged) => IsEmpty
        ? arranged
        : new ArrangedWindowEntries(
            arranged.Favorites.Where(Matches).ToList(),
            arranged.CurrentDesktop.Where(Matches).ToList(),
            arranged.OtherDesktops.Where(Matches).ToList());

    public IReadOnlyList<PinnedApp> Apply(IReadOnlyList<PinnedApp> apps) =>
        IsEmpty ? apps : apps.Where(Matches).ToList();
}
