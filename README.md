# DiscShelf v1 · version 1.2

A native Windows launcher for PC CD/DVD games, with a PS3/PSP-inspired menu, animated waves, a saved library, configurable metadata sources, and importable themes.

[Download DiscShelf for Windows x64](https://github.com/HimariHikari/discshelf/releases/latest) · [Metadata sources](METADATA-SOURCES.md) · [Theme guide](THEMES.md)

![DiscShelf home screen](docs/screenshots/home.png)

## Start

Download **DiscShelf-Windows-x64.zip** from [Releases](https://github.com/HimariHikari/discshelf/releases/latest), extract it, then double-click **DiscShelf.exe**. If building from source, the executable is in `release`. The standalone Windows x64 build includes .NET. Wikipedia/Wikidata work without an account or key; RAWG and IGDB are optional.

1. Keep DiscShelf open and insert a PC game disc. Automatic drive selection remembers the first drive used with an inserted disc. Before that, it uses a drive containing a disc or the first available drive.
2. Click any game tile to see its cover, description, year, developer and publisher. The two example games also show real game information. **Add my copy** turns an example into your own saved entry.
3. **Play** checks the selected drive for games marked **Requires game disc**. If the drive is empty, it sends an open-tray command and asks you to insert the game. Press Play again after insertion. A different identified disc does not launch the game and is not automatically ejected.
4. With the correct disc present, the first Play asks you to choose the installed game's `.exe`. Subsequent presses launch that file. The **…** button changes it. Games that do not need a DVD can have **Requires game disc** turned off.
5. Remove the disc. Its cover and saved information remain available.

Select a DVD drive using the dropdown on Home or **Settings → Disc drives**. Manual selection stays saved. A disconnected manually selected drive does not silently switch to another device. Open/close tray buttons are also available in Settings.

**Add game** accepts a typed title or an extracted disc folder. **Use inserted disc** links a manually added game to the current disc. **Change cover** copies your own PNG/JPG/BMP. **Edit details** lets you enter information yourself; **Find game details** searches available sources. **Search the web** is available when databases cannot identify an obscure disc.

Left/Right browses categories; Down moves into tiles; Enter selects; Up returns to the menu. Escape closes game information, clears search, or returns Home.

## Settings and themes

Settings has Appearance, Disc drives, Metadata, Startup, and Library pages. Options include eight colour themes, JSON import/templates, wave animation and brightness, particles, clock format, tile size, cover fit, font, drive selection, empty-tray opening, automatic scanning and interval, metadata sources/fallback, automatic lookup, cover downloads, boot screen/duration, maximised startup, start page, example games, play history, and minimising after launch.

The boot screen says **discshelfv1**. It is enabled by default and can be disabled in Startup settings.

See [THEMES.md](THEMES.md) for the JSON format. An editable theme example is included in `Themes/example-theme.json`. Import it through Appearance or drop your own JSON into the themes folder and reload.

## Metadata configuration

Edit **MetadataPolicy.cs** to change provider priority, endpoints, retries for missing data, external search links, or the provider registry. Default priority is IGDB → RAWG → Wikipedia; sources without credentials are skipped.

- Wikipedia + Wikidata: no key required; tested live.
- RAWG: optional personal API key.
- IGDB: optional Twitch client ID and client secret.

Credentials are entered under Metadata settings and encrypted for the current Windows user. Free non-commercial access to RAWG/IGDB is subject to their registration, attribution and request limits. Authenticated RAWG/IGDB live calls need your credentials; their response parsing and fallback paths were tested with fixtures.

Other sources can fill missing fields only when there is a single exact title match. Existing information is preserved and source links are retained. If no source finds a match, you can search the web or edit details manually. Editions, region-specific dates and PC ports still need review.

See [METADATA-SOURCES.md](METADATA-SOURCES.md) for source-code entry points, adapter registration and API documentation.

## Saved data

`%LOCALAPPDATA%\DiscShelf\` contains:

- `library.json`: games, metadata, source links, disc identities, favourites and play history.
- `artwork\`: cached covers.
- `settings.json`: saved preferences and Windows-encrypted credentials.
- `themes\`: imported JSON themes.

Library saves use atomic replacement and a backup. Damaged library files are preserved and recovered from backup where possible. Previous DiscShelf libraries remain compatible.

Back up the entire data folder. Launch paths and credentials may need to be reselected/re-entered on another computer. Removing a library entry leaves installed game files and cached artwork in place.

## Practical limits

Disc recognition is a heuristic: volume serial, label, size, and root filenames identify the disc; its title comes from the label, autorun label, or executable metadata. Scan depth and file counts are bounded. Generic old installers and discs without artwork may need manual correction.

DiscShelf never runs installers or autorun commands automatically. Use **Browse disc** to install a game yourself. It does not copy game files or bypass DRM, disc checks, activation, or Windows compatibility requirements. Release years may describe the game's original release rather than its PC port. Recently played records a launch accepted by Windows, not proof that an old game started successfully.

Real tray movement and physical insertion could not be tested here because no optical drive is connected. The Windows eject/load command implementation and the missing/correct/wrong-disc decision paths are included; some drives require their physical tray button or reject software commands. An unreadable disc reported as ready is not automatically ejected.

## Build and verify

Install .NET 10 SDK on Windows:

```powershell
dotnet build DiscShelf.csproj -c Release
dotnet publish DiscShelf.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false -o release
```

Tests run in isolated temporary folders:

```powershell
Start-Process .\release\DiscShelf.exe -ArgumentList '--self-test' -Wait
Start-Process .\release\DiscShelf.exe -ArgumentList '--preview' -Wait
Start-Process .\release\DiscShelf.exe -ArgumentList '--metadata-test' -Wait
```

The self-test verifies drive selection, launch policy, encrypted credentials, settings persistence, themes, metadata fallback/attribution, RAWG/IGDB parsing, cached artwork, and library recovery. Preview renders Home at two sizes, game details, boot, another theme, and all Settings pages; it verifies navigation, persistence, search, favourites and history. The metadata test performs a live no-key lookup and cover download.

## Sources

[RAWG API](https://rawg.io/apidocs), [IGDB API](https://api-docs.igdb.com/), [MediaWiki query API](https://www.mediawiki.org/wiki/API:Query), [Wikidata access](https://www.wikidata.org/wiki/Help:Data_access), [Windows storage eject control](https://learn.microsoft.com/en-us/windows/win32/api/winioctl/ni-winioctl-ioctl_storage_eject_media).

Wikipedia descriptions retain source links and use CC BY-SA terms; Wikidata structured data uses CC0. Image rights vary and remain with their owners. Example covers: [Ultimate alliance.PNG](https://en.wikipedia.org/wiki/File:Ultimate_alliance.PNG) and [Guitar-hero-iii-cover-image.jpg](https://en.wikipedia.org/wiki/File:Guitar-hero-iii-cover-image.jpg).

The UI, waves and disc illustration are native WPF. No external UI framework or NuGet packages are required.
