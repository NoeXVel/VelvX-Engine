/**
 * @file RenderComponent.cpp
 * @brief Registro dos campos UNLOCKED do MeshRenderer no Inspector.
 */

#include "Renderer/RenderComponent.hpp"
#include "Core/GameObject.hpp"

namespace VelvX {

MeshRenderer::MeshRenderer(GameObject* owner) : Component(owner) {
    const std::string& n = owner->name;
    Inspector::expose(n, "color", "glm::vec3", &color, this);
    Inspector::expose(n, "visible", "bool", &visible, this);
}

} // namespace VelvX