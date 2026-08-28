using System.Diagnostics;
using System.Text;

namespace EraseLauncher.Services;

public sealed class PowerShellRunner(LoggingService log)
{
    public async Task<string> RunAsync(string script, CancellationToken cancellationToken)
    {
        var startInfo = CreateStartInfo(script, elevated: false);
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start PowerShell.");
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        var output = await outputTask;
        var error = await errorTask;
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? "A Windows package operation failed." : error.Trim());
        }

        return output.Trim();
    }

    public async Task RunElevatedAsync(string script, CancellationToken cancellationToken)
    {
        var startInfo = CreateStartInfo(script, elevated: true);
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Administrator approval was cancelled.");
        await process.WaitForExitAsync(cancellationToken);
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException("A Windows package operation failed. See the launcher log for details.");
        }

        await log.InfoAsync("Completed elevated Windows package operation.", cancellationToken);
    }

    public static string Quote(string value) => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";

    private static ProcessStartInfo CreateStartInfo(string script, bool elevated)
    {
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes("$ErrorActionPreference='Stop'; " + script));
        return new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = elevated,
            Verb = elevated ? "runas" : string.Empty,
            WindowStyle = elevated ? ProcessWindowStyle.Hidden : ProcessWindowStyle.Hidden,
            RedirectStandardOutput = !elevated,
            RedirectStandardError = !elevated,
            CreateNoWindow = !elevated,
            Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {encoded}"
        };
    }
}
