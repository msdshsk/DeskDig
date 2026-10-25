using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DeskDig.Core;
using static DeskDig.Interop.NativeMethods;

namespace DeskDig.Interop;

/// <summary>アプリアイコンの取得。シェルの画像ファクトリを使い、ストアアプリも同じ経路で得る。</summary>
internal sealed class IconProvider
{
    private static readonly Guid IID_IShellItemImageFactory = new("bcc18b79-ba16-442f-80c4-8a59c30c463b");
    private const int SIIGBF_ICONONLY = 0x4;
    private const int SIIGBF_BIGGERSIZEOK = 0x1;

    private readonly Dictionary<string, ImageSource?> _cache = new(StringComparer.OrdinalIgnoreCase);

    public ImageSource? ForEntry(WindowEntry entry, int size)
    {
        if (entry.AppUserModelId is not null)
        {
            var fromApp = ForParsingName($"shell:AppsFolder\\{entry.AppUserModelId}", size);
            if (fromApp is not null) return fromApp;
        }
        return ForParsingName(entry.ExecutablePath, size) ?? FromWindow((nint)entry.Id.Value);
    }

    public ImageSource? ForPinnedApp(PinnedApp app, int size)
    {
        var parsingName = PinnedAppReader.ParsingNameOf(app);
        return parsingName is null ? null : ForParsingName(parsingName, size);
    }

    private const string AppsFolderPrefix = "shell:AppsFolder\\";

    public ImageSource? ForParsingName(string parsingName, int size)
    {
        bool packaged = parsingName.StartsWith(AppsFolderPrefix, StringComparison.OrdinalIgnoreCase);
        var backdrop = Theming.ThemeManager.Effective == Core.Settings.Theme.Light ? LogoBackdrop.Light : LogoBackdrop.Dark;
        // パッケージ型はテーマで選ぶロゴが変わるので、キャッシュのキーにも背景を含める
        string key = packaged ? $"{size}|{parsingName}|{backdrop}" : $"{size}|{parsingName}";
        if (_cache.TryGetValue(key, out var cached)) return cached;
        ImageSource? image = null;
        if (packaged)
        {
            // パッケージのロゴを直接読む。シェル経由だと一部のアプリで真っ黒な画像になる
            image = PackagedAppLogo.Load(parsingName[AppsFolderPrefix.Length..], size, backdrop);
            if (image is not null)
            {
                _cache[key] = image;
                return image;
            }
        }
        try
        {
            var iid = IID_IShellItemImageFactory;
            SHCreateItemFromParsingName(parsingName, 0, ref iid, out object obj);
            var factory = (IShellItemImageFactory)obj;
            if (factory.GetImage(new SIZE { cx = size, cy = size }, SIIGBF_ICONONLY | SIIGBF_BIGGERSIZEOK, out nint hbitmap) == 0 && hbitmap != 0)
            {
                try
                {
                    image = FromHBitmapWithAlpha(hbitmap);
                }
                finally
                {
                    DeleteObject(hbitmap);
                }
            }
        }
        catch
        {
            image = null;
        }
        _cache[key] = image;
        return image;
    }

    private static ImageSource? FromHBitmapWithAlpha(nint hbitmap)
    {
        using var bitmap = System.Drawing.Image.FromHbitmap(hbitmap);
        // FromHbitmap は 32bpp のアルファを落とすため、生ピクセルを直接読んで BGRA として扱う
        var rect = new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var data = bitmap.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        try
        {
            var source = BitmapSource.Create(bitmap.Width, bitmap.Height, 96, 96, PixelFormats.Bgra32, null, data.Scan0, data.Stride * bitmap.Height, data.Stride);
            source.Freeze();
            return source;
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    private static ImageSource? FromWindow(nint hwnd)
    {
        try
        {
            SendMessageTimeout(hwnd, WM_GETICON, ICON_BIG, 0, SMTO_ABORTIFHUNG, 200, out nint hicon);
            if (hicon == 0) hicon = GetClassLongPtr(hwnd, GCLP_HICON);
            if (hicon == 0) return null;
            var image = Imaging.CreateBitmapSourceFromHIcon(hicon, System.Windows.Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
    }
}
