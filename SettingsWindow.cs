using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace DiscShelf;
public sealed class SettingsWindow : Window
{
    public AppSettings Settings { get; }
    internal TabControl Pages { get; }
    private readonly TextBlock notice = new() { TextWrapping = TextWrapping.Wrap, FontSize = 11, Margin = new Thickness(0, 0, 15, 0) };
    private readonly ThemeCatalog themes;
    private readonly Action<AppSettings> preview;
    private readonly ComboBox themePicker;
    private readonly PasswordBox rawg = new(), igdbSecret = new();
    private readonly TextBox igdbId = new();
    public SettingsWindow(Window owner, AppSettings settings, ThemeCatalog themes, IReadOnlyList<OpticalDrive> drives, Action<AppSettings> preview,
        Action<Window, AppSettings>? editLocations = null, Action<Window, AppSettings>? importGames = null)
    {
        if (owner.IsLoaded) Owner = owner;
        Title = "DiscShelf settings"; Width = 870; Height = 730; MinWidth = 760; MinHeight = 600; WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false;
        SetResourceReference(BackgroundProperty, "ThemePanel");
        Settings = settings.Copy(); this.themes = themes; this.preview = preview;
        var root = new Grid { Margin = new Thickness(28) }; root.SetResourceReference(Panel.BackgroundProperty, "ThemePanel"); root.RowDefinitions.Add(new() { Height = GridLength.Auto }); root.RowDefinitions.Add(new()); root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var title = new StackPanel { Margin = new Thickness(0, 0, 0,22) }; title.Children.Add(new TextBlock { Text = "Make DiscShelf yours", FontSize = 27, FontWeight = FontWeights.Light });
        title.Children.Add(new TextBlock { Text = "Themes, disc drives, metadata and startup", FontSize = 12, Margin = new Thickness(0, 7, 0, 0) }); root.Children.Add(title);
        var tabs = new TabControl { Background = Brushes.Transparent, BorderThickness = new Thickness(0) }; Pages = tabs; Grid.SetRow(tabs, 1); root.Children.Add(tabs);
        var appearance = Page(tabs, "Appearance");
        Heading(appearance, "Themes");
        themePicker = Choice(appearance, "Colour theme", themes.Load(), t => t.Id == Settings.ThemeId);
        themePicker.SelectionChanged += (_, _) => { if (themePicker.SelectedItem is ThemeDefinition theme) { Settings.ThemeId = theme.Id; preview(Settings); } };
        var tools = new WrapPanel { Margin = new Thickness(0, 0, 0, 15) };
        tools.Children.Add(Action("Import theme…", ImportTheme)); tools.Children.Add(Action("Create theme template", () => { Open(themes.ExportTemplate()); notice.Text = "Edit the JSON colours, then use Reload themes."; }));
        tools.Children.Add(Action("Open themes folder", () => Open(themes.Folder))); tools.Children.Add(Action("Reload themes", ReloadThemes)); appearance.Children.Add(tools);
        Note(appearance, "Drop a .json theme into the themes folder, then reload it. Eight themes are included; new ones need no code changes.");
        Check(appearance, "Animate the background waves", Settings.AnimateWaves, v => Settings.AnimateWaves = v);
        Label(appearance, "Wave brightness"); var brightness = new Slider { Minimum = 0, Maximum = 1, Value = Settings.WaveOpacity, TickFrequency = 0.1, Margin = new Thickness(0, 0, 0, 15), MaxWidth = 350, HorizontalAlignment = HorizontalAlignment.Left, Width = 350 };
        brightness.ValueChanged += (_, _) => Settings.WaveOpacity = brightness.Value; appearance.Children.Add(brightness);
        Check(appearance, "Show background particles", Settings.ShowParticles, v => Settings.ShowParticles = v);
        Check(appearance, "Show the clock", Settings.ShowClock, v => Settings.ShowClock = v);
        Check(appearance, "Use a 24-hour clock", Settings.Clock24Hour, v => Settings.Clock24Hour = v);
        Select(appearance, "Game tile size", new[] { "Small", "Standard", "Large" }, Settings.TileSize, v => Settings.TileSize = v);
        Select(appearance, "Game cover layout", new[] { "Fill", "Fit" }, Settings.CoverFit, v => Settings.CoverFit = v);
        Select(appearance, "Interface font", new[] { "Segoe UI", "Tahoma", "Verdana" }, Settings.FontFamily, v => Settings.FontFamily = v);

        var discPage = Page(tabs, "Disc drives"); Heading(discPage, "Choose a DVD drive");
        var choices = DriveChoices(drives, Settings.PreferredDrive);
        var drivePicker = Choice(discPage, "Selected drive", choices, d => d.Root == Settings.PreferredDrive);
        drivePicker.SelectionChanged += (_, _) => { if (drivePicker.SelectedItem is DriveChoice choice) Settings.PreferredDrive = choice.Root; };
        Note(discPage, "Automatic remembers the first drive used with an inserted disc. Before that, it uses a drive containing a disc, or the first available drive.");
        Note(discPage, "First used drive: " + (Settings.FirstUsedDrive.Length > 0 ? Settings.FirstUsedDrive : "Not chosen yet"));
        discPage.Children.Add(Action("Forget automatic drive", () => { Settings.FirstUsedDrive = ""; notice.Text = "Automatic will remember the next drive used after you save."; }));
        var trayTools = new WrapPanel { Margin = new Thickness(0, 12, 0, 15) };
        trayTools.Children.Add(ActionAsync("Open tray", () => Tray(true))); trayTools.Children.Add(ActionAsync("Close tray", () => Tray(false))); discPage.Children.Add(trayTools);
        if (drives.Count == 0) Note(discPage, "No optical drive is connected. A drive will appear here when Windows detects it.");
        Check(discPage, "Open an empty drive when I press Play", Settings.OpenTrayWhenMissing, v => Settings.OpenTrayWhenMissing = v);
        Check(discPage, "Automatically detect inserted games", Settings.ScanAutomatically, v => Settings.ScanAutomatically = v);
        Select(discPage, "Check for discs every", new[] { "2 seconds", "4 seconds", "8 seconds", "15 seconds" }, Settings.ScanIntervalSeconds + " seconds", v => Settings.ScanIntervalSeconds = int.Parse(v.Split(' ')[0]));

        var metadata = Page(tabs, "Metadata"); Heading(metadata, "Game information sources");
        Note(metadata, "Priority: " + string.Join(" → ", MetadataPolicy.ProviderOrder) + ". Sources without credentials are skipped. Wikipedia + Wikidata work without a key.");
        Check(metadata, "Look up newly detected games automatically", Settings.AutoLookup, v => Settings.AutoLookup = v);
        Check(metadata, "Try other sources for missing information", Settings.FillMissingMetadata, v => Settings.FillMissingMetadata = v);
        Check(metadata, "Download and save game covers", Settings.DownloadArtwork, v => Settings.DownloadArtwork = v);
        Check(metadata, "Enable Wikipedia + Wikidata", Settings.WikipediaEnabled, v => Settings.WikipediaEnabled = v);
        Check(metadata, "Enable RAWG when a key is provided", Settings.RawgEnabled, v => Settings.RawgEnabled = v);
        Label(metadata, "RAWG API key"); rawg.Password = SecretStore.Read(Settings.RawgKeyProtected); rawg.Margin = new Thickness(0, 0, 0, 10); metadata.Children.Add(rawg);
        metadata.Children.Add(Action("Get a RAWG key  ↗", () => Open("https://rawg.io/apidocs")));
        Check(metadata, "Enable IGDB when credentials are provided", Settings.IgdbEnabled, v => Settings.IgdbEnabled = v);
        Label(metadata, "Twitch / IGDB client ID"); igdbId.Text = Settings.IgdbClientId; igdbId.Margin = new Thickness(0, 0, 0, 10); metadata.Children.Add(igdbId);
        Label(metadata, "Twitch / IGDB client secret"); igdbSecret.Password = SecretStore.Read(Settings.IgdbSecretProtected); igdbSecret.Margin = new Thickness(0, 0, 0, 10); metadata.Children.Add(igdbSecret);
        metadata.Children.Add(Action("IGDB setup instructions  ↗", () => Open("https://api-docs.igdb.com/")));
        Note(metadata, "RAWG and IGDB offer free non-commercial access under their terms. Keys are saved with Windows user encryption. Free access does not mean unlimited requests.");
        Note(metadata, "To change providers, fallback order, search queries, or endpoints in source, edit MetadataPolicy.cs. If no match is found, use Search the web or Edit details on the game information screen.");

        var startup = Page(tabs, "Startup"); Heading(startup, "When DiscShelf starts");
        startup.Children.Add(Action("Run first-launch setup again", () => { Settings.SetupCompleted = false; notice.Text = "Save settings to open the setup wizard again."; }));
        Check(startup, "Show the discshelfv1 boot screen", Settings.ShowBootScreen, v => Settings.ShowBootScreen = v);
        Select(startup, "Boot screen duration", new[] { "1 second", "2 seconds", "3 seconds", "5 seconds" }, Settings.BootDurationSeconds + (Settings.BootDurationSeconds == 1 ? " second" : " seconds"), v => Settings.BootDurationSeconds = int.Parse(v.Split(' ')[0]));
        Check(startup, "Start maximised", Settings.StartMaximised, v => Settings.StartMaximised = v);
        var starts = new[] { new DriveChoice("Home", "Home"), new("Library", "Games"), new("Recently played", "Recent"), new("Favorites", "Favourites") };
        var pagePicker = Choice(startup, "Start on", starts, p => p.Root == Settings.StartPage); pagePicker.SelectionChanged += (_, _) => Settings.StartPage = ((DriveChoice)pagePicker.SelectedItem).Root;
        var library = Page(tabs, "Library"); Heading(library, "Your collection");
        Note(library, "Your library starts empty. Import installed PC games by choosing their executable files, or insert a game disc to add it.");
        if (importGames != null) library.Children.Add(Action("Import installed games…", () => importGames(this, Settings)));
        if (editLocations != null) library.Children.Add(Action("Edit game locations…", () => editLocations(this, Settings)));
        Label(library, "Default folder when finding installed games");
        var gameFolder = new TextBlock { Text = Settings.PreferredGameFolder.Length > 0 ? Settings.PreferredGameFolder : "Use the last folder chosen in Windows", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) }; library.Children.Add(gameFolder);
        library.Children.Add(Action("Choose default game folder…", () => { var picker = new OpenFolderDialog { Title = "Choose the folder containing installed PC games" }; if (Directory.Exists(Settings.PreferredGameFolder)) picker.InitialDirectory = Settings.PreferredGameFolder; if (picker.ShowDialog(this) == true) { Settings.PreferredGameFolder = picker.FolderName; gameFolder.Text = picker.FolderName; } }));
        Note(library, "Imported game entries and location edits are saved immediately. Other preferences are saved with Save settings.");
        Check(library, "Remember recently played games", Settings.RecordPlayHistory, v => Settings.RecordPlayHistory = v);
        Check(library, "Minimise DiscShelf after launching a game", Settings.MinimiseAfterLaunch, v => Settings.MinimiseAfterLaunch = v);
        Check(library, "New games require a disc by default", Settings.DefaultRequiresDisc, v => Settings.DefaultRequiresDisc = v);
        Check(library, "Ask to install when an uninstalled game disc is inserted", Settings.SuggestInstall, v => Settings.SuggestInstall = v);
        library.Children.Add(Action("Allow removed discs to be detected again", () => { Settings.IgnoredDiscIds.Clear(); notice.Text = "Save settings to allow previously removed discs to be added again."; }));
        Note(library, "Saved data: " + themes.Folder[..^7]); library.Children.Add(Action("Open library folder", () => Open(System.IO.Path.GetDirectoryName(themes.Folder)!)));
        Note(library, "Set the disc check on each game's information screen. Turn it off for installed games that don't need a DVD. Install and Uninstall open the game's own wizard only when you choose them. Right-click a cover or press Delete to remove a library entry; Manage supports multiple games.");
        Note(library, "DiscShelf v1 · 1.3.3\nDescriptions retain their source links. Wikipedia text uses CC BY-SA terms; Wikidata uses CC0. Images keep their original rights. Source links appear with every saved database match.");

