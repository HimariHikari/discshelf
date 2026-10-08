using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace DiscShelf;
internal static class FeatureTests
{
    public static void Run(string root)
    {
        static void Check(bool test, string message) { if (!test) throw new Exception(message); }
        var settings = new AppSettings();
        var drives = new List<OpticalDrive> { new("D:\\", false, ""), new("E:\\", true, "GAME") };
        var disc = new DiscSnapshot("E:\\", "GAME", "disc-e");
        Check(DriveSelection.Resolve(settings, drives) == "E:\\", "Automatic must use the first drive with a disc before it is remembered.");
        Check(DriveSelection.RememberFirst(settings, [disc]) && settings.FirstUsedDrive == "E:\\", "First-used drive must be remembered.");
        Check(!DriveSelection.RememberFirst(settings, [new("D:\\", "OTHER", "d")]), "Later insertions must not replace the first drive.");
        var game = new Game { RequiresDisc = true, DiscId = "disc-e" };
        Check(LaunchPolicy.Evaluate(game, settings, drives, [disc]).Action == LaunchAction.Launch, "Correct disc must permit launch.");
        Check(LaunchPolicy.Evaluate(game, settings, drives, [new("E:\\", "OTHER", "other")]).Action == LaunchAction.WrongDisc, "Wrong discs must not launch or auto-eject.");
        Check(LaunchPolicy.Evaluate(game, settings, drives, []).Action == LaunchAction.DiscMissing, "A ready drive with unreadable identity must not be treated as empty.");
        settings.PreferredDrive = "D:\\";
        Check(LaunchPolicy.Evaluate(game, settings, drives, [disc]).Action == LaunchAction.OpenEmptyTray, "Manual selection must control the tray target, even when another drive has a disc.");
        settings.OpenTrayWhenMissing = false;
        Check(LaunchPolicy.Evaluate(game, settings, drives, []).Action == LaunchAction.DiscMissing, "Disabling automatic tray opening must be respected.");
        settings.PreferredDrive = "Z:\\";
        Check(LaunchPolicy.Evaluate(game, settings, drives, [disc]).Action == LaunchAction.NoDrive, "Disconnected manual drive must not fall back to another device.");
        game.RequiresDisc = false;
        Check(LaunchPolicy.Evaluate(game, settings, [], []).Action == LaunchAction.Launch, "Disc-free games must work without a drive.");
        var secret = SecretStore.Protect("fixture-api-key");
        Check(secret != "fixture-api-key" && SecretStore.Read(secret) == "fixture-api-key", "Credential encryption must round-trip.");
        settings.RawgKeyProtected = secret; settings.ThemeId = "violet"; settings.ShowBootScreen = false;
        var settingsStore = new SettingsStore(Path.Combine(root, "settings-test")); settingsStore.Save(settings);
        var read = settingsStore.Load(); Check(read.PreferredDrive == "Z:\\" && read.ThemeId == "violet" && !read.ShowBootScreen && SecretStore.Read(read.RawgKeyProtected) == "fixture-api-key", "Settings must survive a restart.");
        Check(!File.ReadAllText(Path.Combine(root, "settings-test", "settings.json")).Contains("fixture-api-key"), "API credentials must not be saved as plain text.");
        var themes = new ThemeCatalog(Path.Combine(root, "themes-test")); Check(themes.Load().Count == 8, "Built-in theme collection is incomplete.");
        var template = themes.ExportTemplate(); var imported = themes.Import(template); Check(themes.Load().Any(t => t.Id == imported.Id), "Theme template must import from its own folder.");
        var invalid = Path.Combine(root, "bad-theme.json"); File.WriteAllText(invalid, "{\"Id\":\"../bad\",\"Name\":\"Broken\"}");
        bool rejected = false; try { themes.Import(invalid); } catch (IOException) { rejected = true; } Check(rejected, "Invalid theme IDs must be rejected.");
        Check(new MetadataService().ActiveProviders.SequenceEqual(new[] { "Wikipedia" }), "No-key metadata must work without credential providers.");
        var primary = new GameInfo("Example Game", "Primary description", "2006", "", "", "Action", "", "https://en.wikipedia.org/example") { Sources = [new("Wikipedia", "https://en.wikipedia.org/example")] };
        var fallback = new GameInfo("Example Game", "Different description", "2007", "Test Studio", "Test Publisher", "Different genre", "https://media.rawg.io/cover.jpg", "https://rawg.io/games/example") { Sources = [new("RAWG", "https://rawg.io/games/example")] };
        var providers = new IMetadataProvider[] { new FixtureProvider("Wikipedia", primary), new FixtureProvider("RAWG", fallback), new FixtureProvider("Unavailable", fallback, failure: true) };
        var lookup = new MetadataService(testProviders: providers);
        var matches = lookup.Search("Example Game").GetAwaiter().GetResult(); Check(matches.Count == 2 && lookup.SearchNotice.Contains("Unavailable"), "One failed provider must not prevent other search results.");
        var info = lookup.GetInfo(matches[0]).GetAwaiter().GetResult();
        Check(info.Description == "Primary description" && info.Year == "2006" && info.Developer == "Test Studio" && info.Publisher == "Test Publisher" && info.Sources.Count == 2, "Fallback metadata must fill gaps without overwriting existing fields or losing attribution.");
        var ambiguous = new MetadataService(testProviders: [new FixtureProvider("Wikipedia", primary), new FixtureProvider("RAWG", fallback, ambiguous: true)]);
        var unchanged = ambiguous.GetInfo(new("Example Game", "", 1)).GetAwaiter().GetResult(); Check(unchanged.Developer == "", "Ambiguous fallback matches must not be merged.");
        using var rawgJson = JsonDocument.Parse("{\"name\":\"Example Game\",\"slug\":\"example\",\"released\":\"2006-10-24\",\"description_raw\":\"A game\",\"developers\":[{\"name\":\"Studio\"}],\"publishers\":[{\"name\":\"Publisher\"}],\"genres\":[{\"name\":\"Action\"}],\"background_image\":\"https://media.rawg.io/cover.jpg\"}");
        var rawg = RawgProvider.Parse(rawgJson.RootElement); Check(rawg.Year == "2006" && rawg.Developer == "Studio" && rawg.Sources[0].Name == "RAWG", "RAWG adapter parsing failed.");
        using var igdbJson = JsonDocument.Parse("{\"name\":\"Example Game\",\"first_release_date\":1161648000,\"summary\":\"A game\",\"url\":\"https://www.igdb.com/games/example\",\"genres\":[{\"name\":\"Action\"}],\"cover\":{\"image_id\":\"fixture\"},\"involved_companies\":[{\"developer\":true,\"publisher\":false,\"company\":{\"name\":\"Studio\"}},{\"developer\":false,\"publisher\":true,\"company\":{\"name\":\"Publisher\"}}]}");
        var igdb = IgdbProvider.Parse(igdbJson.RootElement); Check(igdb.Year == "2006" && igdb.Developer == "Studio" && igdb.Publisher == "Publisher" && igdb.ImageUrl.Contains("t_cover_big"), "IGDB adapter parsing failed.");
    }
    private sealed class FixtureProvider(string name, GameInfo info, bool failure = false, bool ambiguous = false) : IMetadataProvider
    {
        public string Name => name; public bool Available => true;
        public Task<List<SearchResult>> Search(string title) => failure ? Task.FromException<List<SearchResult>>(new HttpRequestException("fixture failure")) : Task.FromResult(ambiguous ? new List<SearchResult> { new(info.Title, "", 1, name), new(info.Title, "", 2, name) } : [new(info.Title, "", 1, name)]);
        public Task<GameInfo> GetInfo(SearchResult result) => Task.FromResult(info);
    }
}
