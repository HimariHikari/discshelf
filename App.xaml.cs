using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DiscShelf;
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        if (e.Args.Contains("--operation-fixture"))
        {
            Shutdown(e.Args.Length == 2 && int.TryParse(e.Args[1], out var fixtureCode) ? fixtureCode : 1); return;
        }
        if (e.Args.Any(arg => arg is "--self-test" or "--preview" or "--metadata-test" or "--startup-test"))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
        }
        if (e.Args.Contains("--startup-test")) { _ = VerifyStartup(); return; }
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
                var setupWindow = window.CreateSetupPreview();
                var setupContent = (FrameworkElement)setupWindow.Content;
                for (var page = 0; page < setupWindow.Pages.Items.Count; page++)
                {
                    setupWindow.Pages.SelectedIndex = page;
                    setupContent.Measure(new Size(640, 550)); setupContent.Arrange(new Rect(0, 0, 640, 550)); setupContent.UpdateLayout();
                    var setupBitmap = new RenderTargetBitmap(640, 550, 96, 96, PixelFormats.Pbgra32); setupBitmap.Render(setupContent);
                    var setupEncoder = new PngBitmapEncoder(); setupEncoder.Frames.Add(BitmapFrame.Create(setupBitmap));
                    using var setupOutput = File.Create(Path.Combine(Environment.CurrentDirectory, "preview-setup-" + page + ".png")); setupEncoder.Save(setupOutput);
                }
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
                ManagementUiTests.Run(window, root);
                File.WriteAllText(Path.Combine(Environment.CurrentDirectory, "ui-test-results.txt"), "PASS: full-size and compact UI, game information and Play, install/uninstall actions, first-launch setup, removal/undo, bulk removal/undo, operation controls, settings, boot, themes, navigation, persistence, search, favourites and recent history.\n");
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
    private async Task VerifyStartup()
    {
        var root = Path.Combine(Path.GetTempPath(), "DiscShelf-startup-" + Guid.NewGuid().ToString("N"));
        MainWindow? main = null; System.Windows.Threading.DispatcherTimer? probe = null;
        try
        {
            var store = new SettingsStore(root); store.Save(new AppSettings { ShowBootScreen = false, ScanAutomatically = false });
            main = new MainWindow(root, hiddenTest: true) { Opacity = 0, ShowInTaskbar = false }; MainWindow = main;
            var completion = new TaskCompletionSource();
            probe = new() { Interval = TimeSpan.FromMilliseconds(100) };
            probe.Tick += (_, _) =>
            {
                try
                {
                    var setup = Windows.OfType<SetupWindow>().FirstOrDefault(w => w.IsVisible);
                    if (setup == null) return;
                    setup.Opacity = 0;
                    var importedPath = Path.Combine(root, "AlreadyInstalledGame.exe"); File.WriteAllText(importedPath, "fixture, never executed");
                    setup.ImportedGames.Add(InstalledGameImport.FromExecutable(importedPath, false));
                    var content = (FrameworkElement)setup.Content;
                    var buttons = FindButtons(content).ToList();
                    var next = buttons.First(b => Equals(b.Content, "Next"));
                    next.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                    next.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                    if (setup.Pages.SelectedIndex != 2) throw new Exception("Setup navigation did not reach preferences.");
                    next.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                    probe.Stop(); completion.SetResult();
                }
                catch (Exception error) { probe.Stop(); completion.TrySetException(error); }
            };
            probe.Start(); main.Show();
            await completion.Task.WaitAsync(TimeSpan.FromSeconds(15));
            await Task.Delay(250);
            if (!store.Load().SetupCompleted || !main.IsVisible) throw new Exception("Setup did not save completion or return to the library.");
            var savedImport = new LibraryStore(root).Load(); if (savedImport.Count != 1 || !savedImport[0].LaunchPath.EndsWith("AlreadyInstalledGame.exe")) throw new Exception("Setup did not save its imported game.");
            main.Close();
            main = new MainWindow(root, hiddenTest: true) { Opacity = 0, ShowInTaskbar = false }; MainWindow = main; main.Show();
            await Task.Delay(350);
            if (Windows.OfType<SetupWindow>().Any(w => w.IsVisible)) throw new Exception("Setup must not repeat on the second launch.");
            if (new LibraryStore(root).Load().Count != 1) throw new Exception("The imported game did not survive the second launch.");
            File.WriteAllText(Path.Combine(Environment.CurrentDirectory, "startup-test-results.txt"), "PASS: first launch opens setup, navigation completes, imported installed game and location save, library opens, second launch retains the game and skips completed setup.\n");
            Shutdown(0);
        }
        catch (Exception error) { File.WriteAllText(Path.Combine(Environment.CurrentDirectory, "startup-test-results.txt"), "FAIL: " + error); Shutdown(1); }
        finally
        {
            probe?.Stop(); main?.Close();
            foreach (var setup in Windows.OfType<SetupWindow>().ToArray()) setup.Close();
            var resolved = Path.GetFullPath(root);
            if (resolved.StartsWith(Path.Combine(Path.GetTempPath(), "DiscShelf-startup-"), StringComparison.OrdinalIgnoreCase) && Directory.Exists(resolved)) Directory.Delete(resolved, true);
        }
    }
    private static IEnumerable<System.Windows.Controls.Button> FindButtons(DependencyObject root)
    {
        if (root is System.Windows.Controls.Button button) yield return button;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in FindButtons(VisualTreeHelper.GetChild(root, i))) yield return child;
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
