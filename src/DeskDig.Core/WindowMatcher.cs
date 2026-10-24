namespace DeskDig.Core;

/// <summary>
/// 「どのウインドウを指すか」の照合規則。実行ファイルの同一性と任意のタイトル条件（部分一致）からなる値オブジェクト。
/// </summary>
public sealed record WindowMatcher
{
    public string ExecutablePath { get; }
    public string? TitleCondition { get; }

    public WindowMatcher(string executablePath, string? titleCondition = null)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
            throw new ArgumentException("WindowMatcher には実行ファイルが必要。", nameof(executablePath));
        ExecutablePath = executablePath.Trim();
        TitleCondition = string.IsNullOrWhiteSpace(titleCondition) ? null : titleCondition.Trim();
    }

    public static WindowMatcher ForExecutable(string executablePath) => new(executablePath);

    public bool HasTitleCondition => TitleCondition is not null;

    public string ExecutableName => Path.GetFileName(ExecutablePath);

    public WindowMatcher WithTitleCondition(string? titleCondition) => new(ExecutablePath, titleCondition);

    public bool MatchesExecutable(string executablePath) =>
        string.Equals(ExecutablePath, executablePath, StringComparison.OrdinalIgnoreCase);

    public bool Matches(string executablePath, string title)
    {
        if (!MatchesExecutable(executablePath)) return false;
        return TitleCondition is null || title.Contains(TitleCondition, StringComparison.OrdinalIgnoreCase);
    }

    public bool Matches(WindowEntry entry) => Matches(entry.ExecutablePath, entry.Title);

    /// <summary>タイトル条件付きの規則は実行ファイルのみの規則より優先される。</summary>
    public bool IsMoreSpecificThan(WindowMatcher other) => HasTitleCondition && !other.HasTitleCondition;

    public bool Equals(WindowMatcher? other) =>
        other is not null
        && string.Equals(ExecutablePath, other.ExecutablePath, StringComparison.OrdinalIgnoreCase)
        && string.Equals(TitleCondition, other.TitleCondition, StringComparison.OrdinalIgnoreCase);

    public override int GetHashCode() => HashCode.Combine(
        StringComparer.OrdinalIgnoreCase.GetHashCode(ExecutablePath),
        TitleCondition is null ? 0 : StringComparer.OrdinalIgnoreCase.GetHashCode(TitleCondition));

    public override string ToString() => HasTitleCondition ? $"{ExecutableName} / \"{TitleCondition}\"" : ExecutableName;
}
