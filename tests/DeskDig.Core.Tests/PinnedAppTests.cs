using static DeskDig.Core.Tests.TestData;

namespace DeskDig.Core.Tests;

public class PinnedAppTests
{
    private static PinnedApp App(string id, string? exe = null, string? aumid = null) => new(id, id, exe, aumid);

    [Fact]
    public void Saved_order_is_kept_new_pins_appended_removed_pins_dropped()
    {
        var current = new List<PinnedApp> { App("a"), App("b"), App("c"), App("d") };
        string[] saved = ["c", "gone", "a"];

        var arranged = PinnedAppOrder.Arrange(saved, current);

        Assert.Equal(["c", "a", "b", "d"], arranged.Select(a => a.Id));
    }

    [Fact]
    public void Move_reorders_by_target_position()
    {
        var apps = new List<PinnedApp> { App("a"), App("b"), App("c"), App("d") };

        Assert.Equal(["b", "c", "a", "d"], PinnedAppOrder.Move(apps, "a", "c").Select(a => a.Id));
        Assert.Equal(["d", "a", "b", "c"], PinnedAppOrder.Move(apps, "d", "a").Select(a => a.Id));
        Assert.Same(apps, PinnedAppOrder.Move(apps, "a", "a"));
    }

    [Fact]
    public void Launcher_resolves_topmost_matching_window_or_nothing()
    {
        // シナリオ 6: 既に開いている PinnedApp は新規起動せず既存ウインドウを Activate。
        var editorApp = App("Editor", exe: Editor);
        var storeApp = App("Calc", aumid: "Calc_app!App");
        var arranged = new ArrangedWindowEntries(
            [Entry(1, Browser)],
            [Entry(2, Editor, "second"), Entry(3, Editor, "third")],
            [Entry(4, Terminal, aumid: "Calc_app!App")]);

        Assert.Equal(new WindowId(2), Launcher.ResolveExistingWindow(editorApp, arranged)!.Id);
        Assert.Equal(new WindowId(4), Launcher.ResolveExistingWindow(storeApp, arranged)!.Id);
        Assert.Null(Launcher.ResolveExistingWindow(App("None", exe: @"C:\none.exe"), arranged));
    }
}

public class PalettePositioningTests
{
    [Fact]
    public void Palette_opens_at_anchor_when_it_fits()
    {
        var bounds = PalettePositioning.Resolve(new ScreenPoint(100, 200), 800, 600, Primary.WorkArea);
        Assert.Equal(new Bounds(100, 200, 800, 600), bounds);
    }

    [Fact]
    public void Palette_is_shifted_back_inside_work_area_near_edges()
    {
        var bounds = PalettePositioning.Resolve(new ScreenPoint(3800, 2000), 800, 600, Primary.WorkArea);
        Assert.Equal(new Bounds(3840 - 800, 2100 - 600, 800, 600), bounds);
    }

    [Fact]
    public void Palette_is_shrunk_when_larger_than_work_area()
    {
        var bounds = PalettePositioning.Resolve(new ScreenPoint(4000, 10), 3000, 2000, Secondary.WorkArea);
        Assert.Equal(new Bounds(3840, 0, 1920, 1040), bounds);
    }
}

public class SettingsCatalogTests
{
    [Fact]
    public void Every_declared_setting_is_listed_with_label()
    {
        var catalog = Core.Settings.SettingDescriptor.Catalog();
        Assert.Contains(catalog, d => d.Property.Name == nameof(Core.Settings.PaletteSettings.Hotkey));
        Assert.All(catalog, d => Assert.False(string.IsNullOrWhiteSpace(d.Label)));
    }

    [Fact]
    public void Int_settings_are_clamped_to_declared_range()
    {
        var settings = new Core.Settings.PaletteSettings();
        var columns = Core.Settings.SettingDescriptor.Catalog().Single(d => d.Property.Name == nameof(Core.Settings.PaletteSettings.GridColumns));
        columns.SetValue(settings, 0);
        Assert.Equal(1, settings.GridColumns);
        columns.SetValue(settings, 100);
        Assert.Equal(12, settings.GridColumns);
    }
}
