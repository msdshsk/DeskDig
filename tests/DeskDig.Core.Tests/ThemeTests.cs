using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using DeskDig.Core.Settings;

namespace DeskDig.Core.Tests;

public class ThemeResolutionTests
{
    [Theory]
    [InlineData(Theme.Dark, true, Theme.Dark)]
    [InlineData(Theme.Dark, false, Theme.Dark)]
    [InlineData(Theme.Light, true, Theme.Light)]
    [InlineData(Theme.Light, false, Theme.Light)]
    [InlineData(Theme.System, true, Theme.Light)]
    [InlineData(Theme.System, false, Theme.Dark)]
    public void System_follows_windows_app_mode_and_explicit_choice_wins(Theme preference, bool systemLight, Theme expected)
    {
        Assert.Equal(expected, ThemeResolution.Resolve(preference, systemLight));
    }

    [Fact]
    public void Default_theme_is_System_and_appears_in_settings_catalog()
    {
        Assert.Equal(Theme.System, new PaletteSettings().Theme);
        var descriptor = Assert.Single(SettingDescriptor.Catalog(), d => d.ValueType == typeof(Theme));
        Assert.Equal("テーマ", descriptor.Label);
    }
}

/// <summary>
/// Theme のリソース辞書（src/DeskDig/Themes/*.xaml）を XML として読み、WPF を起動せずに検査する。
/// Dark と Light が同じキー集合を持つこと、色の役割どうしのコントラストが下限を満たすこと、
/// 対象の XAML に色の直書きが残っていないことを保証する（spec §4.10）。
/// </summary>
public class ThemeDictionaryTests
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";
    private static readonly string[] ThemedXaml =
    [
        "src/DeskDig/Palette/PaletteWindow.xaml",
        "src/DeskDig/Palette/SidePanelWindow.xaml",
        "src/DeskDig/Settings/SettingsWindow.xaml",
        "src/DeskDig/Themes/Controls.xaml",
    ];

    private static readonly string[] RequiredKeys =
    [
        "Theme.Background", "Theme.Surface", "Theme.SurfaceHover", "Theme.Border", "Theme.Text", "Theme.TextMuted",
        "Theme.TextHint", "Theme.Input", "Theme.Accent", "Theme.Warning", "Theme.IconPlate",
    ];

    [Fact]
    public void Dark_and_Light_have_the_same_key_set()
    {
        var dark = LoadBrushes("Dark");
        var light = LoadBrushes("Light");

        Assert.Equal(dark.Keys.Order(), light.Keys.Order());
        Assert.All(RequiredKeys, key => Assert.Contains(key, dark.Keys));
    }

    [Theory]
    [InlineData("Dark")]
    [InlineData("Light")]
    public void Every_entry_is_a_SolidColorBrush_with_an_opaque_hex_color(string theme)
    {
        var doc = XDocument.Load(PathOf($"src/DeskDig/Themes/{theme}.xaml"));
        var entries = doc.Root!.Elements().ToList();

        Assert.NotEmpty(entries);
        Assert.All(entries, e =>
        {
            Assert.Equal("SolidColorBrush", e.Name.LocalName);
            Assert.StartsWith("Theme.", (string)e.Attribute(X + "Key")!);
            Assert.Matches("^#[0-9A-Fa-f]{6}$", (string)e.Attribute("Color")!);
        });
    }

    [Theory]
    [InlineData("Dark")]
    [InlineData("Light")]
    public void Text_is_readable_and_accents_are_distinguishable(string theme)
    {
        var c = LoadBrushes(theme);

        // 本文は地と面の両方で WCAG AA（4.5:1）以上
        foreach (var bg in new[] { "Theme.Background", "Theme.Surface", "Theme.Control", "Theme.SurfaceHover", "Theme.Input" })
            Assert.True(Contrast(c["Theme.Text"], c[bg]) >= 4.5, $"{theme}: Text on {bg} = {Contrast(c["Theme.Text"], c[bg]):0.00}");
        // 弱い文字は 4.5:1、ヒントは 3:1 以上
        Assert.True(Contrast(c["Theme.TextMuted"], c["Theme.Background"]) >= 4.5, $"{theme}: TextMuted = {Contrast(c["Theme.TextMuted"], c["Theme.Background"]):0.00}");
        Assert.True(Contrast(c["Theme.TextHint"], c["Theme.Background"]) >= 3.0, $"{theme}: TextHint = {Contrast(c["Theme.TextHint"], c["Theme.Background"]):0.00}");
        // 選択枠と警告色は、面の上で 3:1 以上（UI 部品の下限）
        Assert.True(Contrast(c["Theme.Accent"], c["Theme.Surface"]) >= 3.0, $"{theme}: Accent = {Contrast(c["Theme.Accent"], c["Theme.Surface"]):0.00}");
        Assert.True(Contrast(c["Theme.Warning"], c["Theme.Control"]) >= 3.0, $"{theme}: Warning = {Contrast(c["Theme.Warning"], c["Theme.Control"]):0.00}");
    }

    [Fact]
    public void Icon_plate_is_lighter_than_surface_in_Dark_and_darker_in_Light()
    {
        var dark = LoadBrushes("Dark");
        var light = LoadBrushes("Light");

        // 黒で描かれたアイコンが Dark で、白で描かれたアイコンが Light で見えるよう、板は面より一段強い対比を持つ
        Assert.True(Luminance(dark["Theme.IconPlate"]) > Luminance(dark["Theme.Surface"]) * 1.2, "Dark: IconPlate should be clearly lighter than Surface");
        Assert.True(Luminance(light["Theme.IconPlate"]) < Luminance(light["Theme.Surface"]) * 0.8, "Light: IconPlate should be clearly darker than Surface");
        // 板の上で黒（Dark）/ 白（Light）のアイコンが見える
        Assert.True(Contrast(dark["Theme.IconPlate"], (0, 0, 0)) >= 1.5, "Dark: black icon on IconPlate");
        Assert.True(Contrast(light["Theme.IconPlate"], (255, 255, 255)) >= 1.3, "Light: white icon on IconPlate");
    }

    [Fact]
    public void Themed_xaml_has_no_hard_coded_colors()
    {
        var hex = new Regex("=\"#[0-9A-Fa-f]{3,8}\"");
        var named = new Regex("(Background|Foreground|BorderBrush|Fill|Stroke|CaretBrush)=\"(?!\\{|Transparent\")[A-Za-z]+\"");

        foreach (var relative in ThemedXaml)
        {
            var text = File.ReadAllText(PathOf(relative));
            Assert.False(hex.IsMatch(text), $"{relative} に色の直書きがあります: {hex.Match(text).Value}");
            Assert.False(named.IsMatch(text), $"{relative} に名前付きの色があります: {named.Match(text).Value}");
        }
    }

    // ---- helpers ----

    private static Dictionary<string, (int R, int G, int B)> LoadBrushes(string theme)
    {
        var doc = XDocument.Load(PathOf($"src/DeskDig/Themes/{theme}.xaml"));
        return doc.Root!.Elements()
            .ToDictionary(e => (string)e.Attribute(X + "Key")!, e => Parse((string)e.Attribute("Color")!));
    }

    private static (int R, int G, int B) Parse(string hex) =>
        (int.Parse(hex.AsSpan(1, 2), NumberStyles.HexNumber), int.Parse(hex.AsSpan(3, 2), NumberStyles.HexNumber), int.Parse(hex.AsSpan(5, 2), NumberStyles.HexNumber));

    /// <summary>WCAG 2 の相対輝度。</summary>
    private static double Luminance((int R, int G, int B) c)
    {
        static double Channel(int v)
        {
            double s = v / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
    }

    /// <summary>WCAG 2 のコントラスト比（1..21）。</summary>
    private static double Contrast((int R, int G, int B) a, (int R, int G, int B) b)
    {
        double la = Luminance(a) + 0.05, lb = Luminance(b) + 0.05;
        return la > lb ? la / lb : lb / la;
    }

    private static string PathOf(string relative) => Path.Combine(RepoRoot.Value, relative.Replace('/', Path.DirectorySeparatorChar));

    private static readonly Lazy<string> RepoRoot = new(() =>
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DeskDig.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("DeskDig.sln が見つかりません");
    });
}
