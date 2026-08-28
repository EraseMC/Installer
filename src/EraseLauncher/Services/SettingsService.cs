using System.Text.Json;
using EraseLauncher.Models;

namespace EraseLauncher.Services;

public sealed class SettingsService
{
    private readonly string _settingsPath;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public SettingsService(string? settingsPath = null) => _settingsPath = settingsPath ?? AppPaths.Settings;

    public async Task<LauncherSettings> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_settingsPath))
        {
            return new LauncherSettings();
        }

        await using var stream = File.OpenRead(_settingsPath);
        return await JsonSerializer.DeserializeAsync<LauncherSettings>(stream, JsonOptions, cancellationToken) ?? new LauncherSettings();
    }

    public async Task SaveAsync(LauncherSettings settings, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(AppPaths.Root);
        var temporaryPath = _settingsPath + ".tmp";
        await using (var stream = File.Create(temporaryPath))
        {
            await JsonSerializer.SerializeAsync(stream, settings, JsonOptions, cancellationToken);
        }

        File.Move(temporaryPath, _settingsPath, true);
    }
}
