using System.ComponentModel;
using System.Reflection;

namespace DeskDig.Core.Settings;

public enum PaletteViewMode
{
    [Description("タイル格子")] Grid,
    [Description("縦リスト")] List,
}

public enum LauncherOrderMode
{
    [Description("タスクバーと同じ順")] Taskbar,
    [Description("自分で並べ替える")] Custom,
}

/// <summary>設定画面に 1 行として出す項目であることの宣言。設定画面はこの宣言から行を生成する。</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class SettingAttribute(string label) : Attribute
{
    public string Label { get; } = label;
    public string? Description { get; init; }
    public int Min { get; init; } = int.MinValue;
    public int Max { get; init; } = int.MaxValue;
}

/// <summary>保存される設定。項目の追加はここにプロパティと [Setting] を足すだけで設定画面に現れる。</summary>
public sealed class PaletteSettings
{
    [Setting("デスクトップのダブルクリックで開く", Description = "TriggerSource: DesktopDoubleClick")]
    public bool DesktopDoubleClickEnabled { get; set; } = true;

    [Setting("ホットキーで開く", Description = "TriggerSource: Hotkey")]
    public bool HotkeyEnabled { get; set; } = true;

    [Setting("ホットキー", Description = "例: Ctrl+Alt+Space")]
    public string Hotkey { get; set; } = "Ctrl+Alt+Space";

    [Setting("表示形式")]
    public PaletteViewMode ViewMode { get; set; } = PaletteViewMode.Grid;

    [Setting("格子の列数", Min = 1, Max = 12)]
    public int GridColumns { get; set; } = 4;

    [Setting("Launcher の並び順", Description = "「自分で並べ替える」では Palette 上でアイコンをドラッグして並べ替える")]
    public LauncherOrderMode LauncherOrder { get; set; } = LauncherOrderMode.Taskbar;

    [Setting("Windows サインイン時に自動起動")]
    public bool AutoStart { get; set; }

    /// <summary>ToolsPanel を Palette と同時に毎回開く（AlwaysShown）。ToolsPanel 上のピン型ボタンと連動する。</summary>
    [Setting("ToolsPanel を Palette と同時に開く", Description = "AlwaysShown。ToolsPanel 上のピン型ボタンと同じ設定")]
    public bool AlwaysShown { get; set; }

    /// <summary>Theme。InfoPanel の選択と同じ設定。</summary>
    [Setting("テーマ", Description = "System は Windows の「色」のアプリモードに追従。InfoPanel の選択と同じ設定")]
    public Theme Theme { get; set; } = Theme.System;

    public PaletteSettings Clone() => (PaletteSettings)MemberwiseClone();
}

/// <summary>[Setting] が付いたプロパティ 1 件の記述。</summary>
public sealed record SettingDescriptor(PropertyInfo Property, SettingAttribute Attribute)
{
    public string Label => Attribute.Label;
    public string? Description => Attribute.Description;
    public Type ValueType => Property.PropertyType;

    public object? GetValue(PaletteSettings settings) => Property.GetValue(settings);

    public void SetValue(PaletteSettings settings, object? value)
    {
        if (ValueType == typeof(int) && value is int i)
            value = Math.Clamp(i, Attribute.Min, Attribute.Max);
        Property.SetValue(settings, value);
    }

    public static IReadOnlyList<SettingDescriptor> Catalog() =>
        typeof(PaletteSettings).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => (p, attr: p.GetCustomAttribute<SettingAttribute>()))
            .Where(t => t.attr is not null)
            .Select(t => new SettingDescriptor(t.p, t.attr!))
            .ToList();
}
