namespace EraseLauncher.Models;

public static class AppPaths
{
    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EraseLauncher");

    public static string Cache { get; } = Path.Combine(Root, "Cache");
    public static string Backups { get; } = Path.Combine(Root, "Backups");
    public static string Logs { get; } = Path.Combine(Root, "Logs");
    public static string Settings { get; } = Path.Combine(Root, "settings.json");
}
