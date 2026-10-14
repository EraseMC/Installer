#include "core/Game.hpp"

#include <appmodel.h>
#include <ShlObj.h>
#include <unknwn.h>

#include <format>
#include <vector>

#include <winrt/Windows.ApplicationModel.Core.h>
#include <winrt/Windows.Foundation.h>
#include <winrt/Windows.Storage.h>
#include <winrt/Windows.UI.Core.h>
#include <winrt/Windows.UI.ViewManagement.h>

#include "core/Log.hpp"

namespace erase::game
{
namespace
{
std::wstring g_originalTitle;

std::string packageVersion()
{
    UINT32 length = 0;
    if (GetCurrentPackageId(&length, nullptr) != ERROR_INSUFFICIENT_BUFFER)
    {
        return "unknown";
    }

    std::vector<BYTE> buffer(length);
    if (GetCurrentPackageId(&length, buffer.data()) != ERROR_SUCCESS)
    {
        return "unknown";
    }

    // Minecraft packages 1.21.50 as 1.21.5003.0: the last two digits of the build are the hotfix.
    const auto* id = reinterpret_cast<const PACKAGE_ID*>(buffer.data());
    return std::format("{}.{}.{}", id->version.Major, id->version.Minor, id->version.Build / 100);
}

bool isAppContainer()
{
    DWORD value = 0;
    DWORD size = 0;
    return GetTokenInformation(GetCurrentProcessToken(), TokenIsAppContainer, &value, sizeof(value), &size) && value != 0;
}

std::filesystem::path dataDirectory()
{
    try
    {
        const auto folder = winrt::Windows::Storage::ApplicationData::Current().RoamingFolder();
        return std::filesystem::path(folder.Path().c_str()) / L"EraseClient";
    }
    catch (const winrt::hresult_error& error)
    {
        log::warn("The package data folder is unavailable: {}", winrt::to_string(error.message()));
    }

    std::filesystem::path path;
    PWSTR roaming = nullptr;
    if (SUCCEEDED(SHGetKnownFolderPath(FOLDERID_RoamingAppData, 0, nullptr, &roaming)))
    {
        path = std::filesystem::path(roaming) / L"EraseClient";
    }
    CoTaskMemFree(roaming);
    return path;
}

void setUwpTitle(const std::wstring& title)
{
    using namespace winrt::Windows;
    try
    {
        // The view title can only be changed from the UI thread.
        const auto dispatcher = ApplicationModel::Core::CoreApplication::MainView().CoreWindow().Dispatcher();
        dispatcher.RunAsync(UI::Core::CoreDispatcherPriority::Normal, [title]
        {
            try
            {
                UI::ViewManagement::ApplicationView::GetForCurrentView().Title(title);
            }
            catch (const winrt::hresult_error& error)
            {
                log::warn("Could not set the window title: {}", winrt::to_string(error.message()));
            }
        });
    }
    catch (const winrt::hresult_error& error)
    {
        log::warn("Could not reach the UI thread: {}", winrt::to_string(error.message()));
    }
}

std::wstring currentTitle(HWND window)
{
    // InternalGetWindowText does not send WM_GETTEXT, so it cannot block on a busy UI thread.
    wchar_t buffer[256]{};
    const int length = InternalGetWindowText(window, buffer, static_cast<int>(std::size(buffer)));
    return std::wstring(buffer, length > 0 ? length : 0);
}

void setGdkTitle(HWND window, const std::wstring& title)
{
    SendMessageTimeoutW(window, WM_SETTEXT, 0, reinterpret_cast<LPARAM>(title.c_str()), SMTO_ABORTIFHUNG, 1000, nullptr);
}
}

Info detect()
{
    Info info;
    info.version = packageVersion();
    info.platform = isAppContainer() ? Platform::Uwp : Platform::Gdk;
    info.dataDirectory = dataDirectory();
    return info;
}

void setWindowTitle(const Info& info, HWND window, const std::wstring& title)
{
    if (info.platform == Platform::Uwp)
    {
        setUwpTitle(title);
        return;
    }

    if (!window)
    {
        return;
    }

    const auto current = currentTitle(window);
    if (current == title)
    {
        return;
    }

    if (g_originalTitle.empty())
    {
        g_originalTitle = current;
    }
    setGdkTitle(window, title);
}

void restoreWindowTitle(const Info& info, HWND window)
{
    if (info.platform == Platform::Uwp)
    {
        // An empty view title shows the app's own name again.
        setUwpTitle(L"");
        return;
    }

    if (window && !g_originalTitle.empty())
    {
        setGdkTitle(window, g_originalTitle);
    }
}
}
