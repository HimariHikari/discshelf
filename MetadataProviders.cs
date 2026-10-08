using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DiscShelf;
internal static class JsonFields
{
    public static string Text(JsonElement element, string name) => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
    public static string Names(JsonElement element, string property) => element.TryGetProperty(property, out var array) && array.ValueKind == JsonValueKind.Array ? string.Join(", ", array.EnumerateArray().Select(e => Text(e, "name")).Where(s => s.Length > 0)) : "";
    public static async Task<JsonDocument> Get(HttpClient http, string url)
    {
        using var response = await http.GetAsync(url); response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }
}
public sealed class RawgProvider(HttpClient http, string key) : IMetadataProvider
{
    public string Name => "RAWG";
    public bool Available => !string.IsNullOrWhiteSpace(key);
    public async Task<List<SearchResult>> Search(string title)
    {
        using var data = await JsonFields.Get(http, MetadataPolicy.RawgApi + "games?platforms=4&page_size=8&key=" + Uri.EscapeDataString(key) + "&search=" + Uri.EscapeDataString(title));
        return data.RootElement.GetProperty("results").EnumerateArray().Select(g => new SearchResult(JsonFields.Text(g, "name"), "RAWG · PC · " + JsonFields.Text(g, "released"), g.GetProperty("id").GetInt64(), Name)).ToList();
    }
    public async Task<GameInfo> GetInfo(SearchResult result)
    {
        using var data = await JsonFields.Get(http, MetadataPolicy.RawgApi + "games/" + result.PageId + "?key=" + Uri.EscapeDataString(key));
        return Parse(data.RootElement);
    }
    internal static GameInfo Parse(JsonElement item)
    {
        var released = JsonFields.Text(item, "released");
        var description = JsonFields.Text(item, "description_raw");
        if (description.Length == 0) description = System.Net.WebUtility.HtmlDecode(Regex.Replace(JsonFields.Text(item, "description"), "<[^>]*>", ""));
        var slug = JsonFields.Text(item, "slug"); var url = "https://rawg.io/games/" + Uri.EscapeDataString(slug);
        return new(JsonFields.Text(item, "name"), description, released.Length >= 4 ? released[..4] : "", JsonFields.Names(item, "developers"), JsonFields.Names(item, "publishers"), JsonFields.Names(item, "genres"), JsonFields.Text(item, "background_image"), url)
        { Sources = [new("RAWG", url)] };
    }
}
public sealed class IgdbProvider(HttpClient http, string clientId, string secret) : IMetadataProvider
{
    public string Name => "IGDB";
    public bool Available => !string.IsNullOrWhiteSpace(clientId) && !string.IsNullOrWhiteSpace(secret);
    private string token = "";
    private DateTime expires;
    private readonly SemaphoreSlim tokenLock = new(1, 1);
    private async Task<string> Token()
    {
        await tokenLock.WaitAsync();
        try
        {
            if (token.Length > 0 && DateTime.UtcNow < expires) return token;
            using var body = new FormUrlEncodedContent(new Dictionary<string, string> { ["client_id"] = clientId, ["client_secret"] = secret, ["grant_type"] = "client_credentials" });
            using var response = await http.PostAsync(MetadataPolicy.TwitchTokenApi, body); response.EnsureSuccessStatusCode();
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            token = json.RootElement.GetProperty("access_token").GetString()!;
            expires = DateTime.UtcNow.AddSeconds(json.RootElement.GetProperty("expires_in").GetInt64() - 60); return token;
        }
        finally { tokenLock.Release(); }
    }
    private async Task<JsonDocument> Query(string query)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, MetadataPolicy.IgdbApi);
        request.Headers.Add("Client-ID", clientId); request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await Token());
        request.Content = new StringContent(query, Encoding.UTF8, "text/plain");
        using var response = await http.SendAsync(request); response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }
    public async Task<List<SearchResult>> Search(string title)
    {
        // Use a quoted JSON string to escape Apicalypse search text; never interpolate raw title syntax.
        using var data = await Query("search " + JsonSerializer.Serialize(title, new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }) + "; fields name,summary,first_release_date; where platforms = (6); limit 8;");
        return data.RootElement.EnumerateArray().Select(g => new SearchResult(JsonFields.Text(g, "name"), "IGDB · PC · " + JsonFields.Text(g, "summary"), g.GetProperty("id").GetInt64(), Name)).ToList();
    }
    public async Task<GameInfo> GetInfo(SearchResult result)
    {
        using var data = await Query("fields name,summary,first_release_date,genres.name,cover.image_id,involved_companies.developer,involved_companies.publisher,involved_companies.company.name,url; where id = " + result.PageId + "; limit 1;");
        return Parse(data.RootElement[0]);
    }
    internal static GameInfo Parse(JsonElement item)
    {
        var year = item.TryGetProperty("first_release_date", out var release) && release.ValueKind == JsonValueKind.Number ? DateTimeOffset.FromUnixTimeSeconds(release.GetInt64()).Year.ToString() : "";
        var companies = item.TryGetProperty("involved_companies", out var involved) ? involved.EnumerateArray().ToArray() : [];
        string Companies(string field) => string.Join(", ", companies.Where(c => c.TryGetProperty(field, out var yes) && yes.ValueKind == JsonValueKind.True)
            .Select(c => c.TryGetProperty("company", out var company) ? JsonFields.Text(company, "name") : "").Where(s => s.Length > 0).Distinct());
        var image = item.TryGetProperty("cover", out var cover) ? JsonFields.Text(cover, "image_id") : "";
        var url = JsonFields.Text(item, "url");
        return new(JsonFields.Text(item, "name"), JsonFields.Text(item, "summary"), year, Companies("developer"), Companies("publisher"), JsonFields.Names(item, "genres"),
            image.Length > 0 ? "https://images.igdb.com/igdb/image/upload/t_cover_big/" + Uri.EscapeDataString(image) + ".jpg" : "", url)
        { Sources = [new("IGDB", url)] };
    }
}
