#pragma once
/**
 * @file Camera.hpp
 * @brief Componente Camera: projeção perspectiva/orthográfica derivada do
 *        Transform do GameObject dono. Suporta múltiplas câmeras por cena.
 */

#include "Core/Component.hpp"
#include "Core/Inspector.hpp"
#include <glm/glm.hpp>
#include <memory>

namespace VelvX {

/**
 * @brief Câmera independente: um GameObject com este componente renderiza a
 * cena a partir do seu Transform (posição + rotação). A câmera olha para o
 * eixo -Z local; @c forward() devolve essa direção em mundo.
 */
class Camera : public Component {
public:
    /** @brief Tipo de projeção. */
    enum class Projection { Perspective, Orthographic };

    UNLOCKED float fov = 45.0f; ///< Campo de visão vertical (graus, perspectiva).
    UNLOCKED float nearPlane = 0.1f; ///< Plano próximo.
    UNLOCKED float farPlane = 500.0f; ///< Plano distante.
    UNLOCKED float orthoHeight = 12.0f; ///< Altura do mundo visível (orthográfica).
    UNLOCKED bool active = true; ///< Se true, pode ser a câmera ativa da cena.

    Projection projection = Projection::Perspective; ///< Projeção usada.

    /** @brief Câmera vinculada ao dono (registra campos UNLOCKED). */
    explicit Camera(GameObject* owner);

    /** @brief Matriz de view (inversa da transformação mundial do dono). */
    glm::mat4 viewMatrix() const;

    /** @brief Matriz de projeção para o aspect dado. */
    glm::mat4 projectionMatrix(float aspect) const;

    /** @brief Posição mundial da câmera. */
    glm::vec3 worldPosition() const;

    /** @brief Direção para a qual a câmera olha (mundo, normalizada). */
    glm::vec3 forward() const;

    /** @brief Orienta a câmera para olhar de sua posição até @p target. */
    void lookAt(const glm::vec3& target);

    /** @brief Cópia profunda. */
    std::unique_ptr<Component> clone(GameObject* newOwner) const override;

    /** @brief Nome do tipo. */
    const char* typeName() const override { return "Camera"; }
};

} // namespace VelvX