using EraseLauncher.Models;

namespace EraseLauncher.Services;

public sealed class BackupService
{
    private static readonly string MinecraftDataPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Packages", PackageService.MinecraftFamilyName, "LocalState", "games", "com.mojang");

    public string? Backup(IProgress<double>? progress = null)
    {
        if (!Directory.Exists(MinecraftDataPath))
        {
            return null;
        }

        Directory.CreateDirectory(AppPaths.Backups);
        var destination = Path.Combine(AppPaths.Backups, $"minecraft-{DateTime.UtcNow:yyyyMMdd-HHmmss}");
        CopyDirectory(MinecraftDataPath, destination, progress);
        return destination;
    }

    public void Restore(string backupPath, IProgress<double>? progress = null)
    {
        if (!Directory.Exists(backupPath))
        {
            throw new DirectoryNotFoundException("The Minecraft data backup is not available.");
        }

        CopyDirectory(backupPath, MinecraftDataPath, progress);
    }

    private static void CopyDirectory(string source, string destination, IProgress<double>? progress)
    {
        var files = new DirectoryInfo(source).EnumerateFiles("*", SearchOption.AllDirectories).ToList();
        var totalBytes = Math.Max(1, files.Sum(file => file.Length));
        long copiedBytes = 0;

        Directory.CreateDirectory(destination);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        }

        foreach (var file in files)
        {
            file.CopyTo(Path.Combine(destination, Path.GetRelativePath(source, file.FullName)), true);
            copiedBytes += file.Length;
            progress?.Report((double)copiedBytes / totalBytes);
        }

        progress?.Report(1);
    }
}
