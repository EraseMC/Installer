using EraseLauncher.Models;

namespace EraseLauncher.Services;

public static class InstallationStateMachine
{
    public static bool CanTransition(InstallationState from, InstallationState to)
    {
        if (from is InstallationState.Completed or InstallationState.Failed or InstallationState.Cancelled)
        {
            return false;
        }

        if (to is InstallationState.Failed or InstallationState.Cancelled)
        {
            return true;
        }

        return to > from && to <= InstallationState.Finalizing ||
            from == InstallationState.Finalizing && to == InstallationState.Completed;
    }
}
