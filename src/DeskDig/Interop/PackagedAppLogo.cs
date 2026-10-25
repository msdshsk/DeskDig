using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;
using DeskDig.Core;

namespace DeskDig.Interop;

/// <summary>
/// パッケージ型アプリ（MSIX / ストア）のロゴを、シェルのアイコン API を通さずパッケージから直接読む。
/// シェル経由では一部のアプリ（背景色が transparent のもの）が真っ黒な画像になるため。
/// AppUserModelId → パッケージの場所 → AppxManifest.xml の Square44x44Logo → テーマとサイズに合うバリエーション、の順に解決する。
/// </summary>
internal static class PackagedAppLogo
{
    private const int ERROR_INSUFFICIENT_BUFFER = 122;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetPackagesByPackageFamily(string packageFamilyName, ref uint count, nint packageFullNames, ref uint bufferLength, nint buffer);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetPackagePathByFullName(string packageFullName, ref uint pathLength, StringBuilder? path);

    /// <summary>マニフェストから読んだロゴの基準パスと、板付き表示に使う背景色（transparent なら null）。</summary>
    private sealed record LogoInfo(string LogoBase, Color? BackgroundColor);

    private static readonly Dictionary<string, LogoInfo?> LogoInfoCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>ロゴを読む。取れなければ null（呼び出し側がシェル経由に退避する）。</summary>
    public static ImageSource? Load(string aumid, int size, LogoBackdrop backdrop)
    {
        try
        {
            string? file = ResolveFile(aumid, size, backdrop);
            if (file is null) return null;

            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = new Uri(file, UriKind.Absolute);
            image.EndInit();
            image.Freeze();

            // 透過版（altform）が無く無印を使うときは、タスクバーと同じくマニフェストの背景色で板を敷く
            bool plated = !Path.GetFileName(file).Contains("altform-", StringComparison.OrdinalIgnoreCase);
            var background = ResolveInfo(aumid)?.BackgroundColor;
            return plated && background is { } color ? Plate(image, color) : image;
        }
        catch (Exception ex)
        {
            Diagnostics.Log($"Packaged logo load failed for {aumid}", ex);
            return null;
        }
    }

    /// <summary>サイズと背景に合うロゴ画像のパス。診断にも使う。</summary>
    public static string? ResolveFile(string aumid, int size, LogoBackdrop backdrop)
    {
        string? logoBase = ResolveInfo(aumid)?.LogoBase;
        if (logoBase is null) return null;

        string directory = Path.GetDirectoryName(logoBase)!;
        string baseName = Path.GetFileNameWithoutExtension(logoBase);
        if (!Directory.Exists(directory)) return null;

        var candidates = Directory.EnumerateFiles(directory, baseName + "*.png").Select(f => Path.GetFileName(f)!);
        string? chosen = PackagedLogoVariant.Choose(candidates, baseName, size, backdrop);
        return chosen is null ? null : Path.Combine(directory, chosen);
    }

    /// <summary>ロゴを背景色の板の上に描く。</summary>
    private static ImageSource Plate(BitmapSource logo, Color background)
    {
        int w = logo.PixelWidth, h = logo.PixelHeight;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(background), null, new System.Windows.Rect(0, 0, w, h));
            dc.DrawImage(logo, new System.Windows.Rect(0, 0, w, h));
        }
        var target = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);
        target.Freeze();
        return target;
    }

    private static LogoInfo? ResolveInfo(string aumid)
    {
        lock (LogoInfoCache)
        {
            if (LogoInfoCache.TryGetValue(aumid, out var cached)) return cached;
        }
        var info = ReadInfo(aumid);
        lock (LogoInfoCache)
        {
            LogoInfoCache[aumid] = info;
        }
        return info;
    }

    /// <summary>AppxManifest.xml の Square44x44Logo（修飾子なしの基準名の絶対パス）と BackgroundColor を読む。</summary>
    private static LogoInfo? ReadInfo(string aumid)
    {
        LogoInfo? result = null;
        int bang = aumid.IndexOf('!');
        if (bang > 0)
        {
            string familyName = aumid[..bang];
            string appId = aumid[(bang + 1)..];
            string? fullName = FirstPackageFullName(familyName);
            string? packagePath = fullName is null ? null : PackagePath(fullName);
            string? manifest = packagePath is null ? null : Path.Combine(packagePath, "AppxManifest.xml");
            if (manifest is not null && File.Exists(manifest))
            {
                var doc = XDocument.Load(manifest);
                var application = doc.Descendants()
                    .FirstOrDefault(e => e.Name.LocalName == "Application" && string.Equals((string?)e.Attribute("Id"), appId, StringComparison.OrdinalIgnoreCase));
                var visual = application?.Elements().FirstOrDefault(e => e.Name.LocalName == "VisualElements");
                string? logo = (string?)visual?.Attribute("Square44x44Logo");
                if (!string.IsNullOrWhiteSpace(logo))
                {
                    result = new LogoInfo(Path.Combine(packagePath!, logo.Replace('/', '\\')), ParseBackground((string?)visual?.Attribute("BackgroundColor")));
                }
            }
        }
        return result;
    }

    /// <summary>マニフェストの BackgroundColor（#RRGGBB か色名。transparent は板なし）。</summary>
    private static Color? ParseBackground(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Equals("transparent", StringComparison.OrdinalIgnoreCase)) return null;
        try
        {
            return ColorConverter.ConvertFromString(value) is Color c ? c : null;
        }
        catch
        {
            return null;
        }
    }

    private static string? FirstPackageFullName(string familyName)
    {
        uint count = 0, length = 0;
        int rc = GetPackagesByPackageFamily(familyName, ref count, 0, ref length, 0);
        if (rc != ERROR_INSUFFICIENT_BUFFER || count == 0) return null;

        nint names = Marshal.AllocHGlobal((int)count * nint.Size);
        nint buffer = Marshal.AllocHGlobal((int)length * sizeof(char));
        try
        {
            rc = GetPackagesByPackageFamily(familyName, ref count, names, ref length, buffer);
            if (rc != 0 || count == 0) return null;
            return Marshal.PtrToStringUni(Marshal.ReadIntPtr(names));
        }
        finally
        {
            Marshal.FreeHGlobal(names);
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static string? PackagePath(string fullName)
    {
        uint length = 0;
        int rc = GetPackagePathByFullName(fullName, ref length, null);
        if (rc != ERROR_INSUFFICIENT_BUFFER || length == 0) return null;
        var sb = new StringBuilder((int)length);
        rc = GetPackagePathByFullName(fullName, ref length, sb);
        return rc == 0 ? sb.ToString() : null;
    }
}
