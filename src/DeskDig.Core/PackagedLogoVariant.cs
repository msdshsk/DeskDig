namespace DeskDig.Core;

/// <summary>ロゴを置く背景の明暗。Theme の実効値から決める。</summary>
public enum LogoBackdrop { Dark, Light }

/// <summary>
/// パッケージ型アプリ（MSIX / ストア）のロゴ画像のバリエーション選び。
/// マニフェストの Square44x44Logo は基準名だけを指し、実際のファイルは
/// 「基準名.targetsize-32_altform-unplated.png」のように修飾子付きで複数ある。
/// タスクバーと同じく、暗い背景には altform-unplated、明るい背景には altform-lightunplated を優先し、
/// 無ければ無印（板付き）、それも無ければ逆の altform を使う。
/// </summary>
public static class PackagedLogoVariant
{
    private sealed record Variant(string FileName, int TargetSize, int Scale, string Altform, bool Excluded);

    /// <summary>候補ファイル名（拡張子付き）から、サイズと背景に最も合う 1 件を返す。無ければ null。</summary>
    public static string? Choose(IEnumerable<string> fileNames, string baseName, int size, LogoBackdrop backdrop)
    {
        var variants = fileNames
            .Select(f => Parse(f, baseName))
            .Where(v => v is { Excluded: false })
            .Select(v => v!)
            .ToList();
        if (variants.Count == 0) return null;

        string preferred = backdrop == LogoBackdrop.Dark ? "unplated" : "lightunplated";
        string opposite = backdrop == LogoBackdrop.Dark ? "lightunplated" : "unplated";
        foreach (var altform in new[] { preferred, string.Empty, opposite })
        {
            var group = variants.Where(v => v.Altform == altform).ToList();
            if (group.Count > 0) return PickBySize(group, size).FileName;
        }
        return null;
    }

    /// <summary>targetsize は希望以上で最小、無ければ最大。targetsize が無ければ scale の最大、それも無ければ無印。</summary>
    private static Variant PickBySize(List<Variant> group, int size)
    {
        var targets = group.Where(v => v.TargetSize > 0).ToList();
        if (targets.Count > 0)
        {
            return targets.Where(v => v.TargetSize >= size).OrderBy(v => v.TargetSize).FirstOrDefault()
                ?? targets.OrderByDescending(v => v.TargetSize).First();
        }
        var scales = group.Where(v => v.Scale > 0).OrderByDescending(v => v.Scale).ToList();
        if (scales.Count > 0) return scales[0];
        return group[0];
    }

    /// <summary>「基準名[.修飾子_修飾子...].png」を分解する。基準名が違う、または png でなければ null。</summary>
    private static Variant? Parse(string fileName, string baseName)
    {
        if (!fileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) return null;
        string stem = fileName[..^4];
        if (!stem.StartsWith(baseName, StringComparison.OrdinalIgnoreCase)) return null;
        string rest = stem[baseName.Length..];
        if (rest.Length > 0 && rest[0] != '.') return null; // 別の基準名（例: AppList2）
        var qualifiers = rest.Length > 1 ? rest[1..].Split('_', StringSplitOptions.RemoveEmptyEntries) : [];

        int targetSize = 0, scale = 0;
        string altform = string.Empty;
        bool excluded = false;
        foreach (var q in qualifiers)
        {
            int dash = q.IndexOf('-');
            if (dash <= 0) { excluded = true; continue; }
            string key = q[..dash].ToLowerInvariant();
            string value = q[(dash + 1)..].ToLowerInvariant();
            switch (key)
            {
                case "targetsize": int.TryParse(value, out targetSize); break;
                case "scale": int.TryParse(value, out scale); break;
                case "altform": altform = value; break;
                case "contrast": excluded = true; break;          // ハイコントラスト用は使わない
                case "theme": excluded = value != "dark" && value != "light"; break;
                default: excluded = true; break;                  // lang / layoutdir などは対象外
            }
        }
        return new Variant(fileName, targetSize, scale, altform, excluded);
    }
}
