namespace EraseLauncher.Services;

public sealed class PackageService(PowerShellRunner powerShell)
{
    public async Task<bool> IsInstalledAsync(string packageFamilyName, CancellationToken cancellationToken)
    {
        var command = $"if (Get-AppxPackage -PackageFamilyName {PowerShellRunner.Quote(packageFamilyName)}) {{ 'true' }}";
        return string.Equals(await powerShell.RunAsync(command, cancellationToken), "true", StringComparison.OrdinalIgnoreCase);
    }

    public Task InstallCertificateAsync(string cerPath, string? pfxPath, CancellationToken cancellationToken)
    {
        var script = $"Import-Certificate -FilePath {PowerShellRunner.Quote(cerPath)} -CertStoreLocation 'Cert:\\LocalMachine\\Root' | Out-Null;";
        if (!string.IsNullOrWhiteSpace(pfxPath))
        {
            script += $" Import-PfxCertificate -FilePath {PowerShellRunner.Quote(pfxPath)} -CertStoreLocation 'Cert:\\LocalMachine\\My' -Exportable:$false | Out-Null;";
        }

        return powerShell.RunElevatedAsync(script, cancellationToken);
    }

    public Task InstallPackageAsync(string path, CancellationToken cancellationToken) =>
        powerShell.RunElevatedAsync($"Add-AppxPackage -Path {PowerShellRunner.Quote(path)}", cancellationToken);

    public Task RemoveMinecraftAsync(CancellationToken cancellationToken) =>
        powerShell.RunElevatedAsync("Get-AppxPackage -Name Microsoft.MinecraftUWP | Remove-AppxPackage", cancellationToken);
}
