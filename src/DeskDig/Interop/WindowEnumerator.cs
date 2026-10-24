using DeskDig.Core;
using static DeskDig.Interop.NativeMethods;

namespace DeskDig.Interop;

/// <summary>Alt+Tab に出る条件を満たすトップレベルウインドウを WindowEntry として列挙する。</summary>
internal sealed class WindowEnumerator(VirtualDesktopQuery desktops, ProcessInfo processes)
{
    private static readonly HashSet<string> ExcludedClasses = new(StringComparer.Ordinal)
    {
        "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "Windows.UI.Core.CoreWindow",
    };

    private readonly uint _ownPid = (uint)Environment.ProcessId;

    public IReadOnlyList<WindowEntry> Enumerate(ISet<nint> excludedWindows)
    {
        var entries = new List<WindowEntry>();
        int order = 0;
        EnumWindows((hwnd, _) =>
        {
            if (excludedWindows.Contains(hwnd)) return true;
            var entry = Describe(hwnd, order);
            if (entry is not null)
            {
                entries.Add(entry);
                order++;
            }
            return true;
        }, 0);
        return entries;
    }

    private WindowEntry? Describe(nint hwnd, int stackingOrder)
    {
        if (!IsWindowVisible(hwnd)) return null;
        long exStyle = GetWindowLongPtr(hwnd, GWL_EXSTYLE);
        bool toolWindow = (exStyle & WS_EX_TOOLWINDOW) != 0;
        bool appWindow = (exStyle & WS_EX_APPWINDOW) != 0;
        if (toolWindow && !appWindow) return null;
        if (!appWindow && GetWindow(hwnd, GW_OWNER) != 0) return null;

        string title = GetWindowTextSafe(hwnd);
        if (title.Length == 0) return null;

        string className = GetClassNameSafe(hwnd);
        if (ExcludedClasses.Contains(className)) return null;

        bool onCurrentDesktop = desktops.IsOnCurrentDesktop(hwnd);
        if (DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0 && onCurrentDesktop)
            return null; // 現在のデスクトップにあるのに隠されている（中断中のストアアプリ等）

        nint processWindow = hwnd;
        if (className == "ApplicationFrameWindow")
        {
            nint core = FindChildOfClass(hwnd, "Windows.UI.Core.CoreWindow");
            if (core != 0) processWindow = core;
        }
        GetWindowThreadProcessId(processWindow, out uint pid);
        if (pid == _ownPid || pid == 0) return null;
        var (path, aumid) = processes.Describe(pid);
        if (string.IsNullOrEmpty(path)) return null;

        return new WindowEntry(
            new WindowId(hwnd),
            title,
            processes.AppNameOf(path),
            path,
            onCurrentDesktop,
            stackingOrder,
            IsIconic(hwnd),
            aumid);
    }

    private static nint FindChildOfClass(nint parent, string className)
    {
        nint found = 0;
        EnumChildWindows(parent, (child, _) =>
        {
            if (GetClassNameSafe(child) == className)
            {
                found = child;
                return false;
            }
            return true;
        }, 0);
        return found;
    }
}
