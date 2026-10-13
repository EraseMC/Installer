using System.Diagnostics;
using EraseLauncher.Models;

namespace EraseLauncher.Services;

public sealed class InstallationService(
    DownloadService downloadService,
    PackageService packageService,
    MinecraftService minecraftService,
    BackupService backupService,
    LoggingService log)
{
    private static readonly TimeSpan ReportInterval = TimeSpan.FromMilliseconds(60);

    public async Task InstallAsync(InstallationRequest request, IProgress<InstallationProgress> progress, CancellationToken cancellationToken)
    {
        var version = request.Version;
        var reporter = new Reporter(progress);
        string? backupPath = null;
        try
        {
            reporter.Report(InstallationState.Preparing, "Preparing installation");
            cancellationToken.ThrowIfCancellationRequested();
            EnsureAvailableDiskSpace(version);

            reporter.Report(InstallationState.CheckingPrerequisites, "Checking Windows components");
            if (minecraftService.IsRunning())
            {
                throw new InvalidOperationException("Close Minecraft before changing its version.");
            }

            if (version.Format == PackageFormat.Msixvc &&
                !await packageService.IsInstalledAsync(PackageService.GamingServicesFamilyName, null, cancellationToken))
            {
                throw new GamingServicesMissingException();
            }

            var missingDependencies = new List<DependencyArtifact>();
            foreach (var dependency in version.Dependencies)
            {
                if (string.IsNullOrWhiteSpace(dependency.PackageFamilyName) ||
                    !await packageService.IsInstalledAsync(dependency.PackageFamilyName, dependency.MinVersion, cancellationToken))
                {
                    missingDependencies.Add(dependency);
                }
            }

            var existing = await minecraftService.GetInstallationAsync(cancellationToken);

            // A GDK build replaces another GDK build in place. Every other switch (UWP to GDK,
            // GDK to UWP, UWP to UWP) removes the installed package first.
            var removeExisting = existing is not null && !(version.Format == PackageFormat.Msixvc && existing.IsGdk);

            // Only removing a package deletes its data, so that is the only case that needs a backup.
            var backupData = request.PreserveData && removeExisting;
            var packageArtifacts = new List<DownloadArtifact> { version.Package };
            if (version.Certificate is not null)
            {
                packageArtifacts.Add(version.Certificate);
                if (version.Certificate.Pfx is not null)
                {
                    packageArtifacts.Add(version.Certificate.Pfx);
                }
            }

            reporter.Plan = InstallationProgressPlan.Create(new InstallationPlanOptions(
                DependencyBytes: missingDependencies.Sum(dependency => dependency.Size),
                PackageBytes: packageArtifacts.Sum(artifact => artifact.Size),
                BackupData: backupData,
                InstallCertificate: version.Certificate is not null,
                DependencyCount: missingDependencies.Count,
                RemoveExisting: removeExisting));
            reporter.Report(InstallationState.CheckingPrerequisites, "Checking Windows components", 1);

            var dependencyPaths = await DownloadAllAsync(InstallationState.DownloadingDependencies, "Downloading Windows components", missingDependencies, reporter, cancellationToken);
            var packagePaths = await DownloadAllAsync(InstallationState.DownloadingPackage, $"Downloading Minecraft {version.Id}", packageArtifacts, reporter, cancellationToken);
            var packagePath = packagePaths[0];
            var certificatePath = version.Certificate is not null ? packagePaths[1] : null;
            var pfxPath = version.Certificate?.Pfx is not null ? packagePaths[2] : null;

            if (backupData)
            {
                const string message = "Backing up worlds and packs";
                reporter.Report(InstallationState.BackingUp, message);
                backupPath = await Task.Run(() => backupService.Backup(reporter.For(InstallationState.BackingUp, message)), cancellationToken);
                await log.InfoAsync(backupPath is null ? "No Minecraft data was present to back up." : $"Minecraft data backup created at {backupPath}.", cancellationToken);
            }

            if (certificatePath is not null)
            {
                reporter.Report(InstallationState.InstallingCertificate, "Trusting the package certificate. Windows may ask for approval.");
                await packageService.InstallCertificateAsync(certificatePath, pfxPath, cancellationToken);
            }

            for (var index = 0; index < dependencyPaths.Count; index++)
            {
                var message = $"Installing Windows components ({index + 1} of {dependencyPaths.Count})";
                var offset = index;
                var count = dependencyPaths.Count;
                reporter.Report(InstallationState.InstallingDependencies, message, (double)index / count);
                await packageService.InstallPackageAsync(
                    dependencyPaths[index],
                    isMinecraft: false,
                    new SyncProgress<double>(value => reporter.Update(InstallationState.InstallingDependencies, message, (offset + value) / count)),
                    cancellationToken);
            }

            // Past this point the current package is being replaced. Cancelling halfway
            // would leave no Minecraft installed, so the remaining steps always finish.
            reporter.CanCancel = false;
            cancellationToken.ThrowIfCancellationRequested();

            if (removeExisting)
            {
                const string message = "Removing the current Minecraft version";
                reporter.Report(InstallationState.RemovingExistingVersion, message);
                await packageService.RemoveMinecraftAsync(reporter.For(InstallationState.RemovingExistingVersion, message), CancellationToken.None);
            }

            var installMessage = $"Installing Minecraft {version.Id}";
            reporter.Report(InstallationState.InstallingMinecraft, installMessage);
            await packageService.InstallPackageAsync(packagePath, isMinecraft: true, reporter.For(InstallationState.InstallingMinecraft, installMessage), CancellationToken.None);

            reporter.Report(InstallationState.VerifyingInstallation, "Verifying the installation");
            var installed = await minecraftService.GetInstallationAsync(CancellationToken.None);
            if (installed is null || !VersionService.MatchesMinecraftBuild(installed.Version, version.Id))
            {
                throw new InvalidOperationException("Minecraft was installed, but Windows did not report the expected version.");
            }

            if (backupPath is not null)
            {
                const string message = "Restoring worlds and packs";
                reporter.Report(InstallationState.RestoringData, message);
                await Task.Run(() => backupService.Restore(backupPath, version.Format, reporter.For(InstallationState.RestoringData, message)), CancellationToken.None);
            }

            reporter.Report(InstallationState.Finalizing, "Finishing up");
            await log.InfoAsync($"Installed Minecraft {version.Id}.", CancellationToken.None);
            reporter.Report(InstallationState.Completed, $"Minecraft {version.Id} is ready to play");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            reporter.Report(InstallationState.Cancelled, "Installation cancelled");
            throw;
        }
        catch (Exception exception)
        {
            await log.InfoAsync($"Installation failed: {exception}", CancellationToken.None);
            var message = exception.Message;
            if (backupPath is not null && !reporter.CanCancel)
            {
                message += $" Your worlds and packs are saved in {backupPath}.";
            }

            reporter.Report(InstallationState.Failed, message);
            throw new InstallationException(message, exception);
        }
    }

    private async Task<List<string>> DownloadAllAsync(
        InstallationState state,
        string message,
        IReadOnlyList<DownloadArtifact> artifacts,
        Reporter reporter,
        CancellationToken cancellationToken)
    {
        var paths = new List<string>(artifacts.Count);
        if (artifacts.Count == 0)
        {
            return paths;
        }

        // Each byte is downloaded once and hashed once; hashing is much faster, so it
        // gets a smaller share of the state's progress.
        const double hashShare = 0.12;
        var totalBytes = Math.Max(1, artifacts.Sum(artifact => artifact.Size));
        long completedBytes = 0;
        reporter.Report(state, message);

        foreach (var artifact in artifacts)
        {
            var offset = completedBytes;
            var artifactProgress = new SyncProgress<DownloadProgress>(download =>
            {
                var stageFraction = artifact.Size > 0 ? Math.Clamp((double)download.BytesReceived / artifact.Size, 0, 1) : 0;
                var artifactFraction = download.Stage == TransferStage.Downloading
                    ? stageFraction * (1 - hashShare)
                    : 1 - hashShare + stageFraction * hashShare;
                var done = offset + artifactFraction * artifact.Size;
                var aggregate = download.Stage == TransferStage.Downloading
                    ? new DownloadProgress(offset + download.BytesReceived, totalBytes, download.BytesPerSecond)
                    : new DownloadProgress(offset + download.BytesReceived, totalBytes, 0, TransferStage.Verifying);
                reporter.Update(state, message, done / totalBytes, aggregate);
            });

            paths.Add(await downloadService.DownloadVerifiedAsync(artifact, artifactProgress, cancellationToken));
            completedBytes += artifact.Size;
        }

        reporter.Report(state, message, 1);
        return paths;
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

    /// <summary>
    /// Validates state transitions, keeps the overall percentage monotonic and
    /// throttles repeated updates of the same state so the UI is not flooded.
    /// </summary>
    private sealed class Reporter(IProgress<InstallationProgress> progress)
    {
        private readonly object _gate = new();
        private readonly Stopwatch _sinceReport = Stopwatch.StartNew();
        private InstallationState _state = InstallationState.Idle;
        private double _percentage;

        public InstallationProgressPlan Plan { get; set; } = InstallationProgressPlan.Initial;
        public bool CanCancel { get; set; } = true;

        public IProgress<double> For(InstallationState state, string message) =>
            new SyncProgress<double>(fraction => Update(state, message, fraction));

        /// <summary>Moves to a state. Called from the installation flow only.</summary>
        public void Report(InstallationState state, string message, double fraction = 0)
        {
            lock (_gate)
            {
                if (state != _state)
                {
                    if (!InstallationStateMachine.CanTransition(_state, state))
                    {
                        throw new InvalidOperationException($"Invalid installation-state transition: {_state} to {state}.");
                    }

                    _state = state;
                }

                Publish(state, message, fraction, null);
            }
        }

        /// <summary>
        /// Progress inside the current state. Callbacks can arrive late from worker
        /// threads, so updates for a state that has already ended are dropped.
        /// </summary>
        public void Update(InstallationState state, string message, double fraction, DownloadProgress? download = null)
        {
            lock (_gate)
            {
                if (state != _state || fraction < 1 && _sinceReport.Elapsed < ReportInterval)
                {
                    return;
                }

                Publish(state, message, fraction, download);
            }
        }

        private void Publish(InstallationState state, string message, double fraction, DownloadProgress? download)
        {
            if (state is not (InstallationState.Failed or InstallationState.Cancelled))
            {
                _percentage = Math.Max(_percentage, Plan.GetPercentage(state, fraction));
            }

            _sinceReport.Restart();
            var canCancel = CanCancel && state is not (InstallationState.Completed or InstallationState.Failed or InstallationState.Cancelled);
            progress.Report(new InstallationProgress(state, message, _percentage, download, canCancel));
        }
    }
}

public sealed class GamingServicesMissingException() : Exception(
    "This version needs Xbox Gaming Services. Install it from the Microsoft Store, then try again.");

public sealed class InstallationException(string message, Exception innerException) : Exception(message, innerException);
