using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace DiscShelf;

public static class DiscService
{
    public static List<OpticalDrive> GetOpticalDrives()
    {
        List<OpticalDrive> drives = [];
        foreach (var drive in DriveInfo.GetDrives())
        {
            try { if (drive.DriveType == DriveType.CDRom) drives.Add(new(drive.Name, drive.IsReady, drive.IsReady ? drive.VolumeLabel : "")); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
        return drives.OrderBy(d => d.Root).ToList();
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetVolumeInformation(string root, StringBuilder? name, int size, out uint serial, out uint maxLength, out uint flags, StringBuilder? fsName, int fsSize);
    public static List<DiscSnapshot> GetDiscs()
    {
        List<DiscSnapshot> result = [];
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (drive.DriveType != DriveType.CDRom || !drive.IsReady) continue;
                GetVolumeInformation(drive.Name, null, 0, out var serial, out _, out _, null, 0);
                var names = string.Join("|", Directory.EnumerateFileSystemEntries(drive.Name).Take(100).Select(Path.GetFileName).Order(StringComparer.OrdinalIgnoreCase));
                var id = Identity.Hash($"{serial}|{drive.VolumeLabel}|{drive.TotalSize}|{names}");
                result.Add(new(drive.Name, drive.VolumeLabel, id));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
        return result;
    }
    public static DiscSnapshot FolderSnapshot(string root) => new(root, Path.GetFileName(Path.TrimEndingDirectorySeparator(root)), Identity.Hash("folder:" + Path.GetFullPath(root).ToUpperInvariant()));
    public static DiscScan Scan(DiscSnapshot disc)
    {
        string title = Identity.CleanTitle(disc.Label), artwork = "";
        var autorun = Path.Combine(disc.Root, "autorun.inf");
        if (File.Exists(autorun))
        {
            try
            {
                if (new FileInfo(autorun).Length <= 64 * 1024)
                {
                    var match = Regex.Match(File.ReadAllText(autorun), @"^\s*label\s*=\s*(.+)$", RegexOptions.Multiline | RegexOptions.IgnoreCase);
                    if (match.Success) title = Identity.CleanTitle(match.Groups[1].Value.Trim().Trim('"'));
                }
            }
            catch (IOException) { }
        }
        var files = ReadFiles(disc.Root).ToList();
        foreach (var exe in files.Where(f => Path.GetExtension(f).Equals(".exe", StringComparison.OrdinalIgnoreCase)).Take(12))
        {
            try
            {
                var name = FileVersionInfo.GetVersionInfo(exe).ProductName;
                if (!string.IsNullOrWhiteSpace(name) && !Regex.IsMatch(name, "install|setup|shield|directx|microsoft|adobe|launcher|autorun", RegexOptions.IgnoreCase))
                { title = name.Trim(); break; }
            }
            catch (Exception e) when (e is IOException or ArgumentException or UnauthorizedAccessException) { }
        }
        artwork = files.Where(f => new[] { ".jpg", ".jpeg", ".png", ".bmp" }.Contains(Path.GetExtension(f).ToLowerInvariant()))
            .Where(f => Regex.IsMatch(Path.GetFileNameWithoutExtension(f), "cover|artwork|boxart|front|banner|background", RegexOptions.IgnoreCase))
            .OrderBy(f => Path.GetFileNameWithoutExtension(f).Equals("cover", StringComparison.OrdinalIgnoreCase) ? 0 : 1).FirstOrDefault() ?? "";
        var gameDisc = files.Any(f => new[] { ".exe", ".msi" }.Contains(Path.GetExtension(f).ToLowerInvariant()));
        // An explicit autorun label also supports old installer discs and imported sample folders.
        gameDisc |= File.Exists(autorun) && !Directory.Exists(Path.Combine(disc.Root, "VIDEO_TS"));
        return new(disc, title, artwork, gameDisc);
    }
    internal static IEnumerable<string> ReadFiles(string root)
    {
        var queue = new Queue<(string Path, int Depth)>(); queue.Enqueue((root, 0));
        var count = 0;
        while (queue.Count > 0 && count < 600)
        {
            var (path, depth) = queue.Dequeue();
            string[] files, dirs;
            try { files = Directory.EnumerateFiles(path).Take(600 - count).ToArray(); dirs = depth < 2 ? Directory.EnumerateDirectories(path).Take(30).ToArray() : []; }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { continue; }
            foreach (var file in files) { count++; yield return file; }
            foreach (var dir in dirs)
            {
                try { if ((File.GetAttributes(dir) & FileAttributes.ReparsePoint) == 0) queue.Enqueue((dir, depth + 1)); }
                catch (IOException) { }
            }
        }
    }
}
