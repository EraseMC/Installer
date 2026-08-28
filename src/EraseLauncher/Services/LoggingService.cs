using EraseLauncher.Models;

namespace EraseLauncher.Services;

public sealed class LoggingService
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task InfoAsync(string message, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(AppPaths.Logs);
        var line = $"{DateTimeOffset.Now:O} [INFO] {message}{Environment.NewLine}";
        var path = Path.Combine(AppPaths.Logs, $"launcher-{DateTime.UtcNow:yyyy-MM-dd}.log");

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await File.AppendAllTextAsync(path, line, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }
}
