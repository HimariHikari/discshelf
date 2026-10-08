using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DiscShelf;

public sealed class Game
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "Untitled game";
    public string DiscId { get; set; } = "";
    public string DiscLabel { get; set; } = "";
    public string DiscRoot { get; set; } = "";
    public string LaunchPath { get; set; } = "";
    public string ArtworkPath { get; set; } = "";
    public string ArtworkSource { get; set; } = "";
    public string SourceUrl { get; set; } = "";
    public string Description { get; set; } = "No details yet. Choose Find game details to look up this game.";
    public string ReleaseYear { get; set; } = "";
    public string Developer { get; set; } = "";
    public string Publisher { get; set; } = "";
    public string Genre { get; set; } = "";
    public bool Favorite { get; set; }
    public bool RequiresDisc { get; set; } = true;
    public List<MetadataAttribution> MetadataSources { get; set; } = [];
    public DateTime AddedAt { get; set; } = DateTime.Now;
    public DateTime? LastPlayedAt { get; set; }
    [JsonIgnore] public bool IsDemo { get; set; }
    [JsonIgnore] public bool DiscPresent { get; set; }
    [JsonIgnore] public string Subtitle => string.Join("  ·  ", new[] { ReleaseYear, string.IsNullOrWhiteSpace(Genre) ? "PC DVD" : Genre }.Where(s => !string.IsNullOrWhiteSpace(s)));
    [JsonIgnore] public string Status => IsDemo ? "Sample game" : DiscPresent ? "Disc inserted" : "In library";
    [JsonIgnore] public string Letter => string.IsNullOrWhiteSpace(Title) ? "?" : Title[..1].ToUpperInvariant();
    [JsonIgnore] public Brush Accent => new SolidColorBrush(IsDemo && Title.Contains("Guitar") ? Color.FromRgb(135, 66, 32) : Color.FromRgb(26, 69, 116));
    [JsonIgnore] public ImageSource? Cover
    {
        get
        {
            var resource = ArtworkPath.StartsWith("pack://application:,,,/", StringComparison.Ordinal);
            if (!resource && !File.Exists(ArtworkPath)) return null;
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit(); bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.DecodePixelWidth = 1000; bmp.UriSource = new Uri(resource ? ArtworkPath : Path.GetFullPath(ArtworkPath));
                bmp.EndInit(); bmp.Freeze(); return bmp;
            }
            catch { return null; }
        }
    }
}

public record DiscSnapshot(string Root, string Label, string Id);
public record DiscScan(DiscSnapshot Disc, string Title, string Artwork, bool IsGameDisc = true);
public record MetadataAttribution(string Name, string Url);
public record SearchResult(string Title, string Summary, long PageId, string Provider = "Wikipedia")
{
    public string DisplayName => Title + " · " + Provider;
    public override string ToString() => Title;
}
public record GameInfo(string Title, string Description, string Year, string Developer, string Publisher, string Genre, string ImageUrl, string SourceUrl)
{
    public List<MetadataAttribution> Sources { get; init; } = [];
}

public static class Identity
{
    public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    public static string CleanTitle(string label)
    {
        var text = System.Text.RegularExpressions.Regex.Replace(label, @"[_\-]+", " ");
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\b(?:DVD|CD|ROM|DISC|DISK)\s*[0-9]*\b", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();
        return string.IsNullOrEmpty(text) ? "Unknown PC game" : text;
    }
}
