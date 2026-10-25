namespace DeskDig.Core.Tests;

public class SettingsShortcutTests
{
    [Fact]
    public void Catalog_is_fixed_in_spec_order()
    {
        // spec §4.8 の表の順。設定による出し入れは無い
        Assert.Equal(
            ["environment", "sound-dialog", "network", "bluetooth", "display", "about"],
            SettingsShortcutCatalog.All.Select(s => s.Id));
    }

    [Fact]
    public void Destinations_follow_spec()
    {
        var byId = SettingsShortcutCatalog.All.ToDictionary(s => s.Id);

        Assert.Equal("rundll32.exe", byId["environment"].FileName);
        Assert.Equal("sysdm.cpl,EditEnvironmentVariables", byId["environment"].Arguments);
        Assert.Equal("mmsys.cpl", byId["sound-dialog"].FileName);
        Assert.Equal("ms-settings:network", byId["network"].FileName);
        Assert.Equal("ms-settings:bluetooth", byId["bluetooth"].FileName);
        Assert.Equal("ms-settings:display", byId["display"].FileName);
        Assert.Equal("ms-settings:about", byId["about"].FileName);
    }

    [Fact]
    public void Destination_text_joins_file_name_and_arguments()
    {
        var env = SettingsShortcutCatalog.All.Single(s => s.Id == "environment");
        var about = SettingsShortcutCatalog.All.Single(s => s.Id == "about");

        Assert.Equal("rundll32.exe sysdm.cpl,EditEnvironmentVariables", env.Destination);
        Assert.Equal("ms-settings:about", about.Destination);
    }

    [Fact]
    public void Every_candidate_has_name_glyph_and_unique_id()
    {
        var all = SettingsShortcutCatalog.All;

        Assert.Equal(all.Count, all.Select(s => s.Id).Distinct().Count());
        Assert.All(all, s =>
        {
            Assert.False(string.IsNullOrWhiteSpace(s.Name));
            Assert.False(string.IsNullOrWhiteSpace(s.FileName));
            Assert.False(string.IsNullOrWhiteSpace(s.Glyph));
        });
    }
}
