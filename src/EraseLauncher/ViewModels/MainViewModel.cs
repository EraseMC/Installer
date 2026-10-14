using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Input;
using EraseLauncher.Models;
using EraseLauncher.Services;

namespace EraseLauncher.ViewModels;

public enum ManifestState
{
    Loading,
    Online,
    Offline
}

public enum InstallOutcome
{
    None,
    Running,
    Succeeded,
    Failed,
    Cancelled
}

public sealed class MainViewModel : ObservableObject
{
    public const string DiscordUrl = "https://dsc.gg/erasemc";
    private static readonly TimeSpan ManifestTimeout = TimeSpan.FromSeconds(20);

    private readonly ManifestService _manifestService;
    private readonly MinecraftService _minecraftService;
    private readonly SettingsService _settingsService;
    private readonly InstallationService _installationService;
    private readonly ServerListService _serverListService;
    private readonly ClientService _clientService;
    private readonly LoggingService _log;
    private DownloadArtifact? _clientArtifact;
    private bool _isLaunching;
    private string _launchStatus = string.Empty;
    private CancellationTokenSource? _installationCancellation;
    private MinecraftInstallation? _installation;
    private LauncherPage _currentPage = LauncherPage.Home;
    private ManifestState _manifestState = ManifestState.Loading;
    private string _manifestStatus = "Connecting to Erase CDN";
    private InstallationState _installationState = InstallationState.Idle;
    private InstallOutcome _installOutcome;
    private string _installationTitle = string.Empty;
    private string _installationDetail = string.Empty;
    private double _installationPercentage;
    private bool _canCancelInstallation;
    private bool _isBusy;
    private bool _isConfirmationOpen;
    private bool _isCloseWarningOpen;
    private bool _preserveData = true;
    private string _cacheSizeText = "Calculating";
    private string _storageMessage = string.Empty;
    private VersionCardViewModel? _selectedVersion;
    private LauncherSettings _settings = new();

    public MainViewModel(
        ManifestService manifestService,
        MinecraftService minecraftService,
        SettingsService settingsService,
        InstallationService installationService,
        ServerListService serverListService,
        ClientService clientService,
        LoggingService log)
    {
        _manifestService = manifestService;
        _minecraftService = minecraftService;
        _settingsService = settingsService;
        _installationService = installationService;
        _serverListService = serverListService;
        _clientService = clientService;
        _log = log;

        NavigateCommand = new RelayCommand<LauncherPage>(Navigate);
        OpenInstallCommand = new RelayCommand<VersionCardViewModel>(OpenInstallConfirmation, version => version is not null && !IsBusy);
        ConfirmInstallCommand = new RelayCommand(() => _ = InstallAsync(), () => SelectedVersion is not null && !IsBusy);
        CancelConfirmationCommand = new RelayCommand(() => IsConfirmationOpen = false);
        CancelInstallationCommand = new RelayCommand(() => _installationCancellation?.Cancel(), () => IsBusy && CanCancelInstallation);
        RetryInstallCommand = new RelayCommand(() => _ = InstallAsync(), () => SelectedVersion is not null && !IsBusy);
        FinishInstallCommand = new RelayCommand(FinishInstallSession, () => !IsBusy);
        RefreshCommand = new RelayCommand(() => _ = RefreshAsync(), () => !IsBusy && ManifestState != ManifestState.Loading);
        PlayCommand = new RelayCommand(() => _ = PlayAsync(), () => HasInstalledMinecraft && !IsBusy);
        OpenDiscordCommand = new RelayCommand(() => OpenUrl(DiscordUrl));
        OpenLauncherFolderCommand = new RelayCommand(() => OpenFolder(AppPaths.Root));
        OpenLogsFolderCommand = new RelayCommand(() => OpenFolder(AppPaths.Logs));
        OpenBackupsFolderCommand = new RelayCommand(() => OpenFolder(AppPaths.Backups));
        ClearCacheCommand = new RelayCommand(() => _ = ClearCacheAsync(), () => !IsBusy);
        DismissCloseWarningCommand = new RelayCommand(() => IsCloseWarningOpen = false);
        CloseDialogsCommand = new RelayCommand(() =>
        {
            IsConfirmationOpen = false;
            IsCloseWarningOpen = false;
        });
    }

