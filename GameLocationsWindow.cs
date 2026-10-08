using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace DiscShelf;

public sealed class GameLocationsWindow : Window
{
    public GameLocationsWindow(Window owner, IEnumerable<Game> games, AppSettings settings, Action save)
    {
        if (owner.IsLoaded) Owner = owner;
        Title = "Game locations"; Width = 700; Height = 540; MinWidth = 600; MinHeight = 450; WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false;
        SetResourceReference(BackgroundProperty, "ThemePanel");
        var root = new Grid { Margin = new Thickness(26) }; root.RowDefinitions.Add(new() { Height = GridLength.Auto }); root.RowDefinitions.Add(new()); root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var heading = new StackPanel(); heading.Children.Add(new TextBlock { Text = "Edit game locations", FontSize = 24, Margin = new Thickness(0, 0, 0, 12) });
        heading.Children.Add(new TextBlock { Text = "Select a game, then find its installed executable. Location changes are saved immediately to your library.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 16) }); root.Children.Add(heading);
        var list = new ListBox { ItemsSource = games.OrderBy(g => g.Title).ToList(), DisplayMemberPath = "Title" }; Grid.SetRow(list, 1); root.Children.Add(list);
        var footer = new StackPanel { Margin = new Thickness(0, 16, 0, 0) }; var location = new TextBlock { Text = "Select a game.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 15) }; footer.Children.Add(location);
        var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right }; var change = new Button { Content = "Edit location…", IsEnabled = false, Margin = new Thickness(0, 0, 10, 0) };
        list.SelectionChanged += (_, _) => { change.IsEnabled = list.SelectedItem != null; if (list.SelectedItem is Game game) location.Text = game.LaunchPath.Length > 0 ? game.LaunchPath : "No installed game file linked yet."; };
        change.Click += (_, _) =>
        {
            if (list.SelectedItem is not Game game) return;
            var picker = new OpenFileDialog { Title = "Find the installed executable for " + game.Title, Filter = "Game executable|*.exe", CheckFileExists = true };
            var folder = Path.GetDirectoryName(game.LaunchPath); if (Directory.Exists(folder)) picker.InitialDirectory = folder; else if (Directory.Exists(settings.PreferredGameFolder)) picker.InitialDirectory = settings.PreferredGameFolder;
            if (picker.ShowDialog(this) != true) return;
            if (!Path.GetExtension(picker.FileName).Equals(".exe", StringComparison.OrdinalIgnoreCase)) { MessageBox.Show(this, "Choose the installed game's .exe file.", "Game location"); return; }
            if (!picker.FileName.Equals(game.LaunchPath, StringComparison.OrdinalIgnoreCase)) game.InstalledAppId = "";
            game.LaunchPath = picker.FileName; save(); location.Text = game.LaunchPath;
        };
        buttons.Children.Add(change); var close = new Button { Content = "Done", IsCancel = true }; buttons.Children.Add(close); footer.Children.Add(buttons); Grid.SetRow(footer, 2); root.Children.Add(footer); Content = root;
    }
}
