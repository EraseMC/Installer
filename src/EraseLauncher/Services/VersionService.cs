namespace EraseLauncher.Services;

public static class VersionService
{
    public static bool MatchesMinecraftBuild(Version installed, string requested) =>
        Version.TryParse(requested, out var target) &&
        installed.Major == target.Major &&
        installed.Minor == target.Minor &&
        installed.Build == target.Build;
}
