#pragma once
/**
 * @file Mesh.hpp
 * @brief Malha indexada (pos/normal/uv) desenhada com glDrawElements.
 */

#include <glm/glm.hpp>
#include <cstdint>
#include <vector>

namespace VelvX {

/**
 * @brief Vértice padrão da engine.
 */
struct Vertex {
    glm::vec3 position{0.0f}; ///< Posição local.
    glm::vec3 normal{0.0f, 1.0f, 0.0f}; ///< Normal.
    glm::vec2 uv{0.0f}; ///< Coordenada de textura.
};

/**
 * @brief Malha indexada em GPU (VAO/VBO/EBO).
 */
class Mesh {
public:
    unsigned int vao = 0; ///< Vertex Array Object.
    unsigned int vbo = 0; ///< Vertex Buffer Object.
    unsigned int ebo = 0; ///< Element Buffer Object.
    int indexCount = 0; ///< Número de índices.

    /** @brief Envia vértices/índices para a GPU. */
    void setup(const std::vector<Vertex>& vertices, const std::vector<uint32_t>& indices);

    /** @brief Reenvia apenas vértices (skinning em CPU). */
    void updateVertices(const std::vector<Vertex>& vertices);

    /** @brief glDrawElements com GL_TRIANGLES. */
    void draw() const;

    /** @brief Libera os buffers. */
    void destroy();

    /** @brief Cubo unitário centrado na origem. */
    static Mesh cube();

    /** @brief Plano XZ unitário (y=0, normal +Y). */
    static Mesh plane();

    /** @brief Quad XY unitário (z=0, normal +Z). */
    static Mesh quad();
};

} // namespace VelvX