namespace DeskDig.Core;

/// <summary>
/// Windows のタスクバーに固定されているアプリ 1 件。OS の固定状態を読み取ったもので、
/// Id は固定ショートカットの名前。こちらで持つのは並び順だけ。
/// </summary>
public sealed record PinnedApp(
    string Id,
    string Name,
    string? TargetExecutablePath,
    string? AppUserModelId)
{
    /// <summary>この PinnedApp を起動済みと見なせる WindowEntry か。</summary>
    public bool CorrespondsTo(WindowEntry entry)
    {
        if (TargetExecutablePath is not null
            && string.Equals(TargetExecutablePath, entry.ExecutablePath, StringComparison.OrdinalIgnoreCase))
            return true;
        if (AppUserModelId is not null && entry.AppUserModelId is not null
            && string.Equals(AppUserModelId, entry.AppUserModelId, StringComparison.OrdinalIgnoreCase))
            return true;
        return false;
    }
}

/// <summary>PinnedApp のユーザー定義の並び順。新規固定は末尾に追加、固定解除は除去。</summary>
public static class PinnedAppOrder
{
    public static IReadOnlyList<PinnedApp> Arrange(IReadOnlyList<string> savedIds, IReadOnlyList<PinnedApp> current)
    {
        var byId = current.ToDictionary(a => a.Id, StringComparer.OrdinalIgnoreCase);
        var result = new List<PinnedApp>(current.Count);
        var placed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in savedIds)
        {
            if (byId.TryGetValue(id, out var app) && placed.Add(id)) result.Add(app);
        }
        foreach (var app in current)
        {
            if (placed.Add(app.Id)) result.Add(app);
        }
        return result;
    }

    /// <summary>ドラッグによる並べ替え。movingId を targetId の位置へ移す。</summary>
    public static IReadOnlyList<PinnedApp> Move(IReadOnlyList<PinnedApp> apps, string movingId, string targetId)
    {
        if (string.Equals(movingId, targetId, StringComparison.OrdinalIgnoreCase)) return apps;
        var list = apps.ToList();
        int from = list.FindIndex(a => string.Equals(a.Id, movingId, StringComparison.OrdinalIgnoreCase));
        int to = list.FindIndex(a => string.Equals(a.Id, targetId, StringComparison.OrdinalIgnoreCase));
        if (from < 0 || to < 0) return apps;
        var moving = list[from];
        list.RemoveAt(from);
        list.Insert(to, moving);
        return list;
    }
}

/// <summary>Launcher の振り替え規則。対応する WindowEntry があれば並び順で最上位の 1 件を Activate する。</summary>
public static class Launcher
{
    public static WindowEntry? ResolveExistingWindow(PinnedApp app, ArrangedWindowEntries arranged) =>
        arranged.Flatten().FirstOrDefault(app.CorrespondsTo);
}
