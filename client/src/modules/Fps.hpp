#pragma once

#include "modules/Module.hpp"

namespace erase::modules
{
class Fps final : public Module
{
public:
    Fps();

    void render() override;
};
}
