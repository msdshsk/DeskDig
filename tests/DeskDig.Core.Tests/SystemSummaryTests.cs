namespace DeskDig.Core.Tests;

public class SystemSummaryTests
{
    private static readonly SystemSummary Sample = new(
        "DESKTOP-01", "Windows 11 Pro", "24H2", "26100.2894", "Example CPU 16-Core", "64 GB", "Example GPU");

    [Fact]
    public void Plain_text_has_one_labelled_line_per_item()
    {
        var text = Sample.ToPlainText();

        Assert.Equal(
            [
                "デバイス名: DESKTOP-01",
                "Windows: Windows 11 Pro",
                "バージョン: 24H2 (ビルド 26100.2894)",
                "CPU: Example CPU 16-Core",
                "メモリ: 64 GB",
                "GPU: Example GPU",
            ],
            text.Split(Environment.NewLine));
    }

    [Fact]
    public void Gpu_shows_pending_until_filled()
    {
        var pending = Sample with { Gpu = null };

        Assert.Contains(("GPU", SystemSummary.Pending), pending.Lines());
        Assert.Equal("Example GPU", pending.WithGpu("Example GPU").Gpu);
        Assert.Equal(SystemSummary.Unknown, pending.WithGpu("  ").Gpu);
    }

    [Theory]
    [InlineData(64L * 1024 * 1024, "64 GB")]
    [InlineData(16L * 1024 * 1024, "16 GB")]
    [InlineData(7_900_000L, "7.5 GB")]
    [InlineData(0L, SystemSummary.Unknown)]
    public void Memory_is_formatted_in_gigabytes(long kilobytes, string expected)
    {
        Assert.Equal(expected, SystemSummary.FormatMemory(kilobytes));
    }

    [Theory]
    [InlineData("Windows 10 Pro", 26100, "Windows 11 Pro")]
    [InlineData("Windows 10 Pro", 19045, "Windows 10 Pro")]
    [InlineData("Windows 11 Home", 22631, "Windows 11 Home")]
    [InlineData(null, 26100, SystemSummary.Unknown)]
    public void Edition_is_corrected_for_windows_11_builds(string? productName, int build, string expected)
    {
        Assert.Equal(expected, SystemSummary.NormalizeEdition(productName, build));
    }
}
