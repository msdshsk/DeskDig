namespace DeskDig;

/// <summary>%APPDATA%\DeskDig\log.txt への簡易ログ。失敗しても握りつぶす。</summary>
internal static class Diagnostics
{
    private static readonly object Gate = new();
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DeskDig", "log.txt");

    public static void Log(string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                File.AppendAllText(LogPath, $"{DateTime.Now:HH:mm:ss.fff} {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // ログに失敗しても本体には影響させない
        }
    }

    public static void Log(string context, Exception ex) => Log($"{context}: {ex}");
}
