using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace DiscShelf;

public record InstalledApp(string Id, string Name, string Publisher, string Location, string UninstallCommand, bool IsMsi, string ProductCode)
{
    public override string ToString() => Name + (Publisher.Length > 0 ? " · " + Publisher : "");
}
public record InstalledCatalog(List<InstalledApp> Apps, bool Complete);
public record OperationPlan(string Executable, IReadOnlyList<string> Arguments, string WorkingDirectory, bool IsMsi);
public record OperationResult(int? ExitCode)
{
    public bool Cancelled => ExitCode == 1602;
    public bool Success => ExitCode is 0 or 3010 or 1641;
    public bool NeedsRestart => ExitCode is 3010 or 1641;
}

public static class InstallationService
{
    private const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
    public static InstalledCatalog ReadInstalledApps()
    {
        var apps = new List<InstalledApp>(); var complete = true;
        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var root = RegistryKey.OpenBaseKey(hive, view);
                using var uninstall = root.OpenSubKey(UninstallKey);
                if (uninstall == null) continue;
                foreach (var key in uninstall.GetSubKeyNames())
                {
                    try
                    {
                        using var entry = uninstall.OpenSubKey(key);
                        if (entry == null) continue;
                        string Text(string name) => entry.GetValue(name) as string ?? "";
                        var name = Text("DisplayName");
                        if (name.Length == 0 || Equals(entry.GetValue("SystemComponent"), 1)) continue;
                        apps.Add(new($"{hive}:{view}:{key}", name, Text("Publisher"), Text("InstallLocation"), Text("UninstallString"),
                            Equals(entry.GetValue("WindowsInstaller"), 1), Guid.TryParse(key, out var product) ? product.ToString("B") : ""));
                    }
                    catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException) { complete = false; }
                }
            }
            catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException) { complete = false; }
        }
        return new(apps.DistinctBy(a => a.Id).OrderBy(a => a.Name).ToList(), complete);
    }
    public static List<string> InstallerCandidates(string root) => DiscService.ReadFiles(root)
        .Where(path => Path.GetExtension(path).Equals(".msi", StringComparison.OrdinalIgnoreCase)
            || (Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase)
                && Regex.IsMatch(Path.GetFileNameWithoutExtension(path), @"^(setup|install|autorun)([\s_\-0-9].*)?$", RegexOptions.IgnoreCase)))
        .OrderBy(path => Path.GetFileNameWithoutExtension(path).Equals("setup", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
        .ThenBy(path => path.Length).Take(25).ToList();

    public static OperationPlan InstallPlan(string sourceRoot, string installer)
    {
        var root = Path.GetFullPath(sourceRoot);
        var path = Path.GetFullPath(installer);
        if (!IsInside(root, path) || !File.Exists(path)) throw new IOException("Choose a setup file from this game's disc or imported disc folder.");
        // Imported folders may contain links: don't let them point at executables outside the source.
        for (var parent = Path.GetDirectoryName(path); parent != null && IsInside(root, parent); parent = Path.GetDirectoryName(parent))
            if ((File.GetAttributes(parent) & FileAttributes.ReparsePoint) != 0) throw new IOException("Choose a setup file from a regular folder.");
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("Choose the original setup file, rather than a link.");
        if (Path.GetExtension(path).Equals(".msi", StringComparison.OrdinalIgnoreCase))
            return new(Path.Combine(Environment.SystemDirectory, "msiexec.exe"), ["/i", path, "/norestart"], Path.GetDirectoryName(path)!, true);
        if (!Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase)) throw new IOException("Choose a Windows setup executable (.exe) or installer package (.msi).");
        return new(path, [], Path.GetDirectoryName(path)!, false);
    }
    internal static bool IsInside(string root, string path)
    {
        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var fullPath = Path.GetFullPath(path);
        return fullPath.Equals(fullRoot, StringComparison.OrdinalIgnoreCase) || fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
    public static OperationPlan UninstallPlan(InstalledApp app)
    {
        if (app.IsMsi && Guid.TryParse(app.ProductCode, out var code)) return MsiRemoval(code);
        var command = Environment.ExpandEnvironmentVariables(app.UninstallCommand.Trim());
        var match = Regex.Match(command, "^(?:\"([^\"]+\\.exe)\"|(.+?\\.exe))(?=\\s|$)(.*)$", RegexOptions.IgnoreCase);
        if (!match.Success) throw new IOException("This game has no supported Windows uninstaller. Choose its uninstaller file instead.");
        var executable = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
        var arguments = SplitArguments(match.Groups[3].Value.Trim());
        if (Path.GetFileName(executable).Equals("msiexec.exe", StringComparison.OrdinalIgnoreCase))
        {
            var product = Regex.Match(string.Join(" ", arguments), @"(?:/i|/x|/uninstall)\s*(\{[0-9a-f\-]{36}\})", RegexOptions.IgnoreCase);
            if (!product.Success || !Guid.TryParse(product.Groups[1].Value, out var guid)) throw new IOException("The Windows Installer product code could not be read.");
            return MsiRemoval(guid);
        }
        if (!Path.IsPathFullyQualified(executable) || !File.Exists(executable)) throw new IOException("The registered uninstaller is missing. Choose the game's uninstaller file instead.");
        // No command interpreter or silent uninstall: keep the vendor's interactive wizard visible.
        if (new[] { "cmd.exe", "powershell.exe", "pwsh.exe", "wscript.exe", "cscript.exe", "rundll32.exe", "regsvr32.exe" }.Contains(Path.GetFileName(executable).ToLowerInvariant()))
            throw new IOException("This uninstall command needs Windows Installed apps or the game's own uninstaller file.");
        var interactive = arguments.Where(a => !new[] { "/s", "/silent", "/verysilent", "/quiet", "/qn", "/passive" }.Contains(a.ToLowerInvariant())).ToArray();
        return new(executable, interactive, Path.GetDirectoryName(executable)!, false);
    }
    private static OperationPlan MsiRemoval(Guid product) => new(Path.Combine(Environment.SystemDirectory, "msiexec.exe"), ["/x", product.ToString("B"), "/norestart"], Environment.SystemDirectory, true);
    public static OperationPlan ManualUninstallPlan(string path)
    {
        if (!Path.IsPathFullyQualified(path) || !File.Exists(path) || !Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase)) throw new IOException("Choose the game's uninstaller executable (.exe).");
        return new(path, [], Path.GetDirectoryName(path)!, false);
    }
    public static bool VerifiedRemoved(InstalledApp app, InstalledCatalog after) => after.Complete && !after.Apps.Any(a => a.Id == app.Id);
    public static bool Matches(InstalledApp app, string query)
    {
        var title = MetadataPolicy.MatchKey(app.Name); var key = MetadataPolicy.MatchKey(query);
        return key.Length == 0 || title.Contains(key, StringComparison.OrdinalIgnoreCase);
    }
    public static async Task<OperationResult> Run(OperationPlan plan)
    {
        var start = new ProcessStartInfo(plan.Executable) { UseShellExecute = true, WorkingDirectory = plan.WorkingDirectory };
        foreach (var argument in plan.Arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start);
        if (process == null) return new(null);
        await process.WaitForExitAsync().ConfigureAwait(false);
        return new(process.ExitCode);
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CommandLineToArgvW(string command, out int count);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);
    internal static string[] SplitArguments(string arguments)
    {
        var memory = CommandLineToArgvW("placeholder.exe " + arguments, out var count);
        if (memory == IntPtr.Zero) throw new IOException("The uninstall command could not be read.");
        try { return Enumerable.Range(1, count - 1).Select(i => Marshal.PtrToStringUni(Marshal.ReadIntPtr(memory, i * IntPtr.Size))!).ToArray(); }
        finally { LocalFree(memory); }
    }
}

public static class LibraryPolicy
{
    public static bool CanCatalogue(AppSettings settings, DiscSnapshot disc) => !settings.IgnoredDiscIds.Contains(disc.Id);
    public static void ForgetDisc(AppSettings settings, Game game)
    { if (game.DiscId.Length > 0 && !settings.IgnoredDiscIds.Contains(game.DiscId)) settings.IgnoredDiscIds.Add(game.DiscId); }
    public static void AllowDisc(AppSettings settings, string discId) => settings.IgnoredDiscIds.RemoveAll(id => id == discId);
}
