#pragma once

#include <Windows.h>

#include <filesystem>
#include <string>

namespace erase::game
{
enum class Platform
{
    /// Legacy UWP builds (1.21.50 and older). They run in an AppContainer.
    Uwp,
    /// GDK builds (1.21.120 and newer). They run as a packaged full-trust desktop app.
    Gdk
};

struct Info
{
    /// Short game version, such as "1.21.50", or "unknown" outside a Minecraft package.
    std::string version;
    Platform platform = Platform::Uwp;
    /// Where the client keeps its config and logs. Writable on both platforms.
    std::filesystem::path dataDirectory;
};

Info detect();

void setWindowTitle(const Info& info, HWND window, const std::wstring& title);
void restoreWindowTitle(const Info& info, HWND window);
}
