#include "modules/Modules.hpp"

#include <vector>

#include "core/Log.hpp"
#include "modules/Fps.hpp"

namespace erase::modules
{
namespace
{
std::vector<std::unique_ptr<Module>> g_modules;
}

void initialize(const nlohmann::json& config)
{
    g_modules.clear();
    g_modules.push_back(std::make_unique<Fps>());

    const auto saved = config.find("modules");
    if (saved == config.end() || !saved->is_object())
    {
        return;
    }

    for (const auto& module : g_modules)
    {
        const auto settings = saved->find(module->name());
        if (settings == saved->end() || !settings->is_object())
        {
            continue;
        }

        try
        {
            module->load(*settings);
        }
        catch (const nlohmann::json::exception& error)
        {
            log::warn("Ignoring invalid settings for {}: {}", module->name(), error.what());
        }
    }
}

void save(nlohmann::json& config)
{
    auto& saved = config["modules"];
    if (!saved.is_object())
    {
        saved = nlohmann::json::object();
    }

    for (const auto& module : g_modules)
    {
        auto& settings = saved[module->name()];
        if (!settings.is_object())
        {
            settings = nlohmann::json::object();
        }
        module->save(settings);
    }
}

std::span<const std::unique_ptr<Module>> all()
{
    return g_modules;
}

void render()
{
    for (const auto& module : g_modules)
    {
        if (module->enabled())
        {
            module->render();
        }
    }
}

void onKey(int key)
{
    for (const auto& module : g_modules)
    {
        if (module->key() == key)
        {
            module->toggle();
        }
    }
}
}
