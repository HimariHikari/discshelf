using System.Net.Http;

namespace DiscShelf;
public sealed class MetadataService
{
    private readonly HttpClient http;
    private List<IMetadataProvider> providers;
    private AppSettings settings;
    public string SearchNotice { get; private set; } = "";
    public IReadOnlyList<string> ActiveProviders => providers.Select(p => p.Name).ToList();
    public MetadataService(AppSettings? settings = null, HttpClient? http = null, IEnumerable<IMetadataProvider>? testProviders = null)
    {
        this.http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(18) };
        this.settings = settings ?? new();
        if (!this.http.DefaultRequestHeaders.UserAgent.Any()) this.http.DefaultRequestHeaders.UserAgent.ParseAdd("DiscShelf/1.2 (Windows PC game library)");
        providers = testProviders?.ToList() ?? MetadataPolicy.CreateProviders(this.settings, this.http);
    }
    public void Configure(AppSettings value) { settings = value; providers = MetadataPolicy.CreateProviders(value, http); }
    public async Task<List<SearchResult>> Search(string title)
    {
        var queries = MetadataPolicy.BuildSearchQueries(title).ToList();
        var searches = await Task.WhenAll(providers.Select(async provider =>
        {
            try
            {
                foreach (var query in queries) { var matches = await provider.Search(query); if (matches.Count > 0) return (Matches: matches, Error: ""); }
                return (Matches: new List<SearchResult>(), Error: "");
            }
            catch { return (Matches: new List<SearchResult>(), Error: provider.Name); }
        }));
        var failed = searches.Where(s => s.Error.Length > 0).Select(s => s.Error).ToArray();
        SearchNotice = providers.Count == 0 ? "Enable a metadata source in Settings." : failed.Length > 0 ? "Unavailable: " + string.Join(", ", failed) + ". Other enabled sources were searched." : "Searched " + string.Join(", ", ActiveProviders) + ".";
        return searches.SelectMany(s => s.Matches).DistinctBy(r => (r.Provider, r.PageId)).ToList();
    }
    public async Task<GameInfo> GetInfo(SearchResult result)
    {
        var primary = providers.FirstOrDefault(p => p.Name == result.Provider) ?? throw new InvalidOperationException("Enable this metadata source in Settings first.");
        var info = await primary.GetInfo(result);
        if (info.Sources.Count == 0) info = info with { Sources = [new(primary.Name, info.SourceUrl)] };
        if (!settings.FillMissingMetadata) return info;
        foreach (var other in providers.Where(p => p != primary))
        {
            if (!HasGaps(info)) break;
            try
            {
                var matches = (await other.Search(info.Title)).Where(m => MetadataPolicy.MatchKey(m.Title) == MetadataPolicy.MatchKey(info.Title)).ToList();
                // Never fill details from an ambiguous or merely similar title.
                if (matches.Count != 1) continue;
                var next = await other.GetInfo(matches[0]);
                if (next.Sources.Count == 0) next = next with { Sources = [new(other.Name, next.SourceUrl)] };
                info = MergeMissing(info, next);
            }
            catch { /* Preserve the primary match when a secondary source is unavailable. */ }
        }
        return info;
    }
    internal static bool HasGaps(GameInfo info) => new[] { info.Description, info.Year, info.Developer, info.Publisher, info.Genre, info.ImageUrl }.Any(string.IsNullOrWhiteSpace);
    internal static GameInfo MergeMissing(GameInfo current, GameInfo fallback)
    {
        bool used = false;
        string Fill(string a, string b) { if (string.IsNullOrWhiteSpace(a) && !string.IsNullOrWhiteSpace(b)) { used = true; return b; } return a; }
        var merged = current with { Description = Fill(current.Description, fallback.Description), Year = Fill(current.Year, fallback.Year), Developer = Fill(current.Developer, fallback.Developer),
            Publisher = Fill(current.Publisher, fallback.Publisher), Genre = Fill(current.Genre, fallback.Genre), ImageUrl = Fill(current.ImageUrl, fallback.ImageUrl) };
        return used ? merged with { Sources = current.Sources.Concat(fallback.Sources).DistinctBy(s => s.Url).ToList() } : merged;
    }
    public Task<string> CacheImage(string url, string id, LibraryStore store) => new WikipediaProvider(http).CacheImage(url, id, store);
}
