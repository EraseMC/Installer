using System.Text.Json.Serialization;

namespace EraseLauncher.Models;

public sealed class VersionManifest
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; init; }

    [JsonPropertyName("versions")]
    public List<MinecraftVersion> Versions { get; init; } = [];

    /// <summary>The Erase Client DLL loaded into Minecraft on launch. Optional.</summary>
    [JsonPropertyName("client")]
    public DownloadArtifact? Client { get; init; }
}

public sealed class MinecraftVersion
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("displayName")]
    public required string DisplayName { get; init; }

    /// <summary>
    /// How the build is packaged. Legacy UWP builds are sideloadable APPX files; GDK builds
    /// (1.21.120 and newer) are encrypted MSIXVC files that need Gaming Services and a licence.
    /// </summary>
    [JsonPropertyName("format")]
    public PackageFormat Format { get; init; } = PackageFormat.Appx;

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

    /// <summary>Oldest installed version that satisfies the dependency; any version does when empty.</summary>
    [JsonPropertyName("minVersion")]
    public string? MinVersion { get; init; }
}

[JsonConverter(typeof(JsonStringEnumConverter<PackageFormat>))]
public enum PackageFormat
{
    Appx,
    Msixvc
}
