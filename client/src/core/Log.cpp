#include "core/Log.hpp"

#include <Windows.h>

#include <fstream>
#include <mutex>

namespace erase::log
{
namespace
{
std::mutex g_mutex;
std::ofstream g_file;

std::string_view label(Level level)
{
    switch (level)
    {
    case Level::Warn:
        return "warn";
    case Level::Error:
        return "error";
    default:
        return "info";
    }
}
}

void open(const std::filesystem::path& file)
{
    std::scoped_lock lock(g_mutex);
    std::error_code error;
    std::filesystem::create_directories(file.parent_path(), error);
    g_file.open(file, std::ios::out | std::ios::trunc);
}

void close()
{
    std::scoped_lock lock(g_mutex);
    g_file.close();
}

void write(Level level, std::string_view message)
{
    SYSTEMTIME time{};
    GetLocalTime(&time);
    const auto line = std::format("{:02}:{:02}:{:02}.{:03} [{}] {}\n",
        time.wHour, time.wMinute, time.wSecond, time.wMilliseconds, label(level), message);

    OutputDebugStringA(line.c_str());

    std::scoped_lock lock(g_mutex);
    if (g_file.is_open())
    {
        g_file << line;
        g_file.flush();
    }
}
}
