using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using DeskDig.Core;
using Microsoft.Win32;
using static DeskDig.Interop.NativeMethods;

namespace DeskDig.Interop;

/// <summary>
/// タスクバーに固定されたアプリを PinnedApp として読む（ADR-0003）。
/// 主な取得元はタスクバーが保持するレジストリ値（Taskband の Favorites）で、従来型アプリもパッケージ型アプリも
/// タスクバーの表示順どおりに得られる。読めない場合は固定フォルダのショートカット一覧を予備として使う。
/// </summary>
internal static class PinnedAppReader
{
    private static readonly PROPERTYKEY PKEY_AppUserModel_ID = new(new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), 5);
    private const int SIGDN_NORMALDISPLAY = 0;
    private const int SIGDN_DESKTOPABSOLUTEPARSING = unchecked((int)0x80028000);
    private const string AppsFolderClsid = "{4234d49b-0245-4df3-b780-3893943456e1}";
    private const string TaskbandKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Taskband";
    private const string FavoritesValue = "Favorites";

    public static string PinnedFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        @"Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar");

    /// <summary>従来型アプリの固定ショートカットのパス。パッケージ型には無いので null。</summary>
    public static string? ShortcutPathOf(PinnedApp app) =>
        app.Id.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) ? Path.Combine(PinnedFolder, app.Id) : null;

    /// <summary>アイコン取得やシェル操作に使う解析名。ショートカットのパス、またはアプリフォルダ内の AppUserModelId。</summary>
    public static string? ParsingNameOf(PinnedApp app) =>
        ShortcutPathOf(app) ?? (app.AppUserModelId is null ? null : $"shell:AppsFolder\\{app.AppUserModelId}");

    public static IReadOnlyList<PinnedApp> ReadAll()
    {
        var fromTaskband = ReadTaskband();
        var fromFolder = ReadFolder();
        if (fromTaskband.Count == 0) return fromFolder;

        // レジストリから解決できなかったショートカットがあれば、末尾に足して取りこぼしを防ぐ
        var known = new HashSet<string>(fromTaskband.Select(a => a.Id), StringComparer.OrdinalIgnoreCase);
        var result = new List<PinnedApp>(fromTaskband);
        result.AddRange(fromFolder.Where(a => known.Add(a.Id)));
        return result;
    }

    public static void Launch(PinnedApp app)
    {
        var target = ParsingNameOf(app) ?? throw new InvalidOperationException($"{app.Name} の起動方法が分かりません。");
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch when (ShortcutPathOf(app) is null)
        {
            // shell: 形式を直接扱えない環境向けの予備。エクスプローラーに解決させる
            Process.Start(new ProcessStartInfo("explorer.exe", target) { UseShellExecute = true });
        }
    }

    // ---- タスクバーのレジストリ ----

    private static IReadOnlyList<PinnedApp> ReadTaskband()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(TaskbandKey);
            if (key?.GetValue(FavoritesValue) is not byte[] blob) return [];
            var result = new List<PinnedApp>();
            foreach (var pidl in TaskbandFavorites.Split(blob))
            {
                var app = FromIdList(pidl);
                if (app is not null) result.Add(app);
            }
            return result;
        }
        catch (Exception ex)
        {
            Diagnostics.Log("Taskband read failed", ex);
            return [];
        }
    }

    private static PinnedApp? FromIdList(byte[] pidl)
    {
        nint mem = Marshal.AllocCoTaskMem(pidl.Length);
        try
        {
            Marshal.Copy(pidl, 0, mem, pidl.Length);
            string? parsing = NameFromIdList(mem, SIGDN_DESKTOPABSOLUTEPARSING);
            if (string.IsNullOrEmpty(parsing)) return null;

            if (parsing.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
                return File.Exists(parsing) ? ReadShortcutSafe(parsing) : null;

            // パッケージ型: 解析名は AppUserModelId そのもの、またはアプリフォルダ配下のパス
            string aumid = parsing;
            if (parsing.Contains(AppsFolderClsid, StringComparison.OrdinalIgnoreCase))
            {
                int slash = parsing.LastIndexOf('\\');
                if (slash >= 0) aumid = parsing[(slash + 1)..];
            }
            if (!aumid.Contains('!')) return null; // アプリではないもの（フォルダ等）は Launcher に出さない

            string name = NameFromIdList(mem, SIGDN_NORMALDISPLAY) ?? aumid;
            return new PinnedApp(aumid, name, null, aumid);
        }
        finally
        {
            Marshal.FreeCoTaskMem(mem);
        }
    }

    private static string? NameFromIdList(nint pidl, int sigdn)
    {
        nint name = 0;
        try
        {
            if (SHGetNameFromIDList(pidl, sigdn, out name) != 0 || name == 0) return null;
            return Marshal.PtrToStringUni(name);
        }
        finally
        {
            if (name != 0) CoTaskMemFree(name);
        }
    }

    // ---- 固定フォルダ（予備） ----

    private static IReadOnlyList<PinnedApp> ReadFolder()
    {
        var folder = PinnedFolder;
        if (!Directory.Exists(folder)) return [];
        return Directory.EnumerateFiles(folder, "*.lnk")
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .Select(ReadShortcutSafe)
            .ToList();
    }

    private static PinnedApp ReadShortcutSafe(string file)
    {
        try
        {
            return ReadShortcut(file);
        }
        catch
        {
            return new PinnedApp(Path.GetFileName(file), Path.GetFileNameWithoutExtension(file), null, null);
        }
    }

    private static PinnedApp ReadShortcut(string file)
    {
        var link = (IShellLinkW)new ShellLinkClass();
        ((IPersistFile)link).Load(file, 0);

        string? target = null;
        var sb = new StringBuilder(1024);
        try
        {
            link.GetPath(sb, sb.Capacity, 0, 0);
            if (sb.Length > 0) target = sb.ToString();
        }
        catch
        {
            target = null;
        }

        string? aumid = ReadAumid(link);
        if (aumid is null && target is null) aumid = AumidFromIdList(link);

        return new PinnedApp(Path.GetFileName(file), Path.GetFileNameWithoutExtension(file), target, aumid);
    }

    private static string? ReadAumid(IShellLinkW link)
    {
        if (link is not IPropertyStore store) return null;
        var key = PKEY_AppUserModel_ID;
        try
        {
            store.GetValue(ref key, out var value);
            try
            {
                return value.AsString();
            }
            finally
            {
                PropVariantClear(ref value);
            }
        }
        catch
        {
            return null;
        }
    }

    private static string? AumidFromIdList(IShellLinkW link)
    {
        nint pidl = 0;
        try
        {
            link.GetIDList(out pidl);
            if (pidl == 0) return null;
            var parsing = NameFromIdList(pidl, SIGDN_DESKTOPABSOLUTEPARSING);
            if (parsing is null || !parsing.Contains(AppsFolderClsid, StringComparison.OrdinalIgnoreCase)) return null;
            int slash = parsing.LastIndexOf('\\');
            return slash >= 0 ? parsing[(slash + 1)..] : null;
        }
        catch
        {
            return null;
        }
        finally
        {
            if (pidl != 0) CoTaskMemFree(pidl);
        }
    }
}
