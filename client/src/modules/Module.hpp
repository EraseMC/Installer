#pragma once

#include <string>

#include <nlohmann/json.hpp>

namespace erase::modules
{
/// A feature the player can switch on and off. Every method runs on the render thread.
class Module
{
public:
    Module(std::string name, std::string description);
    virtual ~Module() = default;

    Module(const Module&) = delete;
    Module& operator=(const Module&) = delete;

    const std::string& name() const { return m_name; }
    const std::string& description() const { return m_description; }

    bool enabled() const { return m_enabled; }
    void setEnabled(bool enabled);
    void toggle() { setEnabled(!m_enabled); }

    /// Virtual key that toggles the module, or 0 for none.
    int key() const { return m_key; }
    void setKey(int key) { m_key = key; }

    /// Draws the module's overlay. Only called while the module is enabled.
    virtual void render() {}

    virtual void load(const nlohmann::json& settings);
    virtual void save(nlohmann::json& settings) const;

protected:
    virtual void onEnable() {}
    virtual void onDisable() {}

private:
    std::string m_name;
    std::string m_description;
    bool m_enabled = false;
    int m_key = 0;
};
}
