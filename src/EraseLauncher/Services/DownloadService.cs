using System.Diagnostics;
using System.Net.Http;
using EraseLauncher.Models;

namespace EraseLauncher.Services;

public sealed class DownloadService(HttpClient httpClient, HashService hashService, LoggingService log)
{
    public const int RetryCount = 3;

    public async Task<string> DownloadVerifiedAsync(
        DownloadArtifact artifact,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(AppPaths.Cache);
        var destination = Path.Combine(AppPaths.Cache, artifact.FileName);
        var partial = destination + ".partial";

        if (File.Exists(destination) && await hashService.VerifySha256Async(destination, artifact.Sha256, cancellationToken))
        {
            progress?.Report(new DownloadProgress(artifact.Size, artifact.Size, 0));
            return destination;
        }

        File.Delete(destination);
        foreach (var source in artifact.Urls)
        {
            for (var attempt = 1; attempt <= RetryCount; attempt++)
            {
                try
                {
                    await log.InfoAsync($"Downloading {artifact.FileName} from {source} (attempt {attempt}/{RetryCount}).", cancellationToken);
                    await DownloadToPartialAsync(source, partial, progress, cancellationToken);
                    if (!await hashService.VerifySha256Async(partial, artifact.Sha256, cancellationToken))
                    {
                        File.Delete(partial);
                        throw new IntegrityException($"File verification failed for {artifact.FileName}.");
                    }

                    File.Move(partial, destination, true);
                    return destination;
                }
                catch (OperationCanceledException)
                {
                    DeleteIfExists(partial);
                    throw;
                }
                catch (Exception exception) when (attempt < RetryCount)
                {
                    DeleteIfExists(partial);
                    await log.InfoAsync($"Download attempt failed: {exception.Message}", cancellationToken);
                    await Task.Delay(TimeSpan.FromSeconds(attempt), cancellationToken);
                }
                catch (Exception exception)
                {
                    DeleteIfExists(partial);
                    await log.InfoAsync($"Source failed: {source}. {exception.Message}", cancellationToken);
                }
            }
        }

        throw new DownloadException($"Could not download and verify {artifact.FileName} from any configured source.");
    }

    private async Task DownloadToPartialAsync(string source, string partialPath, IProgress<DownloadProgress>? progress, CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(source, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength;
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var output = new FileStream(partialPath, FileMode.Create, FileAccess.Write, FileShare.None, 131072, true);
        var buffer = new byte[131072];
        var received = 0L;
        var stopwatch = Stopwatch.StartNew();
        long previousBytes = 0;
        var previousTime = TimeSpan.Zero;

        while (true)
        {
            var count = await input.ReadAsync(buffer, cancellationToken);
            if (count == 0)
            {
                break;
            }

            await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
            received += count;
            if (stopwatch.Elapsed - previousTime >= TimeSpan.FromMilliseconds(200))
            {
                var elapsed = stopwatch.Elapsed - previousTime;
                var speed = (received - previousBytes) / elapsed.TotalSeconds;
                progress?.Report(new DownloadProgress(received, total, speed));
                previousBytes = received;
                previousTime = stopwatch.Elapsed;
            }
        }

        progress?.Report(new DownloadProgress(received, total, 0));
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}

public sealed class DownloadException(string message) : Exception(message);
public sealed class IntegrityException(string message) : Exception(message);
