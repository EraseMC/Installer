#pragma once

#include <memory>
#include <span>

#include <nlohmann/json.hpp>

#include "modules/Module.hpp"

namespace erase::modules
{
/// Creates every module and applies its saved settings from the config root.
void initialize(const nlohmann::json& config);
void save(nlohmann::json& config);

std::span<const std::unique_ptr<Module>> all();

void render();
/// Toggles the modules bound to `key`. Called for key presses while the menu is closed.
void onKey(int key);
}
