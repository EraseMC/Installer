using EraseLauncher.Models;
using EraseLauncher.Services;
using System.Net;
using System.Net.Http;
using System.Text;

namespace EraseLauncher.Tests;

public sealed class ManifestServiceTests
{
    [Fact]
    public void Valid_manifest_is_accepted()
    {
        var manifest = new VersionManifest
        {
            SchemaVersion = 1,
            Versions = [CreateVersion("1.16.100")]
        };

        ManifestService.Validate(manifest);
    }

    [Fact]
    public void Manifest_with_non_https_source_is_rejected()
    {
        var version = CreateVersion("1.16.100");
        version.Package.Urls[0] = "http://cdn.erasemc.com/minecraft.appx";

        Assert.Throws<ManifestValidationException>(() => ManifestService.Validate(new VersionManifest { SchemaVersion = 1, Versions = [version] }));
    }

    [Fact]
    public void Gdk_build_may_use_the_xbox_cdn_over_http()
    {
        var version = CreateVersion("1.26.52", PackageFormat.Msixvc);
        version.Package.Urls[0] = "http://assets1.xboxlive.com/Z/build/Microsoft.MinecraftUWP_1.26.5203.0_x64__8wekyb3d8bbwe.msixvc";

        ManifestService.Validate(new VersionManifest { SchemaVersion = 2, Versions = [version] });
    }

    [Theory]
    [InlineData("http://example.com/minecraft.msixvc", PackageFormat.Msixvc)]
    [InlineData("http://assets1.xboxlive.com/minecraft.appx", PackageFormat.Appx)]
    public void Http_is_rejected_outside_the_xbox_cdn_and_gdk_builds(string url, PackageFormat format)
    {
        var version = CreateVersion("1.26.52", format);
        version.Package.Urls[0] = url;

        Assert.Throws<ManifestValidationException>(() => ManifestService.Validate(new VersionManifest { SchemaVersion = 2, Versions = [version] }));
    }

    [Fact]
    public void Unknown_schema_is_rejected() =>
        Assert.Throws<ManifestValidationException>(() => ManifestService.Validate(new VersionManifest { SchemaVersion = 3, Versions = [CreateVersion("1.16.100")] }));

    [Fact]
    public void Package_format_is_parsed_from_json()
    {
        const string json = "{\"id\":\"1.26.52\",\"displayName\":\"x\",\"format\":\"msixvc\",\"package\":{\"fileName\":\"a\",\"sha256\":\"b\"}}";
        var version = System.Text.Json.JsonSerializer.Deserialize<MinecraftVersion>(json)!;

        Assert.Equal(PackageFormat.Msixvc, version.Format);
    }

    [Fact]
    public void Manifest_with_duplicate_versions_is_rejected()
    {
        Assert.Throws<ManifestValidationException>(() => ManifestService.Validate(new VersionManifest
        {
            SchemaVersion = 1,
            Versions = [CreateVersion("1.16.100"), CreateVersion("1.16.100")]
        }));
    }

    [Fact]
    public async Task Manifest_json_is_parsed_from_remote_response()
    {
        const string json = "{\"schemaVersion\":1,\"versions\":[{\"id\":\"1.16.100\",\"displayName\":\"Minecraft Bedrock 1.16.100\",\"package\":{\"fileName\":\"minecraft.appx\",\"urls\":[\"https://cdn.erasemc.com/minecraft.appx\"],\"sha256\":\"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA\",\"size\":1024},\"dependencies\":[]}] }";
        using var client = new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") }));
        var service = new ManifestService(client, new LoggingService(), "https://test.invalid/manifest.json");

        var manifest = await service.GetAsync(CancellationToken.None);

        Assert.Single(manifest.Versions);
        Assert.Equal("1.16.100", manifest.Versions[0].Id);
    }

    private static MinecraftVersion CreateVersion(string id, PackageFormat format = PackageFormat.Appx) => new()
    {
        Id = id,
        Format = format,
        DisplayName = "Minecraft Bedrock " + id,
        Package = new DownloadArtifact
        {
            FileName = "minecraft.appx",
            Size = 1024,
            Sha256 = new string('A', 64),
            Urls = ["https://cdn.erasemc.com/minecraft.appx"]
        }
    };

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> handle) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(handle(request));
    }
}
