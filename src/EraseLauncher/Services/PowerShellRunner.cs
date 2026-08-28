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
            var rawError = string.IsNullOrWhiteSpace(error) ? output : error;
            await log.InfoAsync($"PowerShell operation failed: {rawError}", CancellationToken.None);
            throw new InvalidOperationException(ToUserMessage(rawError));
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
        var preamble = "$ErrorActionPreference='Stop'; $ProgressPreference='SilentlyContinue'; $InformationPreference='SilentlyContinue'; ";
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(preamble + script));
        return new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = elevated,
            Verb = elevated ? "runas" : string.Empty,
            WindowStyle = elevated ? ProcessWindowStyle.Hidden : ProcessWindowStyle.Hidden,
            RedirectStandardOutput = !elevated,
            RedirectStandardError = !elevated,
            CreateNoWindow = !elevated,
            Arguments = $"-NoProfile -NonInteractive -OutputFormat Text -ExecutionPolicy Bypass -EncodedCommand {encoded}"
        };
    }

    private static string ToUserMessage(string rawError)
    {
        if (rawError.Contains("CLIXML", StringComparison.OrdinalIgnoreCase) ||
            rawError.Contains("System.Management.Automation", StringComparison.OrdinalIgnoreCase))
        {
            return "Windows package check failed. Restart Erase Launcher and try again.";
        }

        return string.IsNullOrWhiteSpace(rawError) ? "A Windows package operation failed." : rawError.Trim();
    }
}
