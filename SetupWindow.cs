using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DiscShelf;

public sealed class SetupWindow : Window
{
    public AppSettings Settings { get; }
    public List<Game> ImportedGames { get; } = [];
    internal TabControl Pages { get; } = new();
    private readonly Button back = new() { Content = "Back" }, next = new() { Content = "Next", IsDefault = true };
    private readonly TextBlock step = new();
    public SetupWindow(Window owner, AppSettings settings, ThemeCatalog themes, IReadOnlyList<OpticalDrive> drives)
    {
        if (owner.IsLoaded) Owner = owner;
        Settings = settings.Copy(); Title = "Welcome to DiscShelf"; Width = 700; Height = 620; MinWidth = 640; MinHeight = 570;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false;
        SetResourceReference(BackgroundProperty, "ThemePanel");
        Pages.Background = Brushes.Transparent; Pages.BorderThickness = new Thickness(0);
        var root = new Grid { Margin = new Thickness(30) }; root.SetResourceReference(Panel.BackgroundProperty, "ThemePanel");
        root.RowDefinitions.Add(new() { Height = GridLength.Auto }); root.RowDefinitions.Add(new()); root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var heading = new StackPanel { Margin = new Thickness(0, 0, 0, 22) }; heading.Children.Add(new TextBlock { Text = "Welcome to DiscShelf", FontSize = 30, FontWeight = FontWeights.Light });
        heading.Children.Add(new TextBlock { Text = "A home for your PC disc collection", FontSize = 13, Margin = new Thickness(0, 8, 0, 0) }); root.Children.Add(heading);
        Grid.SetRow(Pages, 1); root.Children.Add(Pages);
        var welcome = Page("Welcome");
        Text(welcome, "Insert. Install. Play.", 23);
        Text(welcome, "DiscShelf saves game covers and information so you can browse your collection even after removing the DVD.");
        Text(welcome, "Choose a drive, set your library preferences, and pick a theme. You can change everything later in Settings.");
        Text(welcome, "Install and Uninstall open your game's own setup wizard. Removing a library entry leaves the installed game on your PC.");
        var drivePage = Page("DVD drive"); Text(drivePage, "Choose your DVD drive", 23);
        var choices = SettingsWindow.DriveChoices(drives, Settings.PreferredDrive);
        var drive = new ComboBox { ItemsSource = choices, SelectedItem = choices.First(c => c.Root == Settings.PreferredDrive), Margin = new Thickness(0, 10, 0, 16) };
        drive.SelectionChanged += (_, _) => { if (drive.SelectedItem is DriveChoice choice) Settings.PreferredDrive = choice.Root; }; drivePage.Children.Add(drive);
        Text(drivePage, drives.Count == 0 ? "No DVD drive is connected. Leave Automatic selected and connect one whenever you're ready." : "Automatic remembers the first drive used with a disc. You can choose a specific drive instead.");
        Check(drivePage, "Open an empty tray when Play needs a disc", Settings.OpenTrayWhenMissing, v => Settings.OpenTrayWhenMissing = v);
        Check(drivePage, "Detect inserted game discs automatically", Settings.ScanAutomatically, v => Settings.ScanAutomatically = v);
        var preferences = Page("Your library"); Text(preferences, "Make it yours", 23);
        Text(preferences, "Already have PC games installed? Find their playable .exe files to add them now. Covers, details and launch locations are saved in your library.");
        var imported = new ListBox { DisplayMemberPath = "Title", MaxHeight = 100, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 0, 14) };
        var import = new Button { Content = "Import installed games…", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 14) };
        import.Click += (_, _) => { InstalledGameImport.AddUnique(ImportedGames, InstalledGameImport.Pick(this, Settings)); imported.ItemsSource = ImportedGames.ToList(); imported.Visibility = ImportedGames.Count > 0 ? Visibility.Visible : Visibility.Collapsed; }; preferences.Children.Add(import); preferences.Children.Add(imported);
        Text(preferences, "Theme"); var theme = new ComboBox { ItemsSource = themes.Load(), DisplayMemberPath = "Name", Margin = new Thickness(0, 0, 0, 15) };
        theme.SelectedItem = theme.Items.Cast<ThemeDefinition>().FirstOrDefault(t => t.Id == Settings.ThemeId) ?? theme.Items[0];
        theme.SelectionChanged += (_, _) => { if (theme.SelectedItem is ThemeDefinition choice) Settings.ThemeId = choice.Id; }; preferences.Children.Add(theme);
        Check(preferences, "New games require a disc by default", Settings.DefaultRequiresDisc, v => Settings.DefaultRequiresDisc = v);
        Text(preferences, "Confirm this for each game when linking it. DiscShelf cannot reliably detect a game's DRM requirement. For a game that works without its DVD, choose No and Play will launch the installed file directly.");
        Check(preferences, "Ask to install when an uninstalled game disc is inserted", Settings.SuggestInstall, v => Settings.SuggestInstall = v);
        Check(preferences, "Look up game details automatically", Settings.AutoLookup, v => Settings.AutoLookup = v);
        Text(preferences, "Wikipedia/Wikidata are ready without a key. Optional RAWG/IGDB credentials are available in Settings.");
        var footer = new Grid { Margin = new Thickness(0, 22, 0, 0) }; footer.ColumnDefinitions.Add(new()); footer.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); footer.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        step.VerticalAlignment = VerticalAlignment.Center; footer.Children.Add(step);
        back.Margin = new Thickness(8, 0, 8, 0); Grid.SetColumn(back, 1); footer.Children.Add(back); Grid.SetColumn(next, 2); footer.Children.Add(next);
        back.Click += (_, _) => Pages.SelectedIndex--;
        next.Click += (_, _) => { if (Pages.SelectedIndex < 2) Pages.SelectedIndex++; else { foreach (var game in ImportedGames) game.RequiresDisc = Settings.DefaultRequiresDisc; Settings.SetupCompleted = true; Settings.Validate(); DialogResult = true; } };
        Pages.SelectionChanged += (_, _) => UpdateStep(); Pages.SelectedIndex = 0; UpdateStep();
        Grid.SetRow(footer, 2); root.Children.Add(footer); Content = root;
    }
    private StackPanel Page(string title)
    {
        var panel = new StackPanel { Margin = new Thickness(16) };
        Pages.Items.Add(new TabItem { Header = title, Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } }); return panel;
    }
    private void UpdateStep() { back.IsEnabled = Pages.SelectedIndex > 0; next.Content = Pages.SelectedIndex == 2 ? "Start my library" : "Next"; step.Text = $"Step {Pages.SelectedIndex + 1} of 3"; }
    private static void Text(Panel panel, string text, double size = 13) => panel.Children.Add(new TextBlock { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 17), LineHeight = size + 7 });
    private static void Check(Panel panel, string label, bool value, Action<bool> change)
    { var box = new CheckBox { Content = label, IsChecked = value, Margin = new Thickness(0, 0, 0, 14) }; box.Click += (_, _) => change(box.IsChecked == true); panel.Children.Add(box); }
}
