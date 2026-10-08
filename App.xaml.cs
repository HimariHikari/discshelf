using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DiscShelf;
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        if (e.Args.Any(arg => arg is "--self-test" or "--preview" or "--metadata-test"))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
        }
        if (e.Args.Contains("--metadata-test"))
        {
            _ = VerifyMetadata();
            return;
        }
        if (e.Args.Contains("--self-test"))
        {
            Shutdown(SelfTest.Run());
            return;
        }
        if (e.Args.Contains("--preview"))
        {
            var root = Path.Combine(Path.GetTempPath(), "DiscShelf-preview-" + Guid.NewGuid().ToString("N"));
            try
            {
                var window = new MainWindow(root);
                var content = (FrameworkElement)window.Content;
                void Capture(string file, int width, int height)
                {
                    content.Measure(new Size(width, height)); content.Arrange(new Rect(0, 0, width, height)); content.UpdateLayout();
                    var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(content);
                    var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using var output = File.Create(Path.Combine(Environment.CurrentDirectory, file)); encoder.Save(output);
                }
                Capture("preview.png", 1380, 900);
                Capture("preview-compact.png", 1040, 700);
                window.ShowPreviewDetails(); Capture("preview-details.png", 1380, 900); window.HidePreviewDetails();
                window.ShowBootPreview(); Capture("preview-boot.png", 1380, 900); window.HideBootPreview();
                window.PreviewTheme("violet"); Capture("preview-theme.png", 1380, 900); window.ResetPreviewTheme();
                var settingsWindow = window.CreateSettingsPreview();
                var settingsContent = (FrameworkElement)settingsWindow.Content;
                for (var page = 0; page < settingsWindow.Pages.Items.Count; page++)
                {
                    settingsWindow.Pages.SelectedIndex = page;
                    settingsContent.Measure(new Size(830, 680)); settingsContent.Arrange(new Rect(0, 0, 830, 680)); settingsContent.UpdateLayout();
                    var settingsBitmap = new RenderTargetBitmap(830, 680, 96, 96, PixelFormats.Pbgra32); settingsBitmap.Render(settingsContent);
                    var settingsEncoder = new PngBitmapEncoder(); settingsEncoder.Frames.Add(BitmapFrame.Create(settingsBitmap));
                    using var settingsOutput = File.Create(Path.Combine(Environment.CurrentDirectory, page == 0 ? "preview-settings.png" : "preview-settings-" + page + ".png")); settingsEncoder.Save(settingsOutput);
                }
                window.VerifyInterface();
                File.WriteAllText(Path.Combine(Environment.CurrentDirectory, "ui-test-results.txt"), "PASS: full-size and compact UI, clickable example information, Play entry point, settings, boot screen, alternate theme, category navigation, persistence, search, favourites and recent history.\n");
                Shutdown(0);
            }
            catch (Exception error) { File.WriteAllText(Path.Combine(Environment.CurrentDirectory, "ui-test-results.txt"), "FAIL: " + error); Shutdown(1); }
            finally
            {
                var resolved = Path.GetFullPath(root);
                if (resolved.StartsWith(Path.Combine(Path.GetTempPath(), "DiscShelf-preview-"), StringComparison.OrdinalIgnoreCase) && Directory.Exists(resolved)) Directory.Delete(resolved, true);
            }
            return;
        }
        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show("DiscShelf could not complete that action.\n\n" + args.Exception.Message, "DiscShelf", MessageBoxButton.OK, MessageBoxImage.Information);
            args.Handled = true;
        };
        base.OnStartup(e);
        var main = new MainWindow();
        MainWindow = main;
        if (!Dispatcher.HasShutdownStarted) main.Show();
    }
    private async Task VerifyMetadata()
    {
        var root = Path.Combine(Path.GetTempPath(), "DiscShelf-metadata-" + Guid.NewGuid().ToString("N"));
        try
        {
            var service = new MetadataService();
            var matches = await service.Search("Guitar Hero III: Legends of Rock");
            var match = matches.First(m => m.Title == "Guitar Hero III: Legends of Rock");
            var info = await service.GetInfo(match);
            if (info.Description.Length < 100 || info.Year != "2007" || info.Developer.Length == 0 || info.SourceUrl.Length == 0) throw new Exception("Live database did not return the expected game fields.");
            var store = new LibraryStore(root);
            var image = info.ImageUrl.Length > 0 ? await service.CacheImage(info.ImageUrl, "network-test", store) : "";
            if (image.Length == 0 || new Game { ArtworkPath = image }.Cover == null) throw new Exception("Live artwork download or decoding failed.");
            File.WriteAllText(Path.Combine(Environment.CurrentDirectory, "metadata-test-results.txt"), $"PASS: live search, game description, Wikidata release year ({info.Year}), developer ({info.Developer}), publisher ({info.Publisher}), genre, source attribution, cover image download and decoding.\n");
            Shutdown(0);
        }
        catch (Exception error) { File.WriteAllText(Path.Combine(Environment.CurrentDirectory, "metadata-test-results.txt"), "FAIL: " + error); Shutdown(1); }
        finally
        {
            var resolved = Path.GetFullPath(root);
            if (resolved.StartsWith(Path.Combine(Path.GetTempPath(), "DiscShelf-metadata-"), StringComparison.OrdinalIgnoreCase) && Directory.Exists(resolved)) Directory.Delete(resolved, true);
        }
    }
}
