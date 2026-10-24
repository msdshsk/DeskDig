using static DeskDig.Core.Tests.TestData;

namespace DeskDig.Core.Tests;

public class WindowMatcherTests
{
    [Fact]
    public void Equality_ignores_case_of_path_and_title_condition()
    {
        var a = new WindowMatcher(@"C:\Apps\Editor.EXE", "Readme");
        var b = new WindowMatcher(@"c:\apps\editor.exe", "readme");

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.NotEqual(a, WindowMatcher.ForExecutable(Editor));
    }

    [Fact]
    public void Blank_title_condition_means_no_condition()
    {
        var m = new WindowMatcher(Editor, "   ");
        Assert.False(m.HasTitleCondition);
        Assert.Equal(WindowMatcher.ForExecutable(Editor), m);
    }

    [Fact]
    public void Rejects_empty_executable()
    {
        Assert.Throws<ArgumentException>(() => new WindowMatcher(""));
    }

    [Fact]
    public void Matches_by_executable_and_partial_title()
    {
        var m = new WindowMatcher(Editor, "proj");
        Assert.True(m.Matches(Entry(1, Editor, "my PROJect")));
        Assert.False(m.Matches(Entry(2, Editor, "other")));
        Assert.False(m.Matches(Entry(3, Browser, "project")));
    }

    [Fact]
    public void Title_conditioned_matcher_is_more_specific()
    {
        var plain = WindowMatcher.ForExecutable(Editor);
        var titled = new WindowMatcher(Editor, "x");
        Assert.True(titled.IsMoreSpecificThan(plain));
        Assert.False(plain.IsMoreSpecificThan(titled));
        Assert.False(titled.IsMoreSpecificThan(titled));
    }
}

public class FavoritesTests
{
    [Fact]
    public void Same_matcher_is_registered_once_and_removable()
    {
        var favorites = new Favorites();
        Assert.True(favorites.Add(WindowMatcher.ForExecutable(Browser)));
        Assert.False(favorites.Add(WindowMatcher.ForExecutable(Browser.ToUpperInvariant())));
        Assert.Single(favorites.Items);
        Assert.True(favorites.IsFavorite(Entry(1, Browser)));
        Assert.True(favorites.Remove(WindowMatcher.ForExecutable(Browser)));
        Assert.Empty(favorites.Items);
    }
}
