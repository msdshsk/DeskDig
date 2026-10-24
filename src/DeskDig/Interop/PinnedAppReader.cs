using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using DeskDig.Core;
using static DeskDig.Interop.NativeMethods;

namespace DeskDig.Interop;

/// <summary>タスクバー固定フォルダのショートカットを PinnedApp として読む（ADR-0001）。</summary>
internal static class PinnedAppReader
{
    private static readonly PROPERTYKEY PKEY_AppUserModel_ID = new(new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), 5);
    private const int SIGDN_DESKTOPABSOLUTEPARSING = unchecked((int)0x80028000);
    private const string AppsFolderClsid = "{4234d49b-0245-4df3-b780-3893943456e1}";

    public static string PinnedFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        @"Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar");

    public static string ShortcutPathOf(PinnedApp app) => Path.Combine(PinnedFolder, app.Id);

    public static IReadOnlyList<PinnedApp> ReadAll()
    {
        var folder = PinnedFolder;
        if (!Directory.Exists(folder)) return [];
        var result = new List<PinnedApp>();
        foreach (var file in Directory.EnumerateFiles(folder, "*.lnk").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                result.Add(Read(file));
            }
            catch
            {
                result.Add(new PinnedApp(Path.GetFileName(file), Path.GetFileNameWithoutExtension(file), null, null));
            }
        }
        return result;
    }

    public static void Launch(PinnedApp app)
    {
        Process.Start(new ProcessStartInfo(ShortcutPathOf(app)) { UseShellExecute = true });
    }

    private static PinnedApp Read(string file)
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
        nint name = 0;
        try
        {
            link.GetIDList(out pidl);
            if (pidl == 0) return null;
            if (SHGetNameFromIDList(pidl, SIGDN_DESKTOPABSOLUTEPARSING, out name) != 0 || name == 0) return null;
            var parsing = Marshal.PtrToStringUni(name);
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
            if (name != 0) CoTaskMemFree(name);
            if (pidl != 0) CoTaskMemFree(pidl);
        }
    }
}
