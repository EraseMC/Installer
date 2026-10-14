#include "gui/Theme.hpp"

#include <Windows.h>

#include <cfloat>

#include "Resources.h"
#include "core/Log.hpp"

namespace erase::gui
{
namespace
{
Fonts g_fonts;

HMODULE currentModule()
{
    HMODULE module = nullptr;
    GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
        reinterpret_cast<LPCWSTR>(&currentModule), &module);
    return module;
}

ImFont* loadFont(int resource, float size)
{
    auto& io = ImGui::GetIO();
    const HMODULE module = currentModule();
    const HRSRC info = FindResourceW(module, MAKEINTRESOURCEW(resource), RT_RCDATA);
    const HGLOBAL handle = info ? LoadResource(module, info) : nullptr;
    void* data = handle ? LockResource(handle) : nullptr;
    if (!data)
    {
        log::warn("Font resource {} is missing; the default font is used.", resource);
        return io.Fonts->AddFontDefault();
    }

    // The resource lives as long as the DLL, so ImGui must not try to free it.
    ImFontConfig config;
    config.FontDataOwnedByAtlas = false;
    config.OversampleH = 2;
    return io.Fonts->AddFontFromMemoryTTF(data, static_cast<int>(SizeofResource(module, info)), size, &config,
        io.Fonts->GetGlyphRangesCyrillic());
}

ImVec4 vec(ImU32 color)
{
    return ImGui::ColorConvertU32ToFloat4(color);
}
}

void applyTheme()
{
    g_fonts.body = loadFont(ERASE_FONT_MEDIUM, 16.0f);
    g_fonts.strong = loadFont(ERASE_FONT_SEMIBOLD, 16.0f);
    g_fonts.title = loadFont(ERASE_FONT_SEMIBOLD, 22.0f);
    ImGui::GetIO().FontDefault = g_fonts.body;

    auto& style = ImGui::GetStyle();
    style.WindowRounding = 14.0f;
    style.ChildRounding = 10.0f;
    style.FrameRounding = 8.0f;
    style.PopupRounding = 10.0f;
    style.ScrollbarRounding = 8.0f;
    style.GrabRounding = 8.0f;
    style.WindowBorderSize = 1.0f;
    style.FrameBorderSize = 0.0f;
    style.WindowPadding = ImVec2(20.0f, 18.0f);
    style.FramePadding = ImVec2(14.0f, 8.0f);
    style.ItemSpacing = ImVec2(10.0f, 10.0f);
    style.ScrollbarSize = 10.0f;

    auto* colors = style.Colors;
    colors[ImGuiCol_WindowBg] = vec(color::Background);
    colors[ImGuiCol_ChildBg] = ImVec4(0, 0, 0, 0);
    colors[ImGuiCol_Border] = vec(color::Border);
    colors[ImGuiCol_Text] = vec(color::Text);
    colors[ImGuiCol_TextDisabled] = vec(color::Muted);
    colors[ImGuiCol_Button] = vec(color::Panel);
    colors[ImGuiCol_ButtonHovered] = vec(color::PanelHover);
    colors[ImGuiCol_ButtonActive] = vec(color::Border);
    colors[ImGuiCol_FrameBg] = vec(color::Panel);
    colors[ImGuiCol_FrameBgHovered] = vec(color::PanelHover);
    colors[ImGuiCol_FrameBgActive] = vec(color::Border);
    colors[ImGuiCol_CheckMark] = vec(color::Accent);
    colors[ImGuiCol_SliderGrab] = vec(color::Accent);
    colors[ImGuiCol_SliderGrabActive] = vec(color::Accent);
    colors[ImGuiCol_ScrollbarBg] = ImVec4(0, 0, 0, 0);
    colors[ImGuiCol_ScrollbarGrab] = vec(color::Border);
    colors[ImGuiCol_ScrollbarGrabHovered] = vec(color::Muted);
    colors[ImGuiCol_ScrollbarGrabActive] = vec(color::Accent);
    colors[ImGuiCol_Separator] = vec(color::Border);
    colors[ImGuiCol_TextSelectedBg] = ImVec4(168 / 255.0f, 240 / 255.0f, 56 / 255.0f, 0.3f);
}

const Fonts& fonts()
{
    return g_fonts;
}

void hudLabel(ImVec2 position, std::string_view text)
{
    ImFont* font = g_fonts.strong ? g_fonts.strong : ImGui::GetFont();
    const float size = font->FontSize;
    const ImVec2 padding(9.0f, 5.0f);
    const ImVec2 textSize = font->CalcTextSizeA(size, FLT_MAX, 0.0f, text.data(), text.data() + text.size());

    auto* draw = ImGui::GetBackgroundDrawList();
    draw->AddRectFilled(position, position + textSize + padding * 2.0f, color::HudBackground, 6.0f);
    draw->AddText(font, size, position + padding, color::Text, text.data(), text.data() + text.size());
}
}
