namespace EraseLauncher.Services;

public static class VersionService
{
    public static bool MatchesMinecraftBuild(Version installed, string requested)
    {
        if (!Version.TryParse(requested, out var target) ||
            installed.Major != target.Major || installed.Minor != target.Minor)
        {
            return false;
        }

        // The known fixed APPX packages encode their fourth semantic component
        // into the Windows package build number: 1.16.100.4 becomes 1.16.10004.0.
        // Compare the stable requested patch prefix without treating it as a
        // different Minecraft release.
        return installed.Build.ToString().StartsWith(target.Build.ToString(), StringComparison.Ordinal);
    }
}
