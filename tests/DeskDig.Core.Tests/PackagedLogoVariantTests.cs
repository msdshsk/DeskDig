namespace DeskDig.Core.Tests;

public class PackagedLogoVariantTests
{
    // 実在のパッケージに近い候補一覧
    private static readonly string[] FullSet =
    [
        "AppList.png", "AppList.scale-200.png",
        "AppList.targetsize-16.png", "AppList.targetsize-16_altform-unplated.png", "AppList.targetsize-16_altform-lightunplated.png",
        "AppList.targetsize-32.png", "AppList.targetsize-32_altform-unplated.png", "AppList.targetsize-32_altform-lightunplated.png",
        "AppList.targetsize-48.png", "AppList.targetsize-48_altform-unplated.png", "AppList.targetsize-48_altform-lightunplated.png",
        "AppList.targetsize-256_altform-unplated.png", "AppList.targetsize-32_contrast-white.png", "AppList2.targetsize-32.png",
    ];

    [Fact]
    public void Dark_prefers_unplated_and_light_prefers_lightunplated_at_matching_size()
    {
        Assert.Equal("AppList.targetsize-32_altform-unplated.png", PackagedLogoVariant.Choose(FullSet, "AppList", 32, LogoBackdrop.Dark));
        Assert.Equal("AppList.targetsize-32_altform-lightunplated.png", PackagedLogoVariant.Choose(FullSet, "AppList", 32, LogoBackdrop.Light));
    }

    [Fact]
    public void Picks_next_larger_targetsize_when_exact_is_missing()
    {
        Assert.Equal("AppList.targetsize-48_altform-unplated.png", PackagedLogoVariant.Choose(FullSet, "AppList", 40, LogoBackdrop.Dark));
        Assert.Equal("AppList.targetsize-256_altform-unplated.png", PackagedLogoVariant.Choose(FullSet, "AppList", 100, LogoBackdrop.Dark));
    }

    [Fact]
    public void Falls_back_to_plated_then_opposite_altform()
    {
        string[] onlyUnplatedAndPlain = ["Logo.png", "Logo.scale-200.png", "Logo.targetsize-24_altform-unplated.png"];

        // Light では lightunplated が無いので無印（板付き）を使い、無印の中では scale の最大
        Assert.Equal("Logo.scale-200.png", PackagedLogoVariant.Choose(onlyUnplatedAndPlain, "Logo", 32, LogoBackdrop.Light));
        // Dark では unplated があるのでそれ
        Assert.Equal("Logo.targetsize-24_altform-unplated.png", PackagedLogoVariant.Choose(onlyUnplatedAndPlain, "Logo", 32, LogoBackdrop.Dark));

        string[] onlyLightUnplated = ["Logo.targetsize-32_altform-lightunplated.png"];
        Assert.Equal("Logo.targetsize-32_altform-lightunplated.png", PackagedLogoVariant.Choose(onlyLightUnplated, "Logo", 32, LogoBackdrop.Dark));
    }

    [Fact]
    public void Ignores_contrast_variants_other_base_names_and_non_png()
    {
        string[] files = ["AppList.targetsize-32_contrast-white.png", "AppList2.targetsize-32.png", "AppList.targetsize-32.jpg"];
        Assert.Null(PackagedLogoVariant.Choose(files, "AppList", 32, LogoBackdrop.Dark));
        Assert.Null(PackagedLogoVariant.Choose([], "AppList", 32, LogoBackdrop.Dark));
    }
}
