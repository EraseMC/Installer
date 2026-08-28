namespace EraseLauncher.Models;

public sealed record DownloadProgress(long BytesReceived, long? TotalBytes, double BytesPerSecond)
{
    public double Percentage => TotalBytes is > 0 ? Math.Min(100, BytesReceived * 100d / TotalBytes.Value) : 0;
}
