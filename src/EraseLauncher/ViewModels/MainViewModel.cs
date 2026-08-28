using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Input;
using EraseLauncher.Models;
using EraseLauncher.Services;

namespace EraseLauncher.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly ManifestService _manifestService;
    private readonly MinecraftService _minecraftService;
    private readonly SettingsService _settingsService;
    private readonly InstallationService _installationService;
    private readonly LoggingService _log;
    private CancellationTokenSource? _installationCancellation;
    private LauncherPage _currentPage = LauncherPage.Home;
    private string _manifestStatus = "Checking Erase CDN…";
    private string _installedVersion = "Minecraft not detected";
    private string _installationMessage = "Waiting to start.";
    private InstallationState _installationState = InstallationState.Idle;
    private double _installationPercentage;
    private bool _isBusy;
    private bool _isConfirmationOpen;
    private bool _preserveData = true;
    private VersionCardViewModel? _selectedVersion;
    private LauncherSettings _settings = new();

    public MainViewModel(
        ManifestService manifestService,
        MinecraftService minecraftService,
        SettingsService settingsService,
        InstallationService installationService,
        LoggingService log)
    {
        _manifestService = manifestService;
        _minecraftService = minecraftService;
        _settingsService = settingsService;
        _installationService = installationService;
        _log = log;

        NavigateCommand = new RelayCommand<LauncherPage>(page => CurrentPage = page);
        OpenInstallCommand = new RelayCommand<VersionCardViewModel>(OpenInstallConfirmation, version => version is not null && !IsBusy);
        ConfirmInstallCommand = new RelayCommand(() => _ = InstallAsync(), () => SelectedVersion is not null && !IsBusy);
        CancelConfirmationCommand = new RelayCommand(() => IsConfirmationOpen = false);
        CancelInstallationCommand = new RelayCommand(() => _installationCancellation?.Cancel(), () => IsBusy);
        RefreshCommand = new RelayCommand(() => _ = RefreshManifestAsync(), () => !IsBusy);
        PlayCommand = new RelayCommand(Play, () => HasInstalledMinecraft && !IsBusy);
        OpenDiscordCommand = new RelayCommand(OpenDiscord);
        OpenLauncherFolderCommand = new RelayCommand(() => OpenFolder(AppPaths.Root));
        OpenLogsFolderCommand = new RelayCommand(() => OpenFolder(AppPaths.Logs));
        ClearCacheCommand = new RelayCommand(ClearCache, () => !IsBusy);
        SaveSettingsCommand = new RelayCommand(() => _ = SaveSettingsAsync(), () => !IsBusy);
    }

    public ObservableCollection<VersionCardViewModel> Versions { get; } = [];
    public ICommand NavigateCommand { get; }
    public ICommand OpenInstallCommand { get; }
    public ICommand ConfirmInstallCommand { get; }
    public ICommand CancelConfirmationCommand { get; }
    public ICommand CancelInstallationCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand PlayCommand { get; }
    public ICommand OpenDiscordCommand { get; }
    public ICommand OpenLauncherFolderCommand { get; }
    public ICommand OpenLogsFolderCommand { get; }
    public ICommand ClearCacheCommand { get; }
    public ICommand SaveSettingsCommand { get; }

    public LauncherPage CurrentPage
    {
        get => _currentPage;
        set
        {
            if (!SetProperty(ref _currentPage, value))
            {
                return;
            }

            OnPropertyChanged(nameof(IsHome));
            OnPropertyChanged(nameof(IsVersions));
            OnPropertyChanged(nameof(IsSettings));
            OnPropertyChanged(nameof(IsInstallation));
        }
    }

    public bool IsHome => CurrentPage == LauncherPage.Home;
    public bool IsVersions => CurrentPage == LauncherPage.Versions;
    public bool IsSettings => CurrentPage == LauncherPage.Settings;
    public bool IsInstallation => CurrentPage == LauncherPage.Installation;
    public string ManifestStatus { get => _manifestStatus; private set => SetProperty(ref _manifestStatus, value); }
    public string InstalledVersion { get => _installedVersion; private set => SetProperty(ref _installedVersion, value); }
    public bool HasInstalledMinecraft { get; private set; }
    public string InstallationMessage { get => _installationMessage; private set => SetProperty(ref _installationMessage, value); }
    public InstallationState InstallationState { get => _installationState; private set => SetProperty(ref _installationState, value); }
    public double InstallationPercentage { get => _installationPercentage; private set => SetProperty(ref _installationPercentage, value); }
    public bool IsBusy { get => _isBusy; private set => SetProperty(ref _isBusy, value); }
    public bool IsConfirmationOpen { get => _isConfirmationOpen; set => SetProperty(ref _isConfirmationOpen, value); }
    public bool PreserveData { get => _preserveData; set => SetProperty(ref _preserveData, value); }
    public VersionCardViewModel? SelectedVersion { get => _selectedVersion; private set => SetProperty(ref _selectedVersion, value); }
    public LauncherSettings Settings { get => _settings; private set => SetProperty(ref _settings, value); }
    public string LauncherVersion => "0.1.0";

    public async Task InitializeAsync()
    {
        try
        {
            Settings = await _settingsService.LoadAsync(CancellationToken.None);
            await RefreshMinecraftAsync();
            await RefreshManifestAsync();
        }
        catch (Exception exception)
        {
            await _log.InfoAsync($"Startup check failed: {exception}");
        }
    }

    public async Task RefreshManifestAsync()
    {
        try
        {
            ManifestStatus = "Updating available versions…";
            var manifest = await _manifestService.GetAsync(CancellationToken.None);
            Versions.Clear();
            foreach (var version in manifest.Versions.OrderByDescending(item => Version.Parse(item.Id)))
            {
                Versions.Add(new VersionCardViewModel(version));
            }

            ManifestStatus = $"Online · {Versions.Count} version(s) available";
        }
        catch (Exception exception)
        {
            Versions.Clear();
            ManifestStatus = "CDN unavailable — try again later";
            await _log.InfoAsync($"Manifest check failed: {exception}");
        }
    }

    private async Task RefreshMinecraftAsync()
    {
        try
        {
            var installation = await _minecraftService.GetInstallationAsync(CancellationToken.None);
            HasInstalledMinecraft = installation is not null;
            InstalledVersion = installation is null ? "Minecraft not detected" : $"Minecraft Bedrock {installation.Version}";
            OnPropertyChanged(nameof(HasInstalledMinecraft));
            ((RelayCommand)PlayCommand).RaiseCanExecuteChanged();
        }
        catch (Exception exception)
        {
            HasInstalledMinecraft = false;
            InstalledVersion = "Could not check Minecraft";
            OnPropertyChanged(nameof(HasInstalledMinecraft));
            await _log.InfoAsync($"Minecraft detection failed: {exception}");
        }
    }

    private void OpenInstallConfirmation(VersionCardViewModel? version)
    {
        if (version is null)
        {
            return;
        }

        SelectedVersion = version;
        PreserveData = true;
        IsConfirmationOpen = true;
        ((RelayCommand)ConfirmInstallCommand).RaiseCanExecuteChanged();
    }

    private async Task InstallAsync()
    {
        if (SelectedVersion is null)
        {
            return;
        }

        IsConfirmationOpen = false;
        IsBusy = true;
        CurrentPage = LauncherPage.Installation;
        InstallationPercentage = 0;
        _installationCancellation = new CancellationTokenSource();
        RaiseCommandStates();

        try
        {
            var progress = new Progress<InstallationProgress>(UpdateInstallationProgress);
            var request = new InstallationRequest(SelectedVersion.Version, PreserveData, Settings.LaunchAfterInstall);
            await _installationService.InstallAsync(request, progress, _installationCancellation.Token);
            await RefreshMinecraftAsync();
            if (Settings.LaunchAfterInstall && HasInstalledMinecraft)
            {
                Play();
            }
        }
        catch (OperationCanceledException)
        {
            // The service already provides the user-facing cancellation state.
        }
        catch (Exception exception)
        {
            InstallationMessage = exception.Message;
        }
        finally
        {
            _installationCancellation.Dispose();
            _installationCancellation = null;
            IsBusy = false;
            RaiseCommandStates();
        }
    }

    private void UpdateInstallationProgress(InstallationProgress progress)
    {
        InstallationState = progress.State;
        InstallationMessage = progress.Message;
        if (progress.Percentage is not null)
        {
            InstallationPercentage = progress.Percentage.Value;
        }
    }

    private void Play()
    {
        try
        {
            _minecraftService.Launch();
        }
        catch (Exception exception)
        {
            InstallationMessage = $"Could not launch Minecraft: {exception.Message}";
            CurrentPage = LauncherPage.Installation;
        }
    }

    private static void OpenDiscord() => Process.Start(new ProcessStartInfo("https://dsc.gg/erasemc") { UseShellExecute = true });

    private static void OpenFolder(string path)
    {
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo("explorer.exe", path) { UseShellExecute = true });
    }

    private void ClearCache()
    {
        if (Directory.Exists(AppPaths.Cache))
        {
            Directory.Delete(AppPaths.Cache, true);
        }
    }

    private async Task SaveSettingsAsync()
    {
        await _settingsService.SaveAsync(Settings, CancellationToken.None);
        InstallationMessage = "Settings saved.";
    }

    private void RaiseCommandStates()
    {
        ((RelayCommand<VersionCardViewModel>)OpenInstallCommand).RaiseCanExecuteChanged();
        ((RelayCommand)ConfirmInstallCommand).RaiseCanExecuteChanged();
        ((RelayCommand)CancelInstallationCommand).RaiseCanExecuteChanged();
        ((RelayCommand)RefreshCommand).RaiseCanExecuteChanged();
        ((RelayCommand)PlayCommand).RaiseCanExecuteChanged();
        ((RelayCommand)ClearCacheCommand).RaiseCanExecuteChanged();
        ((RelayCommand)SaveSettingsCommand).RaiseCanExecuteChanged();
    }
}
