namespace DeskDig.Core.Tests;

internal static class TestData
{
    public const string Browser = @"C:\Apps\browser.exe";
    public const string Editor = @"C:\Apps\editor.exe";
    public const string Terminal = @"C:\Apps\terminal.exe";

    public static WindowEntry Entry(long id, string exe, string title = "", bool current = true, int stacking = 0, bool minimized = false, string? aumid = null) =>
        new(new WindowId(id), title.Length == 0 ? $"Title{id}" : title, Path.GetFileNameWithoutExtension(exe), exe, current, stacking, minimized, aumid);

    public static readonly MonitorInfo Primary = new(@"\\.\DISPLAY1", new Bounds(0, 0, 3840, 2160), new Bounds(0, 0, 3840, 2100));
    public static readonly MonitorInfo Secondary = new(@"\\.\DISPLAY2", new Bounds(3840, 0, 1920, 1080), new Bounds(3840, 0, 1920, 1040));
}
