using System.Text.Json;
using System.Text.Json.Serialization;
using DeskDig.Core;
using DeskDig.Core.Settings;

namespace DeskDig.Persistence;

/// <summary>%APPDATA%\DeskDig 配下の JSON。設定・PlacementProfile・Favorite・PinnedApp の並び順を保存する。</summary>
internal sealed class StateStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public string Directory { get; }

    public StateStore(string? directory = null)
    {
        Directory = directory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DeskDig");
    }

    public PaletteSettings LoadSettings() => Load<PaletteSettings>("settings.json") ?? new PaletteSettings();
    public void SaveSettings(PaletteSettings settings) => Save("settings.json", settings);

    public Favorites LoadFavorites()
    {
        var dto = Load<List<MatcherDto>>("favorites.json") ?? [];
        return new Favorites(dto.Select(d => d.ToMatcher()).Where(m => m is not null).Select(m => new Favorite(m!)));
    }

    public void SaveFavorites(Favorites favorites) =>
        Save("favorites.json", favorites.Items.Select(f => MatcherDto.From(f.Matcher)).ToList());

    public PlacementProfiles LoadPlacementProfiles()
    {
        var dto = Load<List<ProfileDto>>("placement-profiles.json") ?? [];
        var profiles = new PlacementProfiles();
        foreach (var d in dto)
        {
            var matcher = d.Matcher?.ToMatcher();
            var placement = d.ToPlacement();
            if (matcher is not null && placement is not null) profiles.Record(matcher, placement);
        }
        return profiles;
    }

    public void SavePlacementProfiles(PlacementProfiles profiles) =>
        Save("placement-profiles.json", profiles.Items.Select(ProfileDto.From).ToList());

    public IReadOnlyList<string> LoadPinnedAppOrder() => Load<List<string>>("pinned-app-order.json") ?? [];
    public void SavePinnedAppOrder(IEnumerable<string> ids) => Save("pinned-app-order.json", ids.ToList());

    private T? Load<T>(string file) where T : class
    {
        var path = Path.Combine(Directory, file);
        if (!File.Exists(path)) return null;
        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options);
        }
        catch
        {
            return null;
        }
    }

    private void Save<T>(string file, T value)
    {
        System.IO.Directory.CreateDirectory(Directory);
        var path = Path.Combine(Directory, file);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(value, Options));
        File.Move(tmp, path, overwrite: true);
    }

    private sealed class MatcherDto
    {
        public string? ExecutablePath { get; set; }
        public string? TitleCondition { get; set; }

        public static MatcherDto From(WindowMatcher m) => new() { ExecutablePath = m.ExecutablePath, TitleCondition = m.TitleCondition };

        public WindowMatcher? ToMatcher() => string.IsNullOrWhiteSpace(ExecutablePath) ? null : new WindowMatcher(ExecutablePath, TitleCondition);
    }

    private sealed class ProfileDto
    {
        public MatcherDto? Matcher { get; set; }
        public int Left { get; set; }
        public int Top { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public string? MonitorId { get; set; }

        public static ProfileDto From(PlacementProfile p) => new()
        {
            Matcher = MatcherDto.From(p.Matcher),
            Left = p.Placement.Area.Left,
            Top = p.Placement.Area.Top,
            Width = p.Placement.Area.Width,
            Height = p.Placement.Area.Height,
            MonitorId = p.Placement.MonitorId,
        };

        public Placement? ToPlacement()
        {
            if (Width <= 0 || Height <= 0 || string.IsNullOrWhiteSpace(MonitorId)) return null;
            return new Placement(new Bounds(Left, Top, Width, Height), MonitorId);
        }
    }
}
