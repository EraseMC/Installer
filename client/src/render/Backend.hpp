#pragma once

#include <d3d12.h>
#include <dxgi1_4.h>

#include <memory>

#include <imgui.h>

namespace erase::render
{
using ExecuteCommandListsFn = void(STDMETHODCALLTYPE*)(ID3D12CommandQueue*, UINT, ID3D12CommandList* const*);

/// Draws ImGui into the game's swap chain with the graphics API the game uses.
class Backend
{
public:
    virtual ~Backend() = default;

    virtual bool initialize(IDXGISwapChain* swapChain) = 0;
    virtual void newFrame() = 0;
    virtual void render(IDXGISwapChain* swapChain, ImDrawData* drawData) = 0;
    /// Drops every reference to the back buffers, which ResizeBuffers requires.
    virtual void releaseTargets() = 0;
    virtual void shutdown() = 0;
};

std::unique_ptr<Backend> createDx11();
/// D3D12 work has to be submitted on the queue the game presents with; `execute` is the unhooked
/// ExecuteCommandLists, so the client's own submissions are not mistaken for the game's.
std::unique_ptr<Backend> createDx12(ID3D12CommandQueue* queue, ExecuteCommandListsFn execute);
}
