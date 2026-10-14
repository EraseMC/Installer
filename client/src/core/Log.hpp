#pragma once

#include <filesystem>
#include <format>
#include <string_view>
#include <utility>

namespace erase::log
{
enum class Level
{
    Info,
    Warn,
    Error
};

void open(const std::filesystem::path& file);
void close();
void write(Level level, std::string_view message);

template <class... Args>
void info(std::format_string<Args...> format, Args&&... args)
{
    write(Level::Info, std::format(format, std::forward<Args>(args)...));
}

template <class... Args>
void warn(std::format_string<Args...> format, Args&&... args)
{
    write(Level::Warn, std::format(format, std::forward<Args>(args)...));
}

template <class... Args>
void error(std::format_string<Args...> format, Args&&... args)
{
    write(Level::Error, std::format(format, std::forward<Args>(args)...));
}
}
