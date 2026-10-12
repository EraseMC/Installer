namespace EraseLauncher.Models;

public sealed class LauncherSettings
{
    public bool LaunchAfterInstall { get; set; } = true;
    public bool OpenDiscordAfterFirstInstall { get; set; }
    public bool PreserveDataByDefault { get; set; } = true;
    public bool HasCompletedInstall { get; set; }
    public int RetryAttempts { get; set; } = 3;
    public string? SelectedVersionId { get; set; }
}
