using System.Text.Json;
using System.Net.Http;
using EraseLauncher.Models;

namespace EraseLauncher.Services;

public sealed class ManifestService(HttpClient httpClient, LoggingService log, string? manifestUrl = null)
{
    public const string ManifestUrl = "https://cdn.erasemc.com/manifest.json";
    private readonly string _manifestUrl = manifestUrl ?? ManifestUrl;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<VersionManifest> GetAsync(CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(_manifestUrl, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
        var manifest = await JsonSerializer.DeserializeAsync<VersionManifest>(body, JsonOptions, cancellationToken)
            ?? throw new ManifestValidationException("The manifest is empty.");

        Validate(manifest);
        await log.InfoAsync($"Loaded manifest from {_manifestUrl} with {manifest.Versions.Count} version(s).", cancellationToken);
        return manifest;
    }

    public static void Validate(VersionManifest manifest)
    {
        if (manifest.SchemaVersion != 1)
        {
            throw new ManifestValidationException("Unsupported manifest schema.");
        }

        if (manifest.Versions.Count == 0)
        {
            throw new ManifestValidationException("The manifest contains no versions.");
        }

        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var version in manifest.Versions)
        {
            if (string.IsNullOrWhiteSpace(version.Id) || !Version.TryParse(version.Id, out _) || !ids.Add(version.Id))
            {
                throw new ManifestValidationException("A version identifier is invalid or duplicated.");
            }

            if (string.IsNullOrWhiteSpace(version.DisplayName))
            {
                throw new ManifestValidationException($"Version {version.Id} has no display name.");
            }

            ValidateArtifact(version.Package, $"package for {version.Id}");
            if (version.Certificate is not null)
            {
                ValidateArtifact(version.Certificate, $"certificate for {version.Id}");
                if (version.Certificate.Pfx is not null)
                {
                    ValidateArtifact(version.Certificate.Pfx, $"PFX certificate for {version.Id}");
                }
            }

            foreach (var dependency in version.Dependencies)
            {
                ValidateArtifact(dependency, $"dependency for {version.Id}");
            }
        }
    }

    private static void ValidateArtifact(DownloadArtifact artifact, string description)
    {
        if (artifact.Size <= 0 || string.IsNullOrWhiteSpace(artifact.FileName) ||
            artifact.FileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || artifact.FileName.Contains(Path.DirectorySeparatorChar) ||
            artifact.FileName.Contains(Path.AltDirectorySeparatorChar))
        {
            throw new ManifestValidationException($"The {description} has invalid metadata.");
        }

        if (artifact.Urls.Count == 0 || artifact.Urls.Any(url => !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ManifestValidationException($"The {description} has no valid HTTPS source.");
        }

        if (artifact.Sha256.Length != 64 || artifact.Sha256.Any(c => !Uri.IsHexDigit(c)))
        {
            throw new ManifestValidationException($"The {description} has an invalid SHA-256 hash.");
        }
    }
}

public sealed class ManifestValidationException(string message) : Exception(message);