    public ObservableCollection<VersionCardViewModel> Versions { get; } = [];

    public ObservableCollection<InstallStepViewModel> Steps { get; } =
    [
        new("Check"),
        new("Download"),
        new("Prepare"),
        new("Install"),
        new("Finish")
    ];

    public ICommand NavigateCommand { get; }
    public ICommand OpenInstallCommand { get; }
    public ICommand ConfirmInstallCommand { get; }
    public ICommand CancelConfirmationCommand { get; }
    public ICommand CancelInstallationCommand { get; }
    public ICommand RetryInstallCommand { get; }
    public ICommand FinishInstallCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand PlayCommand { get; }
    public ICommand OpenDiscordCommand { get; }
    public ICommand OpenLauncherFolderCommand { get; }
    public ICommand OpenLogsFolderCommand { get; }
    public ICommand OpenBackupsFolderCommand { get; }
    public ICommand ClearCacheCommand { get; }
    public ICommand DismissCloseWarningCommand { get; }
    public ICommand CloseDialogsCommand { get; }

    public string LauncherVersion { get; } = typeof(MainViewModel).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    // Navigation

    public LauncherPage CurrentPage
    {
        get => _currentPage;
        private set
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

    // Two-way so keyboard and accessibility selection of a tab navigate too, not only clicks.
    public bool IsHome { get => CurrentPage == LauncherPage.Home; set => NavigateIf(value, LauncherPage.Home); }
    public bool IsVersions { get => CurrentPage == LauncherPage.Versions; set => NavigateIf(value, LauncherPage.Versions); }
    public bool IsSettings { get => CurrentPage == LauncherPage.Settings; set => NavigateIf(value, LauncherPage.Settings); }
    public bool IsInstallation { get => CurrentPage == LauncherPage.Installation; set => NavigateIf(value, LauncherPage.Installation); }

    // Manifest

    public ManifestState ManifestState
    {
        get => _manifestState;
        private set
        {
            if (SetProperty(ref _manifestState, value))
            {
                OnPropertyChanged(nameof(IsManifestLoading));
                OnPropertyChanged(nameof(IsManifestOnline));
                OnPropertyChanged(nameof(IsManifestOffline));
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public bool IsManifestLoading => ManifestState == ManifestState.Loading;
    public bool IsManifestOnline => ManifestState == ManifestState.Online;
    public bool IsManifestOffline => ManifestState == ManifestState.Offline;
    public string ManifestStatus { get => _manifestStatus; private set => SetProperty(ref _manifestStatus, value); }

    // Installed Minecraft

    public bool HasInstalledMinecraft => _installation is not null;

    public string InstalledVersionText => _installation is null
        ? "Not installed"
        : Versions.FirstOrDefault(card => card.IsInstalled)?.Id ?? _installation.Version.ToString();

    public string InstalledDetail => _installation is null
        ? "Pick a version to install Minecraft Bedrock."
        : Versions.Any(card => card.IsInstalled)
            ? "Minecraft Bedrock is installed and ready to play."
            : "This Minecraft build is not one of the Erase versions.";

    // Installation

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public InstallationState InstallationState { get => _installationState; private set => SetProperty(ref _installationState, value); }
    public string InstallationTitle { get => _installationTitle; private set => SetProperty(ref _installationTitle, value); }
    public string InstallationDetail { get => _installationDetail; private set => SetProperty(ref _installationDetail, value); }

    public double InstallationPercentage
    {
        get => _installationPercentage;
        private set
        {
            if (SetProperty(ref _installationPercentage, value))
            {
                OnPropertyChanged(nameof(InstallationFraction));
                OnPropertyChanged(nameof(InstallationPercentText));
                OnPropertyChanged(nameof(InstallTabText));
            }
        }
    }

    public double InstallationFraction => InstallationPercentage / 100;

    // Floor, so the readout only says 100% when the installation has really finished.
    public string InstallationPercentText => $"{Math.Floor(InstallationPercentage):0}%";

    public bool CanCancelInstallation
    {
        get => _canCancelInstallation;
        private set
        {
            if (SetProperty(ref _canCancelInstallation, value))
            {
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public InstallOutcome InstallOutcome
    {
        get => _installOutcome;
        private set
        {
            if (SetProperty(ref _installOutcome, value))
            {
                OnPropertyChanged(nameof(HasInstallSession));
                OnPropertyChanged(nameof(IsInstallRunning));
                OnPropertyChanged(nameof(IsInstallSucceeded));
                OnPropertyChanged(nameof(IsInstallFailed));
                OnPropertyChanged(nameof(IsInstallCancelled));
                OnPropertyChanged(nameof(IsInstallStopped));
                OnPropertyChanged(nameof(InstallTabText));
            }
        }
    }

    public bool HasInstallSession => InstallOutcome != InstallOutcome.None;
    public bool IsInstallRunning => InstallOutcome == InstallOutcome.Running;
    public bool IsInstallSucceeded => InstallOutcome == InstallOutcome.Succeeded;
    public bool IsInstallFailed => InstallOutcome == InstallOutcome.Failed;
    public bool IsInstallCancelled => InstallOutcome == InstallOutcome.Cancelled;
    public bool IsInstallStopped => InstallOutcome is InstallOutcome.Failed or InstallOutcome.Cancelled;

    public string InstallTabText => InstallOutcome switch
    {
        InstallOutcome.Running => $"Installing {InstallationPercentText}",
        InstallOutcome.Succeeded => "Installed",
        InstallOutcome.Failed => "Install failed",
        InstallOutcome.Cancelled => "Cancelled",
        _ => "Install"
    };

    public VersionCardViewModel? SelectedVersion
    {
        get => _selectedVersion;
        private set
        {
            if (SetProperty(ref _selectedVersion, value))
            {
                OnPropertyChanged(nameof(ReplaceNotice));
            }
        }
    }

    public string ReplaceNotice => _installation is null
        ? "Minecraft is not installed yet, nothing will be replaced."
        : $"Replaces the installed version ({InstalledVersionText}).";

    // Dialogs

    public bool IsConfirmationOpen
    {
        get => _isConfirmationOpen;
        private set
        {
            if (SetProperty(ref _isConfirmationOpen, value))
            {
                OnPropertyChanged(nameof(IsDialogOpen));
            }
        }
    }

    public bool IsCloseWarningOpen
    {
        get => _isCloseWarningOpen;
        private set
        {
            if (SetProperty(ref _isCloseWarningOpen, value))
            {
                OnPropertyChanged(nameof(IsDialogOpen));
            }
        }
    }

    public bool IsDialogOpen => IsConfirmationOpen || IsCloseWarningOpen;
    public bool PreserveData { get => _preserveData; set => SetProperty(ref _preserveData, value); }

    // Settings

    public bool LaunchAfterInstall
    {
        get => _settings.LaunchAfterInstall;
        set => UpdateSetting(() => _settings.LaunchAfterInstall = value);
    }

    public bool OpenDiscordAfterFirstInstall
    {
        get => _settings.OpenDiscordAfterFirstInstall;
        set => UpdateSetting(() => _settings.OpenDiscordAfterFirstInstall = value);
    }

    public bool PreserveDataByDefault
    {
        get => _settings.PreserveDataByDefault;
        set => UpdateSetting(() => _settings.PreserveDataByDefault = value);
    }

    public bool LoadClient
    {
        get => _settings.LoadClient;
        set => UpdateSetting(() => _settings.LoadClient = value);
    }

    /// <summary>What happened to Erase Client on the last launch; empty when there is nothing to say.</summary>
    public string LaunchStatus
    {
        get => _launchStatus;
        private set
        {
            if (SetProperty(ref _launchStatus, value))
            {
                OnPropertyChanged(nameof(HasLaunchStatus));
            }
        }
    }

    public bool HasLaunchStatus => LaunchStatus.Length > 0;

    public string CacheSizeText { get => _cacheSizeText; private set => SetProperty(ref _cacheSizeText, value); }
    public string StorageMessage { get => _storageMessage; private set => SetProperty(ref _storageMessage, value); }

    public async Task InitializeAsync()
    {
        _settings = await _settingsService.LoadAsync(CancellationToken.None);
        OnPropertyChanged(nameof(LaunchAfterInstall));
        OnPropertyChanged(nameof(OpenDiscordAfterFirstInstall));
        OnPropertyChanged(nameof(PreserveDataByDefault));
        OnPropertyChanged(nameof(LoadClient));
        await RefreshAsync();
    }

    /// <summary>Returns false and explains why when closing would interrupt an installation.</summary>
    public bool TryRequestClose()
    {
        if (!IsBusy)
        {
            return true;
        }

        IsConfirmationOpen = false;
        IsCloseWarningOpen = true;
        return false;
    }

    private async Task RefreshAsync()
    {
        await RefreshMinecraftAsync();
        await RefreshManifestAsync();
    }

    private async Task RefreshManifestAsync()
    {
        ManifestState = ManifestState.Loading;
        ManifestStatus = "Connecting to Erase CDN";
        try
        {
            using var timeout = new CancellationTokenSource(ManifestTimeout);
            var manifest = await _manifestService.GetAsync(timeout.Token);
            _clientArtifact = manifest.Client;
            Versions.Clear();
            foreach (var version in manifest.Versions.OrderByDescending(item => Version.Parse(item.Id)))
            {
                Versions.Add(new VersionCardViewModel(version));
            }

            ManifestState = ManifestState.Online;
            ManifestStatus = Versions.Count == 1 ? "Online, 1 version available" : $"Online, {Versions.Count} versions available";
        }
        catch (Exception exception)
        {
            Versions.Clear();
            ManifestState = ManifestState.Offline;
            ManifestStatus = "Erase CDN is unreachable";
            await _log.InfoAsync($"Manifest check failed: {exception}");
        }

        UpdateInstalledFlags();
    }

    private async Task RefreshMinecraftAsync()
    {
        try
        {
            _installation = await _minecraftService.GetInstallationAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            _installation = null;
            await _log.InfoAsync($"Minecraft detection failed: {exception}");
        }

        UpdateInstalledFlags();
    }

    private void UpdateInstalledFlags()
    {
        foreach (var card in Versions)
        {
            card.IsInstalled = _installation is not null && VersionService.MatchesMinecraftBuild(_installation.Version, card.Id);
        }

        OnPropertyChanged(nameof(HasInstalledMinecraft));
        OnPropertyChanged(nameof(InstalledVersionText));
        OnPropertyChanged(nameof(InstalledDetail));
        OnPropertyChanged(nameof(ReplaceNotice));
        CommandManager.InvalidateRequerySuggested();
    }

    private void NavigateIf(bool selected, LauncherPage page)
    {
        if (selected)
        {
            Navigate(page);
        }
    }

    private void Navigate(LauncherPage page)
    {
        CurrentPage = page;
        if (page == LauncherPage.Settings)
        {
            _ = RefreshCacheSizeAsync();
        }
    }

    private void OpenInstallConfirmation(VersionCardViewModel? version)
    {
        if (version is null || IsBusy)
        {
            return;
        }

        SelectedVersion = version;
        PreserveData = _settings.PreserveDataByDefault;
        IsConfirmationOpen = true;
    }

    private async Task InstallAsync()
    {
        if (SelectedVersion is not { } selected || IsBusy)
        {
            return;
        }

        IsConfirmationOpen = false;
        IsBusy = true;
        InstallOutcome = InstallOutcome.Running;
        CurrentPage = LauncherPage.Installation;
        InstallationPercentage = 0;
        InstallationTitle = "Preparing installation";
        InstallationDetail = string.Empty;
        CanCancelInstallation = true;
        foreach (var step in Steps)
        {
            step.Status = StepStatus.Pending;
        }

        _installationCancellation = new CancellationTokenSource();
        try
        {
            var progress = new Progress<InstallationProgress>(UpdateInstallationProgress);
            var request = new InstallationRequest(selected.Version, PreserveData, _settings.LaunchAfterInstall);
            await Task.Run(() => _installationService.InstallAsync(request, progress, _installationCancellation.Token));
            InstallOutcome = InstallOutcome.Succeeded;
            await RefreshMinecraftAsync();
            await OnInstallSucceededAsync(selected);
        }
        catch (OperationCanceledException)
        {
            InstallOutcome = InstallOutcome.Cancelled;
            InstallationTitle = "Installation cancelled";
            InstallationDetail = "Downloaded files are kept, so the next attempt continues where this one stopped.";
            await RefreshMinecraftAsync();
        }
        catch (Exception exception)
        {
            InstallOutcome = InstallOutcome.Failed;
            InstallationTitle = "Installation failed";
            InstallationDetail = exception.Message;
            if (exception.InnerException is GamingServicesMissingException)
            {
                OpenUrl(PackageService.GamingServicesStoreUri);
            }

            await RefreshMinecraftAsync();
        }
        finally
        {
            _installationCancellation.Dispose();
            _installationCancellation = null;
            CanCancelInstallation = false;
            IsBusy = false;
        }
    }

    private async Task OnInstallSucceededAsync(VersionCardViewModel installed)
    {
        var firstInstall = !_settings.HasCompletedInstall;
        _settings.HasCompletedInstall = true;
        _settings.SelectedVersionId = installed.Id;
        await SaveSettingsAsync();

        if (firstInstall && _settings.OpenDiscordAfterFirstInstall)
        {
            OpenUrl(DiscordUrl);
        }

        if (_settings.LaunchAfterInstall && HasInstalledMinecraft)
        {
            await PlayAsync();
        }
    }

    private void UpdateInstallationProgress(InstallationProgress progress)
    {
        // Results reported after the run has ended belong to the finished session.
        if (InstallOutcome != InstallOutcome.Running)
        {
            return;
        }

        InstallationState = progress.State;
        CanCancelInstallation = progress.CanCancel;
        if (progress.Percentage is { } percentage)
        {
            InstallationPercentage = percentage;
        }

        if (progress.State is InstallationState.Failed or InstallationState.Cancelled)
        {
            foreach (var step in Steps.Where(step => step.IsActive))
            {
                step.Status = StepStatus.Failed;
            }

            return;
        }

        InstallationTitle = progress.Message;
        InstallationDetail = DescribeDetail(progress);
        var activeIndex = StepIndex(progress.State);
        for (var index = 0; index < Steps.Count; index++)
        {
            Steps[index].Status = index < activeIndex ? StepStatus.Done : index == activeIndex ? StepStatus.Active : StepStatus.Pending;
        }
    }

    private static string DescribeDetail(InstallationProgress progress)
    {
        if (progress.Download is { TotalBytes: { } total } download)
        {
            if (download.Stage == TransferStage.Verifying)
            {
                return $"Verifying SHA-256  ·  {Format.Bytes(download.BytesReceived)} of {Format.Bytes(total)}";
            }

            var text = $"{Format.Bytes(download.BytesReceived)} of {Format.Bytes(total)}";
            if (download.BytesPerSecond > 0)
            {
                text += $"  ·  {Format.Bytes(download.BytesPerSecond)}/s";
            }

            if (download.Remaining is { } remaining)
            {
                text += $"  ·  {Format.Duration(remaining)} left";
            }

            return text;
        }

        return progress.State switch
        {
            InstallationState.InstallingCertificate => "Approve the Windows prompt if it appears.",
            InstallationState.RemovingExistingVersion or InstallationState.InstallingMinecraft => "Windows is replacing the package. Keep the launcher open.",
            InstallationState.BackingUp or InstallationState.RestoringData => "Worlds, packs and settings from com.mojang.",
            InstallationState.Completed => "Every file was verified before it was installed.",
            _ => string.Empty
        };
    }

    private static int StepIndex(InstallationState state) => state switch
    {
        InstallationState.Idle or InstallationState.Preparing or InstallationState.CheckingPrerequisites => 0,
        InstallationState.DownloadingDependencies or InstallationState.DownloadingPackage or InstallationState.Verifying => 1,
        InstallationState.BackingUp or InstallationState.InstallingCertificate or InstallationState.InstallingDependencies => 2,
        InstallationState.RemovingExistingVersion or InstallationState.InstallingMinecraft or InstallationState.VerifyingInstallation => 3,
        InstallationState.RestoringData or InstallationState.Finalizing => 4,
        _ => 5
    };

    private void FinishInstallSession()
    {
        InstallOutcome = InstallOutcome.None;
        CurrentPage = LauncherPage.Home;
    }

    private async Task PlayAsync()
    {
        // Loading the client waits for the game to start, so a second click must not start another wait.
        if (_isLaunching)
        {
            return;
        }

        _isLaunching = true;
        try
        {
            // Re-checked on every launch: the game rewrites the list, and GDK account
            // folders only appear after an account has played once.
            await _serverListService.EnsureEraseServerAsync();

            var client = _settings.LoadClient ? await PrepareClientAsync() : null;
            await _minecraftService.LaunchAsync();
            if (client is not null)
            {
                LaunchStatus = "Loading Erase Client";
                var loaded = await _clientService.InjectAsync(client, CancellationToken.None);
                LaunchStatus = loaded ? string.Empty : "Erase Client could not be loaded. The launcher log has the details.";
            }
        }
        catch (Exception exception)
        {
            LaunchStatus = string.Empty;
            _ = _log.InfoAsync($"Launch failed: {exception}");
        }
        finally
        {
            _isLaunching = false;
        }
    }

    /// <summary>Returns the client DLL to load, or null to launch the game without it.</summary>
    private async Task<string?> PrepareClientAsync()
    {
        try
        {
            LaunchStatus = "Preparing Erase Client";
            var client = await _clientService.PrepareAsync(_clientArtifact, CancellationToken.None);
            LaunchStatus = string.Empty;
            if (client is null)
            {
                await _log.InfoAsync("No Erase Client build is available; launching without it.");
            }

            return client;
        }
        catch (Exception exception)
        {
            LaunchStatus = "Erase Client could not be prepared. Minecraft starts without it.";
            await _log.InfoAsync($"Preparing Erase Client failed: {exception}");
            return null;
        }
    }

    private void UpdateSetting(Action apply, [System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
    {
        apply();
        OnPropertyChanged(propertyName);
        _ = SaveSettingsAsync();
    }

    private async Task SaveSettingsAsync()
    {
        try
        {
            await _settingsService.SaveAsync(_settings, CancellationToken.None);
        }
        catch (Exception exception)
        {
            await _log.InfoAsync($"Saving settings failed: {exception}");
        }
    }

    private async Task RefreshCacheSizeAsync()
    {
        var size = await Task.Run(() => DirectorySize(AppPaths.Cache));
        CacheSizeText = size == 0 ? "Empty" : Format.Bytes(size);
    }

    private async Task ClearCacheAsync()
    {
        try
        {
            var freed = await Task.Run(() =>
            {
                var size = DirectorySize(AppPaths.Cache);
                if (Directory.Exists(AppPaths.Cache))
                {
                    Directory.Delete(AppPaths.Cache, true);
                }

                return size;
            });
            StorageMessage = freed == 0 ? "The cache was already empty." : $"Freed {Format.Bytes(freed)}.";
        }
        catch (Exception exception)
        {
            StorageMessage = "Some cached files are in use and were not removed.";
            await _log.InfoAsync($"Clearing the cache failed: {exception}");
        }

        await RefreshCacheSizeAsync();
    }

    private static long DirectorySize(string path) => Directory.Exists(path)
        ? new DirectoryInfo(path).EnumerateFiles("*", SearchOption.AllDirectories).Sum(file => file.Length)
        : 0;

    private void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            _ = _log.InfoAsync($"Opening {url} failed: {exception}");
        }
    }

    private void OpenFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            _ = _log.InfoAsync($"Opening {path} failed: {exception}");
        }
    }
}
