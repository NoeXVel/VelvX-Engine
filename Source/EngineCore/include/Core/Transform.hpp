#pragma once
/**
 * @file Transform.hpp
 * @brief Componente de transformação espacial (posição, rotação, escala) da VelvX Engine.
 */

#include <glm/glm.hpp>
#include <glm/gtc/quaternion.hpp>

namespace VelvX {

/**
 * @brief Transformação espacial de um objeto ou cena.
 *
 * Cada cena possui um Transform "parent" vazio (@c Scene::parentTransform) e
 * cada GameObject possui o seu próprio. O campo @c parent permite hierarquias:
 * a matriz mundial é o produto das matrizes locais ao longo da cadeia de pais.
 */
struct Transform {
    glm::vec3 position{0.0f, 0.0f, 0.0f}; ///< Posição local em unidades de mundo.
    glm::quat rotation{1.0f, 0.0f, 0.0f, 0.0f}; ///< Rotação local como quaternion unitário.
    glm::vec3 scale{1.0f, 1.0f, 1.0f}; ///< Escala local por eixo.
    Transform* parent = nullptr; ///< Transform pai (hierarquia); nulo = raiz.

    /** @brief Define a posição local. */
    void setPosition(const glm::vec3& p) { position = p; }
    /** @brief Define a rotação local. */
    void setRotation(const glm::quat& q) { rotation = q; }
    /** @brief Define a escala local. */
    void setScale(const glm::vec3& s) { scale = s; }

    /** @brief Obtém a posição local. */
    const glm::vec3& getPosition() const { return position; }
    /** @brief Obtém a rotação local. */
    const glm::quat& getRotation() const { return rotation; }
    /** @brief Obtém a escala local. */
    const glm::vec3& getScale() const { return scale; }

    /** @brief Matriz local T * R * S. */
    glm::mat4 localMatrix() const;
    /** @brief Matriz mundial, acumulando todos os Transform pais. */
    glm::mat4 worldMatrix() const;
};

} // namespace VelvX