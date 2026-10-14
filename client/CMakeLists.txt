cmake_minimum_required(VERSION 3.25)

project(EraseClient VERSION 0.1.0 LANGUAGES C CXX RC)

if(NOT MSVC OR NOT CMAKE_SIZEOF_VOID_P EQUAL 8)
    message(FATAL_ERROR "Erase Client is built with MSVC for x64 only.")
endif()

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
# The DLL runs inside the game's package, where the desktop VC++ runtime may be missing.
set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")
set(CMAKE_MSVC_DEBUG_INFORMATION_FORMAT "ProgramDatabase")

include(FetchContent)

# None of these are added with add_subdirectory: SOURCE_SUBDIR points at a folder without a
# CMakeLists.txt, so the sources are only downloaded and built by the targets below.
FetchContent_Declare(imgui
    GIT_REPOSITORY https://github.com/ocornut/imgui.git
    GIT_TAG v1.91.9
    GIT_SHALLOW TRUE
    SOURCE_SUBDIR none)
FetchContent_Declare(minhook
    GIT_REPOSITORY https://github.com/TsudaKageyu/minhook.git
    GIT_TAG v1.3.4
    GIT_SHALLOW TRUE
    SOURCE_SUBDIR none)
FetchContent_Declare(json
    URL https://github.com/nlohmann/json/releases/download/v3.11.3/json.tar.xz
    URL_HASH SHA256=d6c65aca6b1ed68e7a182f4757257b107ae403032760ed6ef121c9d55e81757d
    SOURCE_SUBDIR none)
FetchContent_MakeAvailable(imgui minhook json)

add_library(minhook STATIC
    ${minhook_SOURCE_DIR}/src/buffer.c
    ${minhook_SOURCE_DIR}/src/hook.c
    ${minhook_SOURCE_DIR}/src/trampoline.c
    ${minhook_SOURCE_DIR}/src/hde/hde64.c)
target_include_directories(minhook PUBLIC ${minhook_SOURCE_DIR}/include)

add_library(imgui STATIC
    ${imgui_SOURCE_DIR}/imgui.cpp
    ${imgui_SOURCE_DIR}/imgui_draw.cpp
    ${imgui_SOURCE_DIR}/imgui_tables.cpp
    ${imgui_SOURCE_DIR}/imgui_widgets.cpp
    ${imgui_SOURCE_DIR}/backends/imgui_impl_dx11.cpp
    ${imgui_SOURCE_DIR}/backends/imgui_impl_dx12.cpp)
target_include_directories(imgui PUBLIC ${imgui_SOURCE_DIR} ${imgui_SOURCE_DIR}/backends)
target_compile_definitions(imgui PUBLIC IMGUI_DEFINE_MATH_OPERATORS)

# The fonts are shared with the launcher so both look the same.
set(ERASE_FONT_DIR "${CMAKE_CURRENT_SOURCE_DIR}/../src/EraseLauncher/Assets/Fonts")
configure_file(src/Resources.rc.in ${CMAKE_CURRENT_BINARY_DIR}/Resources.rc @ONLY)

add_library(EraseClient SHARED
    src/DllMain.cpp
    src/Client.cpp
    src/core/Config.cpp
    src/core/Game.cpp
    src/core/Log.cpp
    src/hooks/Hooks.cpp
    src/input/Input.cpp
    src/render/Dx11Backend.cpp
    src/render/Dx12Backend.cpp
    src/render/Render.cpp
    src/gui/Gui.cpp
    src/gui/Theme.cpp
    src/modules/Fps.cpp
    src/modules/Module.cpp
    src/modules/Modules.cpp
    ${CMAKE_CURRENT_BINARY_DIR}/Resources.rc)

target_include_directories(EraseClient PRIVATE src ${json_SOURCE_DIR}/include)
target_compile_definitions(EraseClient PRIVATE
    ERASE_CLIENT_VERSION="${PROJECT_VERSION}"
    WIN32_LEAN_AND_MEAN
    NOMINMAX
    UNICODE
    _UNICODE)
target_compile_options(EraseClient PRIVATE /W4 /permissive- /utf-8 /EHsc /Zc:__cplusplus /bigobj)
target_link_options(EraseClient PRIVATE /DEBUG /OPT:REF /OPT:ICF)
target_link_libraries(EraseClient PRIVATE imgui minhook d3d11 d3d12 dxgi windowsapp)
