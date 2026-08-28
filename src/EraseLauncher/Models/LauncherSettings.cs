namespace EraseLauncher.Models;

public sealed class LauncherSettings
{
    public bool LaunchAfterInstall { get; set; } = true;
    public bool OpenDiscordAfterFirstInstall { get; set; }
    public bool CheckForUpdates { get; set; } = true;
    public bool RememberSelectedVersion { get; set; } = true;
    public string PreferredMirror { get; set; } = "Automatic";
    public int RetryAttempts { get; set; } = 3;
    public string? SelectedVersionId { get; set; }
}
