#include "hooks/Hooks.hpp"

#include <Windows.h>

#include <MinHook.h>

#include "core/Log.hpp"

namespace erase::hooks
{
namespace
{
bool g_initialized = false;
}

bool initialize()
{
    const auto status = MH_Initialize();
    if (status != MH_OK)
    {
        log::error("MinHook failed to start: {}", MH_StatusToString(status));
        return false;
    }

    g_initialized = true;
    return true;
}

void shutdown()
{
    if (!g_initialized)
    {
        return;
    }

    MH_DisableHook(MH_ALL_HOOKS);
    MH_Uninitialize();
    g_initialized = false;
}

bool create(void* target, void* detour, void** original, std::string_view name)
{
    auto status = MH_CreateHook(target, detour, original);
    if (status == MH_OK)
    {
        status = MH_EnableHook(target);
    }

    if (status != MH_OK)
    {
        log::error("Could not hook {}: {}", name, MH_StatusToString(status));
        return false;
    }

    log::info("Hooked {} at {}", name, target);
    return true;
}
}
