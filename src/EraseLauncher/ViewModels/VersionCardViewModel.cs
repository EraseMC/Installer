using EraseLauncher.Models;

namespace EraseLauncher.ViewModels;

public sealed class VersionCardViewModel(MinecraftVersion version) : ObservableObject
{
    public MinecraftVersion Version { get; } = version;
    public string Id => Version.Id;
    public string DisplayName => Version.DisplayName;
    public string Subtitle => "Legacy Bedrock build";
}
