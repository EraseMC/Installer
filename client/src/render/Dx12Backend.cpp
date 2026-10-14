#include "render/Backend.hpp"

#include <wrl/client.h>

#include <vector>

#include <imgui_impl_dx12.h>

#include "core/Log.hpp"

using Microsoft::WRL::ComPtr;

namespace erase::render
{
namespace
{
constexpr DWORD FenceTimeoutMs = 1000;

class Dx12Backend final : public Backend
{
public:
    Dx12Backend(ID3D12CommandQueue* queue, ExecuteCommandListsFn execute) : m_queue(queue), m_execute(execute)
    {
    }

    bool initialize(IDXGISwapChain* swapChain) override
    {
        if (FAILED(swapChain->QueryInterface(IID_PPV_ARGS(&m_swapChain))) ||
            FAILED(m_queue->GetDevice(IID_PPV_ARGS(&m_device))))
        {
            return false;
        }

        DXGI_SWAP_CHAIN_DESC desc{};
        if (FAILED(swapChain->GetDesc(&desc)))
        {
            return false;
        }
        m_format = desc.BufferDesc.Format;

        D3D12_DESCRIPTOR_HEAP_DESC srvDesc{};
        srvDesc.Type = D3D12_DESCRIPTOR_HEAP_TYPE_CBV_SRV_UAV;
        srvDesc.NumDescriptors = 1;
        srvDesc.Flags = D3D12_DESCRIPTOR_HEAP_FLAG_SHADER_VISIBLE;
        if (FAILED(m_device->CreateDescriptorHeap(&srvDesc, IID_PPV_ARGS(&m_srvHeap))) ||
            FAILED(m_device->CreateFence(0, D3D12_FENCE_FLAG_NONE, IID_PPV_ARGS(&m_fence))))
        {
            return false;
        }

        m_fenceEvent = CreateEventW(nullptr, FALSE, FALSE, nullptr);
        if (!m_fenceEvent || !createTargets())
        {
            return false;
        }

        m_imgui = ImGui_ImplDX12_Init(m_device.Get(), static_cast<int>(m_frames.size()), m_format, m_srvHeap.Get(),
            m_srvHeap->GetCPUDescriptorHandleForHeapStart(), m_srvHeap->GetGPUDescriptorHandleForHeapStart());
        return m_imgui;
    }

    void newFrame() override
    {
        ImGui_ImplDX12_NewFrame();
    }

    void render(IDXGISwapChain*, ImDrawData* drawData) override
    {
        if (m_frames.empty() || !m_frames.front().buffer)
        {
            if (!createTargets())
            {
                return;
            }
        }

        const UINT index = m_swapChain->GetCurrentBackBufferIndex();
        if (index >= m_frames.size())
        {
            return;
        }

        auto& frame = m_frames[index];
        // The allocator may still be in use by the GPU from the last time this buffer was drawn.
        waitFor(frame.fenceValue);
        if (FAILED(frame.allocator->Reset()) || FAILED(m_list->Reset(frame.allocator.Get(), nullptr)))
        {
            return;
        }

        D3D12_RESOURCE_BARRIER barrier{};
        barrier.Type = D3D12_RESOURCE_BARRIER_TYPE_TRANSITION;
        barrier.Transition.pResource = frame.buffer.Get();
        barrier.Transition.Subresource = D3D12_RESOURCE_BARRIER_ALL_SUBRESOURCES;
        barrier.Transition.StateBefore = D3D12_RESOURCE_STATE_PRESENT;
        barrier.Transition.StateAfter = D3D12_RESOURCE_STATE_RENDER_TARGET;
        m_list->ResourceBarrier(1, &barrier);

        m_list->OMSetRenderTargets(1, &frame.target, FALSE, nullptr);
        ID3D12DescriptorHeap* heaps[] = { m_srvHeap.Get() };
        m_list->SetDescriptorHeaps(1, heaps);
        ImGui_ImplDX12_RenderDrawData(drawData, m_list.Get());

        barrier.Transition.StateBefore = D3D12_RESOURCE_STATE_RENDER_TARGET;
        barrier.Transition.StateAfter = D3D12_RESOURCE_STATE_PRESENT;
        m_list->ResourceBarrier(1, &barrier);
        if (FAILED(m_list->Close()))
        {
            return;
        }

        ID3D12CommandList* lists[] = { m_list.Get() };
        m_execute(m_queue.Get(), 1, lists);
        if (SUCCEEDED(m_queue->Signal(m_fence.Get(), ++m_fenceValue)))
        {
            frame.fenceValue = m_fenceValue;
        }
    }

