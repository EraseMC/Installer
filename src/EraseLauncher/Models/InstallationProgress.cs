namespace EraseLauncher.Models;

public sealed record InstallationProgress(
    InstallationState State,
    string Message,
    double? Percentage = null,
    DownloadProgress? Download = null);
