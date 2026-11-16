#pragma once
/**
 * @file RenderComponent.hpp
 * @brief Componente MeshRenderer: malha, cor, textura e shader de um objeto.
 */

#include "Core/Component.hpp"
#include "Core/Inspector.hpp"
#include <glm/glm.hpp>
#include <memory>
#include <string>

namespace VelvX {

/** @brief Malhas built-in disponíveis no Renderer. */
enum class MeshType {
    Cube,   ///< Cubo unitário 3D.
    Plane,  ///< Plano XZ unitário 3D.
    Quad,   ///< Quad XY unitário 3D.
    Sprite  ///< Sprite 2D em pixels de tela.
};

/**
 * @brief Componente de renderização de malha (built-in ou importada).
 */
class MeshRenderer : public Component {
public:
    UNLOCKED glm::vec3 color{1.0f, 1.0f, 1.0f}; ///< Cor base / tint da textura.
    UNLOCKED bool visible = true; ///< Visibilidade no render.

    MeshType mesh = MeshType::Cube; ///< Malha built-in usada.
    std::string texturePath; ///< Textura albedo opcional (Assets("...")).
    std::string shaderPath; ///< Shader de vértice alternativo (vazio = padrão).
    int importedModel = -1; ///< Índice do modelo importado (Assimp), -1 = built-in.
    int importedMesh = -1; ///< Índice da malha dentro do modelo importado.

    /** @brief Componente vinculado ao dono (registra campos UNLOCKED). */
    explicit MeshRenderer(GameObject* owner);

    /** @brief Cópia profunda dos dados de render. */
    std::unique_ptr<Component> clone(GameObject* newOwner) const override {
        auto c = std::make_unique<MeshRenderer>(newOwner);
        c->color = color;
        c->visible = visible;
        c->mesh = mesh;
        c->texturePath = texturePath;
        c->shaderPath = shaderPath;
        c->importedModel = importedModel;
        c->importedMesh = importedMesh;
        return c;
    }

    /** @brief Nome do tipo para Inspector/serialização. */
    const char* typeName() const override { return "MeshRenderer"; }
};

} // namespace VelvX