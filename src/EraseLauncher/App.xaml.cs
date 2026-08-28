using System.Net;
using System.Net.Http;
using System.Windows;
using EraseLauncher.Services;
using EraseLauncher.ViewModels;

namespace EraseLauncher;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var log = new LoggingService();
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            ConnectTimeout = TimeSpan.FromSeconds(15)
        };
        var httpClient = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(15) };
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("EraseLauncher/0.1.0");

        var powerShell = new PowerShellRunner(log);
        var viewModel = new MainViewModel(
            new ManifestService(httpClient, log),
            new MinecraftService(powerShell),
            new SettingsService(),
            new InstallationService(new DownloadService(httpClient, new HashService(), log), new PackageService(powerShell), new MinecraftService(powerShell), new BackupService(), log),
            log);

        var window = new Views.MainWindow { DataContext = viewModel };
        window.Show();
        _ = viewModel.InitializeAsync();
    }
}
