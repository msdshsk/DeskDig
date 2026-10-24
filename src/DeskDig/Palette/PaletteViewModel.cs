using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using DeskDig.Core;

namespace DeskDig.Palette;

internal abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }

    protected void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>画面上の WindowEntry 1 件。サムネイルの代わりに使う大きなアイコンも持つ。</summary>
internal sealed class WindowEntryItem(WindowEntry entry, ImageSource? icon, ImageSource? largeIcon) : ObservableObject
{
    private bool _isSelected;
    private bool _isFavorite;
    private bool _hasPlacement;

    public WindowEntry Entry { get; } = entry;
    public ImageSource? Icon { get; } = icon;
    public ImageSource? LargeIcon { get; } = largeIcon ?? icon;
    public string AppName => Entry.AppName;
    public string Title => Entry.Title;
    public bool ShowThumbnail => !Entry.IsMinimized;

    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }

    public bool IsFavorite
    {
        get => _isFavorite;
        set { if (Set(ref _isFavorite, value)) Raise(nameof(FavoriteMenuLabel)); }
    }

    public bool HasPlacement { get => _hasPlacement; set => Set(ref _hasPlacement, value); }

    public string FavoriteMenuLabel => IsFavorite ? "Favorite を解除" : "Favorite に追加";
}

/// <summary>Launcher 上の PinnedApp 1 件。</summary>
internal sealed class PinnedAppItem(PinnedApp app, ImageSource? icon) : ObservableObject
{
    private bool _isSelected;
    public PinnedApp App { get; } = app;
    public ImageSource? Icon { get; } = icon;
    public string Name => App.Name;
    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }
}

internal enum SelectionRegion { Windows, Launcher }

/// <summary>Palette 1 回分の表示状態。開くたびに Load で作り直す。</summary>
internal sealed class PaletteViewModel : ObservableObject
{
    private readonly List<WindowEntryItem> _allEntries = new();
    private readonly List<PinnedAppItem> _allApps = new();
    private ArrangedWindowEntries _arranged = ArrangedWindowEntries.Empty;
    private ArrangedWindowEntries _visible = ArrangedWindowEntries.Empty;
    private string _filterText = string.Empty;
    private string? _selectedPinnedAppName;
    private SelectionRegion _region = SelectionRegion.Windows;

    public ObservableCollection<PinnedAppItem> LauncherItems { get; } = new();
    public ObservableCollection<WindowEntryItem> FavoriteItems { get; } = new();
    public ObservableCollection<WindowEntryItem> CurrentItems { get; } = new();
    public ObservableCollection<WindowEntryItem> OtherItems { get; } = new();

    public bool HasFavorites => FavoriteItems.Count > 0;
    public bool HasCurrent => CurrentItems.Count > 0;
    public bool HasOther => OtherItems.Count > 0;
    public bool HasAnyEntry => HasFavorites || HasCurrent || HasOther;
    public bool HasLauncherItems => LauncherItems.Count > 0;

    /// <summary>並び替え・絞り込み後の WindowEntry。Launcher の振り替え先の決定に使う。</summary>
    public ArrangedWindowEntries VisibleEntries => _visible;

    public string FilterText
    {
        get => _filterText;
        set
        {
            if (Set(ref _filterText, value)) Refilter(selectFirst: true);
        }
    }

    public string? SelectedPinnedAppName { get => _selectedPinnedAppName; private set => Set(ref _selectedPinnedAppName, value); }

    public SelectionRegion Region
    {
        get => _region;
        private set { if (Set(ref _region, value)) Raise(nameof(IsLauncherRegion)); }
    }

    public bool IsLauncherRegion => Region == SelectionRegion.Launcher;

    public WindowEntryItem? SelectedEntry => VisibleEntryItems().FirstOrDefault(i => i.IsSelected);
    public PinnedAppItem? SelectedPinnedApp => LauncherItems.FirstOrDefault(i => i.IsSelected);

    public void Load(IEnumerable<WindowEntryItem> entries, ArrangedWindowEntries arranged, IEnumerable<PinnedAppItem> apps)
    {
        _allEntries.Clear();
        _allEntries.AddRange(entries);
        _allApps.Clear();
        _allApps.AddRange(apps);
        _arranged = arranged;
        _filterText = string.Empty;
        Raise(nameof(FilterText));
        Region = SelectionRegion.Windows;
        Refilter(selectFirst: true);
    }

    public void Clear()
    {
        _allEntries.Clear();
        _allApps.Clear();
        _arranged = ArrangedWindowEntries.Empty;
        _visible = ArrangedWindowEntries.Empty;
        FavoriteItems.Clear();
        CurrentItems.Clear();
        OtherItems.Clear();
        LauncherItems.Clear();
        RaiseCounts();
    }

