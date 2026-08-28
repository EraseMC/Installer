using EraseLauncher.Models;

namespace EraseLauncher.Services;

public sealed class InstallationService(
    DownloadService downloadService,
    PackageService packageService,
    MinecraftService minecraftService,
    BackupService backupService,
    LoggingService log)
{
    public async Task InstallAsync(InstallationRequest request, IProgress<InstallationProgress> progress, CancellationToken cancellationToken)
    {
        string? backupPath = null;
        var lastState = InstallationState.Idle;
        try
        {
            Report(InstallationState.Preparing, "Preparing installation.");
            cancellationToken.ThrowIfCancellationRequested();
            EnsureAvailableDiskSpace(request.Version);

            Report(InstallationState.CheckingPrerequisites, "Checking Minecraft and Windows package requirements.");
            if (minecraftService.IsRunning())
            {
                throw new InvalidOperationException("Close Minecraft before changing its version.");
            }

            var downloadedDependencies = new List<(DependencyArtifact Artifact, string Path)>();
            foreach (var dependency in request.Version.Dependencies)
            {
                if (!string.IsNullOrWhiteSpace(dependency.PackageFamilyName) && await packageService.IsInstalledAsync(dependency.PackageFamilyName, cancellationToken))
                {
                    continue;
                }

                Report(InstallationState.DownloadingDependencies, $"Downloading {dependency.FileName}.");
                var path = await downloadService.DownloadVerifiedAsync(dependency, CreateDownloadProgress(InstallationState.DownloadingDependencies, $"Downloading {dependency.FileName}.", progress), cancellationToken);
                downloadedDependencies.Add((dependency, path));
            }

            Report(InstallationState.DownloadingPackage, $"Downloading Minecraft {request.Version.Id}.");
            var packagePath = await downloadService.DownloadVerifiedAsync(request.Version.Package, CreateDownloadProgress(InstallationState.DownloadingPackage, "Downloading Minecraft.", progress), cancellationToken);

            string? certificatePath = null;
            string? pfxPath = null;
            if (request.Version.Certificate is not null)
            {
                Report(InstallationState.Verifying, "Preparing the signing certificate.");
                certificatePath = await downloadService.DownloadVerifiedAsync(request.Version.Certificate, CreateDownloadProgress(InstallationState.Verifying, "Downloading certificate.", progress), cancellationToken);
                if (request.Version.Certificate.Pfx is not null)
                {
                    pfxPath = await downloadService.DownloadVerifiedAsync(request.Version.Certificate.Pfx, CreateDownloadProgress(InstallationState.Verifying, "Downloading certificate material.", progress), cancellationToken);
                }
            }

            if (request.PreserveData)
            {
                Report(InstallationState.BackingUp, "Backing up Minecraft worlds and packs.");
                backupPath = await Task.Run(backupService.Backup, cancellationToken);
                await log.InfoAsync(backupPath is null ? "No Minecraft data was present to back up." : $"Minecraft data backup created at {backupPath}.", cancellationToken);
            }

            if (certificatePath is not null)
            {
                Report(InstallationState.InstallingCertificate, "Installing the package certificate. Windows may ask for approval.");
                await packageService.InstallCertificateAsync(certificatePath, pfxPath, cancellationToken);
            }

            foreach (var (_, dependencyPath) in downloadedDependencies)
            {
                Report(InstallationState.InstallingDependencies, "Installing required Windows components.");
                await packageService.InstallPackageAsync(dependencyPath, cancellationToken);
            }

            var existing = await minecraftService.GetInstallationAsync(cancellationToken);
            if (existing is not null)
            {
                Report(InstallationState.RemovingExistingVersion, "Replacing the currently installed Minecraft package.");
                await packageService.RemoveMinecraftAsync(cancellationToken);
            }

            Report(InstallationState.InstallingMinecraft, $"Installing Minecraft {request.Version.Id}.");
            await packageService.InstallPackageAsync(packagePath, cancellationToken);

            Report(InstallationState.VerifyingInstallation, "Verifying the installed Minecraft package.");
            var installed = await minecraftService.GetInstallationAsync(cancellationToken);
            if (installed is null || !VersionService.MatchesMinecraftBuild(installed.Version, request.Version.Id))
            {
                throw new InvalidOperationException("Minecraft was installed, but Windows did not report the expected version.");
            }

            if (backupPath is not null)
            {
                Report(InstallationState.RestoringData, "Restoring Minecraft worlds and packs.");
                await Task.Run(() => backupService.Restore(backupPath), cancellationToken);
            }

            Report(InstallationState.Finalizing, "Finalizing installation.");
            Report(InstallationState.Completed, $"Minecraft {request.Version.Id} installed successfully.", 100);
        }
        catch (OperationCanceledException)
        {
            Report(InstallationState.Cancelled, "Installation cancelled.");
            throw;
        }
        catch (Exception exception)
        {
            await log.InfoAsync($"Installation failed: {exception}", CancellationToken.None);
            Report(InstallationState.Failed, exception.Message);
            throw;
        }

        void Report(InstallationState state, string message, double? percentage = null)
        {
            if (!InstallationStateMachine.CanTransition(lastState, state))
            {
                throw new InvalidOperationException($"Invalid installation-state transition: {lastState} to {state}.");
            }

            lastState = state;
            progress.Report(new InstallationProgress(state, message, percentage));
        }
    }

    private static void EnsureAvailableDiskSpace(MinecraftVersion version)
    {
        var required = version.Package.Size + version.Dependencies.Sum(dependency => dependency.Size) +
            (version.Certificate?.Size ?? 0) + (version.Certificate?.Pfx?.Size ?? 0);
        var root = Path.GetPathRoot(AppPaths.Cache) ?? throw new InvalidOperationException("Could not locate the download drive.");
        var drive = new DriveInfo(root);
        if (drive.AvailableFreeSpace < required * 2)
        {
            throw new InvalidOperationException("Not enough free disk space to safely download and install Minecraft.");
        }
    }

    private static IProgress<DownloadProgress> CreateDownloadProgress(
        InstallationState state,
        string message,
        IProgress<InstallationProgress> installationProgress) =>
        new Progress<DownloadProgress>(download =>
            installationProgress.Report(new InstallationProgress(state, message, download.Percentage, download)));
}
