using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace DiscShelf;

public static class GameManagementDialogs
{
    internal static Action<Window>? TestDialogLoaded;
    private static bool? Show(Window window)
    {
        if (TestDialogLoaded != null)
        {
            window.Opacity = 0;
            window.Loaded += (_, _) => window.Dispatcher.BeginInvoke(new Action(() => TestDialogLoaded?.Invoke(window)));
        }
        return window.ShowDialog();
    }
    public static bool? DiscRequirement(Window owner, Game game)
    {
        var window = Dialogs.Create(owner, "How does this game play?", 560, 365);
        var panel = new StackPanel { Margin = new Thickness(26) };
        panel.SetResourceReference(Panel.BackgroundProperty, "ThemePanel");
        AddText(panel, "Does " + game.Title + " need its DVD to play?", 21);
        var yes = new RadioButton { Content = "Yes — check the game disc every time I press Play", IsChecked = game.RequiresDisc, Margin = new Thickness(0, 10, 0, 16), GroupName = "disc" };
        var no = new RadioButton { Content = "No — launch the installed game without a disc", IsChecked = !game.RequiresDisc, Margin = new Thickness(0, 0, 0, 18), GroupName = "disc" };
        panel.Children.Add(yes); panel.Children.Add(no);
        AddText(panel, "If you're unsure, keep the disc check enabled. DiscShelf cannot determine DRM requirements automatically. You can change this on the game's information screen.");
        var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        buttons.Children.Add(new Button { Content = "Cancel", IsCancel = true, Margin = new Thickness(0, 0, 10, 0) });
        var save = new Button { Content = "Save choice", IsDefault = true }; save.Click += (_, _) => window.DialogResult = true; buttons.Children.Add(save); panel.Children.Add(buttons);
        window.Content = panel; return Show(window) == true ? yes.IsChecked == true : null;
    }
    public static string? ChooseInstaller(Window owner, string title, string sourceRoot, List<string> candidates)
    {
        var window = Dialogs.Create(owner, "Install " + title, 670, 530);
        var grid = DialogGrid("Install " + title, "Choose the game's setup file. Its own installation wizard will open; follow its instructions, then link the installed game in DiscShelf.", out var panel);
        var paths = candidates.ToList();
        var list = new ListBox { ItemsSource = paths.Select(p => new DriveChoice(p, Path.GetRelativePath(sourceRoot, p))).ToList() };
        if (list.Items.Count > 0) list.SelectedIndex = 0; Grid.SetRow(list, 1); grid.Children.Add(list);
        var footer = new WrapPanel { Margin = new Thickness(0, 18, 0, 0), HorizontalAlignment = HorizontalAlignment.Right };
        var browse = new Button { Content = "Choose setup file…", Margin = new Thickness(0, 0, 10, 0) };
        browse.Click += (_, _) =>
        {
            var picker = new OpenFileDialog { Title = "Choose this game's setup file", InitialDirectory = sourceRoot, Filter = "Game setup|*.exe;*.msi", CheckFileExists = true };
            if (picker.ShowDialog(window) != true) return;
            try { InstallationService.InstallPlan(sourceRoot, picker.FileName); }
            catch (IOException e) { MessageBox.Show(window, e.Message, "Choose setup file"); return; }
            if (!paths.Contains(picker.FileName)) paths.Add(picker.FileName);
            list.ItemsSource = paths.Select(p => new DriveChoice(p, Path.GetRelativePath(sourceRoot, p))).ToList(); list.SelectedItem = list.Items.Cast<DriveChoice>().First(p => p.Root == picker.FileName);
        };
        var run = new Button { Content = "Run installer", IsDefault = true, IsEnabled = list.SelectedItem != null };
        run.Click += (_, _) => window.DialogResult = true; list.SelectionChanged += (_, _) => run.IsEnabled = list.SelectedItem != null;
        footer.Children.Add(browse); footer.Children.Add(new Button { Content = "Cancel", IsCancel = true, Margin = new Thickness(0, 0, 10, 0) }); footer.Children.Add(run);
        Grid.SetRow(footer, 2); grid.Children.Add(footer); window.Content = grid;
        return Show(window) == true && list.SelectedItem is DriveChoice selected ? selected.Root : null;
    }
    public static InstalledApp? ChooseInstalled(Window owner, Game game, InstalledCatalog catalog, bool uninstall)
    {
        var window = Dialogs.Create(owner, uninstall ? "Uninstall " + game.Title : "Find installed game", 720, 610);
        var grid = DialogGrid(uninstall ? "Choose the installed game to uninstall" : "Link an installed game", "Match the correct Windows installation. You can change the search if its installed name differs from the disc title.", out var panel);
        var search = new TextBox { Text = game.Title, Margin = new Thickness(0, 0, 0, 12) }; panel.Children.Add(search);
        var list = new ListBox { DisplayMemberPath = "Name" }; Grid.SetRow(list, 1); grid.Children.Add(list);
        var footer = new StackPanel { Margin = new Thickness(0, 14, 0, 0) }; var details = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 11, Margin = new Thickness(0, 0, 0, 14) }; footer.Children.Add(details);
        var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right }; var choose = new Button { Content = uninstall ? "Choose uninstaller" : "Link installation", IsEnabled = false, IsDefault = true };
        buttons.Children.Add(new Button { Content = "Cancel", IsCancel = true, Margin = new Thickness(0, 0, 10, 0) }); buttons.Children.Add(choose); footer.Children.Add(buttons);
        void Filter()
        {
            var matches = catalog.Apps.Where(a => InstallationService.Matches(a, search.Text)).ToList(); list.ItemsSource = matches;
            var saved = matches.FirstOrDefault(a => a.Id == game.InstalledAppId); if (saved != null) list.SelectedItem = saved;
            details.Text = matches.Count == 0 ? "No matching installation. Try a shorter title or use Choose uninstaller file on the game screen." : "Select the game to see its publisher and installation folder.";
        }
        search.TextChanged += (_, _) => Filter();
        list.SelectionChanged += (_, _) => { choose.IsEnabled = list.SelectedItem != null; if (list.SelectedItem is InstalledApp app) details.Text = app.Publisher + "\n" + (app.Location.Length > 0 ? app.Location : "Installation folder not registered") + (uninstall ? "\n" + app.UninstallCommand : ""); };
        choose.Click += (_, _) => window.DialogResult = true; Filter(); Grid.SetRow(footer, 2); grid.Children.Add(footer); window.Content = grid;
        return Show(window) == true ? list.SelectedItem as InstalledApp : null;
    }
    public static List<Game>? ManageLibrary(Window owner, IEnumerable<Game> games)
    {
        var window = Dialogs.Create(owner, "Remove games from library", 670, 560);
        var grid = DialogGrid("Remove games from your library", "Select one or more games. This removes their DiscShelf entries; installed games stay on your PC. Use Uninstall on a game's screen to remove an installation.", out _);
        var list = new ListBox { ItemsSource = games.OrderBy(g => g.Title).ToList(), DisplayMemberPath = "Title", SelectionMode = SelectionMode.Extended };
        Grid.SetRow(list, 1); grid.Children.Add(list); var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
        var all = new Button { Content = "Select all", Margin = new Thickness(0, 0, 10, 0) }; all.Click += (_, _) => list.SelectAll(); buttons.Children.Add(all);
        buttons.Children.Add(new Button { Content = "Cancel", IsCancel = true, Margin = new Thickness(0, 0, 10, 0) });
        var remove = new Button { Content = "Remove selected", IsEnabled = false }; list.SelectionChanged += (_, _) => { remove.IsEnabled = list.SelectedItems.Count > 0; remove.Content = "Remove selected (" + list.SelectedItems.Count + ")"; };
        remove.Click += (_, _) => window.DialogResult = true; buttons.Children.Add(remove); Grid.SetRow(buttons, 2); grid.Children.Add(buttons); window.Content = grid;
        return Show(window) == true ? list.SelectedItems.Cast<Game>().ToList() : null;
    }
    private static Grid DialogGrid(string title, string description, out StackPanel heading)
    {
        var grid = new Grid { Margin = new Thickness(26) }; grid.SetResourceReference(Panel.BackgroundProperty, "ThemePanel"); grid.RowDefinitions.Add(new() { Height = GridLength.Auto }); grid.RowDefinitions.Add(new()); grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        heading = new StackPanel(); AddText(heading, title, 22); AddText(heading, description); grid.Children.Add(heading); return grid;
    }
    private static void AddText(Panel panel, string text, double size = 13) => panel.Children.Add(new TextBlock { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 15), LineHeight = size + 7 });
}
