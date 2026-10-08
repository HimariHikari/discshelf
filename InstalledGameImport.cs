using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using Microsoft.Win32;

namespace DiscShelf;

public static class InstalledGameImport
{
    public static List<Game> Pick(Window owner, AppSettings settings)
    {
        var picker = new OpenFileDialog { Title = "Find installed PC games — select their playable .exe files", Filter = "Installed game executables|*.exe", Multiselect = true, CheckFileExists = true };
        if (Directory.Exists(settings.PreferredGameFolder)) picker.InitialDirectory = settings.PreferredGameFolder;
        if (picker.ShowDialog(owner) != true) return [];
        var result = new List<Game>(); var errors = new List<string>();
        foreach (var path in picker.FileNames)
        {
            try { result.Add(FromExecutable(path, settings.DefaultRequiresDisc)); }
            catch (IOException error) { errors.Add(Path.GetFileName(path) + ": " + error.Message); }
        }
        if (errors.Count > 0) MessageBox.Show(owner, string.Join("\n", errors), "Some files could not be imported");
        return result;
    }
    public static Game FromExecutable(string path, bool requiresDisc)
    {
        if (!Path.IsPathFullyQualified(path) || !File.Exists(path) || !Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase)) throw new IOException("Choose an installed game's executable (.exe).");
        var file = Path.GetFileNameWithoutExtension(path);
        if (Regex.IsMatch(file, @"^(setup|install|uninstall|unins\d*|dxsetup|vcredist.*)$", RegexOptions.IgnoreCase)) throw new IOException("Choose the game itself, rather than its installer or uninstaller.");
        string title = "";
        try { title = FileVersionInfo.GetVersionInfo(path).ProductName?.Trim() ?? ""; } catch (Exception e) when (e is IOException or ArgumentException or UnauthorizedAccessException) { }
        if (title.Length == 0 || Regex.IsMatch(title, @"^(Microsoft|Windows|Unreal Engine|Unity Player)", RegexOptions.IgnoreCase)) title = Regex.Replace(file, @"[_\-]+", " ");
        return new Game { Title = title, LaunchPath = Path.GetFullPath(path), RequiresDisc = requiresDisc, DiscRequirementConfirmed = false };
    }
    public static List<Game> AddUnique(List<Game> existing, IEnumerable<Game> candidates)
    {
        var added = new List<Game>();
        foreach (var game in candidates)
        {
            if (game.LaunchPath.Length == 0 || existing.Any(g => g.LaunchPath.Equals(game.LaunchPath, StringComparison.OrdinalIgnoreCase))) continue;
            existing.Add(game); added.Add(game);
        }
        return added;
    }
}
