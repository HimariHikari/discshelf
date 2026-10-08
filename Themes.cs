using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;

namespace DiscShelf;
public sealed class ThemeDefinition
{
    public string Id { get; set; } = "my-theme";
    public string Name { get; set; } = "My theme";
    public string Author { get; set; } = "You";
    public string BackgroundTop { get; set; } = "#0C2148";
    public string BackgroundMiddle { get; set; } = "#164B8B";
    public string BackgroundBottom { get; set; } = "#071B37";
    public string Panel { get; set; } = "#163B64";
    public string Accent { get; set; } = "#B4DDFF";
    public string Text { get; set; } = "#F0F5FF";
    public string Muted { get; set; } = "#A3BFDC";
    public string Wave { get; set; } = "#9ECFFF";
    public override string ToString() => Name;
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Name) || Name.Length > 80 || !System.Text.RegularExpressions.Regex.IsMatch(Id, "^[a-zA-Z0-9_-]{1,64}$")) throw new IOException("A theme needs a name and a simple ID (letters, numbers, dashes). ");
        foreach (var value in new[] { BackgroundTop, BackgroundMiddle, BackgroundBottom, Panel, Accent, Text, Muted, Wave })
            if (!System.Text.RegularExpressions.Regex.IsMatch(value, "^#[0-9a-fA-F]{6}([0-9a-fA-F]{2})?$")) throw new IOException("Theme colours must use #RRGGBB or #AARRGGBB.");
    }
}
public sealed class ThemeCatalog
{
    public string Folder { get; }
    public string Notice { get; private set; } = "";
    public ThemeCatalog(string root) { Folder = Path.Combine(root, "themes"); Directory.CreateDirectory(Folder); }
    public static List<ThemeDefinition> BuiltIns() =>
    [
        new() { Id = "classic-blue", Name = "Classic Blue", Author = "DiscShelf" },
        Make("midnight", "Midnight", "#070C1A", "#172849", "#040913", "#13223D", "#A9C9F5"),
        Make("violet", "Violet", "#211A44", "#57408E", "#160D2F", "#302353", "#D8B9FF"),
        Make("emerald", "Emerald", "#082B2B", "#185D5E", "#051A20", "#123D40", "#A6EFE0"),
        Make("sunset", "Sunset", "#371D34", "#82444C", "#241222", "#512C3C", "#FFD4AA"),
        Make("crimson", "Crimson", "#301523", "#73283F", "#1A0C19", "#442033", "#FFC0D1"),
        Make("graphite", "Graphite", "#181E28", "#3D4C60", "#10151E", "#252F3D", "#D8E8F7"),
        Make("ocean", "Ocean", "#092D45", "#147297", "#061E35", "#164561", "#B3EDFF")
    ];
    private static ThemeDefinition Make(string id, string name, string top, string middle, string bottom, string panel, string accent) => new() { Id = id, Name = name, Author = "DiscShelf", BackgroundTop = top, BackgroundMiddle = middle, BackgroundBottom = bottom, Panel = panel, Accent = accent, Wave = accent };
    public List<ThemeDefinition> Load()
    {
        var themes = BuiltIns(); Notice = "";
        foreach (var path in Directory.EnumerateFiles(Folder, "*.json").Take(100))
        {
            try { var theme = Read(path); themes.RemoveAll(t => t.Id == theme.Id); themes.Add(theme); }
            catch (Exception e) when (e is IOException or JsonException or ArgumentException) { Notice += Path.GetFileName(path) + " could not be loaded. "; }
        }
        return themes;
    }
    private static ThemeDefinition Read(string path)
    {
        if (new FileInfo(path).Length > 64 * 1024) throw new IOException("Theme files must be smaller than 64 KB.");
        var theme = JsonSerializer.Deserialize<ThemeDefinition>(File.ReadAllText(path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? throw new JsonException();
        theme.Validate(); return theme;
    }
    public ThemeDefinition Import(string path) { var theme = Read(path); var target = Path.Combine(Folder, theme.Id + ".json"); if (!Path.GetFullPath(path).Equals(Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase)) File.Copy(path, target, true); return theme; }
    public string ExportTemplate()
    {
        var path = Path.Combine(Folder, "my-theme.json");
        if (!File.Exists(path)) File.WriteAllText(path, JsonSerializer.Serialize(new ThemeDefinition(), new JsonSerializerOptions { WriteIndented = true }));
        return path;
    }
    public static void Apply(ResourceDictionary resources, ThemeDefinition theme, AppSettings settings)
    {
        Color C(string value) => (Color)ColorConverter.ConvertFromString(value);
        SolidColorBrush Brush(string value) { var brush = new SolidColorBrush(C(value)); brush.Freeze(); return brush; }
        resources["ThemeTop"] = C(theme.BackgroundTop); resources["ThemeMiddle"] = C(theme.BackgroundMiddle); resources["ThemeBottom"] = C(theme.BackgroundBottom);
        resources["ThemeAccentColor"] = C(theme.Accent); var glow = C(theme.Accent); glow.A = 103; resources["ThemeGlowColor"] = glow;
        foreach (var (key, value) in new[] { ("ThemePanel", theme.Panel), ("ThemeAccent", theme.Accent), ("ThemeText", theme.Text), ("ThemeMuted", theme.Muted), ("ThemeWave", theme.Wave) }) resources[key] = Brush(value);
        resources["ThemeFont"] = new FontFamily(settings.FontFamily);
        var border = C(theme.Accent); border.A = 95; resources["ThemeBorder"] = new SolidColorBrush(border);
        var button = C(theme.Panel); button.A = 125; resources["ThemeButton"] = new SolidColorBrush(button);
        var size = settings.TileSize switch { "Small" => 300.0, "Large" => 420.0, _ => 360.0 };
        resources["TileWidth"] = size; resources["CoverHeight"] = size * 194 / 360; resources["TileHeight"] = size * 194 / 360 + 63;
        resources["CoverRowHeight"] = new GridLength(size * 194 / 360);
        resources["CoverStretch"] = settings.CoverFit == "Fit" ? Stretch.Uniform : Stretch.UniformToFill;
    }
}
