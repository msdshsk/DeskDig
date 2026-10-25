using System.Globalization;
using System.Text;

namespace DeskDig.Core;

/// <summary>
/// InfoPanel に表示する PC の要約。取得に時間がかかる項目（GPU）は null のまま先に表示し、後から埋める。
/// </summary>
/// <param name="DeviceName">デバイス名（コンピューター名）。</param>
/// <param name="WindowsEdition">例: Windows 11 Pro。</param>
/// <param name="WindowsVersion">例: 24H2。</param>
/// <param name="WindowsBuild">例: 26100.2894。</param>
/// <param name="Cpu">プロセッサ名。</param>
/// <param name="MemoryText">メモリ容量の表示文字列。例: 64 GB。</param>
/// <param name="Gpu">GPU 名（複数なら「 / 」区切り）。未取得なら null。</param>
public sealed record SystemSummary(
    string DeviceName,
    string WindowsEdition,
    string WindowsVersion,
    string WindowsBuild,
    string Cpu,
    string MemoryText,
    string? Gpu)
{
    public const string Pending = "取得中…";
    public const string Unknown = "不明";

    /// <summary>表示とコピーの両方に使う行。GPU が未取得なら「取得中…」。</summary>
    public IReadOnlyList<(string Label, string Value)> Lines() =>
    [
        ("デバイス名", DeviceName),
        ("Windows", WindowsEdition),
        ("バージョン", $"{WindowsVersion} (ビルド {WindowsBuild})"),
        ("CPU", Cpu),
        ("メモリ", MemoryText),
        ("GPU", Gpu ?? Pending),
    ];

    /// <summary>クリップボードへ入れるプレーンテキスト。</summary>
    public string ToPlainText()
    {
        var sb = new StringBuilder();
        foreach (var (label, value) in Lines()) sb.Append(label).Append(": ").AppendLine(value);
        return sb.ToString().TrimEnd();
    }

    public SystemSummary WithGpu(string? gpu) => this with { Gpu = string.IsNullOrWhiteSpace(gpu) ? Unknown : gpu };

    /// <summary>物理メモリ容量を人が読む単位にする。整数 GB ならそのまま、端数があれば小数 1 桁。</summary>
    public static string FormatMemory(long kilobytes)
    {
        if (kilobytes <= 0) return Unknown;
        double gb = kilobytes / 1024.0 / 1024.0;
        double rounded = Math.Round(gb, 1);
        return rounded == Math.Floor(rounded)
            ? $"{(long)rounded} GB"
            : rounded.ToString("0.0", CultureInfo.InvariantCulture) + " GB";
    }

    /// <summary>レジストリの ProductName はビルド 22000 以降でも "Windows 10" のままなので、ビルド番号で補正する。</summary>
    public static string NormalizeEdition(string? productName, int build)
    {
        if (string.IsNullOrWhiteSpace(productName)) return Unknown;
        return build >= 22000 && productName.StartsWith("Windows 10", StringComparison.OrdinalIgnoreCase)
            ? "Windows 11" + productName["Windows 10".Length..]
            : productName;
    }
}
