#pragma once

namespace erase::gui
{
/// Styles the freshly created ImGui context.
void setup();
/// Draws the HUD and, while it is open, the menu. Called between NewFrame and Render.
void draw();
/// A key was pressed (not repeated). Called on the render thread before the frame starts.
void onKey(int key);
}
