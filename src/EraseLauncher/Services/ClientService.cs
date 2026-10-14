using System.ComponentModel;
using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using EraseLauncher.Models;

namespace EraseLauncher.Services;

/// <summary>Prepares Erase Client and loads it into a running Minecraft.</summary>
public sealed class ClientService(DownloadService downloadService, LoggingService log)
{
    public const string FileName = "EraseClient.dll";
    private const string ProcessName = "Minecraft.Windows";
    private static readonly TimeSpan ProcessTimeout = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan GraphicsTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);

    // ALL APPLICATION PACKAGES and ALL RESTRICTED APPLICATION PACKAGES. Without read access for
    // them, a UWP build (which runs in an AppContainer) cannot load the DLL.
    private static readonly string[] AppContainerSids = ["S-1-15-2-1", "S-1-15-2-2"];

    /// <summary>
    /// Returns the path of a client DLL Minecraft can load, or null when there is none. A development
    /// build placed next to the launcher wins over the one the manifest offers.
    /// </summary>
    public async Task<string?> PrepareAsync(DownloadArtifact? artifact, CancellationToken cancellationToken)
    {
        string source;
        var development = Path.Combine(AppContext.BaseDirectory, FileName);
        if (File.Exists(development))
        {
            source = development;
            await log.InfoAsync($"Using the development client at {development}.", cancellationToken);
        }
        else if (artifact is not null)
        {
            source = await downloadService.DownloadVerifiedAsync(artifact, null, cancellationToken);
        }
        else
        {
            return null;
        }

        return await Task.Run(() => Stage(source), cancellationToken);
    }

    /// <summary>
    /// Waits for Minecraft to start drawing, then loads the client into it. Returns false when it
    /// could not be loaded; the reason is logged.
    /// </summary>
    public async Task<bool> InjectAsync(string dllPath, CancellationToken cancellationToken)
    {
        using var process = await WaitForProcessAsync(cancellationToken);
        if (process is null)
        {
            await log.InfoAsync("Minecraft did not start, so Erase Client was not loaded.", cancellationToken);
            return false;
        }

        if (HasModule(process, FileName))
        {
            await log.InfoAsync("Erase Client is already loaded in Minecraft.", cancellationToken);
            return true;
        }

        // The client hooks DXGI, so it is loaded once the game has its graphics stack up.
        if (!await WaitForModuleAsync(process, "dxgi.dll", GraphicsTimeout, cancellationToken))
        {
            await log.InfoAsync("Minecraft did not load DXGI in time, so Erase Client was not loaded.", cancellationToken);
            return false;
        }

        try
        {
            await Task.Run(() => ClientInjector.LoadLibrary(process.Id, dllPath), cancellationToken);
        }
        catch (Exception exception) when (exception is Win32Exception or TimeoutException)
        {
            await log.InfoAsync($"Loading Erase Client failed: {exception.Message}", cancellationToken);
            return false;
        }

        if (!HasModule(process, FileName))
        {
            await log.InfoAsync("Minecraft refused to load Erase Client.", cancellationToken);
            return false;
        }

        await log.InfoAsync($"Loaded Erase Client into Minecraft (process {process.Id}).", cancellationToken);
        return true;
    }

    /// <summary>
    /// Copies the DLL into a folder named after its hash. A copy Minecraft still has loaded is
    /// locked, so each build gets its own path instead of overwriting the last one.
    /// </summary>
    private static string Stage(string source)
    {
        string hash;
        using (var stream = File.OpenRead(source))
        {
            hash = Convert.ToHexString(SHA256.HashData(stream))[..16].ToLowerInvariant();
        }

        var directory = Path.Combine(AppPaths.Client, hash);
        var target = Path.Combine(directory, FileName);
        if (!File.Exists(target))
        {
            Directory.CreateDirectory(directory);
            var temporary = target + ".tmp";
            File.Copy(source, temporary, true);
            File.Move(temporary, target, true);
        }

        GrantAppContainerRead(target);
        return target;
    }

    private static void GrantAppContainerRead(string path)
    {
        var file = new FileInfo(path);
        var security = file.GetAccessControl();
        foreach (var sid in AppContainerSids)
        {
            security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(sid), FileSystemRights.ReadAndExecute, AccessControlType.Allow));
        }

        file.SetAccessControl(security);
    }

    private static async Task<Process?> WaitForProcessAsync(CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + ProcessTimeout;
        while (DateTime.UtcNow < deadline)
        {
            var processes = Process.GetProcessesByName(ProcessName);
            foreach (var extra in processes.Skip(1))
            {
                extra.Dispose();
            }

            if (processes.Length > 0)
            {
                return processes[0];
            }

            await Task.Delay(PollInterval, cancellationToken);
        }

        return null;
    }

    private static async Task<bool> WaitForModuleAsync(Process process, string moduleName, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (HasModule(process, moduleName))
            {
                return true;
            }

            if (HasExited(process))
            {
                return false;
            }

            await Task.Delay(PollInterval, cancellationToken);
        }

        return false;
    }

    private static bool HasModule(Process process, string moduleName)
    {
        try
        {
            process.Refresh();
            return process.Modules.Cast<ProcessModule>().Any(module => string.Equals(module.ModuleName, moduleName, StringComparison.OrdinalIgnoreCase));
        }
        // The module list cannot be read while the process is still starting or after it exits.
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }

    private static bool HasExited(Process process)
    {
        try
        {
            return process.HasExited;
        }
        catch (Win32Exception)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
    }
}
