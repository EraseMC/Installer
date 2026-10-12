using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using EraseLauncher.Models;

namespace EraseLauncher.Services;

public sealed class DownloadService(HttpClient httpClient, HashService hashService, LoggingService log)
{
    public const int RetryCount = 3;
    private const int BufferSize = 128 * 1024;
    private static readonly TimeSpan ReportInterval = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan StallTimeout = TimeSpan.FromSeconds(30);

    /// <summary>Delay between attempts. Tests shorten it.</summary>
    public Func<int, TimeSpan> RetryDelay { get; init; } = attempt => TimeSpan.FromSeconds(attempt);

    public async Task<string> DownloadVerifiedAsync(
        DownloadArtifact artifact,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(AppPaths.Cache);
        var destination = Path.Combine(AppPaths.Cache, artifact.FileName);
        var partial = destination + ".partial";

        if (File.Exists(destination))
        {
            if (await VerifyAsync(destination, artifact, progress, cancellationToken).ConfigureAwait(false))
            {
                return destination;
            }

            File.Delete(destination);
        }

        foreach (var source in artifact.Urls)
        {
            for (var attempt = 1; attempt <= RetryCount; attempt++)
            {
                try
                {
                    await log.InfoAsync($"Downloading {artifact.FileName} from {source} (attempt {attempt}/{RetryCount}).", cancellationToken).ConfigureAwait(false);
                    await DownloadToPartialAsync(source, partial, artifact.Size, progress, cancellationToken).ConfigureAwait(false);
                    if (!await VerifyAsync(partial, artifact, progress, cancellationToken).ConfigureAwait(false))
                    {
                        // A corrupt partial file must never be resumed.
                        File.Delete(partial);
                        throw new IntegrityException($"File verification failed for {artifact.FileName}.");
                    }

                    File.Move(partial, destination, true);
                    return destination;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    // The partial file is kept so the next attempt resumes instead of starting over.
                    throw;
                }
                catch (Exception exception) when (attempt < RetryCount)
                {
                    await log.InfoAsync($"Download attempt failed: {exception.Message}", cancellationToken).ConfigureAwait(false);
                    await Task.Delay(RetryDelay(attempt), cancellationToken).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    await log.InfoAsync($"Source failed: {source}. {exception.Message}", cancellationToken).ConfigureAwait(false);
                }
            }
        }

        DeleteIfExists(partial);
        throw new DownloadException($"Could not download {artifact.FileName}. Check your connection and try again.");
    }

    private async Task<bool> VerifyAsync(string path, DownloadArtifact artifact, IProgress<DownloadProgress>? progress, CancellationToken cancellationToken)
    {
        var size = new FileInfo(path).Length;
        if (artifact.Size > 0 && size != artifact.Size)
        {
            return false;
        }

        var lastReport = Stopwatch.StartNew();
        var hashProgress = progress is null ? null : new SyncProgress<long>(hashed =>
        {
            if (lastReport.Elapsed >= ReportInterval || hashed >= size)
            {
                progress.Report(new DownloadProgress(hashed, size, 0, TransferStage.Verifying));
                lastReport.Restart();
            }
        });

        progress?.Report(new DownloadProgress(0, size, 0, TransferStage.Verifying));
        return await hashService.VerifySha256Async(path, artifact.Sha256, cancellationToken, hashProgress).ConfigureAwait(false);
    }

    private async Task DownloadToPartialAsync(string source, string partialPath, long expectedSize, IProgress<DownloadProgress>? progress, CancellationToken cancellationToken)
    {
        var existing = File.Exists(partialPath) ? new FileInfo(partialPath).Length : 0;
        if (expectedSize > 0 && existing > expectedSize)
        {
            File.Delete(partialPath);
            existing = 0;
        }

        if (expectedSize > 0 && existing == expectedSize)
        {
            // Already fully downloaded by an earlier attempt; only verification is left.
            return;
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, source);
        if (existing > 0)
        {
            request.Headers.Range = new RangeHeaderValue(existing, null);
        }

        using var stall = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        stall.CancelAfter(StallTimeout);
        using var response = await SendAsync(request, stall, cancellationToken).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
        {
            File.Delete(partialPath);
            throw new DownloadException("The server rejected the resume request.");
        }

        response.EnsureSuccessStatusCode();
        var resumed = existing > 0 && response.StatusCode == HttpStatusCode.PartialContent;
        if (!resumed)
        {
            existing = 0;
        }

        // The manifest size is authoritative. Content-Length is missing when the
        // response is compressed or chunked, which previously left progress at 0%.
        long? total = expectedSize > 0
            ? expectedSize
            : response.Content.Headers.ContentLength is { } length ? existing + length : null;

        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var output = new FileStream(partialPath, resumed ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, true);
        var buffer = new byte[BufferSize];
        var received = existing;
        var speed = new SpeedMeter();
        var sinceReport = Stopwatch.StartNew();
        progress?.Report(new DownloadProgress(received, total, 0));

        while (true)
        {
            stall.CancelAfter(StallTimeout);
            int count;
            try
            {
                count = await input.ReadAsync(buffer, stall.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException("The download stalled.");
            }

            if (count == 0)
            {
                break;
            }

            await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
            received += count;
            speed.Add(count);
            if (sinceReport.Elapsed >= ReportInterval)
            {
                progress?.Report(new DownloadProgress(received, total, speed.BytesPerSecond));
                sinceReport.Restart();
            }
        }

        progress?.Report(new DownloadProgress(received, total, speed.BytesPerSecond));
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationTokenSource stall, CancellationToken cancellationToken)
    {
        try
        {
            return await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, stall.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // HttpClient reports its own timeouts as cancellation; never mistake them for the user's.
            throw new TimeoutException("The download server did not respond.");
        }
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    /// <summary>Exponentially smoothed transfer speed so the readout does not jitter.</summary>
    private sealed class SpeedMeter
    {
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private long _windowBytes;
        private TimeSpan _windowStart;

        public double BytesPerSecond { get; private set; }

        public void Add(int bytes)
        {
            _windowBytes += bytes;
            var elapsed = _clock.Elapsed - _windowStart;
            if (elapsed < TimeSpan.FromMilliseconds(250))
            {
                return;
            }

            var sample = _windowBytes / elapsed.TotalSeconds;
            BytesPerSecond = BytesPerSecond == 0 ? sample : BytesPerSecond * 0.7 + sample * 0.3;
            _windowBytes = 0;
            _windowStart = _clock.Elapsed;
        }
    }
}

/// <summary>Reports synchronously on the caller's thread, unlike <see cref="Progress{T}"/>.</summary>
public sealed class SyncProgress<T>(Action<T> handler) : IProgress<T>
{
    public void Report(T value) => handler(value);
}

public sealed class DownloadException(string message) : Exception(message);
public sealed class IntegrityException(string message) : Exception(message);
