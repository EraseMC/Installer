using EraseLauncher.Models;

namespace EraseLauncher.ViewModels;

public sealed class VersionCardViewModel(MinecraftVersion version) : ObservableObject
{
    private bool _isInstalled;

    public MinecraftVersion Version { get; } = version;
    public string Id => Version.Id;
    public string DisplayName => Version.DisplayName;
    public bool IsGdk => Version.Format == PackageFormat.Msixvc;

    public string Subtitle => IsGdk
        ? $"{Version.DisplayName}  ·  GDK, needs your Minecraft licence"
        : Version.DisplayName;
    public long DownloadSize => Version.Package.Size + Version.Dependencies.Sum(dependency => dependency.Size);
    public string SizeText => Format.Bytes(DownloadSize);

    public bool IsInstalled
    {
        get => _isInstalled;
        set
        {
            if (SetProperty(ref _isInstalled, value))
            {
                OnPropertyChanged(nameof(ActionText));
            }
        }
    }

    public string ActionText => IsInstalled ? "Reinstall" : "Install";
}
