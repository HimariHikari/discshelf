using System.Windows;
using System.IO;

namespace DiscShelf;

public partial class MainWindow
{
    private readonly DiscInsertionPrompts insertionPrompts = new();
    private Game? offeredGame;
    private DiscSnapshot? offeredDisc;
    private void QueueInstallOffer(Game game, DiscSnapshot disc)
    {
        if (!DiscInsertionPrompts.ShouldOffer(settings, game, disc)) return;
        offeredGame = game; offeredDisc = disc;
    }
    private void UpdateInstallOffer()
    {
        if (offeredGame == null || offeredDisc == null) return;
        if (!games.Contains(offeredGame) || !present.Any(d => d.Root == offeredDisc.Root && d.Id == offeredDisc.Id)
            || !DiscInsertionPrompts.ShouldOffer(settings, offeredGame, offeredDisc)) { DismissInstallOffer(); return; }
        if (closed || operationBusy || BootOverlay.Visibility == Visibility.Visible
            || Application.Current.Windows.OfType<Window>().Any(w => w != this && w.Owner == this && w.IsVisible)) return;
        DiscOfferTitle.Text = offeredGame.Title;
        DiscOfferDrive.Text = "Game disc detected in " + offeredDisc.Root;
        DiscOfferDescription.Text = "Would you like to install this game? Its cover and details are saved in your library either way.";
        var wasHidden = DiscOfferOverlay.Visibility != Visibility.Visible;
        DiscOfferOverlay.Visibility = Visibility.Visible;
        if (wasHidden && IsActive) DiscOfferInstallButton.Focus();
    }
    private void DismissInstallOffer()
    {
        offeredGame = null; offeredDisc = null; DiscOfferOverlay.Visibility = Visibility.Collapsed;
    }
    private void DiscOfferLaterClick(object sender, RoutedEventArgs e)
    {
        var title = offeredGame?.Title; DismissInstallOffer();
        if (title != null) Status(title + " is saved. You can install it later from its game screen.");
    }
    private void DiscOfferInstallClick(object sender, RoutedEventArgs e)
    {
        var game = offeredGame; var disc = offeredDisc;
        if (game == null || disc == null || !present.Any(d => d.Root == disc.Root && d.Id == disc.Id)) { DismissInstallOffer(); return; }
        DismissInstallOffer(); ShowGame(game); InstallClick(sender, e);
    }
    internal async Task VerifyDiscInsertion(Action capture)
    {
        static void Check(bool value, string message) { if (!value) throw new Exception(message); }
        settings.AutoLookup = false; settings.ScanAutomatically = true; settings.SuggestInstall = true;
        var source = Path.Combine(store.Root, "InsertedGameDisc"); Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "autorun.inf"), "[autorun]\nlabel=Detected PC game\n");
        File.WriteAllText(Path.Combine(source, "setup.exe"), "fixture, never executed");
        var disc = DiscService.FolderSnapshot(source); var drive = new OpticalDrive(source, true, disc.Label);
        await ProcessDiscSnapshot([drive], [disc], true);
        Check(games.Count == 1 && store.Load().Count == 1 && DiscOfferOverlay.Visibility == Visibility.Visible && offeredDisc?.Id == disc.Id, "A newly detected game disc must be saved and offer installation.");
        capture();
        DiscOfferLaterClick(this, new RoutedEventArgs());
        for (var i = 0; i < 3; i++) await ProcessDiscSnapshot([drive], [disc], true);
        Check(DiscOfferOverlay.Visibility == Visibility.Collapsed && games.Count == 1, "Skipping must not repeat the prompt or duplicate the library entry while the disc remains inserted.");
        await ProcessDiscSnapshot([drive with { Ready = false }], [], true);
        await ProcessDiscSnapshot([drive], [disc], true);
        Check(DiscOfferOverlay.Visibility == Visibility.Visible && games.Count == 1, "Reinserting an uninstalled known game should offer installation again without duplication.");
        await ProcessDiscSnapshot([drive with { Ready = false }], [], true);
        Check(DiscOfferOverlay.Visibility == Visibility.Collapsed && offeredDisc == null, "Removing a disc must dismiss its installation offer.");
        var game = games.Single(); game.LaunchPath = Path.Combine(store.Root, "installed-game.exe"); File.WriteAllText(game.LaunchPath, "fixture, never executed"); Save();
        await ProcessDiscSnapshot([drive], [disc], true);
        Check(DiscOfferOverlay.Visibility == Visibility.Collapsed, "An already linked installed game must not be offered installation.");
        RemoveEntries([game]); await ProcessDiscSnapshot([drive with { Ready = false }], [], true); await ProcessDiscSnapshot([drive], [disc], true);
        Check(games.Count == 0 && DiscOfferOverlay.Visibility == Visibility.Collapsed, "Removed discs must not be re-added or offered installation.");
        UndoRemoveClick(this, new RoutedEventArgs()); game.LaunchPath = ""; Save(); settings.SuggestInstall = false;
        await ProcessDiscSnapshot([drive with { Ready = false }], [], true); await ProcessDiscSnapshot([drive], [disc], true);
        Check(DiscOfferOverlay.Visibility == Visibility.Collapsed, "Disabling insertion prompts must be respected.");
        settings.SuggestInstall = true; settings.ScanAutomatically = false;
        await ProcessDiscSnapshot([drive with { Ready = false }], [], false); await ProcessDiscSnapshot([drive], [disc], false);
        Check(DiscOfferOverlay.Visibility == Visibility.Collapsed, "Disabled automatic detection must not show an automatic installation prompt.");
        settings.ScanAutomatically = true;
        var movie = Path.Combine(store.Root, "MovieDisc"); Directory.CreateDirectory(movie); Directory.CreateDirectory(Path.Combine(movie, "VIDEO_TS"));
        var movieDisc = DiscService.FolderSnapshot(movie);
        await ProcessDiscSnapshot([new(movie, true, "Movie")], [movieDisc], true);
        Check(games.Count == 1 && DiscOfferOverlay.Visibility == Visibility.Collapsed, "Non-game discs must not show installation prompts.");
    }
}
