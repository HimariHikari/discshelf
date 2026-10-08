using System.IO;
using System.Text.Json;

namespace DiscShelf;

internal static class ManagementTests
{
    public static void Run(string root)
    {
        static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
        var source = Path.Combine(root, "Game DVD"); Directory.CreateDirectory(source);
        var setup = Path.Combine(source, "setup.exe"); var msi = Path.Combine(source, "Game setup.msi"); var gameExe = Path.Combine(source, "game.exe");
        File.WriteAllText(setup, "fixture, never executed"); File.WriteAllText(msi, "fixture, never executed"); File.WriteAllText(gameExe, "fixture, never executed");
        var candidates = InstallationService.InstallerCandidates(source);
        Check(candidates.Contains(setup) && candidates.Contains(msi) && !candidates.Contains(gameExe), "Only setup candidates should be suggested, not game executables.");
        var plan = InstallationService.InstallPlan(source, setup);
        Check(plan.Executable == setup && plan.Arguments.Count == 0 && !plan.IsMsi, "EXE setup plan must keep the installer wizard visible.");
        plan = InstallationService.InstallPlan(source, msi);
        Check(plan.IsMsi && plan.Arguments.SequenceEqual(new[] { "/i", msi, "/norestart" }), "MSI install must target the selected package without silent installation or automatic restart.");
        var sibling = source + "-other"; Directory.CreateDirectory(sibling); var outside = Path.Combine(sibling, "setup.exe"); File.WriteAllText(outside, "fixture");
        var rejected = false; try { InstallationService.InstallPlan(source, outside); } catch (IOException) { rejected = true; }
        Check(rejected && !InstallationService.IsInside(source, outside), "Setup paths outside the chosen disc must be rejected, including sibling-prefix paths.");
        var text = Path.Combine(source, "setup.txt"); File.WriteAllText(text, "fixture");
        rejected = false; try { InstallationService.InstallPlan(source, text); } catch (IOException) { rejected = true; }
        Check(rejected, "Non-executable setup paths must be rejected.");
        var code = "{12345678-1234-1234-1234-123456789abc}";
        var installed = new InstalledApp("fixture-id", "Example Game", "Test Publisher", source, "MsiExec.exe /I" + code, false, "");
        plan = InstallationService.UninstallPlan(installed);
        Check(plan.IsMsi && plan.Arguments.SequenceEqual(new[] { "/x", code, "/norestart" }), "MSI maintenance commands must be converted to interactive uninstall.");
        plan = InstallationService.UninstallPlan(installed with { IsMsi = true, ProductCode = code, UninstallCommand = "" });
        Check(plan.Arguments[0] == "/x", "Registered MSI products need no cached uninstaller executable.");
        var uninstaller = Path.Combine(source, "uninstall game.exe"); File.WriteAllText(uninstaller, "fixture, never executed");
        plan = InstallationService.UninstallPlan(installed with { UninstallCommand = "\"" + uninstaller + "\" /remove \"Edition One\" /S" });
        Check(plan.Executable == uninstaller && plan.Arguments.SequenceEqual(new[] { "/remove", "Edition One" }), "Quoted paths/arguments must survive parsing and silent mode must be removed.");
        plan = InstallationService.UninstallPlan(installed with { UninstallCommand = uninstaller + " /remove" });
        Check(plan.Executable == uninstaller, "Unquoted registered paths containing spaces must be handled.");
        rejected = false; try { InstallationService.UninstallPlan(installed with { UninstallCommand = "https://example.com/remove" }); } catch (IOException) { rejected = true; }
        Check(rejected, "Uninstall URLs must not be executed as commands.");
        rejected = false; try { InstallationService.UninstallPlan(installed with { UninstallCommand = Path.Combine(Environment.SystemDirectory, "cmd.exe") + " /c del files" }); } catch (IOException) { rejected = true; }
        Check(rejected, "Command interpreter uninstall commands must use the explicit fallback.");
        Check(!InstallationService.VerifiedRemoved(installed, new([installed], true)), "A remaining registry entry must not be marked uninstalled.");
        Check(!InstallationService.VerifiedRemoved(installed, new([], false)), "Incomplete registry reads must not confirm uninstall.");
        Check(InstallationService.VerifiedRemoved(installed, new([], true)), "A removed entry in a complete registry scan must confirm uninstall.");
        Check(new OperationResult(1602).Cancelled && !new OperationResult(1602).Success && new OperationResult(3010).NeedsRestart && !new OperationResult(5).Success, "Cancelled/failed/restart-needed operations must remain distinct.");
        var settings = new AppSettings(); Check(!settings.SetupCompleted && settings.DefaultRequiresDisc, "New users must get first-launch setup and a conservative disc default.");
        var game = new Game { Title = "Example Game", DiscId = "removed-disc", LaunchPath = gameExe, RequiresDisc = false, DiscRequirementConfirmed = true, InstalledAppId = "fixture-id" };
        LibraryPolicy.ForgetDisc(settings, game); LibraryPolicy.ForgetDisc(settings, game);
        Check(settings.IgnoredDiscIds.Count == 1 && !LibraryPolicy.CanCatalogue(settings, new("D:\\", "GAME", game.DiscId)), "Removed discs must stay removed without duplicate exclusions.");
        settings.SetupCompleted = true; var settingsStore = new SettingsStore(Path.Combine(root, "management-settings")); settingsStore.Save(settings);
        Check(settingsStore.Load().SetupCompleted && settingsStore.Load().IgnoredDiscIds.Contains(game.DiscId), "Onboarding completion and removed-disc exclusions must persist.");
        LibraryPolicy.AllowDisc(settings, game.DiscId); Check(LibraryPolicy.CanCatalogue(settings, new("D:\\", "GAME", game.DiscId)), "Undo or manual add must restore disc detection.");
        var store = new LibraryStore(Path.Combine(root, "management-library")); store.Save([game]); var restored = store.Load()[0];
        Check(!restored.RequiresDisc && restored.DiscRequirementConfirmed && restored.InstalledAppId == game.InstalledAppId, "Installation and disc-free preferences must survive restart.");
        Check(LaunchPolicy.Evaluate(restored, settings, [], []).Action == LaunchAction.Launch, "A disc-free saved game must launch without a DVD drive.");
        File.WriteAllText(store.LibraryPath, "[{\"Title\":\"Old disc-free game\",\"RequiresDisc\":false},{\"Title\":\"Old disc game\",\"RequiresDisc\":true}]");
        var legacy = store.Load(); Check(legacy.All(g => g.DiscRequirementConfirmed) && !legacy[0].RequiresDisc && legacy[1].RequiresDisc, "Legacy libraries must preserve both disc preferences when upgrading.");
        var registry = InstallationService.ReadInstalledApps(); Check(registry.Apps.All(a => a.Id.Length > 0 && a.Name.Length > 0), "Read-only Windows installation discovery returned invalid entries.");
        var imported = InstalledGameImport.FromExecutable(gameExe, false);
        Check(imported.LaunchPath == gameExe && !imported.RequiresDisc && !imported.DiscRequirementConfirmed && imported.Title.Length > 0, "Installed-game imports must save executable locations and ask for the disc preference on first Play.");
        var collection = new List<Game>(); Check(InstalledGameImport.AddUnique(collection, [imported, InstalledGameImport.FromExecutable(gameExe, true)]).Count == 1 && collection.Count == 1, "Importing the same executable twice must not create duplicate entries.");
        rejected = false; try { InstalledGameImport.FromExecutable(setup, false); } catch (IOException) { rejected = true; } Check(rejected, "Installed-game import must reject installer executables.");
        store.Save(collection); Check(store.Load()[0].LaunchPath == gameExe, "Imported games must survive restart.");
        settings.PreferredGameFolder = source; settingsStore.Save(settings); Check(settingsStore.Load().PreferredGameFolder == source, "The default game search folder must persist.");
        foreach (var exit in new[] { 0, 1602, 5 })
        {
            var result = Task.Run(() => InstallationService.Run(new(Environment.ProcessPath!, ["--operation-fixture", exit.ToString()], root, false))).GetAwaiter().GetResult();
            Check(result.ExitCode == exit, "Visible-wizard process tracking did not preserve fixture exit code " + exit + ".");
        }
    }
}
