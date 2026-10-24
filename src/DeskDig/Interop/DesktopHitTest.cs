using System.Windows.Automation;
using DeskDig.Core;
using static DeskDig.Interop.NativeMethods;

namespace DeskDig.Interop;

/// <summary>その座標がデスクトップの余白（アイコン以外）かを判定する。UI スレッドでは呼ばない。</summary>
internal static class DesktopHitTest
{
    public static bool IsDesktopBlank(ScreenPoint point) => IsDesktopBlank(point, out _);

    public static bool IsDesktopBlank(ScreenPoint point, out string detail)
    {
        nint hwnd = WindowFromPoint(new POINT { X = point.X, Y = point.Y });
        var chain = ClassChain(hwnd);
        detail = string.Join(" <- ", chain);
        if (hwnd == 0) return false;

        bool onDesktop = chain[0] is "Progman" or "WorkerW"
            || (chain[0] == "SysListView32" && chain.Count > 1 && chain[1] == "SHELLDLL_DefView");
        if (!onDesktop) return false;

        nint listView = FindDesktopListView();
        if (listView == 0)
        {
            detail += " | no desktop list";
            return true;
        }
        bool overIcon = IsOverIcon(listView, point, out string iconName);
        detail += overIcon ? $" | icon '{iconName}'" : " | blank";
        return !overIcon;
    }

    /// <summary>デスクトップアイコンを持つ ListView（Progman または WorkerW 配下の SHELLDLL_DefView の子）。</summary>
    private static nint FindDesktopListView()
    {
        nint found = 0;
        EnumWindows((top, _) =>
        {
            string cls = GetClassNameSafe(top);
            if (cls is not ("Progman" or "WorkerW")) return true;
            nint defView = FindWindowEx(top, 0, "SHELLDLL_DefView", null);
            if (defView == 0) return true;
            nint list = FindWindowEx(defView, 0, "SysListView32", null);
            if (list == 0) return true;
            found = list;
            return false;
        }, 0);
        return found;
    }

    private static bool IsOverIcon(nint listView, ScreenPoint point, out string iconName)
    {
        iconName = string.Empty;
        try
        {
            var list = AutomationElement.FromHandle(listView);
            var items = list.FindAll(TreeScope.Children, Condition.TrueCondition);
            foreach (AutomationElement item in items)
            {
                var rect = item.Current.BoundingRectangle;
                if (rect.IsEmpty) continue;
                if (point.X >= rect.Left && point.X < rect.Right && point.Y >= rect.Top && point.Y < rect.Bottom)
                {
                    iconName = item.Current.Name;
                    return true;
                }
            }
            return false;
        }
        catch
        {
            // 判定できない場合は発火させない（誤爆より取り逃しを選ぶ）
            iconName = "(unknown)";
            return true;
        }
    }

    private static List<string> ClassChain(nint hwnd)
    {
        var chain = new List<string>();
        int depth = 0;
        while (hwnd != 0 && depth++ < 6)
        {
            chain.Add(GetClassNameSafe(hwnd));
            hwnd = GetParent(hwnd);
        }
        if (chain.Count == 0) chain.Add("(none)");
        return chain;
    }
}
