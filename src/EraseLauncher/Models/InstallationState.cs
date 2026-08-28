namespace EraseLauncher.Models;

public enum InstallationState
{
    Idle,
    Preparing,
    CheckingPrerequisites,
    DownloadingDependencies,
    DownloadingPackage,
    Verifying,
    BackingUp,
    InstallingCertificate,
    InstallingDependencies,
    RemovingExistingVersion,
    InstallingMinecraft,
    VerifyingInstallation,
    RestoringData,
    Finalizing,
    Completed,
    Failed,
    Cancelled
}
