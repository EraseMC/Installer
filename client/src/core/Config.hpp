#pragma once

#include <filesystem>
#include <mutex>

#include <nlohmann/json.hpp>

namespace erase
{
/// The client's settings, kept as one JSON object that modules read from and write into.
class Config
{
public:
    explicit Config(std::filesystem::path file);

    void load();
    void save();

    nlohmann::json& root() { return m_root; }
    std::mutex& mutex() { return m_mutex; }

private:
    std::filesystem::path m_file;
    nlohmann::json m_root = nlohmann::json::object();
    std::mutex m_mutex;
};
}
