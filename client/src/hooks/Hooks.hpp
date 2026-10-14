#pragma once

#include <string_view>

namespace erase::hooks
{
bool initialize();
/// Disables and removes every hook. Detours may still be returning on other threads afterwards.
void shutdown();

bool create(void* target, void* detour, void** original, std::string_view name);

template <class Function>
bool create(void* target, Function detour, Function* original, std::string_view name)
{
    return create(target, reinterpret_cast<void*>(detour), reinterpret_cast<void**>(original), name);
}
}
