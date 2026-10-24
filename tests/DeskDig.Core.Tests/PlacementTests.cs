using static DeskDig.Core.Tests.TestData;

namespace DeskDig.Core.Tests;

public class PlacementTests
{
    [Theory]
    [InlineData(0, 100)]
    [InlineData(100, 0)]
    [InlineData(-10, 100)]
    public void Non_positive_size_cannot_be_constructed(int w, int h)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Placement(new Bounds(0, 0, w, h), Primary.Id));
    }

    [Fact]
    public void Missing_monitor_reference_cannot_be_constructed()
    {
        Assert.Throws<ArgumentException>(() => new Placement(new Bounds(0, 0, 10, 10), ""));
    }

    [Fact]
    public void Existing_monitor_keeps_placement_unchanged()
    {
        var placement = new Placement(new Bounds(100, 100, 800, 600), Secondary.Id);
        Assert.Same(placement, placement.ResolveAgainst([Primary, Secondary]));
    }

    [Fact]
    public void Missing_monitor_falls_back_to_nearest_and_fits_inside_work_area()
    {
        // 記録時は DISPLAY2（右側）。DISPLAY2 が無くなったら最寄り（DISPLAY1）の作業領域に収める。
        var placement = new Placement(new Bounds(4000, 100, 1500, 900), Secondary.Id);

        var resolved = placement.ResolveAgainst([Primary]);

        Assert.Equal(Primary.Id, resolved.MonitorId);
        Assert.Equal(new Bounds(3840 - 1500, 100, 1500, 900), resolved.Area);
    }

    [Fact]
    public void Oversized_placement_is_shrunk_to_work_area()
    {
        var placement = new Placement(new Bounds(-50, -50, 5000, 5000), "gone");

        var resolved = placement.ResolveAgainst([Secondary]);

        Assert.Equal(Secondary.WorkArea, resolved.Area);
    }

    [Fact]
    public void Window_bounds_expand_visible_area_by_frame_insets()
    {
        var placement = new Placement(new Bounds(100, 50, 800, 600), Primary.Id);
        var insets = new FrameInsets(7, 0, 7, 7);

        Assert.Equal(new Bounds(93, 50, 814, 607), placement.ToWindowBounds(insets));
    }
}

public class PlacementProfilesTests
{
    private static readonly Placement P1 = new(new Bounds(0, 0, 100, 100), Primary.Id);
    private static readonly Placement P2 = new(new Bounds(10, 10, 200, 200), Primary.Id);

    [Fact]
    public void Recording_same_matcher_overwrites()
    {
        var profiles = new PlacementProfiles();
        profiles.Record(WindowMatcher.ForExecutable(Editor), P1);
        profiles.Record(WindowMatcher.ForExecutable(Editor), P2);

        Assert.Single(profiles.Items);
        Assert.Equal(P2, profiles.Items[0].Placement);
    }

    [Fact]
    public void Title_conditioned_profile_wins_over_executable_only()
    {
        var profiles = new PlacementProfiles();
        profiles.Record(WindowMatcher.ForExecutable(Editor), P1);
        profiles.Record(new WindowMatcher(Editor, "notes"), P2);

        Assert.Equal(P2, profiles.FindFor(Entry(1, Editor, "my notes"))!.Placement);
        Assert.Equal(P1, profiles.FindFor(Entry(2, Editor, "other"))!.Placement);
        Assert.Null(profiles.FindFor(Entry(3, Browser)));
    }

    [Fact]
    public void Replacing_matcher_keeps_placement_and_resolves_collision()
    {
        var profiles = new PlacementProfiles();
        var plain = WindowMatcher.ForExecutable(Editor);
        var titled = new WindowMatcher(Editor, "x");
        profiles.Record(plain, P1);
        profiles.Record(titled, P2);

        Assert.True(profiles.ReplaceMatcher(plain, titled));

        Assert.Single(profiles.Items);
        Assert.Equal(titled, profiles.Items[0].Matcher);
        Assert.Equal(P1, profiles.Items[0].Placement);
    }

    [Fact]
    public void Remove_deletes_profile()
    {
        var profiles = new PlacementProfiles();
        profiles.Record(WindowMatcher.ForExecutable(Editor), P1);
        Assert.True(profiles.Remove(WindowMatcher.ForExecutable(Editor)));
        Assert.False(profiles.HasProfileFor(Entry(1, Editor)));
    }
}
