using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DiscShelf;

public sealed class WikipediaProvider : IMetadataProvider
{
    private readonly HttpClient http;
    public string Name => "Wikipedia";
    public bool Available => true;
    public WikipediaProvider(HttpClient? client = null)
    {
        http = client ?? new HttpClient { Timeout = TimeSpan.FromSeconds(18) };
        if (!http.DefaultRequestHeaders.UserAgent.Any()) http.DefaultRequestHeaders.UserAgent.ParseAdd("DiscShelf/1.2 (Windows PC game library)");
    }
    private async Task<JsonDocument> GetJson(string url)
    {
        using var response = await http.GetAsync(url);
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }
    public async Task<List<SearchResult>> Search(string title)
    {
        using var data = await GetJson(MetadataPolicy.WikipediaApi + "?action=query&format=json&formatversion=2&list=search&srlimit=8&srsearch=" + Uri.EscapeDataString(title + " video game"));
        if (!data.RootElement.TryGetProperty("query", out var query)) return [];
        return query.GetProperty("search").EnumerateArray().Select(p => new SearchResult(p.GetProperty("title").GetString()!,
            WebUtility.HtmlDecode(Regex.Replace(p.GetProperty("snippet").GetString() ?? "", "<[^>]*>", "")), p.GetProperty("pageid").GetInt64())).ToList();
    }
    public async Task<GameInfo> GetInfo(SearchResult result)
    {
        using var page = await GetJson(MetadataPolicy.WikipediaApi + $"?action=query&format=json&formatversion=2&prop=extracts%7Cpageimages%7Cpageprops&exintro=1&explaintext=1&piprop=thumbnail&pilicense=any&pithumbsize=1000&pageids={result.PageId}");
        var item = page.RootElement.GetProperty("query").GetProperty("pages")[0];
        var description = item.TryGetProperty("extract", out var ext) ? ext.GetString() ?? "" : "";
        var image = item.TryGetProperty("thumbnail", out var thumb) ? thumb.GetProperty("source").GetString() ?? "" : "";
        string year = "", developer = "", publisher = "", genre = "";
        if (item.TryGetProperty("pageprops", out var props) && props.TryGetProperty("wikibase_item", out var entityId))
        {
            // A missing Wikidata field must not prevent the Wikipedia description from being saved.
            try
            {
                var id = entityId.GetString()!;
                using var entity = await GetJson(MetadataPolicy.WikidataEntities + id + ".json");
                var claims = entity.RootElement.GetProperty("entities").GetProperty(id).GetProperty("claims");
                if (claims.TryGetProperty("P577", out var dates))
                {
                    var years = dates.EnumerateArray().Select(c => Value(c)).Where(v => v.HasValue && v.Value.TryGetProperty("time", out _))
                        .Select(v => v!.Value.GetProperty("time").GetString()!.TrimStart('+')[..4]).Order().ToList();
                    year = years.FirstOrDefault() ?? "";
                }
                var dev = Ids(claims, "P178"); var pub = Ids(claims, "P123"); var genres = Ids(claims, "P136");
                var ids = dev.Concat(pub).Concat(genres).Distinct().ToArray();
                if (ids.Length > 0)
                {
                    using var labels = await GetJson(MetadataPolicy.WikidataApi + "?action=wbgetentities&format=json&props=labels&languages=en&ids=" + string.Join("%7C", ids));
                    string Label(string key)
                    {
                        var e = labels.RootElement.GetProperty("entities").GetProperty(key);
                        return e.TryGetProperty("labels", out var ls) && ls.TryGetProperty("en", out var en) ? en.GetProperty("value").GetString() ?? "" : "";
                    }
                    developer = string.Join(", ", dev.Select(Label).Where(s => s.Length > 0));
                    publisher = string.Join(", ", pub.Select(Label).Where(s => s.Length > 0));
                    genre = string.Join(", ", genres.Select(Label).Where(s => s.Length > 0));
                }
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException) { }
        }
        return new(result.Title.Replace(" (video game)", ""), description, year, developer, publisher, genre, image,
            "https://en.wikipedia.org/?curid=" + result.PageId);
    }
    private static JsonElement? Value(JsonElement claim) => claim.TryGetProperty("mainsnak", out var snak) && snak.TryGetProperty("datavalue", out var data) && data.TryGetProperty("value", out var value) ? value : null;
    private static string[] Ids(JsonElement claims, string property) => claims.TryGetProperty(property, out var array)
        ? array.EnumerateArray().Select(Value).Where(v => v.HasValue && v.Value.ValueKind == JsonValueKind.Object && v.Value.TryGetProperty("id", out _))
            .Select(v => v!.Value.GetProperty("id").GetString()!).Distinct().Take(4).ToArray() : [];
    public async Task<string> CacheImage(string url, string id, LibraryStore store)
    {
        var uri = new Uri(url);
        if (uri.Scheme != "https" || !MetadataPolicy.ArtworkHosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase)) return "";
        using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        var mime = response.Content.Headers.ContentType?.MediaType;
        var ext = mime switch { "image/jpeg" => ".jpg", "image/png" => ".png", _ => "" };
        if (ext == "" || response.Content.Headers.ContentLength > 20 * 1024 * 1024) return "";
        var target = Path.Combine(store.ArtworkDirectory, id + "-" + Guid.NewGuid().ToString("N")[..8] + ext);
        using var input = await response.Content.ReadAsStreamAsync();
        using var output = new MemoryStream();
        var buffer = new byte[8192]; int read;
        while ((read = await input.ReadAsync(buffer)) > 0)
        {
            if (output.Length + read > 20 * 1024 * 1024) return "";
            output.Write(buffer, 0, read);
        }
        await File.WriteAllBytesAsync(target, output.ToArray()); return target;
    }
}
