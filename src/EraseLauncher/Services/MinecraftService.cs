using System.Diagnostics;
using EraseLauncher.Models;
using Windows.ApplicationModel;

namespace EraseLauncher.Services;

public sealed class MinecraftService(PackageService packageService)
{
    public async Task<MinecraftInstallation?> GetInstallationAsync(CancellationToken cancellationToken)
    {
        var package = await packageService.FindMinecraftAsync(cancellationToken);
        if (package is null)
        {
            return null;
        }

        var id = package.Id;
        var version = new Version(id.Version.Major, id.Version.Minor, id.Version.Build, id.Version.Revision);
        return new MinecraftInstallation(id.FullName, id.FamilyName, version, IsRunning(), IsGdk(package),
            package.SignatureKind != PackageSignatureKind.Store);
    }

    public bool IsRunning()
    {
        var processes = Process.GetProcessesByName("Minecraft.Windows");
        foreach (var process in processes)
        {
            process.Dispose();
        }

        return processes.Length > 0;
    }

    public async Task LaunchAsync()
    {
        // GDK builds register their entry point as "Game" and UWP builds as "App", so the
        // package's own app list entry is launched instead of a hardcoded AppsFolder id.
        var package = await packageService.FindMinecraftAsync(CancellationToken.None);
        if (package is not null)
        {
            var entries = await package.GetAppListEntriesAsync();
            if (entries.Count > 0 && await entries[0].LaunchAsync())
            {
                return;
            }
        }

        Process.Start(new ProcessStartInfo("explorer.exe", $"shell:AppsFolder\\{PackageService.MinecraftFamilyName}!App")
        {
            UseShellExecute = true
        });
    }

    /// <summary>GDK packages ship a MicrosoftGame.config; UWP packages never do.</summary>
    private static bool IsGdk(Package package)
    {
        try
        {
            return File.Exists(Path.Combine(package.InstalledLocation.Path, "MicrosoftGame.config"));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return false;
        }
    }
}
