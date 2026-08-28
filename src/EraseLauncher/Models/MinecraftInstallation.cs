namespace EraseLauncher.Models;

public sealed record MinecraftInstallation(
    string FullName,
    string FamilyName,
    Version Version,
    bool IsRunning);
