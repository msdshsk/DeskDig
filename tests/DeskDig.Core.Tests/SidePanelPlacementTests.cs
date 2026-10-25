namespace DeskDig.Core.Tests;

public class SidePanelPlacementTests
{
    private static readonly Bounds Work = TestData.Primary.WorkArea; // 3840 x 2100

    [Fact]
    public void Opens_to_the_right_with_same_top_and_height()
    {
        var palette = new Bounds(1000, 500, 1016, 680);

        var panel = SidePanelPlacement.Resolve(palette, 320, 8, Work);

        Assert.Equal(new Bounds(1000 + 1016 + 8, 500, 320, 680), panel);
    }

    [Fact]
    public void Falls_back_to_the_left_when_right_does_not_fit()
    {
        var palette = new Bounds(3840 - 1016 - 100, 500, 1016, 680); // 右余白 100 px では 320 px が収まらない

        var panel = SidePanelPlacement.Resolve(palette, 320, 8, Work);

        Assert.Equal(palette.Left - 8 - 320, panel.Left);
        Assert.Equal(palette.Top, panel.Top);
        Assert.Equal(palette.Height, panel.Height);
    }

    [Fact]
    public void Right_edge_exactly_fitting_is_still_right()
    {
        var palette = new Bounds(3840 - 1016 - 8 - 320, 0, 1016, 680);

        var panel = SidePanelPlacement.Resolve(palette, 320, 8, Work);

        Assert.Equal(palette.Right + 8, panel.Left);
        Assert.Equal(Work.Right, panel.Right);
    }

    [Fact]
    public void Clamps_inside_work_area_when_neither_side_fits()
    {
        var narrow = new Bounds(0, 0, 1200, 800);
        var palette = new Bounds(50, 0, 1016, 680);

        var panel = SidePanelPlacement.Resolve(palette, 320, 8, narrow);

        Assert.True(panel.Left >= narrow.Left);
        Assert.True(panel.Right <= narrow.Right);
    }
}
