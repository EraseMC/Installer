namespace EraseLauncher.Models;

/// <param name="Percentage">Overall installation progress, 0 to 100. Never decreases during a run.</param>
/// <param name="CanCancel">False once the existing Minecraft package is being replaced.</param>
public sealed record InstallationProgress(
    InstallationState State,
    string Message,
    double? Percentage = null,
    DownloadProgress? Download = null,
    bool CanCancel = true);
