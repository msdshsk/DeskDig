using static DeskDig.Core.Tests.TestData;

namespace DeskDig.Core.Tests;

public class EntryFilterTests
{
    [Fact]
    public void Matches_app_name_or_title_case_insensitively()
    {
        var filter = new EntryFilter("EDIT");

        Assert.True(filter.Matches(Entry(1, Editor, "readme.md")));
        Assert.True(filter.Matches(Entry(2, Browser, "Editing docs")));
        Assert.False(filter.Matches(Entry(3, Terminal, "pwsh")));
    }

    [Fact]
    public void Empty_filter_matches_everything()
    {
        Assert.True(EntryFilter.None.Matches(Entry(1, Editor)));
        Assert.True(new EntryFilter("   ").Matches(Entry(1, Editor)));
    }

    [Fact]
    public void Applies_to_every_section_and_to_pinned_apps()
    {
        // シナリオ 7: 一致するウインドウと PinnedApp だけが残る。
        var arranged = new ArrangedWindowEntries(
            [Entry(1, Browser, "Mail")],
            [Entry(2, Editor, "notes"), Entry(3, Terminal)],
            [Entry(4, Editor, "other")]);
        var apps = new List<PinnedApp>
        {
            new("Editor.lnk", "Editor", Editor, null),
            new("Browser.lnk", "Browser", Browser, null),
        };
        var filter = new EntryFilter("edit");

        var filtered = filter.Apply(arranged);
        var filteredApps = filter.Apply(apps);

        Assert.Empty(filtered.Favorites);
        Assert.Equal([new WindowId(2)], filtered.CurrentDesktop.Select(e => e.Id));
        Assert.Equal([new WindowId(4)], filtered.OtherDesktops.Select(e => e.Id));
        Assert.Equal(["Editor.lnk"], filteredApps.Select(a => a.Id));
        Assert.Equal(new WindowId(2), filtered.Flatten().First().Id);
    }
}
