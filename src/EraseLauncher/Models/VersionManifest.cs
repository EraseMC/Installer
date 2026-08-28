using System.Text.Json.Serialization;

namespace EraseLauncher.Models;

public sealed class VersionManifest
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; init; }

    [JsonPropertyName("versions")]
    public List<MinecraftVersion> Versions { get; init; } = [];
}

public sealed class MinecraftVersion
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("displayName")]
    public required string DisplayName { get; init; }

    [JsonPropertyName("package")]
    public required DownloadArtifact Package { get; init; }

    [JsonPropertyName("certificate")]
    public CertificateArtifact? Certificate { get; init; }

    [JsonPropertyName("dependencies")]
    public List<DependencyArtifact> Dependencies { get; init; } = [];
}

public class DownloadArtifact
{
    [JsonPropertyName("urls")]
    public List<string> Urls { get; init; } = [];

    [JsonPropertyName("sha256")]
    public required string Sha256 { get; init; }

    [JsonPropertyName("size")]
    public long Size { get; init; }

    [JsonPropertyName("fileName")]
    public required string FileName { get; init; }
}

public sealed class CertificateArtifact : DownloadArtifact
{
    [JsonPropertyName("pfx")]
    public DownloadArtifact? Pfx { get; init; }
}

public sealed class DependencyArtifact : DownloadArtifact
{
    [JsonPropertyName("packageFamilyName")]
    public string? PackageFamilyName { get; init; }
}
