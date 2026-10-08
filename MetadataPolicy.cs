using System.Net.Http;
using System.Text.RegularExpressions;

namespace DiscShelf;

// EDIT THIS FILE to change database priority, endpoints, missing-data searches,
// or register another provider. Keys belong in Settings, never in source code.
public static class MetadataPolicy
{
    public static readonly string[] ProviderOrder = ["IGDB", "RAWG", "Wikipedia"];
    public const string WikipediaApi = "https://en.wikipedia.org/w/api.php";
    public const string WikidataApi = "https://www.wikidata.org/w/api.php";
    public const string WikidataEntities = "https://www.wikidata.org/wiki/Special:EntityData/";
    public const string RawgApi = "https://api.rawg.io/api/";
    public const string IgdbApi = "https://api.igdb.com/v4/games";
    public const string TwitchTokenApi = "https://id.twitch.tv/oauth2/token";
    public static readonly string[] ArtworkHosts = ["upload.wikimedia.org", "media.rawg.io", "images.igdb.com"];
    public const string ManualSearchUrl = "https://www.google.com/search?q={query}";

    // A second title search is attempted only if the first one yields no results.
    public static IEnumerable<string> BuildSearchQueries(string title)
    {
        var cleaned = Identity.CleanTitle(title);
        return new[] { title.Trim(), cleaned }.Where(s => s.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase);
    }
    public static string MatchKey(string title) => Regex.Replace(Regex.Replace(title, @"\s*\((?:video game|computer game|[0-9]{4} video game)\)\s*", "", RegexOptions.IgnoreCase), @"[^a-z0-9]", "", RegexOptions.IgnoreCase).ToLowerInvariant();
    public static string WebSearch(string title) => ManualSearchUrl.Replace("{query}", Uri.EscapeDataString(title + " PC game developer release date"));
    public static List<IMetadataProvider> CreateProviders(AppSettings settings, HttpClient http)
    {
        // Register new adapters here, then add their Name to ProviderOrder above.
        IMetadataProvider[] all = [new WikipediaProvider(http), new RawgProvider(http, SecretStore.Read(settings.RawgKeyProtected)),
            new IgdbProvider(http, settings.IgdbClientId, SecretStore.Read(settings.IgdbSecretProtected))];
        return ProviderOrder.Select(name => all.FirstOrDefault(p => p.Name == name)).OfType<IMetadataProvider>()
            .Where(p => p.Available && (p.Name switch { "Wikipedia" => settings.WikipediaEnabled, "RAWG" => settings.RawgEnabled, "IGDB" => settings.IgdbEnabled, _ => true })).ToList();
    }
}
public interface IMetadataProvider
{
    string Name { get; }
    bool Available { get; }
    Task<List<SearchResult>> Search(string title);
    Task<GameInfo> GetInfo(SearchResult result);
}
