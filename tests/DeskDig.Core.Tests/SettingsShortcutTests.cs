using DeskDig.Core.Settings;

namespace DeskDig.Core.Tests;

public class SettingsShortcutTests
{
    [Fact]
    public void Defaults_show_about_sound_and_network_only()
    {
        var enabled = SettingsShortcutCatalog.Enabled(new PaletteSettings());

        Assert.Equal(["about", "sound", "network"], enabled.Select(s => s.Id));
    }

    [Fact]
    public void Enabling_a_candidate_in_settings_adds_it_in_catalog_order()
    {
        var settings = new PaletteSettings { ShortcutWifi = true, ShortcutAbout = false };

        var enabled = SettingsShortcutCatalog.Enabled(settings);

        Assert.Equal(["sound", "network", "wifi"], enabled.Select(s => s.Id));
    }

    [Fact]
    public void Filter_matches_name_or_keyword_case_insensitively()
    {
        // シナリオ 9: 「音量」と入力すると「サウンド」だけが残る
        var all = SettingsShortcutCatalog.All;
        var byVolume = all.Where(s => s.Matches(new EntryFilter("音量"))).Select(s => s.Id).ToList();
        var byWifi = all.Where(s => s.Matches(new EntryFilter("WIFI"))).Select(s => s.Id).ToList();

        Assert.Equal(["sound"], byVolume);
        Assert.Equal(["wifi"], byWifi);
        Assert.Equal(all.Count, all.Count(s => s.Matches(EntryFilter.None)));
    }

    [Fact]
    public void Every_candidate_has_destination_glyph_and_unique_id()
    {
        var all = SettingsShortcutCatalog.All;

        Assert.Equal(all.Count, all.Select(s => s.Id).Distinct().Count());
        Assert.All(all, s =>
        {
            Assert.False(string.IsNullOrWhiteSpace(s.Destination));
            Assert.False(string.IsNullOrWhiteSpace(s.Glyph));
        });
    }
}
