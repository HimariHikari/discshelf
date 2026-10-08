using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace DiscShelf;

public partial class MainWindow
{
    private bool operationBusy;
    private List<Game> lastRemoved = [];
    private async Task RunSetup()
    {
        opticalDrives = await Task.Run(DiscService.GetOpticalDrives);
        if (closed) return;
        var wizard = CreateSetupPreview();
        if (hiddenTest) wizard.Opacity = 0;
        if (wizard.ShowDialog() != true) { Status("Setup can be completed next time, or from Settings → Startup."); return; }
        var added = InstalledGameImport.AddUnique(games, wizard.ImportedGames); Save();
        settings = wizard.Settings; settingsStore.Save(settings); metadata.Configure(settings); view = settings.StartPage; observed.Clear(); ApplySettings(settings); Refresh();
        if (settings.AutoLookup) foreach (var game in added) _ = TryAutomaticInfo(game);
        Status("Your library is ready. Insert a game DVD or add an installed game.");
    }
    internal SetupWindow CreateSetupPreview() => new(this, settings, themes, opticalDrives);
    private void ImportInstalledGames(Window owner, AppSettings draft)
    {
        var candidates = InstalledGameImport.Pick(owner, draft); if (candidates.Count == 0) return;
        var added = InstalledGameImport.AddUnique(games, candidates); Save(); Refresh();
        Status(added.Count == 0 ? "These game locations are already in your library." : "Imported " + added.Count + " installed games. Edit titles or locations on each game's screen.");
        if (settings.AutoLookup) foreach (var game in added) _ = TryAutomaticInfo(game);
        if (owner == this && added.Count > 0) ShowGame(added[0]);
    }
    private bool ConfirmDiscRequirement(Game game)
    {
        if (game.DiscRequirementConfirmed) return true;
        var choice = GameManagementDialogs.DiscRequirement(this, game);
        if (choice == null) return false;
        game.RequiresDisc = choice.Value; game.DiscRequirementConfirmed = true; Save(); Refresh(); return true;
    }
    private void UpdateManagementDetails(Game game)
    {
        InstallationStatus.Text = game.LaunchPath.Length == 0 ? "No installed game linked. Install from your DVD or edit the game location." : File.Exists(game.LaunchPath) ? "Installed game linked" : "The linked game file is missing. Install again or edit the game location.";
        GameLocationText.Text = game.LaunchPath.Length == 0 ? "Game location: not set" : "Game location: " + game.LaunchPath;
        InstallButton.IsEnabled = LinkInstalledButton.IsEnabled = UninstallButton.IsEnabled = ManualUninstallButton.IsEnabled = !game.IsDemo && !operationBusy;
        RemoveButton.IsEnabled = ChangeLaunchButton.IsEnabled = RequiresDiscCheck.IsEnabled = !game.IsDemo && !operationBusy;
        LaunchButton.IsEnabled = !operationBusy;
    }
    private void SetOperationBusy(bool busy, string message)
    {
        operationBusy = busy; SettingsNav.IsEnabled = ManageLibraryButton.IsEnabled = UndoRemoveButton.IsEnabled = !busy;
        OperationBanner.Visibility = busy ? Visibility.Visible : Visibility.Collapsed; OperationText.Text = message;
        if (selected != null) FillDetails(); Status(message);
    }
    private void ConfirmRemove(Game game)
    {
        if (operationBusy || game.IsDemo) return;
        if (MessageBox.Show(this, "Remove “" + game.Title + "” from DiscShelf?\n\nIts installation stays on your PC. This disc will not be added again automatically. You can undo this removal or choose Add this disc later.", "Remove from library", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        RemoveEntries([game]);
    }
    private void RemoveEntries(IEnumerable<Game> entries)
    {
        var removed = entries.Where(g => games.Contains(g) && !g.IsDemo).Distinct().ToList(); if (removed.Count == 0) return;
        var oldSettings = settings.Copy();
        foreach (var game in removed) { LibraryPolicy.ForgetDisc(settings, game); games.Remove(game); }
        try { settingsStore.Save(settings); Save(); }
        catch { settings = oldSettings; games.AddRange(removed); settingsStore.Save(settings); throw; }
        lastRemoved = removed; UndoRemoveButton.Visibility = Visibility.Visible;
        if (selected != null && removed.Contains(selected)) { selected = null; DetailOverlay.Visibility = Visibility.Collapsed; }
        Refresh(); Status(removed.Count == 1 ? "Removed “" + removed[0].Title + "” from your library." : "Removed " + removed.Count + " games from your library.");
    }
    private void UndoRemoveClick(object sender, RoutedEventArgs e)
    {
        if (operationBusy || lastRemoved.Count == 0) return;
        var restored = lastRemoved.Where(g => !games.Any(existing => existing.Id == g.Id || g.DiscId.Length > 0 && existing.DiscId == g.DiscId)).ToList();
        var oldSettings = settings.Copy();
        foreach (var game in restored) { games.Add(game); LibraryPolicy.AllowDisc(settings, game.DiscId); }
        try { settingsStore.Save(settings); Save(); }
        catch { settings = oldSettings; foreach (var game in restored) games.Remove(game); settingsStore.Save(settings); throw; }
        lastRemoved.Clear(); UndoRemoveButton.Visibility = Visibility.Collapsed; Refresh(); Status("Library removal undone.");
    }
    private void ManageLibraryClick(object sender, RoutedEventArgs e)
    {
        if (operationBusy) return;
        if (games.Count == 0) { Status("Your library is empty. Add a game first."); return; }
        var entries = GameManagementDialogs.ManageLibrary(this, games); if (entries != null) RemoveEntries(entries);
    }
    private static Game? ContextGame(object sender) => (sender as MenuItem)?.Tag as Game;
    private void ContextViewClick(object sender, RoutedEventArgs e) { if (ContextGame(sender) is Game game) ShowGame(game); }
    private void ContextRemoveClick(object sender, RoutedEventArgs e) { if (ContextGame(sender) is Game game) ConfirmRemove(game); }
    private void ContextPlayClick(object sender, RoutedEventArgs e) { if (ContextGame(sender) is Game game && !game.IsDemo) { ShowGame(game); LaunchClick(sender, e); } }
    private void ContextInstallClick(object sender, RoutedEventArgs e) { if (ContextGame(sender) is Game game && !game.IsDemo) { ShowGame(game); InstallClick(sender, e); } }
    private void ContextUninstallClick(object sender, RoutedEventArgs e) { if (ContextGame(sender) is Game game && !game.IsDemo) { ShowGame(game); UninstallClick(sender, e); } }
    private async void InstallClick(object sender, RoutedEventArgs e)
    {
        if (selected is not Game game || game.IsDemo || operationBusy) return;
        try
        {
            string root, expectedDiscId = "";
            if (game.DiscRoot.Length > 3 && Directory.Exists(game.DiscRoot)) root = game.DiscRoot;
            else
            {
                var snapshot = await Task.Run(() => (Drives: DiscService.GetOpticalDrives(), Discs: DiscService.GetDiscs()));
                if (closed || operationBusy) return;
                var decision = LaunchPolicy.Evaluate(new Game { RequiresDisc = true, DiscId = game.DiscId }, settings, snapshot.Drives, snapshot.Discs);
                if (decision.Action != LaunchAction.Launch)
                {
                    if (decision.Action == LaunchAction.OpenEmptyTray) await Task.Run(() => DriveControl.SetTray(decision.DriveRoot, true));
                    MessageBox.Show(this, "Insert this game's installation disc in the selected drive, then choose Install from disc again. You can also import an extracted disc folder using Add game.", "Installation disc needed"); return;
                }
                root = decision.DriveRoot;
                expectedDiscId = snapshot.Discs.First(d => d.Root == root).Id;
            }
            var candidates = await Task.Run(() => InstallationService.InstallerCandidates(root));
            if (closed || operationBusy || !games.Contains(game)) return;
            var path = GameManagementDialogs.ChooseInstaller(this, game.Title, root, candidates); if (path == null) return;
            if (expectedDiscId.Length > 0)
            {
                var currentDiscs = await Task.Run(DiscService.GetDiscs);
                if (!currentDiscs.Any(d => d.Root == root && d.Id == expectedDiscId)) { MessageBox.Show(this, "The installation disc changed or was removed. Insert the correct disc and try again.", "Disc changed"); return; }
            }
            var plan = InstallationService.InstallPlan(root, path);
            SetOperationBusy(true, "Installing “" + game.Title + "”… Complete the game's setup wizard.");
            var result = await InstallationService.Run(plan);
            if (closed) return;
            SetOperationBusy(false, result.Cancelled ? "Installation cancelled. Your library entry is unchanged." : "Setup finished. Link the installed game's executable to play.");
            if (result.Cancelled) return;
            if (!result.Success) { MessageBox.Show(this, "The setup process finished with code " + (result.ExitCode?.ToString() ?? "unknown") + ". The game was not marked installed. If a separate wizard is still running, finish it and use Edit location to link the game.", "Check installation"); return; }
            if (result.NeedsRestart) MessageBox.Show(this, "Windows Installer reports that a restart is needed. Restart when convenient, then link the installed game.", "Restart needed");
            if (MessageBox.Show(this, "If the game's setup has finished successfully, link its installed executable now.\n\nSome setup launchers open a second wizard; finish that wizard first.", "Link installed game?", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes) await LinkInstalledGame(game);
        }
        catch (Win32Exception error) when (error.NativeErrorCode == 1223) { Status("Installation was cancelled by Windows."); }
        catch (Exception error) { MessageBox.Show(this, error.Message, "Could not install game"); }
        finally { if (!closed && operationBusy) SetOperationBusy(false, "Ready."); }
    }
    private async void LinkInstalledClick(object sender, RoutedEventArgs e)
    { if (selected is Game game && !game.IsDemo && !operationBusy) { try { await LinkInstalledGame(game); } catch (Exception error) { MessageBox.Show(this, error.Message, "Could not link installation"); } } }
    private async Task LinkInstalledGame(Game game)
    {
        var catalog = await Task.Run(InstallationService.ReadInstalledApps); if (closed || !games.Contains(game)) return;
        var matched = GameManagementDialogs.ChooseInstalled(this, game, catalog, false);
        if (matched == null) return;
        if (!PickLaunch(game, matched.Location)) return;
        game.InstalledAppId = matched.Id; Save(); Refresh(); Status("Installed copy linked to “" + game.Title + "”.");
    }
    private async void UninstallClick(object sender, RoutedEventArgs e)
    {
        if (selected is not Game game || game.IsDemo || operationBusy) return;
        try
        {
            var catalog = await Task.Run(InstallationService.ReadInstalledApps); if (closed || operationBusy) return;
            var app = GameManagementDialogs.ChooseInstalled(this, game, catalog, true); if (app == null) return;
            if (MessageBox.Show(this, "Uninstall “" + app.Name + "” from this PC?\n\nPublisher: " + app.Publisher + "\n" + (app.Location.Length > 0 ? "Folder: " + app.Location + "\n" : "") + "\nIts own uninstaller will open. Your DiscShelf entry stays saved unless you remove it after uninstalling.", "Uninstall installed game", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            var fresh = await Task.Run(InstallationService.ReadInstalledApps); var current = fresh.Apps.FirstOrDefault(a => a.Id == app.Id);
            if (current == null || current.UninstallCommand != app.UninstallCommand || current.ProductCode != app.ProductCode) { MessageBox.Show(this, "This installation changed. Select it again before uninstalling.", "Installation changed"); return; }
            var plan = InstallationService.UninstallPlan(current);
            SetOperationBusy(true, "Uninstalling “" + current.Name + "”… Complete its removal wizard.");
            var result = await InstallationService.Run(plan); if (closed) return;
            var after = await Task.Run(InstallationService.ReadInstalledApps); if (closed) return;
            SetOperationBusy(false, result.Cancelled ? "Uninstall cancelled. Your library entry is unchanged." : "Uninstaller finished.");
            if (result.Cancelled) return;
            if (plan.IsMsi && !result.Success) { MessageBox.Show(this, "Windows Installer finished with code " + result.ExitCode + ". The saved installation link was kept.", "Uninstall did not complete"); return; }
            if (!InstallationService.VerifiedRemoved(current, after)) { Status("Removal could not be confirmed. Your library and launch file were kept. Finish any remaining uninstall window, then check the game again."); return; }
            CompleteUninstall(game, current.Id);
            if (result.NeedsRestart) MessageBox.Show(this, "Windows Installer reports that a restart is needed to finish removal.", "Restart needed");
        }
        catch (Win32Exception error) when (error.NativeErrorCode == 1223) { Status("Uninstall was cancelled by Windows."); }
        catch (Exception error) { MessageBox.Show(this, error.Message + "\n\nYou can choose the game's own uninstaller file on its information screen.", "Could not uninstall game"); }
        finally { if (!closed && operationBusy) SetOperationBusy(false, "Ready."); }
    }
    private void CompleteUninstall(Game game, string appId)
    {
        if (appId.Length == 0 || game.InstalledAppId.Length == 0 || game.InstalledAppId == appId) { game.LaunchPath = ""; game.InstalledAppId = ""; Save(); Refresh(); }
        Status("Game uninstalled. Cover and details are still saved.");
        if (MessageBox.Show(this, "The game has been uninstalled. Remove “" + game.Title + "” from DiscShelf as well?\n\nChoose No to keep its cover and details.", "Remove library entry too?", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes) RemoveEntries([game]);
    }
    private async void ManualUninstallClick(object sender, RoutedEventArgs e)
    {
        if (selected is not Game game || game.IsDemo || operationBusy) return;
        var picker = new OpenFileDialog { Title = "Choose the game's uninstaller (for example unins000.exe)", Filter = "Uninstaller executable|*.exe", CheckFileExists = true };
        if (Directory.Exists(Path.GetDirectoryName(game.LaunchPath))) picker.InitialDirectory = Path.GetDirectoryName(game.LaunchPath);
        if (picker.ShowDialog(this) != true) return;
        if (MessageBox.Show(this, "Run this game's uninstaller?\n\n" + picker.FileName + "\n\nIts own wizard will open to remove the installed game.", "Run uninstaller", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        try
        {
            var plan = InstallationService.ManualUninstallPlan(picker.FileName); SetOperationBusy(true, "Removing “" + game.Title + "”… Complete its uninstall wizard.");
            var result = await InstallationService.Run(plan); if (closed) return;
            SetOperationBusy(false, "Uninstaller finished. Your library entry is still saved.");
            if (result.Cancelled) return;
            if (MessageBox.Show(this, "Did the game's uninstall wizard finish successfully?\n\nChoose Yes only after it confirms removal. Choosing No keeps your launch file and library entry.", "Confirm game removal", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes) CompleteUninstall(game, "");
        }
        catch (Win32Exception error) when (error.NativeErrorCode == 1223) { Status("Uninstall was cancelled by Windows."); }
        catch (Exception error) { MessageBox.Show(this, error.Message, "Could not run uninstaller"); }
        finally { if (!closed && operationBusy) SetOperationBusy(false, "Ready."); }
    }
}
