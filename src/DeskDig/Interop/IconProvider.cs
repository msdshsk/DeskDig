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

    /// <summary>
    /// シェルが返す 32bpp のビットマップを、透明度を保ったまま WPF の画像にする。
    /// System.Drawing 経由（Image.FromHbitmap → LockBits）は形式変換で透明度を落とし、透明だった画素が不透明の黒になるので使わない。
    /// GDI の GetDIBits で画素をそのまま取り出し、シェルの慣例どおり乗算済みアルファ（PBGRA）として扱う。
    /// </summary>
    private static ImageSource? FromHBitmapWithAlpha(nint hbitmap)
    {
        if (GetObject(hbitmap, Marshal.SizeOf<BITMAP>(), out BITMAP bm) == 0 || bm.bmWidth <= 0 || bm.bmHeight <= 0) return null;
        int width = bm.bmWidth, height = bm.bmHeight, stride = width * 4;
        var pixels = new byte[stride * height];

        var info = new BITMAPINFOHEADER
        {
            biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
            biWidth = width,
            biHeight = -height, // 上から下の並びで受け取る
            biPlanes = 1,
            biBitCount = 32,
            biCompression = 0, // BI_RGB
        };
        nint hdc = CreateCompatibleDC(0);
        try
        {
            var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
            try
            {
                if (GetDIBits(hdc, hbitmap, 0, (uint)height, handle.AddrOfPinnedObject(), ref info, 0 /* DIB_RGB_COLORS */) == 0) return null;
            }
            finally
            {
                handle.Free();
            }
        }
        finally
        {
            DeleteDC(hdc);
        }

        // 透明度を持たない古い形式（アルファが全画素 0）は不透明として扱う
        bool anyAlpha = false;
        for (int i = 3; i < pixels.Length; i += 4)
        {
            if (pixels[i] != 0) { anyAlpha = true; break; }
        }
        if (!anyAlpha)
        {
            for (int i = 3; i < pixels.Length; i += 4) pixels[i] = 255;
        }

        var source = BitmapSource.Create(width, height, 96, 96, PixelFormats.Pbgra32, null, pixels, stride);
        source.Freeze();
        return source;
    }

    [DllImport("gdi32.dll")] private static extern int GetObject(nint hgdiobj, int cbBuffer, out BITMAP lpvObject);
    [DllImport("gdi32.dll")] private static extern nint CreateCompatibleDC(nint hdc);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(nint hdc);
    [DllImport("gdi32.dll")] private static extern int GetDIBits(nint hdc, nint hbm, uint start, uint lines, nint bits, ref BITMAPINFOHEADER bmi, uint usage);

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAP
    {
        public int bmType, bmWidth, bmHeight, bmWidthBytes;
        public ushort bmPlanes, bmBitsPixel;
        public nint bmBits;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth, biHeight;
        public ushort biPlanes, biBitCount;
        public uint biCompression, biSizeImage;
        public int biXPelsPerMeter, biYPelsPerMeter;
        public uint biClrUsed, biClrImportant;
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
