#include "modules/Fps.hpp"

#include <format>

#include <imgui.h>

#include "gui/Theme.hpp"

namespace erase::modules
{
Fps::Fps() : Module("FPS", "Frames per second in the corner of the screen.")
{
}

void Fps::render()
{
    const auto text = std::format("{} FPS", static_cast<int>(ImGui::GetIO().Framerate + 0.5f));
    gui::hudLabel(ImVec2(12.0f, 12.0f), text);
}
}
