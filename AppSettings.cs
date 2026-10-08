using System.IO;
using System.Text.Json;

namespace DiscShelf;

public sealed class AppSettings
{
    public bool SetupCompleted { get; set; }
    public bool DefaultRequiresDisc { get; set; } = true;
    public string PreferredGameFolder { get; set; } = "";
    public bool SuggestInstall { get; set; } = true;
    public List<string> IgnoredDiscIds { get; set; } = [];
    public string ThemeId { get; set; } = "classic-blue";
    public string PreferredDrive { get; set; } = "";
    public string FirstUsedDrive { get; set; } = "";
    public bool OpenTrayWhenMissing { get; set; } = true;
    public bool ScanAutomatically { get; set; } = true;
    public int ScanIntervalSeconds { get; set; } = 4;
    public bool ShowBootScreen { get; set; } = true;
    public int BootDurationSeconds { get; set; } = 2;
    public bool StartMaximised { get; set; }
    public string StartPage { get; set; } = "Home";
    public bool AnimateWaves { get; set; } = true;
    public double WaveOpacity { get; set; } = 0.8;
    public bool ShowParticles { get; set; } = true;
    public bool ShowClock { get; set; } = true;
    public bool Clock24Hour { get; set; } = true;
    public string TileSize { get; set; } = "Standard";
    public string CoverFit { get; set; } = "Fill";
    public string FontFamily { get; set; } = "Segoe UI";
    public bool RecordPlayHistory { get; set; } = true;
    public bool MinimiseAfterLaunch { get; set; }
    public bool AutoLookup { get; set; } = true;
    public bool FillMissingMetadata { get; set; } = true;
    public bool DownloadArtwork { get; set; } = true;
    public bool WikipediaEnabled { get; set; } = true;
    public bool RawgEnabled { get; set; } = true;
    public bool IgdbEnabled { get; set; } = true;
    public string RawgKeyProtected { get; set; } = "";
    public string IgdbClientId { get; set; } = "";
    public string IgdbSecretProtected { get; set; } = "";
    public AppSettings Copy() => JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(this))!;
    public void Validate()
    {
        IgnoredDiscIds = (IgnoredDiscIds ?? []).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().ToList();
        ScanIntervalSeconds = Math.Clamp(ScanIntervalSeconds, 2, 30);
        BootDurationSeconds = Math.Clamp(BootDurationSeconds, 1, 5);
        WaveOpacity = double.IsFinite(WaveOpacity) ? Math.Clamp(WaveOpacity, 0, 1) : 0.8;
        if (!new[] { "Home", "Library", "Recently played", "Favorites" }.Contains(StartPage)) StartPage = "Home";
        if (!new[] { "Small", "Standard", "Large" }.Contains(TileSize)) TileSize = "Standard";
        if (CoverFit is not ("Fill" or "Fit")) CoverFit = "Fill";
        if (!new[] { "Segoe UI", "Tahoma", "Verdana" }.Contains(FontFamily)) FontFamily = "Segoe UI";
    }
}
public sealed class SettingsStore(string root)
{
    private string PathName => Path.Combine(root, "settings.json");
    public string Notice { get; private set; } = "";
    public AppSettings Load()
    {
        try
        {
            var settings = File.Exists(PathName) ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(PathName)) ?? throw new JsonException() : new();
            settings.Validate(); return settings;
        }
        catch (Exception e) when (e is IOException or JsonException)
        {
            File.Copy(PathName, PathName + ".damaged-" + DateTime.Now.ToString("yyyyMMddHHmmssfff"), true);
            Notice = "Settings could not be read. Defaults are in use; the original settings file was preserved.";
            return new();
        }
    }
    public void Save(AppSettings settings)
    {
        settings.Validate(); Directory.CreateDirectory(root);
        File.WriteAllText(PathName + ".tmp", JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        if (File.Exists(PathName)) File.Replace(PathName + ".tmp", PathName, PathName + ".bak");
        else File.Move(PathName + ".tmp", PathName);
    }
}
