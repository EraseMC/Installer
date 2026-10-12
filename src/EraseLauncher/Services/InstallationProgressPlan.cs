using EraseLauncher.Models;

namespace EraseLauncher.Services;

public sealed record InstallationPlanOptions(
    long DependencyBytes = 0,
    long PackageBytes = 0,
    bool BackupData = false,
    bool InstallCertificate = false,
    int DependencyCount = 0,
    bool RemoveExisting = false);

/// <summary>
/// Maps a state and the fraction completed inside it to an overall percentage.
/// Each state that will actually run gets a weight; downloads share their budget
/// by byte count, so a 600 MB package moves the bar more than a 2 KB certificate.
/// </summary>
public sealed class InstallationProgressPlan
{
    private const double DownloadBudget = 58;
    private readonly Dictionary<InstallationState, double> _weights;
    private readonly double _total;

    private InstallationProgressPlan(Dictionary<InstallationState, double> weights)
    {
        _weights = weights;
        _total = weights.Values.Sum();
    }

    public static InstallationProgressPlan Initial { get; } = Create(new InstallationPlanOptions());

    public static InstallationProgressPlan Create(InstallationPlanOptions options)
    {
        var downloadBytes = Math.Max(1, options.DependencyBytes + options.PackageBytes);
        var weights = new Dictionary<InstallationState, double>
        {
            [InstallationState.Preparing] = 1,
            [InstallationState.CheckingPrerequisites] = 2,
            [InstallationState.DownloadingDependencies] = DownloadBudget * options.DependencyBytes / downloadBytes,
            [InstallationState.DownloadingPackage] = DownloadBudget * Math.Max(1, options.PackageBytes) / downloadBytes,
            [InstallationState.BackingUp] = options.BackupData ? 6 : 0,
            [InstallationState.InstallingCertificate] = options.InstallCertificate ? 3 : 0,
            [InstallationState.InstallingDependencies] = options.DependencyCount > 0 ? 6 : 0,
            [InstallationState.RemovingExistingVersion] = options.RemoveExisting ? 5 : 0,
            [InstallationState.InstallingMinecraft] = 16,
            [InstallationState.VerifyingInstallation] = 1,
            [InstallationState.RestoringData] = options.BackupData ? 4 : 0,
            [InstallationState.Finalizing] = 1
        };

        return new InstallationProgressPlan(weights);
    }

    public double GetPercentage(InstallationState state, double fraction)
    {
        if (state == InstallationState.Completed)
        {
            return 100;
        }

        var before = _weights.Where(pair => pair.Key < state).Sum(pair => pair.Value);
        var current = _weights.GetValueOrDefault(state) * Math.Clamp(fraction, 0, 1);
        return Math.Clamp((before + current) * 100 / _total, 0, 100);
    }
}
