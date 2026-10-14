#include "render/Backend.hpp"

#include <d3d11.h>
#include <wrl/client.h>

#include <imgui_impl_dx11.h>

using Microsoft::WRL::ComPtr;

namespace erase::render
{
namespace
{
class Dx11Backend final : public Backend
{
public:
    bool initialize(IDXGISwapChain* swapChain) override
    {
        if (FAILED(swapChain->GetDevice(IID_PPV_ARGS(&m_device))))
        {
            return false;
        }

        m_device->GetImmediateContext(&m_context);
        m_imgui = ImGui_ImplDX11_Init(m_device.Get(), m_context.Get());
        return m_imgui;
    }

    void newFrame() override
    {
        ImGui_ImplDX11_NewFrame();
    }

    void render(IDXGISwapChain* swapChain, ImDrawData* drawData) override
    {
        if (!m_target)
        {
            ComPtr<ID3D11Texture2D> buffer;
            if (FAILED(swapChain->GetBuffer(0, IID_PPV_ARGS(&buffer))) ||
                FAILED(m_device->CreateRenderTargetView(buffer.Get(), nullptr, &m_target)))
            {
                return;
            }
        }

        // The ImGui backend restores pipeline state but not the bound targets, so those are put back here.
        ID3D11RenderTargetView* previousTargets[D3D11_SIMULTANEOUS_RENDER_TARGET_COUNT]{};
        ComPtr<ID3D11DepthStencilView> previousDepth;
        m_context->OMGetRenderTargets(D3D11_SIMULTANEOUS_RENDER_TARGET_COUNT, previousTargets, &previousDepth);

        m_context->OMSetRenderTargets(1, m_target.GetAddressOf(), nullptr);
        ImGui_ImplDX11_RenderDrawData(drawData);

        m_context->OMSetRenderTargets(D3D11_SIMULTANEOUS_RENDER_TARGET_COUNT, previousTargets, previousDepth.Get());
        for (auto* target : previousTargets)
        {
            if (target)
            {
                target->Release();
            }
        }
    }

    void releaseTargets() override
    {
        m_target.Reset();
    }

    void shutdown() override
    {
        if (m_imgui)
        {
            ImGui_ImplDX11_Shutdown();
            m_imgui = false;
        }
        m_target.Reset();
        m_context.Reset();
        m_device.Reset();
    }

private:
    ComPtr<ID3D11Device> m_device;
    ComPtr<ID3D11DeviceContext> m_context;
    ComPtr<ID3D11RenderTargetView> m_target;
    bool m_imgui = false;
};
}

std::unique_ptr<Backend> createDx11()
{
    return std::make_unique<Dx11Backend>();
}
}
