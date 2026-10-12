using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using EraseLauncher.Models;
using EraseLauncher.Services;

namespace EraseLauncher.Tests;

public sealed class ProgressTests
{
    [Fact]
    public void Progress_plan_moves_forward_through_every_state_and_ends_at_100()
    {
        var plan = InstallationProgressPlan.Create(new InstallationPlanOptions(
            DependencyBytes: 5_000_000,
            PackageBytes: 600_000_000,
            BackupData: true,
            InstallCertificate: true,
            DependencyCount: 1,
            RemoveExisting: true));

        var previous = 0d;
        foreach (var state in Enum.GetValues<InstallationState>().Where(state => state is > InstallationState.Idle and <= InstallationState.Finalizing))
        {
            foreach (var fraction in new[] { 0, 0.5, 1 })
            {
                var percentage = plan.GetPercentage(state, fraction);
                Assert.InRange(percentage, previous, 100);
                previous = percentage;
            }
        }

        Assert.True(plan.GetPercentage(InstallationState.Finalizing, 0) < 100, "The bar must not read 100% before the last step.");
        Assert.Equal(100, plan.GetPercentage(InstallationState.Completed, 0));
    }

    [Fact]
    public void Progress_plan_gives_the_package_download_most_of_the_bar()
    {
        var plan = InstallationProgressPlan.Create(new InstallationPlanOptions(PackageBytes: 600_000_000, InstallCertificate: true));

        var start = plan.GetPercentage(InstallationState.DownloadingPackage, 0);
        var halfway = plan.GetPercentage(InstallationState.DownloadingPackage, 0.5);
        var done = plan.GetPercentage(InstallationState.DownloadingPackage, 1);

        Assert.InRange(start, 1, 10);
        Assert.InRange(halfway, 30, 50);
        Assert.InRange(done, 55, 85);
    }

    [Fact]
    public async Task Download_reports_real_percentage_when_server_sends_no_content_length()
    {
        var content = RandomNumberGenerator.GetBytes(512 * 1024);
        var artifact = CreateArtifact(content);
        using var client = new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new UnknownLengthContent(content) }));
        var service = new DownloadService(client, new HashService(), new LoggingService());
        var reports = new List<DownloadProgress>();

        var path = await service.DownloadVerifiedAsync(artifact, new SyncProgress<DownloadProgress>(reports.Add), CancellationToken.None);

        try
        {
            var downloads = reports.Where(report => report.Stage == TransferStage.Downloading).ToList();
            Assert.NotEmpty(downloads);
            Assert.All(downloads, report => Assert.Equal(content.Length, report.TotalBytes));
            Assert.Equal(100, downloads[^1].Percentage);
            Assert.Contains(reports, report => report.Stage == TransferStage.Verifying && report.Percentage == 100);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Download_resumes_a_partial_file_with_a_range_request()
    {
        var content = RandomNumberGenerator.GetBytes(64 * 1024);
        var artifact = CreateArtifact(content);
        var half = content.Length / 2;
        Directory.CreateDirectory(AppPaths.Cache);
        var partial = Path.Combine(AppPaths.Cache, artifact.FileName + ".partial");
        await File.WriteAllBytesAsync(partial, content[..half]);

        long? requestedFrom = null;
        using var client = new HttpClient(new StubHandler(request =>
        {
            requestedFrom = request.Headers.Range?.Ranges.Single().From;
            return new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = new ByteArrayContent(content[half..]) };
        }));
        var service = new DownloadService(client, new HashService(), new LoggingService());
        var reports = new List<DownloadProgress>();

        var path = await service.DownloadVerifiedAsync(artifact, new SyncProgress<DownloadProgress>(reports.Add), CancellationToken.None);

        try
        {
            Assert.Equal(half, requestedFrom);
            Assert.Equal(content, await File.ReadAllBytesAsync(path));
            Assert.Equal(half, reports.First(report => report.Stage == TransferStage.Downloading).BytesReceived);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Cached_file_is_reverified_with_progress_instead_of_downloaded_again()
    {
        var content = RandomNumberGenerator.GetBytes(16 * 1024);
        var artifact = CreateArtifact(content);
        Directory.CreateDirectory(AppPaths.Cache);
        var cached = Path.Combine(AppPaths.Cache, artifact.FileName);
        await File.WriteAllBytesAsync(cached, content);
        var requests = 0;
        using var client = new HttpClient(new StubHandler(_ =>
        {
            requests++;
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }));
        var reports = new List<DownloadProgress>();

        try
        {
            var path = await new DownloadService(client, new HashService(), new LoggingService())
                .DownloadVerifiedAsync(artifact, new SyncProgress<DownloadProgress>(reports.Add), CancellationToken.None);

            Assert.Equal(cached, path);
            Assert.Equal(0, requests);
            Assert.Equal(100, reports[^1].Percentage);
        }
        finally
        {
            File.Delete(cached);
        }
    }

    private static DownloadArtifact CreateArtifact(byte[] content) => new()
    {
        FileName = $"progress-test-{Guid.NewGuid():N}.appx",
        Sha256 = Convert.ToHexString(SHA256.HashData(content)),
        Size = content.Length,
        Urls = ["https://cdn.invalid/file"]
    };

    /// <summary>Behaves like a compressed or chunked response: no Content-Length header.</summary>
    private sealed class UnknownLengthContent(byte[] content) : HttpContent
    {
        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            for (var offset = 0; offset < content.Length; offset += 16 * 1024)
            {
                await stream.WriteAsync(content.AsMemory(offset, Math.Min(16 * 1024, content.Length - offset)));
            }
        }

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> handle) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(handle(request));
    }
}
