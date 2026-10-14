#include "core/Config.hpp"

#include <Windows.h>

#include <fstream>

#include "core/Log.hpp"

namespace erase
{
Config::Config(std::filesystem::path file) : m_file(std::move(file))
{
}

void Config::load()
{
    std::scoped_lock lock(m_mutex);
    std::ifstream stream(m_file);
    if (!stream)
    {
        return;
    }

    auto parsed = nlohmann::json::parse(stream, nullptr, false);
    if (parsed.is_discarded() || !parsed.is_object())
    {
        log::warn("The config at {} is not valid JSON, defaults are used.", m_file.string());
        return;
    }

    m_root = std::move(parsed);
}

void Config::save()
{
    std::scoped_lock lock(m_mutex);
    std::error_code error;
    std::filesystem::create_directories(m_file.parent_path(), error);

    // Written next to the real file and swapped in, so a crash mid-write cannot lose the config.
    auto temporary = m_file;
    temporary += L".tmp";
    {
        std::ofstream stream(temporary, std::ios::out | std::ios::trunc);
        if (!stream)
        {
            log::warn("Could not write the config to {}.", temporary.string());
            return;
        }
        stream << m_root.dump(2);
    }

    if (!MoveFileExW(temporary.c_str(), m_file.c_str(), MOVEFILE_REPLACE_EXISTING))
    {
        log::warn("Could not replace the config at {} (error {}).", m_file.string(), GetLastError());
    }
}
}
