using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Microsoft.Win32;

namespace DiscShelf;

public partial class MainWindow : Window
{
    private readonly LibraryStore store;
    private readonly MetadataService metadata = new();
    private readonly List<Game> games;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(4) };
    private readonly Dictionary<string, string> observed = [];
    private List<DiscSnapshot> present = [];
    private Game? selected;
    private string view = "Home";
    private bool scanning, closed;
    private readonly bool hiddenTest;
    private AppSettings settings = new();
    private SettingsStore settingsStore = null!;
    private ThemeCatalog themes = null!;
    private List<OpticalDrive> opticalDrives = [];
    private bool updatingDrivePicker;
    private static readonly string[] Categories = ["Home", "Library", "Recently played", "Favorites", "Settings"];
    public MainWindow() : this(null) { }
    internal MainWindow(string? dataRoot, bool hiddenTest = false)
    {
        this.hiddenTest = hiddenTest;
        InitializeComponent();
        try { store = new(dataRoot); games = store.Load(); settingsStore = new(store.Root); settings = settingsStore.Load(); themes = new(store.Root); metadata.Configure(settings); view = settings.StartPage; }
        catch (Exception e) { MessageBox.Show(e.Message, "Library could not be opened", MessageBoxButton.OK, MessageBoxImage.Error); Application.Current.Shutdown(); store = null!; games = []; return; }
        ApplySettings(settings); Refresh();
        Loaded += async (_, _) =>
        {
            if (store.RecoveryNotice.Length > 0) MessageBox.Show(store.RecoveryNotice, "Library recovered");
            if (settingsStore.Notice.Length > 0) Status(settingsStore.Notice);
            if (settings.StartMaximised) WindowState = WindowState.Maximized;
            if (settings.ShowBootScreen) await PlayBootScreen();
            if (closed) return;
            if (!settings.SetupCompleted) await RunSetup();
            if (closed) return;
            await ScanDrives(settings.ScanAutomatically); timer.Start();
        };
        timer.Tick += async (_, _) => { UpdateClock(); await ScanDrives(settings.ScanAutomatically); };
        Closed += (_, _) => { closed = true; timer.Stop(); };
        PreviewKeyDown += HandleNavigationKey;
        UpdateClock();
        Activated += (_, _) => UpdateInstallOffer();
    }
    private void Save() => store.Save(games);
    private void Status(string message) => StatusText.Text = message;
    private static string Field(string value) => string.IsNullOrWhiteSpace(value) ? "Not available" : value;
    private void Refresh()
    {
        if (GameCards == null || games == null) return;
        foreach (var game in games) game.DiscPresent = present.Any(d => d.Id == game.DiscId);
        GameCount.Text = games.Count.ToString(); GameCountLabel.Text = games.Count == 1 ? "game in your library" : "games in your library";
        PageTitle.Text = view == "Favorites" ? "Favourites" : view == "Library" ? "Games" : view;
        Heading.Text = view switch { "Recently played" => "Recently played", "Favorites" => "Favourites", _ => "Game library" };
        var query = SearchBox.Text.Trim(); SearchPlaceholder.Visibility = query.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        IEnumerable<Game> filtered = view switch
        {
            "Recently played" => games.Where(g => g.LastPlayedAt != null).OrderByDescending(g => g.LastPlayedAt),
            "Favorites" => games.Where(g => g.Favorite).OrderBy(g => g.Title),
            "Library" => games.OrderBy(g => g.Title),
            _ => games.OrderByDescending(g => g.AddedAt)
        };
        filtered = filtered.Where(g => g.Title.Contains(query, StringComparison.OrdinalIgnoreCase) || g.Developer.Contains(query, StringComparison.OrdinalIgnoreCase));
        var visible = filtered.ToList(); GameCards.ItemsSource = visible;
        LibraryAttributionLinks.ItemsSource = visible.SelectMany(g => g.MetadataSources).DistinctBy(s => s.Name).Select(s => new MetadataAttribution(s.Name,
            s.Name switch { "RAWG" => "https://rawg.io/", "IGDB" => "https://www.igdb.com/", "Wikipedia" => "https://en.wikipedia.org/", _ => s.Url })).ToList();
        EmptyPanel.Visibility = visible.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        SectionHeading.Text = view == "Home" ? "Recently added" : view == "Library" ? "All games" : view == "Recently played" ? "Your last sessions" : "Your favourite games";
        SectionNote.Text = visible.Count + (visible.Count == 1 ? " game" : " games");
        EmptyHeading.Text = query.Length > 0 ? "No matching games" : view == "Recently played" ? "No recent games" : view == "Favorites" ? "No favourites yet" : "No games yet";
        EmptyDescription.Text = query.Length > 0 ? "Try another title or developer." : view == "Recently played" ? "Launch a game from your library to see it here." : view == "Favorites" ? "Open a game and choose Add to favourites." : "Insert a PC game disc, or choose Add game to import an installed game.";
        foreach (var (button, name) in new[] { (HomeNav, "Home"), (LibraryNav, "Library"), (RecentNav, "Recently played"), (FavoritesNav, "Favorites") })
        {
            button.Tag = name == view ? "active" : "";
        }
        if (present.Count > 0)
        {
            var current = games.FirstOrDefault(g => g.DiscId == present[0].Id);
            DriveEyebrow.Text = "Disc inserted · " + present[0].Root;
            DriveHeading.Text = current?.Title ?? Identity.CleanTitle(present[0].Label);
            DriveDescription.Text = current != null ? "Install, play or manage this game. Its details stay here after you remove the disc." : !LibraryPolicy.CanCatalogue(settings, present[0]) ? "You removed this disc from the library. Choose Add this disc to restore it." : "Choose Check for disc to add this game to your library.";
            DiscAction.Content = current != null ? "View game" : !LibraryPolicy.CanCatalogue(settings, present[0]) ? "Add this disc" : "Check for disc"; DriveDot.Fill = new SolidColorBrush(Color.FromRgb(157, 230, 217));
        }
        else
        {
            var driveRoot = DriveSelection.Resolve(settings, opticalDrives);
            DriveEyebrow.Text = driveRoot != null ? driveRoot + " · Waiting for disc" : "Disc drive · No optical drive connected"; DriveHeading.Text = "Insert a game disc";
            DriveDescription.Text = "Your game’s cover and details stay here after you remove the disc.";
            DiscAction.Content = "Check for disc"; DriveDot.Fill = new SolidColorBrush(Color.FromRgb(173, 206, 233));
        }
        if (selected != null && DetailOverlay.Visibility == Visibility.Visible) FillDetails();
        UpdateInstallOffer();
    }
    private async Task ScanDrives(bool catalogue = true)
    {
        if (scanning || closed || operationBusy) return;
        scanning = true;
        try
        {
            var snapshot = await Task.Run(() => (Drives: DiscService.GetOpticalDrives(), Discs: DiscService.GetDiscs()));
            if (closed) return;
            await ProcessDiscSnapshot(snapshot.Drives, snapshot.Discs, catalogue);
        }
        catch (Exception e) { Status("Disc scan could not finish: " + e.Message); }
        finally { scanning = false; }
    }
    private async Task ProcessDiscSnapshot(List<OpticalDrive> drives, List<DiscSnapshot> snapshotDiscs, bool catalogue)
    {
        opticalDrives = drives;
        if (DriveSelection.RememberFirst(settings, snapshotDiscs)) settingsStore.Save(settings);
        var chosen = DriveSelection.Resolve(settings, opticalDrives);
        var discs = snapshotDiscs.Where(d => d.Root == chosen).ToList();
        var inserted = insertionPrompts.Observe(discs);
        present = discs; UpdateDrivePicker();
        foreach (var root in observed.Keys.Where(root => discs.All(d => d.Root != root)).ToList()) observed.Remove(root);
        foreach (var disc in discs)
        {
            if (!catalogue) break;
            if (!LibraryPolicy.CanCatalogue(settings, disc)) continue;
            if (observed.TryGetValue(disc.Root, out var old) && old == disc.Id) continue;
            var game = games.FirstOrDefault(g => g.DiscId == disc.Id);
            if (game == null)
            {
                Status("Reading " + disc.Root + "…");
                var scan = await Task.Run(() => DiscService.Scan(disc));
                if (closed) return;
                if (!scan.IsGameDisc) { observed[disc.Root] = disc.Id; Status("This disc wasn’t recognised as a PC game. Choose Add game to add it manually."); continue; }
                // The tray may have opened while reading: don't create an entry for an incomplete scan.
                if (!Directory.Exists(disc.Root)) continue;
                game = games.FirstOrDefault(g => g.DiscId.Length == 0 && MetadataPolicy.MatchKey(g.Title) == MetadataPolicy.MatchKey(scan.Title));
                if (game != null) { game.DiscId = disc.Id; game.DiscRoot = disc.Root; game.DiscLabel = disc.Label; Save(); }
                else game = AddScan(scan);
                Refresh();
                if (settings.AutoLookup) _ = TryAutomaticInfo(game);
            }
            else { game.DiscRoot = disc.Root; Save(); Status(game.Title + " is ready. Its artwork is already saved."); }
            observed[disc.Root] = disc.Id;
            if (inserted.Contains(disc.Id)) QueueInstallOffer(game, disc);
        }
        Refresh();
    }
    private Game AddScan(DiscScan scan)
    {
        var game = new Game { Title = scan.Title, DiscId = scan.Disc.Id, DiscRoot = scan.Disc.Root, DiscLabel = scan.Disc.Label,
            RequiresDisc = settings.DefaultRequiresDisc && System.Text.RegularExpressions.Regex.IsMatch(scan.Disc.Root, @"^[A-Za-z]:\\$") };
        LibraryPolicy.AllowDisc(settings, scan.Disc.Id); settingsStore.Save(settings);
        if (scan.Artwork.Length > 0)
        {
            try { game.ArtworkPath = store.CopyArtwork(scan.Artwork, game.Id); game.ArtworkSource = "Artwork copied from your disc."; }
            catch (IOException) { }
        }
        games.Add(game); Save(); Status(game.Title + " added to your library."); return game;
    }
    private async Task TryAutomaticInfo(Game game)
    {
        var originalTitle = game.Title;
        try
        {
            var matches = await metadata.Search(originalTitle);
            var exact = matches.Where(m => MetadataPolicy.MatchKey(m.Title) == MetadataPolicy.MatchKey(originalTitle)).ToList();
            var preferred = exact.FirstOrDefault();
            if (preferred == null || exact.Count(m => m.Provider == preferred.Provider) != 1) { Status("Added " + game.Title + ". Choose Find game details to complete its information."); return; }
            var info = await metadata.GetInfo(preferred);
            if (closed || !games.Contains(game) || game.Title != originalTitle || game.SourceUrl.Length > 0) return;
            await ApplyInfo(game, info); Status("Game details saved for " + game.Title + ".");
        }
        catch { if (!closed) Status(game.Title + " is saved. Game details are unavailable right now; try again later."); }
    }
    private async Task ApplyInfo(Game game, GameInfo info)
    {
        string image = "";
        if (settings.DownloadArtwork && game.ArtworkPath.Length == 0 && info.ImageUrl.Length > 0)
        {
            try { image = await metadata.CacheImage(info.ImageUrl, game.Id, store); }
            catch { /* Text metadata can be saved even when the artwork download fails. */ }
        }
        if (closed || !games.Contains(game)) return;
        string Preserve(string existing, string incoming) => string.IsNullOrWhiteSpace(incoming) ? existing : incoming;
        game.Title = info.Title; game.Description = Preserve(game.Description, info.Description); game.ReleaseYear = Preserve(game.ReleaseYear, info.Year);
        game.Developer = Preserve(game.Developer, info.Developer); game.Publisher = Preserve(game.Publisher, info.Publisher); game.Genre = Preserve(game.Genre, info.Genre); game.SourceUrl = info.SourceUrl;
        game.MetadataSources = info.Sources.Concat(game.MetadataSources).DistinctBy(s => s.Url).ToList();
        if (image.Length > 0) { game.ArtworkPath = image; game.ArtworkSource = "Cover saved from a linked metadata source. Original image rights apply."; }
        Save(); Refresh();
    }
    private void ShowGame(Game game) { selected = game; DetailOverlay.Visibility = Visibility.Visible; FillDetails(); }
    private void FillDetails()
    {
        if (selected is not Game game) return;
        DetailTitle.Text = game.Title; DetailLetter.Text = game.Letter; DetailCover.Source = game.Cover;
        DetailStatus.Text = game.Status; DetailSubtitle.Text = game.Subtitle;
        DetailDescription.Text = game.Description == "No game information yet. Use Find game info to choose a database match." ? "No details yet. Choose Find game details to look up this game." : game.Description;
        DetailDeveloper.Text = Field(game.Developer); DetailPublisher.Text = Field(game.Publisher); DetailYear.Text = Field(game.ReleaseYear);
        DetailPlayed.Text = game.LastPlayedAt?.ToString("dd MMM yyyy, HH:mm") ?? "Not played yet";
        SourceButton.Visibility = game.SourceUrl.Length > 0 ? Visibility.Visible : Visibility.Collapsed; ArtworkCredit.Text = game.ArtworkSource;
        SourceButton.Content = (game.MetadataSources.FirstOrDefault(s => s.Url == game.SourceUrl)?.Name ?? "Wikipedia") + "  ↗";
        AdditionalSources.ItemsSource = game.MetadataSources.Where(s => s.Url != game.SourceUrl).ToList();
        FavoriteButton.Content = game.Favorite ? "★ Remove from favourites" : "☆ Add to favourites";
        foreach (var b in new[] { InfoButton, FavoriteButton, ArtworkButton, RenameButton, RemoveButton, ChangeLaunchButton }) b.IsEnabled = !game.IsDemo;
        LaunchButton.IsEnabled = true; RequiresDiscCheck.IsChecked = game.RequiresDisc; RequiresDiscCheck.IsEnabled = !game.IsDemo;
        OpenDiscButton.Visibility = game.DiscPresent ? Visibility.Visible : Visibility.Collapsed;
        LaunchButton.Content = "▶  Play";
        LaunchHint.Text = game.LaunchPath.Length == 0 ? "Install from your disc, or edit the game location to link an existing installation." : game.RequiresDisc ? "Play checks the game disc every time." : "Plays without a disc.";
        UpdateManagementDetails(game);
    }
    private void Navigate(string name) { view = name; DetailOverlay.Visibility = Visibility.Collapsed; SearchBox.Clear(); Refresh(); }
    private void HomeClick(object sender, RoutedEventArgs e) => Navigate("Home");
    private void LibraryClick(object sender, RoutedEventArgs e) => Navigate("Library");
    private void RecentClick(object sender, RoutedEventArgs e) => Navigate("Recently played");
    private void FavoritesClick(object sender, RoutedEventArgs e) => Navigate("Favorites");
    private void SearchChanged(object sender, TextChangedEventArgs e) => Refresh();
    private void CardClick(object sender, RoutedEventArgs e) { if (((Button)sender).Tag is Game game) ShowGame(game); }
    private async void ScanClick(object sender, RoutedEventArgs e)
    {
        if (present.Count > 0 && games.FirstOrDefault(g => g.DiscId == present[0].Id) is Game game) ShowGame(game);
        else
        {
            if (present.FirstOrDefault() is DiscSnapshot disc) { LibraryPolicy.AllowDisc(settings, disc.Id); settingsStore.Save(settings); observed.Remove(disc.Root); }
            Status("Checking for a game disc…"); await ScanDrives(); if (present.Count == 0) Status("No game disc found. Insert a DVD or choose Add game.");
        }
    }
    private async void AddClick(object sender, RoutedEventArgs e)
    {
        var dialog = Dialogs.Create(this, "Add a game", 520, 430);
        var panel = new StackPanel { Margin = new Thickness(25) };
        panel.Children.Add(new TextBlock { Text = "Add a game to your library", FontSize = 20, FontWeight = FontWeights.Light, Margin = new Thickness(0, 0, 0, 15) });
        panel.Children.Add(new TextBlock { Text = "Game title", FontSize = 11, Margin = new Thickness(0, 0, 0, 6) });
        var title = new TextBox { ToolTip = "Enter the game title" }; panel.Children.Add(title);
        var add = new Button { Content = "Add to library", IsDefault = true, Margin = new Thickness(0, 14, 0, 12) };
        var folder = new Button { Content = "Import a disc folder…" }; panel.Children.Add(add); panel.Children.Add(folder);
        var installed = new Button { Content = "Import installed games…", Margin = new Thickness(0, 12, 0, 0) }; panel.Children.Add(installed);
        panel.Children.Add(new TextBlock { Text = "Inserted DVDs are added automatically while DiscShelf is open.", FontSize = 11, Foreground = Brushes.LightSteelBlue, Margin = new Thickness(0, 14, 0, 0), TextWrapping = TextWrapping.Wrap });
        bool scanFolder = false, importInstalled = false;
        add.Click += (_, _) => { if (title.Text.Trim().Length > 0) dialog.DialogResult = true; };
        folder.Click += (_, _) => { scanFolder = true; dialog.DialogResult = true; };
        installed.Click += (_, _) => { importInstalled = true; dialog.DialogResult = true; };
        dialog.Content = panel; dialog.Loaded += (_, _) => title.Focus();
        if (dialog.ShowDialog() != true) return;
        if (scanFolder) { await ImportFolder(); return; }
        if (importInstalled) { ImportInstalledGames(this, settings); return; }
        var game = new Game { Title = title.Text.Trim(), RequiresDisc = settings.DefaultRequiresDisc }; games.Add(game); Save(); Refresh(); ShowGame(game);
        Status(game.Title + " added. Choose Find game details to complete its information.");
    }
    private async Task ImportFolder()
    {
        var picker = new OpenFolderDialog { Title = "Choose a game disc or extracted disc folder" };
        if (picker.ShowDialog(this) != true) return;
        try
        {
            Status("Reading disc folder…");
            var snapshot = DiscService.FolderSnapshot(picker.FolderName);
            var existing = games.FirstOrDefault(g => g.DiscId == snapshot.Id);
            if (existing != null) { ShowGame(existing); Status("This folder is already in your library."); return; }
            var scan = await Task.Run(() => DiscService.Scan(snapshot));
            var game = AddScan(scan); Refresh(); ShowGame(game); if (settings.AutoLookup) _ = TryAutomaticInfo(game);
        }
        catch (Exception e) { Status("Could not read that folder: " + e.Message); }
    }
    private async void InfoClick(object sender, RoutedEventArgs e)
    {
        if (selected is not Game game || game.IsDemo) return;
        var dialog = new MetadataDialog(this, metadata, game.Title);
        if (dialog.ShowDialog() != true || dialog.SelectedInfo == null) return;
        InfoButton.IsEnabled = false; Status("Saving details and artwork…");
        try { await ApplyInfo(game, dialog.SelectedInfo); Status("Details saved locally for " + game.Title + "."); }
        catch (Exception error) { Status("Details could not be saved: " + error.Message); }
        finally { FillDetails(); }
    }
    private void FavoriteClick(object sender, RoutedEventArgs e) { if (selected is not Game game || game.IsDemo) return; game.Favorite = !game.Favorite; Save(); Refresh(); }
    private void RenameClick(object sender, RoutedEventArgs e)
    {
        if (selected is not Game game || game.IsDemo) return;
        var name = Dialogs.Ask(this, "Edit game title", "Game title", game.Title);
        if (name == null) return; game.Title = name; Save(); Refresh();
    }
    private void ArtworkClick(object sender, RoutedEventArgs e)
    {
        if (selected is not Game game || game.IsDemo) return;
        var picker = new OpenFileDialog { Title = "Choose a game cover", Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp" };
        if (picker.ShowDialog(this) != true) return;
        try { var image = store.CopyArtwork(picker.FileName, game.Id); var probe = new Game { ArtworkPath = image }; if (probe.Cover == null) { File.Delete(image); throw new IOException("That file is not a readable image."); } game.ArtworkPath = image; game.ArtworkSource = "Artwork chosen by you, saved locally."; Save(); Refresh(); }
        catch (Exception error) { MessageBox.Show(error.Message, "Could not save artwork"); }
    }
    private void RemoveClick(object sender, RoutedEventArgs e)
    {
        if (selected is not Game game || game.IsDemo) return;
        ConfirmRemove(game);
    }
    private bool PickLaunch(Game game, string installationFolder = "")
    {
        var picker = new OpenFileDialog { Title = "Select the installed game’s .exe file", Filter = "Game executable (*.exe)|*.exe", CheckFileExists = true };
        if (game.LaunchPath.Length > 0 && Directory.Exists(Path.GetDirectoryName(game.LaunchPath))) picker.InitialDirectory = Path.GetDirectoryName(game.LaunchPath);
        else if (installationFolder.Length > 0 && Directory.Exists(installationFolder)) picker.InitialDirectory = installationFolder;
        else if (Directory.Exists(settings.PreferredGameFolder)) picker.InitialDirectory = settings.PreferredGameFolder;
        if (picker.ShowDialog(this) != true) return false;
        if (!Path.GetExtension(picker.FileName).Equals(".exe", StringComparison.OrdinalIgnoreCase)) { MessageBox.Show("Choose a Windows .exe file.", "Choose an executable"); return false; }
        if (!picker.FileName.Equals(game.LaunchPath, StringComparison.OrdinalIgnoreCase)) game.InstalledAppId = "";
        game.LaunchPath = picker.FileName; Save(); FillDetails(); Status("Game location saved for " + game.Title + "."); return ConfirmDiscRequirement(game);
    }
    private void ChangeLaunchClick(object sender, RoutedEventArgs e) { if (selected is Game game && !game.IsDemo) PickLaunch(game); }
    private async void LaunchClick(object sender, RoutedEventArgs e)
    {
        if (selected is not Game game) return;
        LaunchButton.IsEnabled = false;
        try
        {
            if (operationBusy) { Status("Finish the current installation or uninstall before playing."); return; }
            if (!game.IsDemo)
            {
                if (game.LaunchPath.Length == 0 && !PickLaunch(game)) return;
                if (!ConfirmDiscRequirement(game)) return;
            }
            if (game.RequiresDisc || game.IsDemo)
            {
            var snapshot = await Task.Run(() => (Drives: DiscService.GetOpticalDrives(), Discs: DiscService.GetDiscs()));
            opticalDrives = snapshot.Drives;
            if (DriveSelection.RememberFirst(settings, snapshot.Discs)) settingsStore.Save(settings);
            var driveRoot = DriveSelection.Resolve(settings, opticalDrives);
            present = snapshot.Discs.Where(d => d.Root == driveRoot).ToList(); UpdateDrivePicker();
            var decision = LaunchPolicy.Evaluate(game, settings, opticalDrives, snapshot.Discs);
            if (decision.Action == LaunchAction.NoDrive) { MessageBox.Show("Connect a DVD drive, then select it in Settings. For a game that does not need a DVD, turn off Requires game disc.", "No DVD drive found"); return; }
            if (decision.Action == LaunchAction.OpenEmptyTray)
            {
                try { await Task.Run(() => DriveControl.SetTray(decision.DriveRoot, true)); MessageBox.Show("The open-tray command was sent to " + decision.DriveRoot + ".\nInsert " + game.Title + " and press Play again.", "Insert game disc"); }
                catch (Exception error) { MessageBox.Show(error.Message, "DVD drive could not open"); }
                return;
            }
            if (decision.Action is LaunchAction.DiscMissing or LaunchAction.WrongDisc) { MessageBox.Show("Insert the correct disc for " + game.Title + " in " + decision.DriveRoot + " and press Play again.", "Game disc needed"); return; }
            if (game.IsDemo)
            {
                return;
            }
            }
        if (!Path.GetExtension(game.LaunchPath).Equals(".exe", StringComparison.OrdinalIgnoreCase)) { MessageBox.Show("Choose a Windows .exe file using the … button.", "Choose an executable"); return; }
        if (!File.Exists(game.LaunchPath)) { MessageBox.Show("The launch file is unavailable. Reinsert its disc or choose a new executable using the … button.", "Game unavailable"); return; }
        try
        {
            Process.Start(new ProcessStartInfo(game.LaunchPath) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(game.LaunchPath)! });
            if (settings.RecordPlayHistory) game.LastPlayedAt = DateTime.Now; Save(); Refresh(); Status("Launched " + game.Title + ".");
            if (settings.MinimiseAfterLaunch) WindowState = WindowState.Minimized;
        }
        catch (Exception error) { MessageBox.Show("The game could not start.\n\n" + error.Message, "Launch failed"); }
        }
        catch (Exception error) { MessageBox.Show("Play could not complete.\n\n" + error.Message, "DiscShelf"); }
        finally { LaunchButton.IsEnabled = true; }
    }
    private void OpenDiscClick(object sender, RoutedEventArgs e) { if (selected is Game game && game.DiscPresent) OpenPath(game.DiscRoot); }
    private void SourceClick(object sender, RoutedEventArgs e) { if (selected?.SourceUrl is string url && Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == "https") OpenPath(url); }
    private static void OpenPath(string path) => Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    private async void SettingsClick(object sender, RoutedEventArgs e)
    {
        var window = CreateSettingsPreview();
        if (window.ShowDialog() == true) { settings = window.Settings; settingsStore.Save(settings); metadata.Configure(settings); observed.Clear(); ApplySettings(settings); Refresh(); if (!settings.SetupCompleted) await RunSetup(); _ = ScanDrives(settings.ScanAutomatically); Status("Settings saved."); }
        else ApplySettings(settings);
    }
    internal SettingsWindow CreateSettingsPreview() => new(this, settings, themes, opticalDrives, ApplySettings,
        (owner, draft) => new GameLocationsWindow(owner, games, draft, () => { Save(); Refresh(); }).ShowDialog(), ImportInstalledGames);
    internal void PreviewTheme(string id) { var draft = settings.Copy(); draft.ThemeId = id; ApplySettings(draft); }
    internal void ResetPreviewTheme() => ApplySettings(settings);
    private void ApplySettings(AppSettings value)
    {
        var theme = themes.Load().FirstOrDefault(t => t.Id == value.ThemeId) ?? ThemeCatalog.BuiltIns()[0];
        ThemeCatalog.Apply(Application.Current.Resources, theme, value);
        timer.Interval = TimeSpan.FromSeconds(value.ScanIntervalSeconds);
        WaveLayer.Opacity = value.WaveOpacity; ParticleLayer.Visibility = value.ShowParticles ? Visibility.Visible : Visibility.Collapsed;
        ClockText.Visibility = value.ShowClock ? Visibility.Visible : Visibility.Collapsed;
        WaveDrift.BeginAnimation(TranslateTransform.XProperty, null); WaveDrift.BeginAnimation(TranslateTransform.YProperty, null);
        if (value.AnimateWaves)
        {
            WaveDrift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-15, 22, TimeSpan.FromSeconds(14)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
            WaveDrift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-6, 9, TimeSpan.FromSeconds(14)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
        }
        UpdateDrivePicker();
    }
    private void UpdateDrivePicker()
    {
        if (DrivePicker.IsDropDownOpen) return;
        updatingDrivePicker = true;
        try { var choices = SettingsWindow.DriveChoices(opticalDrives, settings.PreferredDrive); DrivePicker.ItemsSource = choices; DrivePicker.SelectedItem = choices.First(c => c.Root == settings.PreferredDrive); }
        finally { updatingDrivePicker = false; }
    }
    private async void DriveSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (updatingDrivePicker || settingsStore == null || DrivePicker.SelectedItem is not DriveChoice choice) return;
        settings.PreferredDrive = choice.Root; settingsStore.Save(settings); observed.Clear(); await ScanDrives(settings.ScanAutomatically);
    }
    private async void OpenTrayClick(object sender, RoutedEventArgs e)
    {
        opticalDrives = await Task.Run(DiscService.GetOpticalDrives); UpdateDrivePicker();
        var root = DriveSelection.Resolve(settings, opticalDrives);
        if (root == null) { MessageBox.Show("Connect a DVD drive or select an available drive in Settings.", "No DVD drive found"); return; }
        try { await Task.Run(() => DriveControl.SetTray(root, true)); Status("Open-tray command sent to " + root); }
        catch (Exception error) { MessageBox.Show(error.Message, "DVD drive could not open"); }
    }
    private void RequiresDiscChanged(object sender, RoutedEventArgs e) { if (selected is Game game && !game.IsDemo) { game.RequiresDisc = RequiresDiscCheck.IsChecked == true; game.DiscRequirementConfirmed = true; Save(); FillDetails(); } }
    private void LinkDiscClick(object sender, RoutedEventArgs e)
    {
        if (selected is not Game game || game.IsDemo) return;
        var disc = present.FirstOrDefault();
        if (disc == null) { MessageBox.Show("Insert this game’s disc in the selected drive first.", "No readable disc"); return; }
        if (games.Any(g => g != game && g.DiscId == disc.Id)) { MessageBox.Show("This disc is already linked to another library entry.", "Disc already linked"); return; }
        LibraryPolicy.AllowDisc(settings, disc.Id); settingsStore.Save(settings);
        game.DiscId = disc.Id; game.DiscRoot = disc.Root; game.DiscLabel = disc.Label; Save(); Refresh(); Status("Inserted disc linked to " + game.Title + ".");
    }
    private void AttributionClick(object sender, RoutedEventArgs e) { if (((Button)sender).Tag is string url && Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == "https") OpenPath(url); }
    private void WebSearchClick(object sender, RoutedEventArgs e) { if (selected is Game game) OpenPath(MetadataPolicy.WebSearch(game.Title)); }
    private void EditDetailsClick(object sender, RoutedEventArgs e)
    {
        if (selected is not Game game || game.IsDemo) return;
        var dialog = new GameDetailsDialog(this, game);
        if (dialog.ShowDialog() == true) { dialog.SaveTo(game); Save(); Refresh(); }
    }
    private async Task PlayBootScreen()
    {
        BootOverlay.Visibility = Visibility.Visible;
        await Task.Delay(TimeSpan.FromSeconds(settings.BootDurationSeconds)); if (closed) return;
        BootOverlay.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(350)));
        await Task.Delay(350); BootOverlay.Visibility = Visibility.Collapsed; BootOverlay.BeginAnimation(OpacityProperty, null);
    }
    internal void ShowBootPreview() => BootOverlay.Visibility = Visibility.Visible;
    internal void HideBootPreview() => BootOverlay.Visibility = Visibility.Collapsed;
    private void CloseDetail(object sender, RoutedEventArgs e) => DetailOverlay.Visibility = Visibility.Collapsed;
    private void BackdropClick(object sender, MouseButtonEventArgs e) => DetailOverlay.Visibility = Visibility.Collapsed;
    private void DragTitle(object sender, MouseButtonEventArgs e) { if (e.OriginalSource is Button) return; if (e.ClickCount == 2) WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized; else if (e.LeftButton == MouseButtonState.Pressed) DragMove(); }
    private void MinimizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void MaximizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void CloseClick(object sender, RoutedEventArgs e) => Close();
    private void UpdateClock()
    {
        var london = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("GMT Standard Time"));
        ClockText.Text = london.ToString(settings.Clock24Hour ? "dd MMM   HH:mm" : "dd MMM   h:mm tt");
    }
    private void MoveCategory(int direction)
    {
        var index = Keyboard.FocusedElement == SettingsNav ? Categories.Length - 1 : Array.IndexOf(Categories, view);
        var next = Categories[(index + direction + Categories.Length) % Categories.Length];
        if (next == "Settings") { SettingsNav.Focus(); return; }
        Navigate(next);
        CurrentCategoryButton().Focus();
    }
    private Button CurrentCategoryButton() => view switch { "Library" => LibraryNav, "Recently played" => RecentNav, "Favorites" => FavoritesNav, _ => HomeNav };
    private void HandleNavigationKey(object sender, KeyEventArgs e)
    {
        if (BootOverlay.Visibility == Visibility.Visible) { e.Handled = true; return; }
        if (DiscOfferOverlay.Visibility == Visibility.Visible)
        {
            if (e.Key == Key.Escape) { DiscOfferLaterClick(this, new RoutedEventArgs()); e.Handled = true; }
            return;
        }
        if (e.Key == Key.Escape)
        {
            if (DetailOverlay.Visibility == Visibility.Visible) DetailOverlay.Visibility = Visibility.Collapsed;
            else if (SearchBox.Text.Length > 0) SearchBox.Clear();
            else Navigate("Home");
            CurrentCategoryButton().Focus(); e.Handled = true; return;
        }
        if (Keyboard.FocusedElement is TextBox || DetailOverlay.Visibility == Visibility.Visible || Keyboard.Modifiers != ModifierKeys.None) return;
        var cardFocused = Keyboard.FocusedElement is Button { Tag: Game };
        if (e.Key == Key.Delete && Keyboard.FocusedElement is Button { Tag: Game game } && !game.IsDemo) { ConfirmRemove(game); e.Handled = true; return; }
        if (e.Key is Key.Left or Key.Right && !cardFocused)
        { MoveCategory(e.Key == Key.Right ? 1 : -1); e.Handled = true; }
        else if (e.Key == Key.Down && !cardFocused && GameCards.Items.Count > 0)
        {
            GameCards.UpdateLayout();
            if (GameCards.ItemContainerGenerator.ContainerFromIndex(0) is DependencyObject container)
                FindButton(container)?.Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.Up && cardFocused) { CurrentCategoryButton().Focus(); e.Handled = true; }
        else if (e.Key == Key.Enter && Keyboard.FocusedElement is not Button && GameCards.Items.Count > 0)
        { ShowGame((Game)GameCards.Items[0]); e.Handled = true; }
    }
    private static Button? FindButton(DependencyObject root)
    {
        if (root is Button button) return button;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            if (FindButton(VisualTreeHelper.GetChild(root, i)) is Button found) return found;
        return null;
    }
    internal void ShowPreviewDetails() => ShowGame(new Game { Title = "Preview Adventure", Description = "Game details and installation options appear here for games you add to your collection.", RequiresDisc = false });
    internal void HidePreviewDetails() => DetailOverlay.Visibility = Visibility.Collapsed;
    internal void VerifyInterface()
    {
        static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
        Assert(GameCards.Items.Count == 0 && games.Count == 0 && EmptyPanel.Visibility == Visibility.Visible, "A new library must start empty without bundled example games.");
        Assert((string)HomeNav.Tag == "active" && string.IsNullOrEmpty((string)LibraryNav.Tag), "Category selection is not visible.");
        MoveCategory(1); Assert(view == "Library" && (string)LibraryNav.Tag == "active", "Right-arrow category navigation failed.");
        MoveCategory(-1); Assert(view == "Home", "Left-arrow category navigation failed.");
        Navigate("Library"); Assert(GameCards.Items.Count == 0 && EmptyPanel.Visibility == Visibility.Visible, "Empty library did not render.");
        var game = new Game { Title = "Smoke Test Adventure", Developer = "Test Studio" }; games.Add(game); Save(); Refresh();
        Assert(GameCards.Items.Count == 1 && store.Load().Count == 1, "New game did not populate library.");
        SearchBox.Text = "adventure"; Assert(GameCards.Items.Count == 1, "Title search failed.");
        SearchBox.Text = "Test Studio"; Assert(GameCards.Items.Count == 1, "Developer search failed.");
        SearchBox.Text = "missing"; Assert(GameCards.Items.Count == 0, "No-match state failed.");
        Navigate("Recently played"); Assert(GameCards.Items.Count == 0, "Unplayed games must not appear in recent.");
        game.LastPlayedAt = DateTime.Now; Refresh(); Assert(GameCards.Items.Count == 1, "Recent games filter failed.");
        Navigate("Favorites"); Assert(GameCards.Items.Count == 0, "Favorites must start empty.");
        game.Favorite = true; Refresh(); Assert(GameCards.Items.Count == 1, "Favorites filter failed.");
        ShowGame(game); Assert(LaunchButton.IsEnabled && DetailTitle.Text == game.Title, "Real game details did not render.");
        Assert(InstallButton.IsEnabled && UninstallButton.IsEnabled && RemoveButton.IsEnabled, "Real games need install, uninstall, and remove actions.");
        game.DiscId = "ui-disc"; game.RequiresDisc = false; game.DiscRequirementConfirmed = true; Save(); Refresh();
        Assert(RequiresDiscCheck.IsChecked == false && LaunchHint.Text.Contains("Install", StringComparison.OrdinalIgnoreCase), "Disc-free preferences must appear in game details.");
        RemoveEntries([game]); Assert(games.Count == 0 && store.Load().Count == 0 && settings.IgnoredDiscIds.Contains("ui-disc") && UndoRemoveButton.Visibility == Visibility.Visible, "Library removal must save and suppress automatic re-add.");
        UndoRemoveClick(this, new RoutedEventArgs()); Assert(games.Count == 1 && store.Load().Count == 1 && !settings.IgnoredDiscIds.Contains("ui-disc"), "Undo must restore the game and disc recognition.");
        var other = new Game { Title = "Second game", DiscId = "ui-disc-2" }; games.Add(other); Save();
        RemoveEntries([game, other]); Assert(games.Count == 0 && lastRemoved.Count == 2, "Multiple games must be removable together.");
        UndoRemoveClick(this, new RoutedEventArgs()); Assert(games.Count == 2 && store.Load().Count == 2, "Bulk undo must restore both games.");
        SetOperationBusy(true, "UI operation fixture"); ShowGame(game); Assert(!InstallButton.IsEnabled && !RemoveButton.IsEnabled && OperationBanner.Visibility == Visibility.Visible, "Installation must prevent conflicting game actions.");
        SetOperationBusy(false, "Ready"); Assert(InstallButton.IsEnabled && RemoveButton.IsEnabled, "Game actions must return after the operation.");
        var wizard = CreateSetupPreview(); Assert(wizard.Pages.Items.Count == 3 && !wizard.Settings.SetupCompleted, "First-launch setup must have three steps and preserve completion until finished.");
        wizard.Settings.ThemeId = "violet"; Assert(settings.ThemeId != "violet", "Cancelled setup must not mutate saved settings.");
    }
}
