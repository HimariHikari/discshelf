using System.IO;

namespace DiscShelf;

public sealed class DiscInsertionPrompts
{
    private readonly Dictionary<string, string> present = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> Observe(IEnumerable<DiscSnapshot> discs)
    {
        var snapshot = discs.ToList();
        foreach (var root in present.Keys.Where(root => !snapshot.Any(d => d.Root.Equals(root, StringComparison.OrdinalIgnoreCase))).ToList()) present.Remove(root);
        var inserted = new HashSet<string>();
        foreach (var disc in snapshot)
        {
            if (!present.TryGetValue(disc.Root, out var previous) || previous != disc.Id) inserted.Add(disc.Id);
            present[disc.Root] = disc.Id;
        }
        return inserted;
    }
    public static bool ShouldOffer(AppSettings settings, Game game, DiscSnapshot disc) => settings.ScanAutomatically && settings.SuggestInstall
        && LibraryPolicy.CanCatalogue(settings, disc) && (game.LaunchPath.Length == 0 || !File.Exists(game.LaunchPath));
}
