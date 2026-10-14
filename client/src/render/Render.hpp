#pragma once

#include <Windows.h>

namespace erase::render
{
/// Hooks the DXGI swap chain so the client draws on top of every frame the game presents.
bool install();
/// Tears down ImGui on the render thread. Must run before the hooks are removed.
void uninstall();
/// The game window, once the first frame has been presented.
HWND window();
}
