namespace EraseLauncher.Models;

public sealed record InstallationRequest(MinecraftVersion Version, bool PreserveData, bool LaunchAfterInstall);
