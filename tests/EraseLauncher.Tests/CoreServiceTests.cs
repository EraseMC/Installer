using System.Security.Cryptography;
using System.Net;
using System.Net.Http;
using EraseLauncher.Models;
using EraseLauncher.Services;

namespace EraseLauncher.Tests;

public sealed class CoreServiceTests
{
    [Fact]
    public async Task Hash_service_accepts_matching_hash_and_rejects_mismatch()
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, "erase");
            var hash = Convert.ToHexString(SHA256.HashData("erase"u8));
            var service = new HashService();
            Assert.True(await service.VerifySha256Async(path, hash, CancellationToken.None));
            Assert.False(await service.VerifySha256Async(path, new string('0', 64), CancellationToken.None));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Settings_round_trip()
    {
        var path = Path.Combine(Path.GetTempPath(), $"erase-settings-{Guid.NewGuid():N}.json");
        try
        {
            var service = new SettingsService(path);
            await service.SaveAsync(new LauncherSettings { LaunchAfterInstall = false, RetryAttempts = 5, SelectedVersionId = "1.18.12" }, CancellationToken.None);
            var settings = await service.LoadAsync(CancellationToken.None);
            Assert.False(settings.LaunchAfterInstall);
            Assert.Equal(5, settings.RetryAttempts);
            Assert.Equal("1.18.12", settings.SelectedVersionId);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("1.16.100.4", "1.16.100", true)]
    [InlineData("1.18.12.1", "1.16.100", false)]
    public void Minecraft_build_comparison_uses_major_minor_build(string installed, string requested, bool expected) =>
        Assert.Equal(expected, VersionService.MatchesMinecraftBuild(Version.Parse(installed), requested));

    [Fact]
    public void Terminal_installation_state_cannot_transition()
    {
        Assert.True(InstallationStateMachine.CanTransition(InstallationState.Preparing, InstallationState.DownloadingPackage));
        Assert.False(InstallationStateMachine.CanTransition(InstallationState.Completed, InstallationState.Preparing));
        Assert.False(InstallationStateMachine.CanTransition(InstallationState.Failed, InstallationState.InstallingMinecraft));
    }

    [Fact]
    public async Task Download_falls_back_after_retries_and_verifies_result()
    {
        var content = "verified mirror payload"u8.ToArray();
        var expectedHash = Convert.ToHexString(SHA256.HashData(content));
        var fileName = $"download-test-{Guid.NewGuid():N}.appx";
        var requests = 0;
        using var client = new HttpClient(new StubHandler(request =>
        {
            requests++;
            return request.RequestUri!.Host == "primary.invalid"
                ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(content) };
        }));
        var artifact = new DownloadArtifact
        {
            FileName = fileName,
            Sha256 = expectedHash,
            Size = content.Length,
            Urls = ["https://primary.invalid/file", "https://backup.invalid/file"]
        };
        var service = new DownloadService(client, new HashService(), new LoggingService());

        var path = await service.DownloadVerifiedAsync(artifact, null, CancellationToken.None);

        try
        {
            Assert.Equal(4, requests);
            Assert.True(await new HashService().VerifySha256Async(path, expectedHash, CancellationToken.None));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> handle) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(handle(request));
    }
}
