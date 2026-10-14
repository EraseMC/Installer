#pragma once

#include <Windows.h>

#include <string>
#include <vector>

#include <nlohmann/json.hpp>

namespace erase::input
{
/// Input captured on the game's UI thread and handed to the render thread, which owns ImGui.
struct Event
{
    enum class Kind
    {
        Key,
        Char,
        MouseMove,
        MouseButton,
        MouseWheel
    };

    Kind kind = Kind::Key;
    /// Virtual key (left and right modifiers are told apart), UTF-16 unit, or mouse button 0-2.
    int code = 0;
    bool down = false;
    /// Cursor position in client pixels, or the wheel delta in notches in y.
    float x = 0.0f;
    float y = 0.0f;
};

void load(const nlohmann::json& config);
void save(nlohmann::json& config);

bool install(HWND window);
void uninstall();

bool menuOpen();
void setMenuOpen(bool open);
int menuKey();
/// While a key binding is being recorded, the menu key is delivered as a normal key.
void setCapturing(bool capturing);

std::vector<Event> drain();
std::string keyName(int key);
}
