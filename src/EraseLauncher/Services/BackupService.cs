using EraseLauncher.Models;

namespace EraseLauncher.Services;

public sealed class BackupService
{
    private const string MinecraftDataRelativePath = "Microsoft.MinecraftUWP_8wekyb3d8bbwe\\LocalState\\games\\com.mojang";

    public string? Backup()
    {
        var source = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Packages", MinecraftDataRelativePath);
        if (!Directory.Exists(source))
        {
            return null;
        }

        Directory.CreateDirectory(AppPaths.Backups);
        var destination = Path.Combine(AppPaths.Backups, $"minecraft-{DateTime.UtcNow:yyyyMMdd-HHmmss}");
        CopyDirectory(source, destination);
        return destination;
    }

    public void Restore(string backupPath)
    {
        var destination = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Packages", MinecraftDataRelativePath);
        if (!Directory.Exists(backupPath))
        {
            throw new DirectoryNotFoundException("The Minecraft data backup is not available.");
        }

        Directory.CreateDirectory(destination);
        CopyDirectory(backupPath, destination);
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source))
        {
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), true);
        }

        foreach (var directory in Directory.EnumerateDirectories(source))
        {
            CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
        }
    }
}
