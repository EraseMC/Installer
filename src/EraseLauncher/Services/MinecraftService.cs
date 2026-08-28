using System.Diagnostics;
using System.Text.Json;
using EraseLauncher.Models;

namespace EraseLauncher.Services;

public sealed class MinecraftService(PowerShellRunner powerShell)
{
    public async Task<MinecraftInstallation?> GetInstallationAsync(CancellationToken cancellationToken)
    {
        const string command = "Get-AppxPackage -Name Microsoft.MinecraftUWP | Select-Object PackageFullName,PackageFamilyName,Version | ConvertTo-Json -Compress";
        var output = await powerShell.RunAsync(command, cancellationToken);
        if (string.IsNullOrWhiteSpace(output))
        {
            return null;
        }

        using var document = JsonDocument.Parse(output);
        var package = document.RootElement.ValueKind == JsonValueKind.Array ? document.RootElement[0] : document.RootElement;
        var fullName = package.GetProperty("PackageFullName").GetString();
        var familyName = package.GetProperty("PackageFamilyName").GetString();
        var version = package.GetProperty("Version").GetString();
        if (fullName is null || familyName is null || version is null || !Version.TryParse(version, out var parsedVersion))
        {
            throw new InvalidOperationException("Windows returned invalid Minecraft package metadata.");
        }

        return new MinecraftInstallation(fullName, familyName, parsedVersion, IsRunning());
    }

    public bool IsRunning() => Process.GetProcessesByName("Minecraft.Windows").Length > 0;

    public void Launch()
    {
        Process.Start(new ProcessStartInfo("explorer.exe", "shell:AppsFolder\\Microsoft.MinecraftUWP_8wekyb3d8bbwe!App")
        {
            UseShellExecute = true
        });
    }
}
