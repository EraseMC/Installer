#include <Windows.h>

#include "Client.hpp"

namespace
{
DWORD WINAPI clientThread(LPVOID parameter)
{
    erase::client::run();
    FreeLibraryAndExitThread(static_cast<HMODULE>(parameter), 0);
}
}

BOOL APIENTRY DllMain(HMODULE module, DWORD reason, LPVOID)
{
    if (reason == DLL_PROCESS_ATTACH)
    {
        DisableThreadLibraryCalls(module);
        // Nothing else may run under the loader lock, so the client starts on its own thread.
        if (const HANDLE thread = CreateThread(nullptr, 0, &clientThread, module, 0, nullptr))
        {
            CloseHandle(thread);
        }
    }
    return TRUE;
}
