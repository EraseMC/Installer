#include "modules/Module.hpp"

namespace erase::modules
{
Module::Module(std::string name, std::string description) : m_name(std::move(name)), m_description(std::move(description))
{
}

void Module::setEnabled(bool enabled)
{
    if (m_enabled == enabled)
    {
        return;
    }

    m_enabled = enabled;
    if (enabled)
    {
        onEnable();
    }
    else
    {
        onDisable();
    }
}

void Module::load(const nlohmann::json& settings)
{
    if (const auto it = settings.find("key"); it != settings.end() && it->is_number_integer())
    {
        m_key = it->get<int>();
    }
    if (const auto it = settings.find("enabled"); it != settings.end() && it->is_boolean())
    {
        setEnabled(it->get<bool>());
    }
}

void Module::save(nlohmann::json& settings) const
{
    settings["enabled"] = m_enabled;
    settings["key"] = m_key;
}
}
