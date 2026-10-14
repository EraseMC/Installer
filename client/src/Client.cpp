#include "Client.hpp"

#include <unknwn.h>

#include <format>
#include <memory>
#include <string>

#include <winrt/base.h>

#include "core/Config.hpp"
#include "core/Log.hpp"
#include "hooks/Hooks.hpp"
#include "input/Input.hpp"
#include "modules/Modules.hpp"
#include "render/Render.hpp"

namespace erase::client
{
namespace
{
constexpr DWORD TitleCheckIntervalMs = 1000;

game::Info g_game;
std::unique_ptr<Config> g_config;
HANDLE g_unloadEvent = nullptr;

std::wstring widen(std::string_view text)
{
    return std::wstring(text.begin(), text.end());
}

std::wstring windowTitle()
{
    return std::format(L"Erase Client {} · {}", widen(Version), widen(g_game.version));
}

/// Waits for an unload request while keeping the window title. UWP keeps a title once it is set,
/// but a GDK game may write its own back, so that one is checked every second.
void waitForUnload()
{
    HWND titled = nullptr;
    while (WaitForSingleObject(g_unloadEvent, TitleCheckIntervalMs) == WAIT_TIMEOUT)
    {
        const HWND window = render::window();
        if (window && (window != titled || g_game.platform == game::Platform::Gdk))
        {
            game::setWindowTitle(g_game, window, windowTitle());
            titled = window;
        }
    }

    if (titled)
    {
        game::restoreWindowTitle(g_game, titled);
    }
}
}

void run()
{
    // A second injection would hook everything twice. The name is per package, so per game.
    const HANDLE instance = CreateMutexW(nullptr, TRUE, L"EraseClient.Instance");
    if (!instance || GetLastError() == ERROR_ALREADY_EXISTS)
    {
        if (instance)
        {
            CloseHandle(instance);
        }
        return;
    }

    bool apartment = false;
    try
    {
        winrt::init_apartment(winrt::apartment_type::multi_threaded);
        apartment = true;
    }
    catch (const winrt::hresult_error&)
    {
    }

    g_game = game::detect();
    log::open(g_game.dataDirectory / L"logs" / L"client.log");
    log::info("Erase Client {} on Minecraft {} ({}).", Version, g_game.version,
        g_game.platform == game::Platform::Gdk ? "GDK" : "UWP");
    log::info("Settings are kept in {}.", g_game.dataDirectory.string());

    g_config = std::make_unique<Config>(g_game.dataDirectory / L"config.json");
    g_config->load();
    {
        std::scoped_lock lock(g_config->mutex());
        input::load(g_config->root());
        modules::initialize(g_config->root());
    }

    g_unloadEvent = CreateEventW(nullptr, TRUE, FALSE, nullptr);
    if (g_unloadEvent && hooks::initialize() && render::install())
    {
        log::info("Ready.");
        waitForUnload();
    }

    render::uninstall();
    hooks::shutdown();
    saveConfig();
    log::info("Unloaded.");
    log::close();

    if (g_unloadEvent)
    {
        CloseHandle(g_unloadEvent);
        g_unloadEvent = nullptr;
    }
    if (apartment)
    {
        winrt::uninit_apartment();
    }
    ReleaseMutex(instance);
    CloseHandle(instance);

    // Hooked calls that started before the hooks were removed may still be returning through this DLL.
    Sleep(250);
}

const game::Info& game()
{
    return g_game;
}

void saveConfig()
{
    if (!g_config)
    {
        return;
    }

    {
        std::scoped_lock lock(g_config->mutex());
        input::save(g_config->root());
        modules::save(g_config->root());
    }
    g_config->save();
}

void requestUnload()
{
    if (g_unloadEvent)
    {
        SetEvent(g_unloadEvent);
    }
}
}
