#include "render/Render.hpp"

#include <corewindow.h>
#include <d3d11.h>
#include <d3d12.h>
#include <dxgi1_4.h>
#include <wrl/client.h>

#include <algorithm>
#include <array>
#include <atomic>
#include <memory>
#include <vector>

#include <imgui.h>

#include "core/Log.hpp"
#include "gui/Gui.hpp"
#include "hooks/Hooks.hpp"
#include "input/Input.hpp"
#include "render/Backend.hpp"

using Microsoft::WRL::ComPtr;

namespace erase::render
{
namespace
{
using PresentFn = HRESULT(STDMETHODCALLTYPE*)(IDXGISwapChain*, UINT, UINT);
using Present1Fn = HRESULT(STDMETHODCALLTYPE*)(IDXGISwapChain1*, UINT, UINT, const DXGI_PRESENT_PARAMETERS*);
using ResizeBuffersFn = HRESULT(STDMETHODCALLTYPE*)(IDXGISwapChain*, UINT, UINT, UINT, DXGI_FORMAT, UINT);
using ResizeBuffers1Fn = HRESULT(STDMETHODCALLTYPE*)(IDXGISwapChain3*, UINT, UINT, UINT, DXGI_FORMAT, UINT, const UINT*, IUnknown* const*);

// Vtable slots, counted from IUnknown through IDXGISwapChain3 and ID3D12CommandQueue.
constexpr std::size_t PresentSlot = 8;
constexpr std::size_t ResizeBuffersSlot = 13;
constexpr std::size_t Present1Slot = 22;
constexpr std::size_t ResizeBuffers1Slot = 39;
constexpr std::size_t ExecuteCommandListsSlot = 10;

// D3D11 and D3D12 swap chains can be different classes, so each gets its own set of trampolines.
constexpr std::size_t MaxImplementations = 2;

struct SwapChainMethods
{
    void* present = nullptr;
    void* resizeBuffers = nullptr;
    void* present1 = nullptr;
    void* resizeBuffers1 = nullptr;
};

std::array<PresentFn, MaxImplementations> g_present{};
std::array<Present1Fn, MaxImplementations> g_present1{};
std::array<ResizeBuffersFn, MaxImplementations> g_resizeBuffers{};
std::array<ResizeBuffers1Fn, MaxImplementations> g_resizeBuffers1{};
ExecuteCommandListsFn g_executeCommandLists = nullptr;

std::atomic<ID3D12CommandQueue*> g_queue = nullptr;
std::atomic<HWND> g_window = nullptr;
std::atomic<bool> g_stopRequested = false;
std::atomic<bool> g_stopped = false;
HANDLE g_stoppedEvent = nullptr;

// Only touched on the render thread.
std::unique_ptr<Backend> g_backend;
IDXGISwapChain* g_swapChain = nullptr;
IDXGISwapChain* g_failedSwapChain = nullptr;
LARGE_INTEGER g_lastFrame{};
LARGE_INTEGER g_frequency{};
thread_local bool t_inPresent = false;

void** vtableOf(void* object)
{
    return *static_cast<void***>(object);
}

SwapChainMethods methodsOf(IDXGISwapChain* swapChain)
{
    auto** vtable = vtableOf(swapChain);
    SwapChainMethods methods{ vtable[PresentSlot], vtable[ResizeBuffersSlot] };

    ComPtr<IDXGISwapChain3> swapChain3;
    if (SUCCEEDED(swapChain->QueryInterface(IID_PPV_ARGS(&swapChain3))))
    {
        methods.present1 = vtable[Present1Slot];
        methods.resizeBuffers1 = vtable[ResizeBuffers1Slot];
    }
    return methods;
}

/// A hidden window for the throwaway swap chains whose vtables point at the real DXGI code.
class DummyWindow
{
public:
    DummyWindow()
    {
        WNDCLASSEXW windowClass{};
        windowClass.cbSize = sizeof(windowClass);
        windowClass.lpfnWndProc = DefWindowProcW;
        windowClass.hInstance = GetModuleHandleW(nullptr);
        windowClass.lpszClassName = ClassName;
        RegisterClassExW(&windowClass);
        m_window = CreateWindowExW(0, ClassName, L"", WS_OVERLAPPEDWINDOW, 0, 0, 64, 64, nullptr, nullptr, windowClass.hInstance, nullptr);
    }

    ~DummyWindow()
    {
        if (m_window)
        {
            DestroyWindow(m_window);
        }
        UnregisterClassW(ClassName, GetModuleHandleW(nullptr));
    }

