#pragma once
/**
 * @file CollisionSystem.hpp
 * @brief Colisão AABB, triggers e raycast contra a cena ativa.
 */

#include <glm/glm.hpp>
#include <string>
#include <vector>

namespace VelvX {

class GameObject;

/**
 * @brief Sistema de colisão: caixas alinhadas aos eixos derivadas dos
 * Colliders (ou da escala dos MeshRenderers) da cena ativa.
 */
class CollisionSystem {
public:
    /** @brief Caixa de colisão de um objeto. */
    struct AABB {
        glm::vec3 min{0.0f}; ///< Canto mínimo mundial.
        glm::vec3 max{0.0f}; ///< Canto máximo mundial.
        GameObject* object = nullptr; ///< Objeto dono.
        bool trigger = false; ///< Caixa é trigger.
        int layer = 0; ///< Camada de colisão do dono.
    };

    /** @brief Reconstrói caixas e dispara hooks onTrigger3D de novos overlaps. */
    static void update();

    /** @brief true se as caixas dos objetos nomeados se sobrepõem neste frame. */
    static bool overlaps(const std::string& a, const std::string& b);

    /**
     * @brief Raycast contra todas as caixas (slab method).
     * @param origin Origem do raio.
     * @param dir Direção (normalizada internamente).
     * @param maxDist Distância máxima.
     * @param hitPoint Saída opcional do ponto de impacto.
     * @return Objeto atingido mais próximo, ou nullptr.
     */
    static GameObject* raycast(const glm::vec3& origin, const glm::vec3& dir,
                               float maxDist, glm::vec3* hitPoint = nullptr);

    /** @brief Caixas do frame atual. */
    static const std::vector<AABB>& boxes();
};

} // namespace VelvX