using System.Buffers.Binary;

namespace DeskDig.Core;

/// <summary>
/// Windows のタスクバーに固定されているアプリ 1 件。OS の固定状態を読み取ったもので、こちらで持つのは並び順だけ。
/// Id は従来型アプリなら固定ショートカットのファイル名、パッケージ型（MSIX / ストア）なら AppUserModelId。
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

/// <summary>
/// タスクバーの固定一覧を保持するレジストリ値（Taskband の Favorites）の分解。
/// 値は「1 バイトのヘッダー、4 バイトのサイズ（リトルエンディアン）、そのサイズ分の PIDL」の繰り返しで、
/// PIDL は 2 バイトの 0 で終わる。並びはタスクバーの表示順と一致する。
/// PIDL の中身の解釈は OS のシェル API に任せ、ここでは区切りだけを扱う。
/// </summary>
public static class TaskbandFavorites
{
    private const int EntryHeaderLength = 1 + sizeof(int);

    /// <summary>値を PIDL のバイト列に分解する。途中で形式が崩れていたら、そこまでの分を返す。</summary>
    public static IReadOnlyList<byte[]> Split(ReadOnlySpan<byte> blob)
    {
        var result = new List<byte[]>();
        int offset = 0;
        while (offset + EntryHeaderLength <= blob.Length)
        {
            int size = BinaryPrimitives.ReadInt32LittleEndian(blob.Slice(offset + 1, sizeof(int)));
            if (size < 2 || offset + EntryHeaderLength + size > blob.Length) break;
            var pidl = blob.Slice(offset + EntryHeaderLength, size);
            if (pidl[^1] != 0 || pidl[^2] != 0) break;
            result.Add(pidl.ToArray());
            offset += EntryHeaderLength + size;
        }
        return result;
    }
}

/// <summary>Launcher の振り替え規則。対応する WindowEntry があれば並び順で最上位の 1 件を Activate する。</summary>
public static class Launcher
{
    public static WindowEntry? ResolveExistingWindow(PinnedApp app, ArrangedWindowEntries arranged) =>
        arranged.Flatten().FirstOrDefault(app.CorrespondsTo);
}
