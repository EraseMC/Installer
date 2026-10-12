namespace EraseLauncher.Models;

public enum TransferStage
{
    Downloading,
    Verifying
}

/// <summary>
/// Progress of one transfer. <see cref="TotalBytes"/> comes from the manifest when the
/// server does not send Content-Length, so the percentage is always meaningful.
/// </summary>
public sealed record DownloadProgress(
    long BytesReceived,
    long? TotalBytes,
    double BytesPerSecond,
    TransferStage Stage = TransferStage.Downloading)
{
    public double Percentage => TotalBytes is > 0 ? Math.Clamp(BytesReceived * 100d / TotalBytes.Value, 0, 100) : 0;

    public TimeSpan? Remaining => BytesPerSecond > 1 && TotalBytes is > 0 && Stage == TransferStage.Downloading
        ? TimeSpan.FromSeconds(Math.Max(0, TotalBytes.Value - BytesReceived) / BytesPerSecond)
        : null;
}
