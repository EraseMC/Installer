#pragma once

#include <string_view>

#include <imgui.h>

namespace erase::gui
{
/// The Erase palette, shared with the launcher and the website.
namespace color
{
constexpr ImU32 Background = IM_COL32(9, 12, 9, 245);
constexpr ImU32 Panel = IM_COL32(16, 20, 15, 255);
constexpr ImU32 PanelHover = IM_COL32(22, 28, 20, 255);
constexpr ImU32 Border = IM_COL32(34, 42, 31, 255);
constexpr ImU32 Text = IM_COL32(232, 237, 230, 255);
constexpr ImU32 Muted = IM_COL32(138, 148, 135, 255);
constexpr ImU32 Accent = IM_COL32(168, 240, 56, 255);
constexpr ImU32 AccentText = IM_COL32(9, 12, 9, 255);
constexpr ImU32 Track = IM_COL32(40, 48, 37, 255);
constexpr ImU32 Scrim = IM_COL32(0, 0, 0, 110);
constexpr ImU32 HudBackground = IM_COL32(9, 12, 9, 170);
}

struct Fonts
{
    ImFont* body = nullptr;
    ImFont* strong = nullptr;
    ImFont* title = nullptr;
};

/// Loads the Onest fonts embedded in the DLL and styles ImGui. Needs a current ImGui context.
void applyTheme();
const Fonts& fonts();

/// A small text box on the HUD, behind the menu.
void hudLabel(ImVec2 position, std::string_view text);
}