    private void Refilter(bool selectFirst)
    {
        var filter = new EntryFilter(_filterText);
        _visible = filter.Apply(_arranged);
        var byId = _allEntries.ToDictionary(i => i.Entry.Id);

        Fill(FavoriteItems, _visible.Favorites, byId);
        Fill(CurrentItems, _visible.CurrentDesktop, byId);
        Fill(OtherItems, _visible.OtherDesktops, byId);

        var visibleApps = filter.Apply(_allApps.Select(a => a.App).ToList()).Select(a => a.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        LauncherItems.Clear();
        foreach (var app in _allApps.Where(a => visibleApps.Contains(a.App.Id))) LauncherItems.Add(app);

        RaiseCounts();
        if (selectFirst) SelectFirst();
    }

    private static void Fill(ObservableCollection<WindowEntryItem> target, IReadOnlyList<WindowEntry> entries, Dictionary<WindowId, WindowEntryItem> byId)
    {
        target.Clear();
        foreach (var e in entries)
        {
            if (byId.TryGetValue(e.Id, out var item)) target.Add(item);
        }
    }

    private void RaiseCounts()
    {
        Raise(nameof(HasFavorites));
        Raise(nameof(HasCurrent));
        Raise(nameof(HasOther));
        Raise(nameof(HasAnyEntry));
        Raise(nameof(HasLauncherItems));
    }

    public IReadOnlyList<WindowEntryItem> VisibleEntryItems() => FavoriteItems.Concat(CurrentItems).Concat(OtherItems).ToList();

    /// <summary>先頭を選択。WindowEntry が無ければ Launcher の先頭。</summary>
    public void SelectFirst()
    {
        var entries = VisibleEntryItems();
        if (entries.Count > 0)
        {
            Region = SelectionRegion.Windows;
            SelectEntry(entries[0]);
        }
        else if (LauncherItems.Count > 0)
        {
            Region = SelectionRegion.Launcher;
            SelectApp(LauncherItems[0]);
        }
        else
        {
            SelectEntry(null);
            SelectApp(null);
        }
    }

    public void ToggleRegion()
    {
        if (Region == SelectionRegion.Windows && LauncherItems.Count > 0)
        {
            Region = SelectionRegion.Launcher;
            if (SelectedPinnedApp is null) SelectApp(LauncherItems[0]);
        }
        else if (Region == SelectionRegion.Launcher && VisibleEntryItems().Count > 0)
        {
            Region = SelectionRegion.Windows;
            if (SelectedEntry is null) SelectEntry(VisibleEntryItems()[0]);
        }
    }

    /// <summary>矢印キーによる選択移動。columns は現在の領域で 1 行に並ぶ数。</summary>
    public void MoveSelection(int dx, int dy, int entryColumns, int launcherColumns)
    {
        if (Region == SelectionRegion.Launcher)
        {
            var apps = LauncherItems;
            if (apps.Count == 0) return;
            int index = SelectedPinnedApp is null ? -1 : apps.IndexOf(SelectedPinnedApp);
            int next = index < 0 ? 0 : Math.Clamp(index + dx + dy * Math.Max(1, launcherColumns), 0, apps.Count - 1);
            SelectApp(apps[next]);
        }
        else
        {
            var entries = VisibleEntryItems().ToList();
            if (entries.Count == 0) return;
            int index = SelectedEntry is null ? -1 : entries.IndexOf(SelectedEntry);
            int next = index < 0 ? 0 : Math.Clamp(index + dx + dy * Math.Max(1, entryColumns), 0, entries.Count - 1);
            SelectEntry(entries[next]);
        }
    }

    public void SelectEntry(WindowEntryItem? item)
    {
        foreach (var e in _allEntries) e.IsSelected = ReferenceEquals(e, item);
        if (item is not null)
        {
            Region = SelectionRegion.Windows;
            foreach (var a in _allApps) a.IsSelected = false;
            SelectedPinnedAppName = null;
        }
    }

    public void SelectApp(PinnedAppItem? item)
    {
        foreach (var a in _allApps) a.IsSelected = ReferenceEquals(a, item);
        SelectedPinnedAppName = item?.Name;
        if (item is not null)
        {
            Region = SelectionRegion.Launcher;
            foreach (var e in _allEntries) e.IsSelected = false;
        }
    }

    public void ReorderLauncher(IReadOnlyList<PinnedApp> ordered)
    {
        var byId = _allApps.ToDictionary(a => a.App.Id, StringComparer.OrdinalIgnoreCase);
        _allApps.Clear();
        foreach (var app in ordered)
        {
            if (byId.TryGetValue(app.Id, out var item)) _allApps.Add(item);
        }
        Refilter(selectFirst: false);
    }
}
