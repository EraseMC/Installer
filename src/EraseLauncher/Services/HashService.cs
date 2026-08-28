using System.Security.Cryptography;

namespace EraseLauncher.Services;

public sealed class HashService
{
    public async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash);
    }

    public async Task<bool> VerifySha256Async(string path, string expectedHash, CancellationToken cancellationToken)
    {
        var actualHash = await ComputeSha256Async(path, cancellationToken);
        return string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase);
    }
}
