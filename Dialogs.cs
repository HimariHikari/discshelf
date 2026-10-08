using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DiscShelf;

public static class Dialogs
{
    public static Window Create(Window owner, string title, double width = 510, double height = 340)
    {
        var window = new Window { Title = title, Width = width, Height = height, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = new SolidColorBrush(Color.FromRgb(12, 30, 55)), Foreground = Brushes.White, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false };
        if (owner.IsLoaded) window.Owner = owner;
        window.SetResourceReference(Window.BackgroundProperty, "ThemePanel"); return window;
    }
    public static string? Ask(Window owner, string title, string caption, string initial = "")
    {
        var window = Create(owner, title, 490, 245);
        var panel = new StackPanel { Margin = new Thickness(25) };
        panel.Children.Add(new TextBlock { Text = caption, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 15) });
        var input = new TextBox { Text = initial }; panel.Children.Add(input);
        var ok = new Button { Content = "Save", IsDefault = true, Margin = new Thickness(0, 20, 0, 0), HorizontalAlignment = HorizontalAlignment.Right };
        ok.Click += (_, _) => { if (input.Text.Trim().Length > 0) window.DialogResult = true; };
        panel.Children.Add(ok); window.Content = panel; window.Loaded += (_, _) => { input.Focus(); input.SelectAll(); };
        return window.ShowDialog() == true ? input.Text.Trim() : null;
    }
}

public sealed class MetadataDialog : Window
{
    private readonly MetadataService service;
    private readonly TextBox input;
    private readonly ListBox results;
    private readonly TextBlock notice;
    private readonly Button search, choose;
    public GameInfo? SelectedInfo { get; private set; }
    public MetadataDialog(Window owner, MetadataService service, string title)
    {
        this.service = service; Owner = owner; Title = "Find game details"; Width = 620; Height = 550;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; Background = new SolidColorBrush(Color.FromRgb(12, 30, 55)); ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false;
        SetResourceReference(BackgroundProperty, "ThemePanel");
        var grid = new Grid { Margin = new Thickness(24) };
        grid.RowDefinitions.Add(new() { Height = GridLength.Auto }); grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) }); grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var top = new Grid(); top.ColumnDefinitions.Add(new()); top.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        input = new TextBox { Text = title }; search = new Button { Content = "Search", Margin = new Thickness(10, 0, 0, 0), IsDefault = true };
        top.Children.Add(input); top.Children.Add(search); Grid.SetColumn(search, 1); grid.Children.Add(top);
        notice = new TextBlock { Text = "Search for your game, then choose the correct edition.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 16, 0, 16), Foreground = new SolidColorBrush(Color.FromRgb(150, 176, 208)) };
        Grid.SetRow(notice, 1); grid.Children.Add(notice);
        results = new ListBox { DisplayMemberPath = "DisplayName" }; Grid.SetRow(results, 2); grid.Children.Add(results);
        choose = new Button { Content = "Save game details", HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0), IsEnabled = false };
        var footer = new StackPanel();
        var commands = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
        var web = new Button { Content = "Search the web", HorizontalAlignment = HorizontalAlignment.Left };
        web.Click += (_, _) => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(MetadataPolicy.WebSearch(input.Text)) { UseShellExecute = true });
        commands.Children.Add(web); commands.Children.Add(choose); footer.Children.Add(commands);
        var credits = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        foreach (var (name, url) in new[] { ("Wikipedia / Wikidata", "https://en.wikipedia.org/"), ("RAWG", "https://rawg.io/"), ("IGDB", "https://www.igdb.com/") })
        { var link = new Button { Content = name, Background = Brushes.Transparent, BorderBrush = Brushes.Transparent, Padding = new Thickness(0, 4, 15, 4) }; link.Click += (_, _) => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); credits.Children.Add(link); }
        footer.Children.Add(credits); Grid.SetRow(footer, 3); grid.Children.Add(footer); Content = grid;
        search.Click += async (_, _) => await Search();
        results.SelectionChanged += (_, _) => { choose.IsEnabled = results.SelectedItem != null; if (results.SelectedItem is SearchResult result) notice.Text = result.Summary; };
        choose.Click += async (_, _) =>
        {
            if (results.SelectedItem is not SearchResult result) return;
            SetBusy(true); notice.Text = "Loading game details…";
            try { SelectedInfo = await service.GetInfo(result); DialogResult = true; }
            catch (Exception) { notice.Text = "Game details couldn’t be loaded. Check your connection and try again."; }
            finally { SetBusy(false); }
        };
        Loaded += async (_, _) => await Search();
    }
    private void SetBusy(bool busy) { search.IsEnabled = !busy; input.IsEnabled = !busy; results.IsEnabled = !busy; choose.IsEnabled = !busy && results.SelectedItem != null; }
    private async Task Search()
    {
        if (string.IsNullOrWhiteSpace(input.Text)) return;
        SetBusy(true); notice.Text = "Searching for games…";
        try { results.ItemsSource = await service.Search(input.Text.Trim()); notice.Text = results.Items.Count == 0 ? "No matches found. Try another spelling, Search the web, or edit the game’s details manually. " + service.SearchNotice : "Choose the correct game, then save its details. " + service.SearchNotice; }
        catch (Exception) { notice.Text = "Search is unavailable right now. Check your connection and try again."; }
        finally { SetBusy(false); }
    }
}

public sealed class GameDetailsDialog : Window
{
    private readonly Dictionary<string, TextBox> inputs = [];
    public GameDetailsDialog(Window owner, Game game)
    {
        Owner = owner; Title = "Edit game details"; Width = 570; Height = 640; WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false;
        SetResourceReference(BackgroundProperty, "ThemePanel");
        var panel = new StackPanel { Margin = new Thickness(25) };
        panel.Children.Add(new TextBlock { Text = "Edit game details", FontSize = 23, FontWeight = FontWeights.Light, Margin = new Thickness(0, 0, 0, 18) });
        foreach (var (label, value) in new[] { ("Title", game.Title), ("Release year", game.ReleaseYear), ("Developer", game.Developer), ("Publisher", game.Publisher), ("Genre", game.Genre), ("Description", game.Description) })
        {
            panel.Children.Add(new TextBlock { Text = label, FontSize = 11, Margin = new Thickness(0, 0, 0, 6) });
            var box = new TextBox { Text = value, Margin = new Thickness(0, 0, 0, 10), AcceptsReturn = label == "Description", TextWrapping = TextWrapping.Wrap };
            if (label == "Description") { box.Height = 110; box.VerticalScrollBarVisibility = ScrollBarVisibility.Auto; }
            inputs[label] = box; panel.Children.Add(box);
        }
        var save = new Button { Content = "Save details", HorizontalAlignment = HorizontalAlignment.Right, IsDefault = true };
        save.Click += (_, _) => { if (inputs["Title"].Text.Trim().Length > 0) DialogResult = true; }; panel.Children.Add(save);
        Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }
    public void SaveTo(Game game)
    { game.Title = inputs["Title"].Text.Trim(); game.ReleaseYear = inputs["Release year"].Text.Trim(); game.Developer = inputs["Developer"].Text.Trim(); game.Publisher = inputs["Publisher"].Text.Trim(); game.Genre = inputs["Genre"].Text.Trim(); game.Description = inputs["Description"].Text.Trim(); }
}
