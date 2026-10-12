using System.Globalization;

namespace EraseLauncher.ViewModels;

public static class Format
{
    private static readonly string[] Units = ["B", "KB", "MB", "GB", "TB"];

    public static string Bytes(double bytes)
    {
        var unit = 0;
        while (bytes >= 1024 && unit < Units.Length - 1)
        {
            bytes /= 1024;
            unit++;
        }

        var format = unit == 0 ? "0" : bytes >= 100 ? "0" : "0.0";
        return bytes.ToString(format, CultureInfo.InvariantCulture) + " " + Units[unit];
    }

    public static string Duration(TimeSpan duration)
    {
        if (duration.TotalSeconds < 60)
        {
            return $"{Math.Max(1, (int)Math.Ceiling(duration.TotalSeconds))} s";
        }

        if (duration.TotalMinutes < 60)
        {
            return $"{(int)duration.TotalMinutes} min {duration.Seconds} s";
        }

        return $"{(int)duration.TotalHours} h {duration.Minutes} min";
    }
}
