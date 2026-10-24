using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using DeskDig.Core;
using DeskDig.Core.Settings;
using DeskDig.Palette;
using DeskDig.Triggers;

namespace DeskDig.Settings;

internal enum SettingEditorKind { Bool, Int, Text, Enum }

internal sealed record EnumOption(object Value, string Label);

/// <summary>設定画面の 1 行。[Setting] 宣言 1 件に対応する。</summary>
internal sealed class SettingRow : ObservableObject
{
    private object? _value;

    public SettingRow(SettingDescriptor descriptor, PaletteSettings source)
    {
        Descriptor = descriptor;
        _value = descriptor.GetValue(source);
        if (descriptor.ValueType == typeof(bool)) Kind = SettingEditorKind.Bool;
        else if (descriptor.ValueType == typeof(int)) Kind = SettingEditorKind.Int;
        else if (descriptor.ValueType.IsEnum)
        {
            Kind = SettingEditorKind.Enum;
            Options = Enum.GetValues(descriptor.ValueType).Cast<object>().Select(v => new EnumOption(v, DisplayNameOf(v))).ToList();
        }
        else Kind = SettingEditorKind.Text;
    }

    public SettingDescriptor Descriptor { get; }
    public SettingEditorKind Kind { get; }
    public string Label => Descriptor.Label;
    public string? Description => Descriptor.Description;
    public bool HasDescription => !string.IsNullOrEmpty(Description);
    public IReadOnlyList<EnumOption> Options { get; } = [];

    public object? Value { get => _value; set => Set(ref _value, value); }

    /// <summary>行の値を設定オブジェクトへ書き戻す。変換できない入力は無視する。</summary>
    public void WriteTo(PaletteSettings target)
    {
        object? converted = Kind switch
        {
            SettingEditorKind.Int => Value is int i ? i : int.TryParse(Value?.ToString(), out var parsed) ? parsed : null,
            SettingEditorKind.Bool => Value is bool b ? b : null,
            SettingEditorKind.Enum => Value is not null && Enum.IsDefined(Descriptor.ValueType, Value) ? Value : null,
            _ => Value?.ToString() ?? string.Empty,
        };
        if (converted is not null) Descriptor.SetValue(target, converted);
    }

    private static string DisplayNameOf(object enumValue)
    {
        var member = enumValue.GetType().GetField(enumValue.ToString()!);
        return member?.GetCustomAttribute<DescriptionAttribute>()?.Description ?? enumValue.ToString()!;
    }
}

internal sealed class PlacementProfileRow : ObservableObject
{
    private string _titleCondition;

    public PlacementProfileRow(PlacementProfile profile)
    {
        Original = profile;
        _titleCondition = profile.Matcher.TitleCondition ?? string.Empty;
    }

    public PlacementProfile Original { get; }
    public string ExecutableName => Original.Matcher.ExecutableName;
    public string ExecutablePath => Original.Matcher.ExecutablePath;
    public string PlacementText
    {
        get
        {
            var a = Original.Placement.Area;
            return $"{a.Width} × {a.Height} @ ({a.Left}, {a.Top})  {Original.Placement.MonitorId}";
        }
    }
    public string TitleCondition { get => _titleCondition; set => Set(ref _titleCondition, value); }

    public WindowMatcher EditedMatcher => Original.Matcher.WithTitleCondition(TitleCondition);
}

internal sealed class FavoriteRow(Favorite favorite)
{
    public Favorite Favorite { get; } = favorite;
    public string Display => Favorite.Matcher.ToString();
    public string ExecutablePath => Favorite.Matcher.ExecutablePath;
}

internal sealed class SettingEditorSelector : DataTemplateSelector
{
    public DataTemplate? Bool { get; set; }
    public DataTemplate? Int { get; set; }
    public DataTemplate? Text { get; set; }
    public DataTemplate? Enum { get; set; }

    public override DataTemplate? SelectTemplate(object item, DependencyObject container) => (item as SettingRow)?.Kind switch
    {
        SettingEditorKind.Bool => Bool,
        SettingEditorKind.Int => Int,
        SettingEditorKind.Enum => Enum,
        _ => Text,
    };
}

internal sealed class SettingsViewModel : ObservableObject
{
    public ObservableCollection<SettingRow> Rows { get; } = new();
    public ObservableCollection<PlacementProfileRow> Profiles { get; } = new();
    public ObservableCollection<FavoriteRow> Favorites { get; } = new();

    public bool HasNoProfiles => Profiles.Count == 0;
    public bool HasNoFavorites => Favorites.Count == 0;

    public SettingsViewModel(PaletteSettings settings, PlacementProfiles profiles, Favorites favorites)
    {
        foreach (var descriptor in SettingDescriptor.Catalog()) Rows.Add(new SettingRow(descriptor, settings));
        foreach (var p in profiles.Items) Profiles.Add(new PlacementProfileRow(p));
        foreach (var f in favorites.Items) Favorites.Add(new FavoriteRow(f));
        Profiles.CollectionChanged += (_, _) => Raise(nameof(HasNoProfiles));
        Favorites.CollectionChanged += (_, _) => Raise(nameof(HasNoFavorites));
    }
}

public partial class SettingsWindow : Window
{
    public static readonly BooleanToVisibilityConverter BoolToVisibility = new();

    private readonly PaletteController _controller;
    private readonly SettingsViewModel _viewModel;

    internal SettingsWindow(PaletteController controller)
    {
        _controller = controller;
        _viewModel = new SettingsViewModel(controller.Settings.Clone(), controller.PlacementProfiles, controller.Favorites);
        InitializeComponent();
        DataContext = _viewModel;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var settings = _controller.Settings.Clone();
        foreach (var row in _viewModel.Rows) row.WriteTo(settings);

        if (settings.HotkeyEnabled && !HotkeyGesture.TryParse(settings.Hotkey, out _))
        {
            MessageBox.Show(this, $"ホットキー \"{settings.Hotkey}\" を解釈できません。例: Ctrl+Alt+Space", "DeskDig", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var profiles = new PlacementProfiles();
        foreach (var row in _viewModel.Profiles) profiles.Record(row.EditedMatcher, row.Original.Placement);
        var favorites = new Favorites(_viewModel.Favorites.Select(r => r.Favorite));

        _controller.ApplySettings(settings, profiles, favorites);
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    private void DeleteProfile_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is PlacementProfileRow row) _viewModel.Profiles.Remove(row);
    }

    private void DeleteFavorite_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is FavoriteRow row) _viewModel.Favorites.Remove(row);
    }
}
