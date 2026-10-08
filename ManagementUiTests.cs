using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DiscShelf;

internal static class ManagementUiTests
{
    public static void Run(MainWindow owner, string root)
    {
        static void Check(bool value, string message) { if (!value) throw new Exception(message); }
        Exception? failure = null;
        void Observe(string name, Action<Window> action)
        {
            GameManagementDialogs.TestDialogLoaded = window =>
            {
                try { action(window); Capture(window, name); }
                catch (Exception error) { failure = error; }
                finally { if (window.IsVisible) window.DialogResult = failure == null; }
            };
        }
        var game = new Game { Title = "Example Adventure", RequiresDisc = true };
        try
        {
            Observe("disc-choice", window =>
            {
                var radios = Descendants<RadioButton>((DependencyObject)window.Content).ToList(); Check(radios.Count == 2, "Disc choice must show both options."); radios[1].IsChecked = true;
            });
            Check(GameManagementDialogs.DiscRequirement(owner, game) == false, "The No disc choice must be returned.");
            var source = Path.Combine(root, "ui-install"); Directory.CreateDirectory(source); var setup = Path.Combine(source, "setup.exe"); File.WriteAllText(setup, "fixture, never executed");
            Observe("install", window => { var list = Descendants<ListBox>((DependencyObject)window.Content).Single(); Check(list.Items.Count == 1, "Installer dialog must show candidates."); list.SelectedIndex = 0; });
            Check(GameManagementDialogs.ChooseInstaller(owner, game.Title, source, [setup]) == setup, "Installer selection must return the actual setup path.");
            var app = new InstalledApp("fixture", game.Title, "Example Studio", source, "MsiExec.exe /X{12345678-1234-1234-1234-123456789abc}", true, "{12345678-1234-1234-1234-123456789abc}");
            var unrelated = app with { Id = "other", Name = "A different program" };
            Observe("uninstall", window =>
            {
                var list = Descendants<ListBox>((DependencyObject)window.Content).Single(); Check(list.Items.Count == 1, "Uninstall search must filter unrelated programs."); list.SelectedIndex = 0;
                Check(Descendants<TextBlock>((DependencyObject)window.Content).Any(t => t.Text.Contains("Example Studio")), "Uninstall selection must identify the publisher.");
            });
            Check(GameManagementDialogs.ChooseInstalled(owner, game, new([app, unrelated], true), true)?.Id == app.Id, "Uninstall choice must use the selected registry entry.");
            Observe("remove", window =>
            {
                var list = Descendants<ListBox>((DependencyObject)window.Content).Single(); list.SelectAll();
                Check(list.SelectedItems.Count == 2 && Descendants<Button>((DependencyObject)window.Content).Any(b => Equals(b.Content, "Remove selected (2)") && b.IsEnabled), "Bulk removal must count selected games and enable the action.");
            });
            Check(GameManagementDialogs.ManageLibrary(owner, [game, new Game { Title = "Another Game" }])?.Count == 2, "Bulk dialog must return both selected games.");
            if (failure != null) throw failure;
        }
        finally { GameManagementDialogs.TestDialogLoaded = null; }
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T match) yield return match;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) foreach (var child in Descendants<T>(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
    private static void Capture(Window window, string name)
    {
        var content = (FrameworkElement)window.Content; var width = Math.Max(1, (int)(content.ActualWidth + content.Margin.Left + content.Margin.Right)); var height = Math.Max(1, (int)(content.ActualHeight + content.Margin.Top + content.Margin.Bottom));
        content.Measure(new Size(width, height)); content.Arrange(new Rect(0, 0, width, height)); content.UpdateLayout(); var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(content);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(Path.Combine(Environment.CurrentDirectory, "preview-manage-" + name + ".png")); encoder.Save(output);
    }
}
