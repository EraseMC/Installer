using System.Net;
using System.Net.Http;
using System.Windows;
using System.Windows.Threading;
using EraseLauncher.Services;
using EraseLauncher.ViewModels;

namespace EraseLauncher;

public partial class App : Application
{
    private Mutex? _singleInstance;
    private LoggingService? _log;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Two launchers deploying packages at the same time would corrupt each other's work.
        _singleInstance = new Mutex(true, @"Local\EraseLauncher.SingleInstance", out var isFirstInstance);
        if (!isFirstInstance)
        {
            MessageBox.Show("Erase Launcher is already running.", "Erase Launcher", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        var log = _log = new LoggingService();
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            _ = log.InfoAsync($"Unobserved task exception: {args.Exception}");
            args.SetObserved();
        };

        var version = typeof(App).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            ConnectTimeout = TimeSpan.FromSeconds(15)
        };

        // Large packages can take a long time on slow connections; downloads detect
        // stalls themselves instead of relying on a whole-request timeout.
        var httpClient = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd($"EraseLauncher/{version}");

        var packageService = new PackageService(new PowerShellRunner(log), log);
        var downloadService = new DownloadService(httpClient, new HashService(), log);
        var minecraftService = new MinecraftService(packageService);
        var viewModel = new MainViewModel(
            new ManifestService(httpClient, log),
            minecraftService,
            new SettingsService(),
            new InstallationService(downloadService, packageService, minecraftService, new BackupService(), log),
            new ServerListService(log),
            new ClientService(downloadService, log),
            log);

        var window = new Views.MainWindow { DataContext = viewModel };
        MainWindow = window;
        window.Show();
        _ = viewModel.InitializeAsync();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _singleInstance?.Dispose();
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _ = _log?.InfoAsync($"Unhandled exception: {e.Exception}");
        e.Handled = true;
    }
}
