using System.Drawing;
using System.Windows.Forms;

namespace DeskDig.Tray;

/// <summary>タスクトレイアイコン。メニューは「設定を開く」「終了」。</summary>
internal sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly Icon _drawnIcon;

    public event Action? OpenSettingsRequested;
    public event Action? ExitRequested;

    public TrayIcon()
    {
        _drawnIcon = LoadApplicationIcon() ?? DrawIcon();
        var menu = new ContextMenuStrip();
        menu.Items.Add("設定を開く", null, (_, _) => OpenSettingsRequested?.Invoke());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("終了", null, (_, _) => ExitRequested?.Invoke());
        _icon = new NotifyIcon
        {
            Icon = _drawnIcon,
            Text = "DeskDig",
            Visible = true,
            ContextMenuStrip = menu,
        };
        _icon.DoubleClick += (_, _) => OpenSettingsRequested?.Invoke();
    }

    public void ShowBalloon(string title, string text) => _icon.ShowBalloonTip(3000, title, text, ToolTipIcon.Warning);

    /// <summary>exe に埋め込まれたアプリアイコン（scripts/make-icon.ps1 で生成）。取れなければ null。</summary>
    private static Icon? LoadApplicationIcon()
    {
        try
        {
            var exe = Environment.ProcessPath;
            return exe is null ? null : Icon.ExtractAssociatedIcon(exe);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>アプリアイコンが取れない場合の予備。同じ図柄をその場で描く。</summary>
    private static Icon DrawIcon()
    {
        using var bitmap = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.Clear(Color.Transparent);
            using var back = new SolidBrush(Color.FromArgb(40, 44, 52));
            g.FillRectangle(back, 2, 2, 28, 28);
            using var tile = new SolidBrush(Color.FromArgb(97, 175, 239));
            g.FillRectangle(tile, 6, 6, 8, 8);
            g.FillRectangle(tile, 18, 6, 8, 8);
            g.FillRectangle(tile, 6, 18, 8, 8);
            using var accent = new SolidBrush(Color.FromArgb(229, 192, 123));
            g.FillRectangle(accent, 18, 18, 8, 8);
        }
        nint handle = bitmap.GetHicon();
        try
        {
            return (Icon)Icon.FromHandle(handle).Clone();
        }
        finally
        {
            Interop.NativeMethods.DestroyIcon(handle);
        }
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _drawnIcon.Dispose();
    }
}
