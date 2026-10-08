using System.IO;
using System.Text.Json;

namespace DiscShelf;

public sealed class LibraryStore
{
    public string Root { get; }
    public string ArtworkDirectory => Path.Combine(Root, "artwork");
    public string LibraryPath => Path.Combine(Root, "library.json");
    public string RecoveryNotice { get; private set; } = "";
    private readonly JsonSerializerOptions options = new() { WriteIndented = true };
    public LibraryStore(string? root = null)
    {
        Root = root ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DiscShelf");
        Directory.CreateDirectory(ArtworkDirectory);
    }
    public List<Game> Load()
    {
        if (!File.Exists(LibraryPath)) return [];
        try { return Read(LibraryPath); }
        catch (Exception e) when (e is JsonException or IOException)
        {
            try
            {
                var games = Read(LibraryPath + ".bak");
                var recovery = LibraryPath + ".damaged-" + DateTime.Now.ToString("yyyyMMddHHmmssfff");
                File.Copy(LibraryPath, recovery);
                File.Copy(LibraryPath + ".bak", LibraryPath, true);
                RecoveryNotice = "Your library was recovered from its backup. The damaged file has been kept in the data folder.";
                return games;
            }
            catch (Exception recovery) when (recovery is JsonException or IOException)
            { throw new IOException("The library and its backup could not be read. Your saved files have been preserved at " + Root, e); }
        }
    }
    private List<Game> Read(string path) => JsonSerializer.Deserialize<List<Game>>(File.ReadAllText(path), options) ?? throw new JsonException("Empty library file.");
    public void Save(IEnumerable<Game> games)
    {
        var temporary = LibraryPath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(games.Where(g => !g.IsDemo), options));
        if (File.Exists(LibraryPath)) File.Replace(temporary, LibraryPath, LibraryPath + ".bak");
        else File.Move(temporary, LibraryPath);
    }
    public string CopyArtwork(string source, string gameId)
    {
        var info = new FileInfo(source);
        if (!info.Exists || info.Length > 20 * 1024 * 1024) throw new IOException("Choose an image smaller than 20 MB.");
        var ext = info.Extension.ToLowerInvariant();
        if (ext is not (".jpg" or ".jpeg" or ".png" or ".bmp")) throw new IOException("Choose a PNG, JPG, or BMP image.");
        var destination = Path.Combine(ArtworkDirectory, gameId + "-" + Guid.NewGuid().ToString("N")[..8] + ext);
        File.Copy(source, destination, true);
        return destination;
    }
}
