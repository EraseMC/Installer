using System.Security.Cryptography;

namespace EraseLauncher.Services;

public sealed class HashService
{
    private const int BufferSize = 1024 * 1024;

    public async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken, IProgress<long>? bytesHashed = null)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, true);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[BufferSize];
        long total = 0;
        int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            hash.AppendData(buffer, 0, read);
            total += read;
            bytesHashed?.Report(total);
        }

        return Convert.ToHexString(hash.GetHashAndReset());
    }

    public async Task<bool> VerifySha256Async(string path, string expectedHash, CancellationToken cancellationToken, IProgress<long>? bytesHashed = null)
    {
        var actualHash = await ComputeSha256Async(path, cancellationToken, bytesHashed).ConfigureAwait(false);
        return string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase);
    }
}
