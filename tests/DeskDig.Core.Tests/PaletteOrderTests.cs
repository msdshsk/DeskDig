using static DeskDig.Core.Tests.TestData;

namespace DeskDig.Core.Tests;

public class PaletteOrderTests
{
    [Fact]
    public void RecentActivation_newest_first_then_stacking_order()
    {
        // シナリオ 2: A を Activate、次に B を Activate → B, A の順。C は Palette 外操作なので記録されない。
        var a = Entry(1, Editor, stacking: 2);
        var b = Entry(2, Terminal, stacking: 1);
        var c = Entry(3, Browser, stacking: 0);
        var recent = new RecentActivation();
        recent.Record(a.Id);
        recent.Record(b.Id);

        var arranged = PaletteOrder.Arrange([c, a, b], new Favorites(), recent);

        Assert.Equal([b.Id, a.Id, c.Id], arranged.CurrentDesktop.Select(e => e.Id));
    }

    [Fact]
    public void Entries_without_recent_activation_follow_stacking_order()
    {
        var top = Entry(1, Editor, stacking: 0);
        var mid = Entry(2, Terminal, stacking: 1);
        var bottom = Entry(3, Browser, stacking: 2);

        var arranged = PaletteOrder.Arrange([bottom, top, mid], new Favorites(), new RecentActivation());

        Assert.Equal([top.Id, mid.Id, bottom.Id], arranged.CurrentDesktop.Select(e => e.Id));
    }

    [Fact]
    public void Favorites_come_first_in_registration_order_regardless_of_desktop()
    {
        // シナリオ 3: ブラウザを Favorite に追加 → ブラウザの全ウインドウが最上段。
        var browser1 = Entry(1, Browser, current: true, stacking: 5);
        var browser2 = Entry(2, Browser, current: false, stacking: 1);
        var editor = Entry(3, Editor, stacking: 0);
        var terminal = Entry(4, Terminal, stacking: 2);
        var favorites = new Favorites();
        favorites.Add(WindowMatcher.ForExecutable(Terminal));
        favorites.Add(WindowMatcher.ForExecutable(Browser));

        var arranged = PaletteOrder.Arrange([browser1, editor, browser2, terminal], favorites, new RecentActivation());

        Assert.Equal([terminal.Id, browser2.Id, browser1.Id], arranged.Favorites.Select(e => e.Id));
        Assert.Equal([editor.Id], arranged.CurrentDesktop.Select(e => e.Id));
        Assert.Empty(arranged.OtherDesktops);
    }

    [Fact]
    public void Other_desktop_entries_are_separated_and_ordered_by_same_rule()
    {
        // シナリオ 5: 他のデスクトップは区切りの下。
        var here = Entry(1, Editor, current: true, stacking: 1);
        var thereOld = Entry(2, Terminal, current: false, stacking: 0);
        var thereRecent = Entry(3, Browser, current: false, stacking: 3);
        var recent = new RecentActivation();
        recent.Record(thereRecent.Id);

        var arranged = PaletteOrder.Arrange([thereOld, here, thereRecent], new Favorites(), recent);

        Assert.Equal([here.Id], arranged.CurrentDesktop.Select(e => e.Id));
        Assert.Equal([thereRecent.Id, thereOld.Id], arranged.OtherDesktops.Select(e => e.Id));
    }

    [Fact]
    public void Favorite_without_matching_window_does_not_appear()
    {
        var favorites = new Favorites();
        favorites.Add(WindowMatcher.ForExecutable(Browser));

        var arranged = PaletteOrder.Arrange([Entry(1, Editor)], favorites, new RecentActivation());

        Assert.Empty(arranged.Favorites);
        Assert.Single(arranged.CurrentDesktop);
    }

    [Fact]
    public void Title_conditioned_favorite_only_lifts_matching_titles()
    {
        var favorites = new Favorites();
        favorites.Add(new WindowMatcher(Browser, "Mail"));
        var mail = Entry(1, Browser, "Mail - Inbox");
        var news = Entry(2, Browser, "News");

        var arranged = PaletteOrder.Arrange([news, mail], favorites, new RecentActivation());

        Assert.Equal([mail.Id], arranged.Favorites.Select(e => e.Id));
        Assert.Equal([news.Id], arranged.CurrentDesktop.Select(e => e.Id));
    }
}

public class RecentActivationTests
{
    [Fact]
    public void Re_recording_moves_to_front_without_duplicates()
    {
        var recent = new RecentActivation();
        var a = new WindowId(1);
        var b = new WindowId(2);
        recent.Record(a);
        recent.Record(b);
        recent.Record(a);

        Assert.Equal([a, b], recent.NewestFirst);
        Assert.Equal(0, recent.RankOf(a));
        Assert.Null(recent.RankOf(new WindowId(99)));
    }
}
