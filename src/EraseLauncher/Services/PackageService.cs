using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using Windows.Foundation;
using Windows.Management.Deployment;

namespace EraseLauncher.Services;

/// <summary>
/// Windows package operations. Packages are deployed for the current user through
/// <see cref="PackageManager"/>, which needs no elevation and reports real progress.
/// Only trusting the signing certificate requires administrator approval.
/// </summary>
public sealed class PackageService(PowerShellRunner powerShell, LoggingService log)
{
    public const string MinecraftFamilyName = "Microsoft.MinecraftUWP_8wekyb3d8bbwe";
    public const string GamingServicesFamilyName = "Microsoft.GamingServices_8wekyb3d8bbwe";
    public const string GamingServicesStoreUri = "ms-windows-store://pdp/?productid=9MWPM2CQNLHN";

    private readonly PackageManager _packageManager = new();

    /// <summary>True when the family is installed at <paramref name="minVersion"/> or newer.</summary>
    public Task<bool> IsInstalledAsync(string packageFamilyName, string? minVersion, CancellationToken cancellationToken) =>
        Task.Run(() =>
        {
            var required = minVersion is null ? null : Version.Parse(minVersion);
            return _packageManager.FindPackagesForUser(string.Empty, packageFamilyName).Any(package =>
            {
                var version = package.Id.Version;
                return required is null || new Version(version.Major, version.Minor, version.Build, version.Revision) >= required;
            });
        }, cancellationToken);

    public Task<Windows.ApplicationModel.Package?> FindMinecraftAsync(CancellationToken cancellationToken) =>
        Task.Run(() => _packageManager.FindPackagesForUser(string.Empty, MinecraftFamilyName).FirstOrDefault(), cancellationToken);

    public static bool IsCertificateTrusted(string cerPath)
    {
        using var certificate = new X509Certificate2(cerPath);
        foreach (var storeName in new[] { StoreName.Root, StoreName.TrustedPeople })
        {
            using var store = new X509Store(storeName, StoreLocation.LocalMachine);
            store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
            if (store.Certificates.Find(X509FindType.FindByThumbprint, certificate.Thumbprint, false).Count > 0)
            {
                return true;
            }
        }

        return false;
    }

    public async Task InstallCertificateAsync(string cerPath, string? pfxPath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(pfxPath) && IsCertificateTrusted(cerPath))
        {
            await log.InfoAsync("The package certificate is already trusted.", cancellationToken);
            return;
        }

        var script = $"Import-Certificate -FilePath {PowerShellRunner.Quote(cerPath)} -CertStoreLocation 'Cert:\\LocalMachine\\Root' | Out-Null;";
        if (!string.IsNullOrWhiteSpace(pfxPath))
        {
            script += $" Import-PfxCertificate -FilePath {PowerShellRunner.Quote(pfxPath)} -CertStoreLocation 'Cert:\\LocalMachine\\My' -Exportable:$false | Out-Null;";
        }

        await powerShell.RunElevatedAsync(script, cancellationToken);
    }

    public async Task InstallPackageAsync(string path, bool isMinecraft, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        // Frameworks such as VCLibs are shared with other apps, so they are never force-closed.
        // Minecraft may be replaced by an older build in place (a GDK downgrade), so it
        // accepts any installed version.
        var options = isMinecraft
            ? DeploymentOptions.ForceTargetApplicationShutdown | DeploymentOptions.ForceUpdateFromAnyVersion
            : DeploymentOptions.None;
        await log.InfoAsync($"Deploying {Path.GetFileName(path)}.", cancellationToken);
        await RunDeploymentAsync(_packageManager.AddPackageAsync(new Uri(path), null, options), progress, cancellationToken);
    }

    public async Task RemoveMinecraftAsync(IProgress<double>? progress, CancellationToken cancellationToken)
    {
        var packages = await Task.Run(() => _packageManager.FindPackagesForUser(string.Empty, MinecraftFamilyName).ToList(), cancellationToken);
        for (var index = 0; index < packages.Count; index++)
        {
            var offset = index;
            var scaled = progress is null ? null : new SyncProgress<double>(value => progress.Report((offset + value) / packages.Count));
            await log.InfoAsync($"Removing {packages[index].Id.FullName}.", cancellationToken);
            await RunDeploymentAsync(_packageManager.RemovePackageAsync(packages[index].Id.FullName), scaled, cancellationToken);
        }
    }

    private static async Task RunDeploymentAsync(
        IAsyncOperationWithProgress<DeploymentResult, DeploymentProgress> operation,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        operation.Progress = (_, value) => progress?.Report(Math.Clamp(value.percentage / 100d, 0, 1));
        operation.Completed = (_, _) => completion.TrySetResult();

        await using (cancellationToken.Register(operation.Cancel))
        {
            await completion.Task.ConfigureAwait(false);
        }

        switch (operation.Status)
        {
            case AsyncStatus.Completed:
                progress?.Report(1);
                return;
            case AsyncStatus.Canceled:
                throw new OperationCanceledException(cancellationToken);
            default:
                string? errorText = null;
                try
                {
                    errorText = operation.GetResults()?.ErrorText;
                }
                catch (COMException)
                {
                }

                throw new PackageDeploymentException(operation.ErrorCode?.HResult ?? 0, errorText);
        }
    }
}

public sealed class PackageDeploymentException(int hresult, string? details)
    : Exception(DescribeError(hresult, details))
{
    public int ErrorCode { get; } = hresult;
    public string? Details { get; } = details;

    private static string DescribeError(int hresult, string? details)
    {
        var code = $"0x{hresult:X8}";
        return unchecked((uint)hresult) switch
        {
            0x800B0109 or 0x800B010A => $"Windows does not trust the package signature ({code}). Try the installation again and approve the certificate prompt.",
            0x80073CFF => $"Sideloading is disabled on this PC ({code}). Turn on \"Install apps from any source\" or Developer Mode in Windows Settings.",
            0x80073D02 => $"Minecraft is still running ({code}). Close it and try again.",
            0x80073CF3 => $"A required Windows component is missing or out of date ({code}).",
            0x80073CF9 or 0x80073D01 => $"Windows blocked the package installation ({code}). Check the launcher log for details.",
            0x80070070 => $"There is not enough free disk space ({code}).",
            _ => FirstLine(details) is { Length: > 0 } line ? $"{line} ({code})" : $"Windows could not install the package ({code})."
        };
    }

    private static string? FirstLine(string? text) =>
        text?.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
}
