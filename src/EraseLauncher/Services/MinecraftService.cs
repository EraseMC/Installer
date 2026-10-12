using System.Diagnostics;
using EraseLauncher.Models;

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
        return new MinecraftInstallation(id.FullName, id.FamilyName, version, IsRunning());
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

    public void Launch()
    {
        Process.Start(new ProcessStartInfo("explorer.exe", $"shell:AppsFolder\\{PackageService.MinecraftFamilyName}!App")
        {
            UseShellExecute = true
        });
    }
}
