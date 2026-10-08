# Editing metadata sources

Start with `MetadataPolicy.cs`. It is the source-code configuration point for:

- `ProviderOrder`: priority used for automatic matching and searches. Default: IGDB, RAWG, Wikipedia. Unconfigured or disabled providers are skipped.
- `CreateProviders`: the adapter registry. Add an implementation of `IMetadataProvider` here and include its `Name` in `ProviderOrder`.
- API endpoint constants: Wikipedia, Wikidata, RAWG, IGDB, and the Twitch token service.
- `BuildSearchQueries`: title queries to retry when the first search finds nothing. The default retries the cleaned disc title.
- `MatchKey`: conservative title matching used for automatic association and metadata fallback.
- `ManualSearchUrl` and `WebSearch`: the external search used by Search the web when databases do not find a disc.
- `ArtworkHosts`: HTTPS hosts from which cover images may be cached.

`MetadataLookup.cs` contains the lookup workflow. Search shows results from each available provider, with the provider name visible. A provider failure does not discard other results. Selecting a game gets its primary metadata; when missing-field fallback is enabled, other providers are searched in order. Only one exact title match can be used to fill gaps. Existing non-empty fields are preserved. Fallback information retains additional source links. These matches still need review for editions, platform ports, and similarly named releases.

`MetadataService.cs` implements Wikipedia + Wikidata. `MetadataProviders.cs` implements RAWG and IGDB. Their `Parse` methods map each database's response into `GameInfo`. `Models.cs` defines game fields and saved source attribution.

For a new provider, implement:

```csharp
public interface IMetadataProvider
{
    string Name { get; }
    bool Available { get; }
    Task<List<SearchResult>> Search(string title);
    Task<GameInfo> GetInfo(SearchResult result);
}
```

Return your provider name in every `SearchResult`, a stable numeric ID in `PageId`, and attribution links in `GameInfo.Sources`. If an API uses string IDs, extend `SearchResult` to include a string ID before adding it. Register the provider in `CreateProviders`, add settings for any required credentials, and add cover hosts to `ArtworkHosts` where appropriate. Keep credentials in Settings, not source code or diagnostic output.

## Included sources

| Source | Access | DiscShelf behaviour |
| --- | --- | --- |
| Wikipedia + Wikidata | No API key | Available by default. Description/cover from Wikipedia; structured fields from Wikidata when available. |
| RAWG | Personal API key | Optional PC game search and detailed game/cover lookup. Source links remain visible in results, library, and game details. |
| IGDB | Twitch client ID + client secret | Optional PC game search. DiscShelf obtains and temporarily caches an app access token. |

[RAWG's API documentation](https://rawg.io/apidocs) describes free non-commercial personal access and attribution requirements. [IGDB's API documentation](https://api-docs.igdb.com/) describes free non-commercial access, registration, and Twitch authentication. Their terms and rate limits still apply. DiscShelf does not bypass API registration, request limits, or paid access restrictions.

Optional credentials are encrypted using Windows DPAPI for the current user before being written to `settings.json`. They may need to be entered again after moving to another computer or Windows account.

Without credentials, no calls are made to RAWG or IGDB. Wikipedia/Wikidata were tested live. RAWG and IGDB response parsing and fallback behaviour were verified with fixture data; authenticated live calls need your credentials.

If all sources fail or information is still missing, the existing library entry remains usable. Use Search the web or Edit details to fill it manually, or change the fallback configuration above and rebuild. No scraper or automatic executable search is used.