    void releaseTargets() override
    {
        waitFor(m_fenceValue);
        for (auto& frame : m_frames)
        {
            frame.buffer.Reset();
        }
    }

    void shutdown() override
    {
        waitFor(m_fenceValue);
        if (m_imgui)
        {
            ImGui_ImplDX12_Shutdown();
            m_imgui = false;
        }
        m_frames.clear();
        m_list.Reset();
        m_rtvHeap.Reset();
        m_srvHeap.Reset();
        m_fence.Reset();
        if (m_fenceEvent)
        {
            CloseHandle(m_fenceEvent);
            m_fenceEvent = nullptr;
        }
        m_device.Reset();
        m_swapChain.Reset();
        m_queue.Reset();
    }

private:
    struct Frame
    {
        ComPtr<ID3D12CommandAllocator> allocator;
        ComPtr<ID3D12Resource> buffer;
        D3D12_CPU_DESCRIPTOR_HANDLE target{};
        UINT64 fenceValue = 0;
    };

    /// (Re)creates a render target per back buffer. The buffer count can change on ResizeBuffers.
    bool createTargets()
    {
        DXGI_SWAP_CHAIN_DESC desc{};
        if (FAILED(m_swapChain->GetDesc(&desc)) || desc.BufferCount == 0)
        {
            return false;
        }

        if (m_frames.size() != desc.BufferCount)
        {
            waitFor(m_fenceValue);
            m_frames.clear();
            m_frames.resize(desc.BufferCount);

            D3D12_DESCRIPTOR_HEAP_DESC rtvDesc{};
            rtvDesc.Type = D3D12_DESCRIPTOR_HEAP_TYPE_RTV;
            rtvDesc.NumDescriptors = desc.BufferCount;
            m_rtvHeap.Reset();
            if (FAILED(m_device->CreateDescriptorHeap(&rtvDesc, IID_PPV_ARGS(&m_rtvHeap))))
            {
                m_frames.clear();
                return false;
            }

            for (auto& frame : m_frames)
            {
                if (FAILED(m_device->CreateCommandAllocator(D3D12_COMMAND_LIST_TYPE_DIRECT, IID_PPV_ARGS(&frame.allocator))))
                {
                    m_frames.clear();
                    return false;
                }
            }

            if (!m_list)
            {
                if (FAILED(m_device->CreateCommandList(0, D3D12_COMMAND_LIST_TYPE_DIRECT, m_frames.front().allocator.Get(), nullptr, IID_PPV_ARGS(&m_list))) ||
                    FAILED(m_list->Close()))
                {
                    m_frames.clear();
                    return false;
                }
            }
        }

        const UINT step = m_device->GetDescriptorHandleIncrementSize(D3D12_DESCRIPTOR_HEAP_TYPE_RTV);
        auto handle = m_rtvHeap->GetCPUDescriptorHandleForHeapStart();
        for (UINT i = 0; i < desc.BufferCount; ++i)
        {
            auto& frame = m_frames[i];
            if (FAILED(m_swapChain->GetBuffer(i, IID_PPV_ARGS(&frame.buffer))))
            {
                log::error("Could not get D3D12 back buffer {}.", i);
                return false;
            }

            frame.target = handle;
            m_device->CreateRenderTargetView(frame.buffer.Get(), nullptr, handle);
            handle.ptr += step;
        }

        return true;
    }

    void waitFor(UINT64 value)
    {
        if (m_fence && value != 0 && m_fence->GetCompletedValue() < value &&
            SUCCEEDED(m_fence->SetEventOnCompletion(value, m_fenceEvent)))
        {
            WaitForSingleObject(m_fenceEvent, FenceTimeoutMs);
        }
    }

    ComPtr<ID3D12CommandQueue> m_queue;
    ExecuteCommandListsFn m_execute;
    ComPtr<IDXGISwapChain3> m_swapChain;
    ComPtr<ID3D12Device> m_device;
    ComPtr<ID3D12DescriptorHeap> m_rtvHeap;
    ComPtr<ID3D12DescriptorHeap> m_srvHeap;
    ComPtr<ID3D12GraphicsCommandList> m_list;
    ComPtr<ID3D12Fence> m_fence;
    HANDLE m_fenceEvent = nullptr;
    UINT64 m_fenceValue = 0;
    DXGI_FORMAT m_format = DXGI_FORMAT_R8G8B8A8_UNORM;
    std::vector<Frame> m_frames;
    bool m_imgui = false;
};
}

std::unique_ptr<Backend> createDx12(ID3D12CommandQueue* queue, ExecuteCommandListsFn execute)
{
    return std::make_unique<Dx12Backend>(queue, execute);
}
}
