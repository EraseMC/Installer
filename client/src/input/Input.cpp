#include "input/Input.hpp"

#include <windowsx.h>

#include <atomic>
#include <format>
#include <mutex>

#include "core/Log.hpp"

namespace erase::input
{
namespace
{
constexpr std::size_t MaxQueuedEvents = 512;

std::atomic<HWND> g_window = nullptr;
std::atomic<WNDPROC> g_original = nullptr;
std::atomic<bool> g_menuOpen = false;
std::atomic<bool> g_capturing = false;
std::atomic<int> g_menuKey = VK_RSHIFT;

std::mutex g_mutex;
std::vector<Event> g_events;
bool g_pointerButtons[3]{};

void push(const Event& event)
{
    std::scoped_lock lock(g_mutex);
    if (g_events.size() < MaxQueuedEvents)
    {
        g_events.push_back(event);
    }
}

/// Windows reports both shifts as VK_SHIFT; the scan code and extended flag tell them apart.
int resolveKey(WPARAM wParam, LPARAM lParam)
{
    const bool extended = (lParam & (1 << 24)) != 0;
    switch (wParam)
    {
    case VK_SHIFT:
    {
        const auto key = MapVirtualKeyW(static_cast<UINT>((lParam >> 16) & 0xFF), MAPVK_VSC_TO_VK_EX);
        return key != 0 ? static_cast<int>(key) : VK_LSHIFT;
    }
    case VK_CONTROL:
        return extended ? VK_RCONTROL : VK_LCONTROL;
    case VK_MENU:
        return extended ? VK_RMENU : VK_LMENU;
    default:
        return static_cast<int>(wParam);
    }
}

void pushMouse(float x, float y)
{
    push({ .kind = Event::Kind::MouseMove, .x = x, .y = y });
}

void pushButton(int button, bool down)
{
    push({ .kind = Event::Kind::MouseButton, .code = button, .down = down });
}

/// UWP windows deliver the mouse as pointer messages, with the position in screen coordinates.
void pushPointer(HWND window, UINT message, WPARAM wParam, LPARAM lParam)
{
    POINT point{ GET_X_LPARAM(lParam), GET_Y_LPARAM(lParam) };
    ScreenToClient(window, &point);
    pushMouse(static_cast<float>(point.x), static_cast<float>(point.y));

    if (message == WM_POINTERWHEEL)
    {
        push({ .kind = Event::Kind::MouseWheel, .y = static_cast<float>(GET_WHEEL_DELTA_WPARAM(wParam)) / WHEEL_DELTA });
        return;
    }

    const bool buttons[3] = {
        IS_POINTER_FIRSTBUTTON_WPARAM(wParam),
        IS_POINTER_SECONDBUTTON_WPARAM(wParam),
        IS_POINTER_THIRDBUTTON_WPARAM(wParam)
    };
    for (int button = 0; button < 3; ++button)
    {
        if (buttons[button] != g_pointerButtons[button])
        {
            g_pointerButtons[button] = buttons[button];
            pushButton(button, buttons[button]);
        }
    }
}

LRESULT CALLBACK windowProc(HWND window, UINT message, WPARAM wParam, LPARAM lParam)
{
    const bool open = g_menuOpen.load();

    switch (message)
    {
    case WM_KEYDOWN:
    case WM_SYSKEYDOWN:
    case WM_KEYUP:
    case WM_SYSKEYUP:
    {
        const int key = resolveKey(wParam, lParam);
        const bool down = message == WM_KEYDOWN || message == WM_SYSKEYDOWN;
        const bool repeat = down && (lParam & (1 << 30)) != 0;

        if (down && !repeat && key == g_menuKey.load() && !g_capturing.load())
        {
            setMenuOpen(!open);
            return 0;
        }

        if (!repeat)
        {
            push({ .kind = Event::Kind::Key, .code = key, .down = down });
        }

        if (open)
        {
            return 0;
        }
        break;
    }
    case WM_CHAR:
        if (open)
        {
            push({ .kind = Event::Kind::Char, .code = static_cast<int>(wParam) });
            return 0;
        }
        break;
    case WM_MOUSEMOVE:
        if (open)
        {
            pushMouse(static_cast<float>(GET_X_LPARAM(lParam)), static_cast<float>(GET_Y_LPARAM(lParam)));
            return 0;
        }
        break;
    case WM_LBUTTONDOWN:
    case WM_LBUTTONDBLCLK:
    case WM_LBUTTONUP:
    case WM_RBUTTONDOWN:
    case WM_RBUTTONDBLCLK:
    case WM_RBUTTONUP:
    case WM_MBUTTONDOWN:
    case WM_MBUTTONDBLCLK:
    case WM_MBUTTONUP:
        if (open)
        {
            const int button = message <= WM_LBUTTONDBLCLK ? 0 : message <= WM_RBUTTONDBLCLK ? 1 : 2;
            const bool down = message != WM_LBUTTONUP && message != WM_RBUTTONUP && message != WM_MBUTTONUP;
            pushButton(button, down);
            return 0;
        }
        break;
    case WM_MOUSEWHEEL:
        if (open)
        {
            push({ .kind = Event::Kind::MouseWheel, .y = static_cast<float>(GET_WHEEL_DELTA_WPARAM(wParam)) / WHEEL_DELTA });
            return 0;
        }
        break;
    case WM_POINTERUPDATE:
    case WM_POINTERDOWN:
    case WM_POINTERUP:
    case WM_POINTERWHEEL:
        if (open)
        {
            pushPointer(window, message, wParam, lParam);
            return 0;
        }
        break;
    case WM_INPUT:
        // Raw mouse input turns the camera; it is still passed to DefWindowProc so Windows can free it.
        if (open)
        {
            return DefWindowProcW(window, message, wParam, lParam);
        }
        break;
    default:
        break;
    }

    return CallWindowProcW(g_original.load(), window, message, wParam, lParam);
}
}

void load(const nlohmann::json& config)
{
    if (config.is_object() && config.contains("menuKey") && config["menuKey"].is_number_integer())
    {
        const int key = config["menuKey"].get<int>();
        if (key > 0 && key < 256)
        {
            g_menuKey = key;
        }
    }
}

void save(nlohmann::json& config)
{
    config["menuKey"] = g_menuKey.load();
}

bool install(HWND window)
{
    if (g_window.load() == window)
    {
        return true;
    }

    uninstall();

    SetLastError(ERROR_SUCCESS);
    const auto original = reinterpret_cast<WNDPROC>(
        SetWindowLongPtrW(window, GWLP_WNDPROC, reinterpret_cast<LONG_PTR>(&windowProc)));
    if (!original && GetLastError() != ERROR_SUCCESS)
    {
        log::error("Could not subclass the game window (error {}).", GetLastError());
        return false;
    }

    g_original = original;
    g_window = window;
    log::info("Listening for input on window {}.", static_cast<void*>(window));
    return true;
}

void uninstall()
{
    const HWND window = g_window.exchange(nullptr);
    if (!window)
    {
        return;
    }

    // Another hook may have subclassed the window after us; restoring then would cut it off.
    if (GetWindowLongPtrW(window, GWLP_WNDPROC) == reinterpret_cast<LONG_PTR>(&windowProc))
    {
        SetWindowLongPtrW(window, GWLP_WNDPROC, reinterpret_cast<LONG_PTR>(g_original.load()));
    }
    else
    {
        log::warn("The game window was subclassed after the client; input is left hooked.");
    }
}

bool menuOpen()
{
    return g_menuOpen.load();
}

void setMenuOpen(bool open)
{
    g_menuOpen = open;
}

int menuKey()
{
    return g_menuKey.load();
}

void setCapturing(bool capturing)
{
    g_capturing = capturing;
}

std::vector<Event> drain()
{
    std::scoped_lock lock(g_mutex);
    std::vector<Event> events;
    events.swap(g_events);
    return events;
}

std::string keyName(int key)
{
    if (key <= 0)
    {
        return "None";
    }

    LONG lParam = static_cast<LONG>(MapVirtualKeyW(static_cast<UINT>(key), MAPVK_VK_TO_VSC)) << 16;
    switch (key)
    {
    case VK_RCONTROL:
    case VK_RMENU:
    case VK_INSERT:
    case VK_DELETE:
    case VK_HOME:
    case VK_END:
    case VK_PRIOR:
    case VK_NEXT:
    case VK_LEFT:
    case VK_RIGHT:
    case VK_UP:
    case VK_DOWN:
    case VK_DIVIDE:
    case VK_NUMLOCK:
        lParam |= 1 << 24;
        break;
    default:
        break;
    }

    wchar_t name[64]{};
    const int length = GetKeyNameTextW(lParam, name, static_cast<int>(std::size(name)));
    if (length <= 0)
    {
        return std::format("Key {}", key);
    }

    const int size = WideCharToMultiByte(CP_UTF8, 0, name, length, nullptr, 0, nullptr, nullptr);
    std::string result(static_cast<std::size_t>(size), '\0');
    WideCharToMultiByte(CP_UTF8, 0, name, length, result.data(), size, nullptr, nullptr);
    return result;
}
}