        var footer = new Grid { Margin = new Thickness(0, 20, 0, 0) }; footer.ColumnDefinitions.Add(new()); footer.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); footer.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); footer.Children.Add(notice);
        var cancel = new Button { Content = "Cancel", IsCancel = true, Margin = new Thickness(8, 0, 0, 0) }; Grid.SetColumn(cancel, 1); footer.Children.Add(cancel);
        var save = new Button { Content = "Save settings", IsDefault = true, Margin = new Thickness(8, 0, 0, 0) }; Grid.SetColumn(save, 2); footer.Children.Add(save);
        save.Click += (_, _) => { Settings.RawgKeyProtected = SecretStore.Protect(rawg.Password.Trim()); Settings.IgdbSecretProtected = SecretStore.Protect(igdbSecret.Password.Trim()); Settings.IgdbClientId = igdbId.Text.Trim(); Settings.Validate(); DialogResult = true; };
        Grid.SetRow(footer, 2); root.Children.Add(footer); Content = root;
    }
    public static List<DriveChoice> DriveChoices(IReadOnlyList<OpticalDrive> drives, string selected)
    {
        var result = new List<DriveChoice> { new("", "Automatic · first used drive") }; result.AddRange(drives.Select(d => new DriveChoice(d.Root, d.ToString())));
        if (selected.Length > 0 && drives.All(d => d.Root != selected)) result.Add(new(selected, selected + " · Disconnected")); return result;
    }
    private async Task Tray(bool open)
    {
        var drive = DriveSelection.Resolve(Settings, await Task.Run(DiscService.GetOpticalDrives));
        if (drive == null) { notice.Text = "Connect or select a DVD drive first."; return; }
        try { await Task.Run(() => DriveControl.SetTray(drive, open)); notice.Text = (open ? "Open" : "Close") + " command sent to " + drive; }
        catch (Exception e) { notice.Text = e.Message; }
    }
    private void ReloadThemes() { themePicker.ItemsSource = themes.Load(); themePicker.SelectedItem = ((IEnumerable<ThemeDefinition>)themePicker.ItemsSource).FirstOrDefault(t => t.Id == Settings.ThemeId); notice.Text = themes.Notice.Length > 0 ? themes.Notice : "Themes reloaded."; }
    private void ImportTheme()
    {
        var picker = new OpenFileDialog { Filter = "DiscShelf theme (*.json)|*.json", Title = "Import a theme" }; if (picker.ShowDialog(this) != true) return;
        try { var theme = themes.Import(picker.FileName); Settings.ThemeId = theme.Id; ReloadThemes(); notice.Text = theme.Name + " imported."; }
        catch (Exception e) { notice.Text = e.Message; }
    }
    private static void Open(string path) => Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    private static StackPanel Page(TabControl tabs, string name)
    { var panel = new StackPanel { Margin = new Thickness(8, 20, 18, 10) }; tabs.Items.Add(new TabItem { Header = name, Style = (Style)Application.Current.FindResource(typeof(TabItem)), Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } }); return panel; }
    private static void Heading(Panel panel, string text) => panel.Children.Add(new TextBlock { Text = text, FontSize = 20, FontWeight = FontWeights.Light, Margin = new Thickness(0, 0, 0, 16) });
    private static void Label(Panel panel, string text) => panel.Children.Add(new TextBlock { Text = text, FontSize = 11, Margin = new Thickness(0, 0, 0, 7) });
    private static void Note(Panel panel, string text) => panel.Children.Add(new TextBlock { Text = text, FontSize = 12, TextWrapping = TextWrapping.Wrap, LineHeight = 20, Margin = new Thickness(0, 0, 0, 16) });
    private static void Check(Panel panel, string text, bool initial, Action<bool> change)
    { var check = new CheckBox { Content = text, IsChecked = initial, Margin = new Thickness(0, 4, 0, 13), FontSize = 12 }; check.Checked += (_, _) => change(true); check.Unchecked += (_, _) => change(false); panel.Children.Add(check); }
    private static ComboBox Choice<T>(Panel panel, string label, IEnumerable<T> values, Func<T, bool> selected)
    { Label(panel, label); var items = values.ToList(); var combo = new ComboBox { ItemsSource = items, SelectedItem = items.FirstOrDefault(selected), Margin = new Thickness(0, 0, 0, 16), MaxWidth = 450, HorizontalAlignment = HorizontalAlignment.Left, MinWidth = 300 }; panel.Children.Add(combo); return combo; }
    private static void Select(Panel panel, string label, IEnumerable<string> values, string current, Action<string> changed)
    { var combo = Choice(panel, label, values, s => s == current); combo.SelectionChanged += (_, _) => { if (combo.SelectedItem is string text) changed(text); }; }
    private static Button Action(string text, Action click)
    { var button = new Button { Content = text, Margin = new Thickness(0, 0, 8, 10), HorizontalAlignment = HorizontalAlignment.Left }; button.Click += (_, _) => click(); return button; }
    private static Button ActionAsync(string text, Func<Task> click)
    { var button = new Button { Content = text, Margin = new Thickness(0, 0, 8, 10) }; button.Click += async (_, _) => { button.IsEnabled = false; try { await click(); } finally { button.IsEnabled = true; } }; return button; }
}
