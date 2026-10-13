using System.Text;

namespace EraseLauncher.Services;

/// <summary>
/// Keeps the Erase server in Minecraft's own server list. UWP builds keep one list in the
/// package's LocalState; GDK builds keep one per signed-in account under %AppData%.
/// </summary>
public sealed class ServerListService(LoggingService log, string? uwpRoot = null, string? gdkUsersRoot = null)
{
    public const string ServerName = "Erase";
    public const string ServerHost = "erasemc.com";
    public const int ServerPort = 19132;

    private const string ListFileName = "external_servers.txt";

    private readonly string _uwpRoot = uwpRoot ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Packages", PackageService.MinecraftFamilyName, "LocalState");

    private readonly string _gdkUsersRoot = gdkUsersRoot ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Minecraft Bedrock", "Users");

    /// <summary>Adds the Erase server to every server list it can find. Never throws.</summary>
    public async Task EnsureEraseServerAsync(CancellationToken cancellationToken = default)
    {
        foreach (var directory in FindListDirectories())
        {
            var path = Path.Combine(directory, ListFileName);
            try
            {
                if (await EnsureServerAsync(path, cancellationToken))
                {
                    await log.InfoAsync($"Added {ServerHost}:{ServerPort} to {path}.", cancellationToken);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                await log.InfoAsync($"Could not update {path}: {exception.Message}", cancellationToken);
            }
        }
    }

    /// <summary>Returns true when the list was changed.</summary>
    public static async Task<bool> EnsureServerAsync(string path, CancellationToken cancellationToken)
    {
        var lines = File.Exists(path)
            ? (await File.ReadAllLinesAsync(path, cancellationToken)).Where(line => line.Length > 0).ToList()
            : [];

        if (lines.Any(IsEraseEntry))
        {
            return false;
        }

        // The game orders the list by index, so Erase goes first and the rest move down.
        var updated = new StringBuilder();
        updated.Append($"1:{ServerName}:{ServerHost}:{ServerPort}:{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}\n");
        var index = 2;
        foreach (var line in lines)
        {
            var separator = line.IndexOf(':');
            var rest = separator > 0 && int.TryParse(line.AsSpan(0, separator), out _) ? line[(separator + 1)..] : line;
            updated.Append($"{index++}:{rest}\n");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporaryPath = path + ".erase-tmp";
        await File.WriteAllTextAsync(temporaryPath, updated.ToString(), new UTF8Encoding(false), cancellationToken);
        File.Move(temporaryPath, path, true);
        return true;
    }

    /// <summary>An entry is index:name:host:port:timestamp; the name itself may contain colons.</summary>
    private static bool IsEraseEntry(string line)
    {
        var parts = line.Split(':');
        return parts.Length >= 5 &&
            string.Equals(parts[^3].Trim(), ServerHost, StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(parts[^2], out var port) && port == ServerPort;
    }

    private IEnumerable<string> FindListDirectories()
    {
        // LocalState exists once the UWP package has been installed, even before first launch.
        if (Directory.Exists(_uwpRoot))
        {
            yield return Path.Combine(_uwpRoot, "games", "com.mojang", "minecraftpe");
        }

        // Account folders only appear after an account has played once; "Shared" holds
        // worlds and packs, not settings.
        if (Directory.Exists(_gdkUsersRoot))
        {
            foreach (var user in Directory.EnumerateDirectories(_gdkUsersRoot))
            {
                var mojang = Path.Combine(user, "games", "com.mojang");
                if (!string.Equals(Path.GetFileName(user), "Shared", StringComparison.OrdinalIgnoreCase) && Directory.Exists(mojang))
                {
                    yield return Path.Combine(mojang, "minecraftpe");
                }
            }
        }
    }
}
