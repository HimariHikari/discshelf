using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace DiscShelf;
public record OpticalDrive(string Root, bool Ready, string Label)
{
    public override string ToString() => Root + " · " + (Ready ? (Label.Length > 0 ? Label : "Disc inserted") : "Empty");
}
public record DriveChoice(string Root, string Name) { public override string ToString() => Name; }
public static class DriveSelection
{
    public static string? Resolve(AppSettings settings, IReadOnlyList<OpticalDrive> drives)
    {
        if (settings.PreferredDrive.Length > 0) return drives.FirstOrDefault(d => d.Root.Equals(settings.PreferredDrive, StringComparison.OrdinalIgnoreCase))?.Root;
        return drives.FirstOrDefault(d => d.Root.Equals(settings.FirstUsedDrive, StringComparison.OrdinalIgnoreCase))?.Root
            ?? drives.FirstOrDefault(d => d.Ready)?.Root ?? drives.FirstOrDefault()?.Root;
    }
    public static bool RememberFirst(AppSettings settings, IEnumerable<DiscSnapshot> discs)
    {
        if (settings.FirstUsedDrive.Length > 0 || settings.PreferredDrive.Length > 0) return false;
        var first = discs.FirstOrDefault(); if (first == null) return false;
        settings.FirstUsedDrive = first.Root; return true;
    }
}
public enum LaunchAction { Launch, NoDrive, OpenEmptyTray, DiscMissing, WrongDisc }
public record LaunchDecision(LaunchAction Action, string DriveRoot = "");
public static class LaunchPolicy
{
    public static LaunchDecision Evaluate(Game game, AppSettings settings, IReadOnlyList<OpticalDrive> drives, IReadOnlyList<DiscSnapshot> discs)
    {
        if (!game.RequiresDisc) return new(LaunchAction.Launch);
        var root = DriveSelection.Resolve(settings, drives);
        if (root == null) return new(LaunchAction.NoDrive);
        var disc = discs.FirstOrDefault(d => d.Root == root);
        if (disc == null) return new(drives.First(d => d.Root == root).Ready ? LaunchAction.DiscMissing : settings.OpenTrayWhenMissing ? LaunchAction.OpenEmptyTray : LaunchAction.DiscMissing, root);
        if (game.DiscId.Length > 0 && game.DiscId != disc.Id) return new(LaunchAction.WrongDisc, root);
        return new(LaunchAction.Launch, root);
    }
}
public static class DriveControl
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string file, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(SafeFileHandle device, uint control, IntPtr input, int inputSize, IntPtr output, int outputSize, out int returned, IntPtr overlapped);
    public static void SetTray(string root, bool open)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(root, @"^[A-Za-z]:\\$")) throw new IOException("Choose an optical drive.");
        var drive = new DriveInfo(root);
        if (drive.DriveType != DriveType.CDRom) throw new IOException("Only CD/DVD drives can be opened here.");
        using var handle = CreateFile(@"\\.\" + root[..2], 0x80000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
        if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows could not access this DVD drive.");
        if (!DeviceIoControl(handle, open ? 0x002D4808u : 0x002D480Cu, IntPtr.Zero, 0, IntPtr.Zero, 0, out _, IntPtr.Zero))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "This drive could not move its tray. Use its physical eject button if needed.");
    }
}