    HWND get() const { return m_window; }

private:
    static constexpr const wchar_t* ClassName = L"EraseClientDummyWindow";
    HWND m_window = nullptr;
};

bool readD3D11(HWND window, std::vector<SwapChainMethods>& methods)
{
    DXGI_SWAP_CHAIN_DESC desc{};
    desc.BufferCount = 1;
    desc.BufferDesc.Format = DXGI_FORMAT_R8G8B8A8_UNORM;
    desc.BufferUsage = DXGI_USAGE_RENDER_TARGET_OUTPUT;
    desc.OutputWindow = window;
    desc.SampleDesc.Count = 1;
    desc.Windowed = TRUE;
    desc.SwapEffect = DXGI_SWAP_EFFECT_DISCARD;

    ComPtr<IDXGISwapChain> swapChain;
    ComPtr<ID3D11Device> device;
    ComPtr<ID3D11DeviceContext> context;
    for (const auto driver : { D3D_DRIVER_TYPE_HARDWARE, D3D_DRIVER_TYPE_WARP })
    {
        if (SUCCEEDED(D3D11CreateDeviceAndSwapChain(nullptr, driver, nullptr, 0, nullptr, 0, D3D11_SDK_VERSION,
                &desc, &swapChain, &device, nullptr, &context)))
        {
            methods.push_back(methodsOf(swapChain.Get()));
            return true;
        }
    }

    log::warn("Could not create a D3D11 swap chain to locate Present.");
    return false;
}

bool readD3D12(HWND window, std::vector<SwapChainMethods>& methods)
{
    ComPtr<ID3D12Device> device;
    if (FAILED(D3D12CreateDevice(nullptr, D3D_FEATURE_LEVEL_11_0, IID_PPV_ARGS(&device))))
    {
        log::info("D3D12 is unavailable; only D3D11 will be drawn on.");
        return false;
    }

    D3D12_COMMAND_QUEUE_DESC queueDesc{};
    queueDesc.Type = D3D12_COMMAND_LIST_TYPE_DIRECT;
    ComPtr<ID3D12CommandQueue> queue;
    ComPtr<IDXGIFactory2> factory;
    if (FAILED(device->CreateCommandQueue(&queueDesc, IID_PPV_ARGS(&queue))) ||
        FAILED(CreateDXGIFactory1(IID_PPV_ARGS(&factory))))
    {
        return false;
    }

    g_executeCommandLists = reinterpret_cast<ExecuteCommandListsFn>(vtableOf(queue.Get())[ExecuteCommandListsSlot]);

    DXGI_SWAP_CHAIN_DESC1 desc{};
    desc.Width = 64;
    desc.Height = 64;
    desc.Format = DXGI_FORMAT_R8G8B8A8_UNORM;
    desc.SampleDesc.Count = 1;
    desc.BufferUsage = DXGI_USAGE_RENDER_TARGET_OUTPUT;
    desc.BufferCount = 2;
    desc.SwapEffect = DXGI_SWAP_EFFECT_FLIP_DISCARD;

    ComPtr<IDXGISwapChain1> swapChain;
    if (FAILED(factory->CreateSwapChainForHwnd(queue.Get(), window, &desc, nullptr, nullptr, &swapChain)))
    {
        log::warn("Could not create a D3D12 swap chain to locate Present.");
        return true;
    }

    methods.push_back(methodsOf(swapChain.Get()));
    return true;
}

HWND windowOf(IDXGISwapChain* swapChain)
{
    ComPtr<IDXGISwapChain1> swapChain1;
    if (SUCCEEDED(swapChain->QueryInterface(IID_PPV_ARGS(&swapChain1))))
    {
        HWND window = nullptr;
        if (SUCCEEDED(swapChain1->GetHwnd(&window)) && window)
        {
            return window;
        }

        // UWP builds present into a CoreWindow rather than an HWND.
        ComPtr<ICoreWindowInterop> interop;
        if (SUCCEEDED(swapChain1->GetCoreWindow(IID_PPV_ARGS(&interop))) &&
            SUCCEEDED(interop->get_WindowHandle(&window)))
        {
            return window;
        }
    }

    DXGI_SWAP_CHAIN_DESC desc{};
    return SUCCEEDED(swapChain->GetDesc(&desc)) ? desc.OutputWindow : nullptr;
}

/// Finds the queue a D3D12 swap chain presents on. DXGI hands it out as the swap chain's "device";
/// if that fails, the first direct queue the game submitted work to is used.
ComPtr<ID3D12CommandQueue> queueOf(IDXGISwapChain* swapChain)
{
    ComPtr<ID3D12CommandQueue> queue;
    if (SUCCEEDED(swapChain->GetDevice(IID_PPV_ARGS(&queue))))
    {
        return queue;
    }

    queue = g_queue.load();
    return queue;
}

void releaseCapturedQueue()
{
    if (auto* queue = g_queue.exchange(nullptr))
    {
        queue->Release();
    }
}

void teardown()
{
    if (g_backend)
    {
        g_backend->shutdown();
        g_backend.reset();
    }

    if (ImGui::GetCurrentContext())
    {
        ImGui::DestroyContext();
    }

    g_swapChain = nullptr;
}

bool attach(IDXGISwapChain* swapChain)
{
    if (swapChain == g_failedSwapChain)
    {
        return false;
    }

    // A new swap chain (after a fullscreen switch, for example) replaces the old one; ImGui is kept.
    if (g_backend)
    {
        g_backend->shutdown();
        g_backend.reset();
        g_swapChain = nullptr;
    }

    std::unique_ptr<Backend> backend;
    const char* api = "D3D11";
    ComPtr<ID3D11Device> d3d11;
    if (SUCCEEDED(swapChain->GetDevice(IID_PPV_ARGS(&d3d11))))
    {
        backend = createDx11();
    }
    else
    {
        const auto queue = queueOf(swapChain);
        if (!queue || !g_executeCommandLists)
        {
            // The game has not submitted any work yet; try again on the next frame.
            return false;
        }
        backend = createDx12(queue.Get(), g_executeCommandLists);
        api = "D3D12";
    }

    const HWND window = windowOf(swapChain);
    if (!window)
    {
        log::error("Could not find the window the game presents to.");
        g_failedSwapChain = swapChain;
        return false;
    }

    if (!ImGui::GetCurrentContext())
    {
        ImGui::CreateContext();
        auto& io = ImGui::GetIO();
        io.IniFilename = nullptr;
        io.LogFilename = nullptr;
        io.ConfigFlags |= ImGuiConfigFlags_NoMouseCursorChange;
        io.BackendPlatformName = "erase_win32";
        gui::setup();
    }

    if (!backend->initialize(swapChain))
    {
        log::error("Could not start the {} renderer.", api);
        backend->shutdown();
        g_failedSwapChain = swapChain;
        return false;
    }

    g_backend = std::move(backend);
    g_swapChain = swapChain;
    g_window = window;
    QueryPerformanceFrequency(&g_frequency);
    QueryPerformanceCounter(&g_lastFrame);
    input::install(window);
    log::info("Drawing with {} on window {}.", api, static_cast<void*>(window));
    return true;
}

ImGuiKey toImGuiKey(int key)
{
    if (key >= '0' && key <= '9')
    {
        return static_cast<ImGuiKey>(ImGuiKey_0 + (key - '0'));
    }
    if (key >= 'A' && key <= 'Z')
    {
        return static_cast<ImGuiKey>(ImGuiKey_A + (key - 'A'));
    }

    switch (key)
    {
    case VK_TAB: return ImGuiKey_Tab;
    case VK_LEFT: return ImGuiKey_LeftArrow;
    case VK_RIGHT: return ImGuiKey_RightArrow;
    case VK_UP: return ImGuiKey_UpArrow;
    case VK_DOWN: return ImGuiKey_DownArrow;
    case VK_PRIOR: return ImGuiKey_PageUp;
    case VK_NEXT: return ImGuiKey_PageDown;
    case VK_HOME: return ImGuiKey_Home;
    case VK_END: return ImGuiKey_End;
    case VK_INSERT: return ImGuiKey_Insert;
    case VK_DELETE: return ImGuiKey_Delete;
    case VK_BACK: return ImGuiKey_Backspace;
    case VK_SPACE: return ImGuiKey_Space;
    case VK_RETURN: return ImGuiKey_Enter;
    case VK_ESCAPE: return ImGuiKey_Escape;
    case VK_LSHIFT: return ImGuiKey_LeftShift;
    case VK_RSHIFT: return ImGuiKey_RightShift;
    case VK_LCONTROL: return ImGuiKey_LeftCtrl;
    case VK_RCONTROL: return ImGuiKey_RightCtrl;
    case VK_LMENU: return ImGuiKey_LeftAlt;
    case VK_RMENU: return ImGuiKey_RightAlt;
    default: return ImGuiKey_None;
    }
}

void applyKey(ImGuiIO& io, int key, bool down)
{
    switch (key)
    {
    case VK_LSHIFT:
    case VK_RSHIFT:
        io.AddKeyEvent(ImGuiMod_Shift, down);
        break;
    case VK_LCONTROL:
    case VK_RCONTROL:
        io.AddKeyEvent(ImGuiMod_Ctrl, down);
        break;
    case VK_LMENU:
    case VK_RMENU:
        io.AddKeyEvent(ImGuiMod_Alt, down);
        break;
    default:
        break;
    }

    if (const auto imguiKey = toImGuiKey(key); imguiKey != ImGuiKey_None)
    {
        io.AddKeyEvent(imguiKey, down);
    }
}

void applyInput(ImGuiIO& io, float scaleX, float scaleY)
{
    for (const auto& event : input::drain())
    {
        if (event.kind == input::Event::Kind::Key && event.down)
        {
            gui::onKey(event.code);
        }

        // ImGui only sees input while the menu is open; otherwise it belongs to the game.
        if (!input::menuOpen())
        {
            continue;
        }

        switch (event.kind)
        {
        case input::Event::Kind::Key:
            applyKey(io, event.code, event.down);
            break;
        case input::Event::Kind::Char:
            io.AddInputCharacterUTF16(static_cast<ImWchar16>(event.code));
            break;
        case input::Event::Kind::MouseMove:
            io.AddMousePosEvent(event.x * scaleX, event.y * scaleY);
            break;
        case input::Event::Kind::MouseButton:
            io.AddMouseButtonEvent(event.code, event.down);
            break;
        case input::Event::Kind::MouseWheel:
            io.AddMouseWheelEvent(0.0f, event.y);
            break;
        }
    }
}

void drawFrame(IDXGISwapChain* swapChain)
{
    DXGI_SWAP_CHAIN_DESC desc{};
    if (FAILED(swapChain->GetDesc(&desc)) || desc.BufferDesc.Width == 0 || desc.BufferDesc.Height == 0)
    {
        return;
    }

    auto& io = ImGui::GetIO();
    io.DisplaySize = ImVec2(static_cast<float>(desc.BufferDesc.Width), static_cast<float>(desc.BufferDesc.Height));

    // Window messages are in client pixels, which differ from the back buffer when Windows scales the game.
    RECT client{};
    GetClientRect(g_window.load(), &client);
    const float scaleX = client.right > 0 ? io.DisplaySize.x / static_cast<float>(client.right) : 1.0f;
    const float scaleY = client.bottom > 0 ? io.DisplaySize.y / static_cast<float>(client.bottom) : 1.0f;

    LARGE_INTEGER now{};
    QueryPerformanceCounter(&now);
    io.DeltaTime = std::max(static_cast<float>(now.QuadPart - g_lastFrame.QuadPart) / static_cast<float>(g_frequency.QuadPart), 1.0f / 10000.0f);
    g_lastFrame = now;

    applyInput(io, scaleX, scaleY);
    io.MouseDrawCursor = input::menuOpen();

    g_backend->newFrame();
    ImGui::NewFrame();
    gui::draw();
    ImGui::Render();
    g_backend->render(swapChain, ImGui::GetDrawData());
}

void onPresent(IDXGISwapChain* swapChain)
{
    if (g_stopped.load() || t_inPresent)
    {
        return;
    }

    t_inPresent = true;
    if (g_stopRequested.load())
    {
        teardown();
        g_stopped = true;
        SetEvent(g_stoppedEvent);
    }
    else if ((g_backend && swapChain == g_swapChain) || attach(swapChain))
    {
        drawFrame(swapChain);
    }
    t_inPresent = false;
}

void onResize(IDXGISwapChain* swapChain)
{
    if (g_backend && swapChain == g_swapChain)
    {
        g_backend->releaseTargets();
    }
}

template <std::size_t Slot>
HRESULT STDMETHODCALLTYPE presentHook(IDXGISwapChain* swapChain, UINT syncInterval, UINT flags)
{
    if ((flags & DXGI_PRESENT_TEST) == 0)
    {
        onPresent(swapChain);
    }
    return g_present[Slot](swapChain, syncInterval, flags);
}

template <std::size_t Slot>
HRESULT STDMETHODCALLTYPE present1Hook(IDXGISwapChain1* swapChain, UINT syncInterval, UINT flags, const DXGI_PRESENT_PARAMETERS* parameters)
{
    if ((flags & DXGI_PRESENT_TEST) == 0)
    {
        onPresent(swapChain);
    }
    return g_present1[Slot](swapChain, syncInterval, flags, parameters);
}

template <std::size_t Slot>
HRESULT STDMETHODCALLTYPE resizeBuffersHook(IDXGISwapChain* swapChain, UINT count, UINT width, UINT height, DXGI_FORMAT format, UINT flags)
{
    onResize(swapChain);
    return g_resizeBuffers[Slot](swapChain, count, width, height, format, flags);
}

template <std::size_t Slot>
HRESULT STDMETHODCALLTYPE resizeBuffers1Hook(IDXGISwapChain3* swapChain, UINT count, UINT width, UINT height, DXGI_FORMAT format, UINT flags,
    const UINT* nodeMasks, IUnknown* const* queues)
{
    onResize(swapChain);
    return g_resizeBuffers1[Slot](swapChain, count, width, height, format, flags, nodeMasks, queues);
}

void STDMETHODCALLTYPE executeCommandListsHook(ID3D12CommandQueue* queue, UINT count, ID3D12CommandList* const* lists)
{
    if (!g_queue.load(std::memory_order_relaxed) && queue->GetDesc().Type == D3D12_COMMAND_LIST_TYPE_DIRECT)
    {
        ID3D12CommandQueue* expected = nullptr;
        queue->AddRef();
        if (!g_queue.compare_exchange_strong(expected, queue))
        {
            queue->Release();
        }
    }
    g_executeCommandLists(queue, count, lists);
}

template <std::size_t Slot>
void hookImplementation(const SwapChainMethods& methods)
{
    hooks::create(methods.present, &presentHook<Slot>, &g_present[Slot], "IDXGISwapChain::Present");
    hooks::create(methods.resizeBuffers, &resizeBuffersHook<Slot>, &g_resizeBuffers[Slot], "IDXGISwapChain::ResizeBuffers");
    if (methods.present1)
    {
        hooks::create(methods.present1, &present1Hook<Slot>, &g_present1[Slot], "IDXGISwapChain1::Present1");
    }
    if (methods.resizeBuffers1)
    {
        hooks::create(methods.resizeBuffers1, &resizeBuffers1Hook<Slot>, &g_resizeBuffers1[Slot], "IDXGISwapChain3::ResizeBuffers1");
    }
}
}

bool install()
{
    g_stoppedEvent = CreateEventW(nullptr, TRUE, FALSE, nullptr);

    std::vector<SwapChainMethods> found;
    {
        DummyWindow window;
        if (!window.get())
        {
            log::error("Could not create a window to locate the DXGI functions (error {}).", GetLastError());
            return false;
        }

        readD3D11(window.get(), found);
        readD3D12(window.get(), found);
    }

    // Usually both APIs share one DXGI implementation; it must only be hooked once.
    std::vector<SwapChainMethods> unique;
    for (const auto& methods : found)
    {
        const bool seen = std::any_of(unique.begin(), unique.end(), [&](const SwapChainMethods& other) { return other.present == methods.present; });
        if (!seen && unique.size() < MaxImplementations)
        {
            unique.push_back(methods);
        }
    }

    if (unique.empty())
    {
        log::error("No DXGI swap chain could be created, so nothing can be drawn.");
        return false;
    }

    hookImplementation<0>(unique[0]);
    if (unique.size() > 1)
    {
        hookImplementation<1>(unique[1]);
    }

    if (g_executeCommandLists)
    {
        hooks::create(reinterpret_cast<void*>(g_executeCommandLists), &executeCommandListsHook, &g_executeCommandLists, "ID3D12CommandQueue::ExecuteCommandLists");
    }

    return true;
}

void uninstall()
{
    input::setMenuOpen(false);
    if (g_stoppedEvent && !g_stopped.load())
    {
        g_stopRequested = true;
        // If the game is minimised it may not present; ImGui is then left in place rather than
        // torn down from this thread while the render thread could still be using it.
        if (WaitForSingleObject(g_stoppedEvent, 2000) != WAIT_OBJECT_0)
        {
            log::warn("The game did not present a frame in time; the renderer was not shut down cleanly.");
        }
    }

    input::uninstall();
    releaseCapturedQueue();
    if (g_stoppedEvent)
    {
        CloseHandle(g_stoppedEvent);
        g_stoppedEvent = nullptr;
    }
}

HWND window()
{
    return g_window.load();
}
}
