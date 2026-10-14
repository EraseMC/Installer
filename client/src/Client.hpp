#pragma once

#include <Windows.h>

#include "core/Game.hpp"

namespace erase::client
{
inline constexpr const char* Version = ERASE_CLIENT_VERSION;

/// Runs the client on its own thread until it is unloaded.
void run();

const game::Info& game();
/// Writes the current module and input settings to disk.
void saveConfig();
void requestUnload();
}
