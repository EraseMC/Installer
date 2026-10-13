using EraseLauncher.Services;

namespace EraseLauncher.Tests;

public sealed class ServerListServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"erase-servers-{Guid.NewGuid():N}");

    [Fact]
    public async Task Erase_is_added_first_and_existing_servers_move_down()
    {
        var path = Path.Combine(_root, "external_servers.txt");
        Directory.CreateDirectory(_root);
        await File.WriteAllTextAsync(path, "1:zeqa:zeqa.net:19132:1791912774\n2:local:127.0.0.1:19162:1791840181\n");

        Assert.True(await ServerListService.EnsureServerAsync(path, CancellationToken.None));

        var lines = await File.ReadAllLinesAsync(path);
        Assert.Equal(3, lines.Length);
        Assert.StartsWith("1:Erase:erasemc.com:19132:", lines[0]);
        Assert.Equal("2:zeqa:zeqa.net:19132:1791912774", lines[1]);
        Assert.Equal("3:local:127.0.0.1:19162:1791840181", lines[2]);
    }

    [Fact]
    public async Task Existing_entry_under_any_name_is_left_alone()
    {
        var path = Path.Combine(_root, "external_servers.txt");
        Directory.CreateDirectory(_root);
        const string content = "1:zeqa:zeqa.net:19132:1\n2:my erase:ErAsEmC.com:19132:2\n";
        await File.WriteAllTextAsync(path, content);

        Assert.False(await ServerListService.EnsureServerAsync(path, CancellationToken.None));
        Assert.Equal(content, await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task Missing_list_is_created()
    {
        var path = Path.Combine(_root, "minecraftpe", "external_servers.txt");

        Assert.True(await ServerListService.EnsureServerAsync(path, CancellationToken.None));
        Assert.Single(await File.ReadAllLinesAsync(path));
    }

    [Fact]
    public async Task Gdk_account_folders_are_updated_but_shared_is_not()
    {
        var users = Path.Combine(_root, "Users");
        Directory.CreateDirectory(Path.Combine(users, "123", "games", "com.mojang"));
        Directory.CreateDirectory(Path.Combine(users, "Shared", "games", "com.mojang"));
        Directory.CreateDirectory(Path.Combine(users, "456"));
        var service = new ServerListService(new LoggingService(), Path.Combine(_root, "missing-uwp"), users);

        await service.EnsureEraseServerAsync();

        Assert.True(File.Exists(Path.Combine(users, "123", "games", "com.mojang", "minecraftpe", "external_servers.txt")));
        Assert.False(Directory.Exists(Path.Combine(users, "Shared", "games", "com.mojang", "minecraftpe")));
        Assert.False(Directory.Exists(Path.Combine(users, "456", "games")));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }
}
