using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace EraseLauncher.Services;

public sealed class PowerShellRunner(LoggingService log)
{
    private const int ErrorCancelled = 1223;

    /// <summary>
    /// Runs a script elevated. Elevated processes cannot redirect output, so the script
    /// writes its error to a temporary file that is read back for the log.
    /// </summary>
    public async Task RunElevatedAsync(string script, CancellationToken cancellationToken)
    {
        var errorPath = Path.Combine(Path.GetTempPath(), $"erase-elevated-{Guid.NewGuid():N}.txt");
        var wrapped = $"try {{ {script} }} catch {{ $_ | Out-String | Set-Content -LiteralPath {Quote(errorPath)} -Encoding UTF8; exit 1 }}";

        Process? process;
        try
        {
            process = Process.Start(CreateStartInfo(wrapped));
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == ErrorCancelled)
        {
            throw new InvalidOperationException("Administrator approval was declined. The certificate is required to install Minecraft.");
        }

        using (process ?? throw new InvalidOperationException("Could not start the Windows certificate operation."))
        {
            await process.WaitForExitAsync(cancellationToken);
            if (process.ExitCode != 0)
            {
                var details = File.Exists(errorPath) ? await File.ReadAllTextAsync(errorPath, CancellationToken.None) : "No details were reported.";
                await log.InfoAsync($"Elevated operation failed: {details}", CancellationToken.None);
                TryDelete(errorPath);
                throw new InvalidOperationException("Windows could not trust the package certificate. See the launcher log for details.");
            }
        }

        TryDelete(errorPath);
        await log.InfoAsync("Completed elevated Windows operation.", cancellationToken);
    }

    public static string Quote(string value) => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";

    private static ProcessStartInfo CreateStartInfo(string script)
    {
        var preamble = "$ErrorActionPreference='Stop'; $ProgressPreference='SilentlyContinue'; $InformationPreference='SilentlyContinue'; ";
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(preamble + script));
        return new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden,
            Arguments = $"-NoProfile -NonInteractive -OutputFormat Text -ExecutionPolicy Bypass -EncodedCommand {encoded}"
        };
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
    }
}
