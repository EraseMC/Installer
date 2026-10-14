#include "gui/Gui.hpp"

#include <Windows.h>

#include <algorithm>
#include <format>
#include <string>

#include <imgui.h>

#include "Client.hpp"
#include "gui/Theme.hpp"
#include "input/Input.hpp"
#include "modules/Modules.hpp"

namespace erase::gui
{
namespace
{
constexpr float TileHeight = 96.0f;
constexpr float FooterHeight = 38.0f;

bool g_wasOpen = false;
modules::Module* g_binding = nullptr;

void stopBinding()
{
    g_binding = nullptr;
    input::setCapturing(false);
}

void onMenuClosed()
{
    stopBinding();
    // Releases any key or button ImGui still thinks is held from before the menu closed.
    ImGui::GetIO().AddFocusEvent(false);
    client::saveConfig();
}

void drawSwitch(ImDrawList* draw, ImVec2 position, bool on)
{
    const ImVec2 size(34.0f, 18.0f);
    const float radius = size.y * 0.5f - 3.0f;
    const float knobX = on ? position.x + size.x - radius - 3.0f : position.x + radius + 3.0f;
    draw->AddRectFilled(position, position + size, on ? color::Accent : color::Track, size.y * 0.5f);
    draw->AddCircleFilled(ImVec2(knobX, position.y + size.y * 0.5f), radius, on ? color::AccentText : color::Muted);
}

void moduleTile(modules::Module& module, float width)
{
    ImGui::PushID(&module);
    const ImVec2 position = ImGui::GetCursorScreenPos();
    const ImVec2 size(width, TileHeight);
    ImGui::InvisibleButton("tile", size, ImGuiButtonFlags_MouseButtonLeft | ImGuiButtonFlags_MouseButtonRight);
    const bool hovered = ImGui::IsItemHovered();
    if (ImGui::IsItemClicked(ImGuiMouseButton_Left))
    {
        module.toggle();
    }
    if (ImGui::IsItemClicked(ImGuiMouseButton_Right))
    {
        g_binding = &module;
        input::setCapturing(true);
    }

    const bool on = module.enabled();
    const auto& font = fonts();
    auto* draw = ImGui::GetWindowDrawList();
    draw->AddRectFilled(position, position + size, hovered ? color::PanelHover : color::Panel, 10.0f);
    draw->AddRect(position, position + size, on ? color::Accent : color::Border, 10.0f, 0, 1.0f);

    draw->AddText(font.strong, font.strong->FontSize, position + ImVec2(14.0f, 12.0f), color::Text, module.name().c_str());
    drawSwitch(draw, position + ImVec2(width - 48.0f, 13.0f), on);

    const ImVec4 clip(position.x, position.y, position.x + width - 14.0f, position.y + TileHeight - 28.0f);
    draw->AddText(font.body, 14.0f, position + ImVec2(14.0f, 38.0f), color::Muted, module.description().c_str(), nullptr,
        width - 28.0f, &clip);

    const auto bind = g_binding == &module ? std::string("Press a key, Backspace to clear")
        : module.key() != 0 ? std::format("Key: {}", input::keyName(module.key()))
        : std::string("Right-click to bind");
    draw->AddText(font.body, 13.0f, position + ImVec2(14.0f, TileHeight - 24.0f), g_binding == &module ? color::Accent : color::Muted,
        bind.c_str());

    ImGui::PopID();
}

void rightAligned(const std::string& text, ImU32 textColor)
{
    ImGui::SameLine(0.0f, 0.0f);
    const float width = ImGui::CalcTextSize(text.c_str()).x;
    ImGui::SetCursorPosX(ImGui::GetCursorPosX() + std::max(0.0f, ImGui::GetContentRegionAvail().x - width));
    ImGui::PushStyleColor(ImGuiCol_Text, textColor);
    ImGui::TextUnformatted(text.c_str());
    ImGui::PopStyleColor();
}

void header()
{
    const auto& info = client::game();
    ImGui::PushFont(fonts().title);
    ImGui::PushStyleColor(ImGuiCol_Text, color::Accent);
    ImGui::TextUnformatted("ERASE");
    ImGui::PopStyleColor();
    ImGui::PopFont();

    ImGui::SameLine();
    ImGui::SetCursorPosY(ImGui::GetCursorPosY() + 5.0f);
    ImGui::TextDisabled("Client %s", client::Version);

    rightAligned(std::format("Minecraft {} · {}", info.version, info.platform == game::Platform::Gdk ? "GDK" : "UWP"),
        color::Muted);
    ImGui::Spacing();
}

void moduleGrid()
{
    ImGui::BeginChild("modules", ImVec2(0.0f, -FooterHeight));
    const auto list = modules::all();
    const int columns = ImGui::GetContentRegionAvail().x > 560.0f ? 3 : 2;
    const float spacing = ImGui::GetStyle().ItemSpacing.x;
    const float width = (ImGui::GetContentRegionAvail().x - spacing * static_cast<float>(columns - 1)) / static_cast<float>(columns);

    int index = 0;
    for (const auto& module : list)
    {
        if (index % columns != 0)
        {
            ImGui::SameLine();
        }
        moduleTile(*module, width);
        ++index;
    }
    ImGui::EndChild();
}

void footer()
{
    ImGui::SetCursorPosY(ImGui::GetCursorPosY() + 6.0f);
    ImGui::AlignTextToFramePadding();
    ImGui::TextDisabled("%s opens the menu · right-click a module to bind a key", input::keyName(input::menuKey()).c_str());

    ImGui::SameLine(0.0f, 0.0f);
    const float width = ImGui::CalcTextSize("Unload").x + ImGui::GetStyle().FramePadding.x * 2.0f;
    ImGui::SetCursorPosX(ImGui::GetCursorPosX() + std::max(0.0f, ImGui::GetContentRegionAvail().x - width));
    if (ImGui::Button("Unload"))
    {
        input::setMenuOpen(false);
        client::requestUnload();
    }
}

void drawMenu()
{
    const auto& io = ImGui::GetIO();
    ImGui::GetBackgroundDrawList()->AddRectFilled(ImVec2(0.0f, 0.0f), io.DisplaySize, color::Scrim);

    const ImVec2 size(std::min(760.0f, io.DisplaySize.x - 40.0f), std::min(500.0f, io.DisplaySize.y - 40.0f));
    ImGui::SetNextWindowPos(io.DisplaySize * 0.5f, ImGuiCond_Always, ImVec2(0.5f, 0.5f));
    ImGui::SetNextWindowSize(size, ImGuiCond_Always);
    if (ImGui::Begin("##erase-menu", nullptr, ImGuiWindowFlags_NoDecoration | ImGuiWindowFlags_NoMove | ImGuiWindowFlags_NoSavedSettings))
    {
        header();
        moduleGrid();
        footer();
    }
    ImGui::End();
}
}

void setup()
{
    applyTheme();
}

void draw()
{
    modules::render();

    if (!input::menuOpen())
    {
        if (g_wasOpen)
        {
            g_wasOpen = false;
            onMenuClosed();
        }
        return;
    }

    g_wasOpen = true;
    drawMenu();
}

void onKey(int key)
{
    if (g_binding)
    {
        if (key == VK_BACK || key == VK_DELETE)
        {
            g_binding->setKey(0);
        }
        else if (key != VK_ESCAPE)
        {
            g_binding->setKey(key);
        }
        stopBinding();
        return;
    }

    if (input::menuOpen())
    {
        if (key == VK_ESCAPE)
        {
            input::setMenuOpen(false);
        }
        return;
    }

    modules::onKey(key);
}
}
