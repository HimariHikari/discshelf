using System.IO;

namespace DiscShelf;

public static class SelfTest
{
    public static int Run()
    {
        var root = Path.Combine(Path.GetTempPath(), "DiscShelf-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            FeatureTests.Run(root);
            static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
            var store = new LibraryStore(Path.Combine(root, "data"));
            Assert(store.Load().Count == 0, "New library must be empty.");
            var discPath = Path.Combine(root, "MY_GAME_DVD1"); Directory.CreateDirectory(discPath);
            File.WriteAllText(Path.Combine(discPath, "autorun.inf"), "[autorun]\nlabel=The Example Game\nopen=should-never-run.exe");
            File.WriteAllBytes(Path.Combine(discPath, "cover.png"), Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jRZkAAAAASUVORK5CYII="));
            var disc = DiscService.FolderSnapshot(discPath); var scan = DiscService.Scan(disc);
            Assert(scan.Title == "The Example Game", "Autorun label was not recognized.");
            Assert(scan.IsGameDisc, "PC disc evidence was not recognized.");
            var movie = Path.Combine(root, "Movie"); Directory.CreateDirectory(movie); Directory.CreateDirectory(Path.Combine(movie, "VIDEO_TS"));
            Assert(!DiscService.Scan(DiscService.FolderSnapshot(movie)).IsGameDisc, "A movie disc must not be auto-added as a PC game.");
            Assert(scan.Artwork.EndsWith("cover.png"), "Disc artwork was not detected.");
            Assert(DiscService.FolderSnapshot(discPath).Id == disc.Id, "Disc identity must be stable.");
            Assert(Identity.CleanTitle("MY_GAME_DVD1") == "MY GAME", "Disc name normalization failed.");
            var game = new Game { Title = scan.Title, DiscId = disc.Id, ArtworkPath = store.CopyArtwork(scan.Artwork, "test"), Favorite = true, Developer = "Example Studio", ReleaseYear = "2006" };
            store.Save([game, new Game { IsDemo = true, Title = "Preview" }]);
            var restored = new LibraryStore(store.Root).Load();
            Assert(restored.Count == 1, "Preview games must not be persisted.");
            Assert(restored[0].Favorite && restored[0].Developer == "Example Studio" && restored[0].ReleaseYear == "2006", "Metadata did not survive restart.");
            Assert(restored[0].LastPlayedAt == null, "Inserting a disc must not mark it as played.");
            File.Delete(scan.Artwork); Assert(File.Exists(restored[0].ArtworkPath), "Artwork must survive source removal.");
            game.LastPlayedAt = new DateTime(2026, 10, 8, 12, 30, 0); store.Save([game]);
            Assert(store.Load()[0].LastPlayedAt == game.LastPlayedAt, "Play history did not survive restart.");
            Assert(File.Exists(store.LibraryPath + ".bak"), "Atomic save must create a backup.");
            File.WriteAllText(store.LibraryPath, "corrupted json");
            var recovered = store.Load(); Assert(recovered.Count == 1 && store.RecoveryNotice.Length > 0, "Library recovery failed.");
            File.WriteAllText(store.LibraryPath, "invalid"); File.WriteAllText(store.LibraryPath + ".bak", "also invalid");
            bool failed = false; try { store.Load(); } catch (IOException) { failed = true; }
            Assert(failed && File.ReadAllText(store.LibraryPath) == "invalid", "Unrecoverable data must be preserved.");
            File.WriteAllText(Path.Combine(root, "test.exe"), "not an image");
            failed = false; try { store.CopyArtwork(Path.Combine(root, "test.exe"), "test"); } catch (IOException) { failed = true; }
            Assert(failed, "Non-image artwork must be rejected.");
            File.WriteAllText(Path.Combine(Environment.CurrentDirectory, "self-test-results.txt"), "PASS: drive selection/first-use memory, correct/missing/wrong disc launch policy, disconnected drive handling, disc-free games, credential encryption, settings persistence, 8 built-in themes, theme import validation, metadata provider fallback and failure handling, source attribution, ambiguous-match protection, RAWG/IGDB parsing, disc recognition, cached artwork, library persistence and recovery.\n");
            return 0;
        }
        catch (Exception error) { File.WriteAllText(Path.Combine(Environment.CurrentDirectory, "self-test-results.txt"), "FAIL: " + error + "\n"); return 1; }
        finally
        {
            var resolved = Path.GetFullPath(root);
            if (resolved.StartsWith(Path.Combine(Path.GetTempPath(), "DiscShelf-tests-"), StringComparison.OrdinalIgnoreCase)) Directory.Delete(resolved, true);
        }
    }
}
