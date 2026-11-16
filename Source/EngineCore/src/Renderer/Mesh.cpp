/**
 * @file Mesh.cpp
 * @brief Buffers de malha e geometria built-in.
 */

#include "Renderer/Mesh.hpp"
#include <glad/gl.h>
#include <cstddef>

namespace VelvX {

void Mesh::setup(const std::vector<Vertex>& vertices, const std::vector<uint32_t>& indices) {
    indexCount = static_cast<int>(indices.size());
    glGenVertexArrays(1, &vao);
    glGenBuffers(1, &vbo);
    glGenBuffers(1, &ebo);
    glBindVertexArray(vao);
    glBindBuffer(GL_ARRAY_BUFFER, vbo);
    glBufferData(GL_ARRAY_BUFFER, static_cast<GLsizeiptr>(vertices.size() * sizeof(Vertex)),
                 vertices.data(), GL_DYNAMIC_DRAW);
    glBindBuffer(GL_ELEMENT_ARRAY_BUFFER, ebo);
    glBufferData(GL_ELEMENT_ARRAY_BUFFER, static_cast<GLsizeiptr>(indices.size() * sizeof(uint32_t)),
                 indices.data(), GL_STATIC_DRAW);
    glEnableVertexAttribArray(0);
    glVertexAttribPointer(0, 3, GL_FLOAT, GL_FALSE, sizeof(Vertex),
                          reinterpret_cast<void*>(offsetof(Vertex, position)));
    glEnableVertexAttribArray(1);
    glVertexAttribPointer(1, 3, GL_FLOAT, GL_FALSE, sizeof(Vertex),
                          reinterpret_cast<void*>(offsetof(Vertex, normal)));
    glEnableVertexAttribArray(2);
    glVertexAttribPointer(2, 2, GL_FLOAT, GL_FALSE, sizeof(Vertex),
                          reinterpret_cast<void*>(offsetof(Vertex, uv)));
    glBindVertexArray(0);
}

void Mesh::updateVertices(const std::vector<Vertex>& vertices) {
    glBindBuffer(GL_ARRAY_BUFFER, vbo);
    glBufferSubData(GL_ARRAY_BUFFER, 0, static_cast<GLsizeiptr>(vertices.size() * sizeof(Vertex)),
                    vertices.data());
    glBindBuffer(GL_ARRAY_BUFFER, 0);
}

void Mesh::draw() const {
    glBindVertexArray(vao);
    glDrawElements(GL_TRIANGLES, indexCount, GL_UNSIGNED_INT, nullptr);
    glBindVertexArray(0);
}

void Mesh::destroy() {
    if (vao) glDeleteVertexArrays(1, &vao);
    if (vbo) glDeleteBuffers(1, &vbo);
    if (ebo) glDeleteBuffers(1, &ebo);
    vao = vbo = ebo = 0;
    indexCount = 0;
}

Mesh Mesh::cube() {
    const glm::vec3 p[8] = {
        {-0.5f, -0.5f, -0.5f}, {0.5f, -0.5f, -0.5f}, {0.5f, 0.5f, -0.5f}, {-0.5f, 0.5f, -0.5f},
        {-0.5f, -0.5f, 0.5f}, {0.5f, -0.5f, 0.5f}, {0.5f, 0.5f, 0.5f}, {-0.5f, 0.5f, 0.5f}
    };
    struct Face { int a, b, c, d; glm::vec3 n; };
    const Face faces[6] = {
        {4, 5, 6, 7, {0, 0, 1}}, {1, 0, 3, 2, {0, 0, -1}},
        {5, 1, 2, 6, {1, 0, 0}}, {0, 4, 7, 3, {-1, 0, 0}},
        {3, 7, 6, 2, {0, 1, 0}}, {0, 1, 5, 4, {0, -1, 0}}
    };
    const glm::vec2 uvs[4] = {{0, 0}, {1, 0}, {1, 1}, {0, 1}};
    std::vector<Vertex> vertices;
    std::vector<uint32_t> indices;
    for (const auto& face : faces) {
        const int corner[4] = {face.a, face.b, face.c, face.d};
        const uint32_t base = static_cast<uint32_t>(vertices.size());
        for (int i = 0; i < 4; ++i) {
            vertices.push_back({p[corner[i]], face.n, uvs[i]});
        }
        indices.insert(indices.end(), {base, base + 1, base + 2, base, base + 2, base + 3});
    }
    Mesh mesh;
    mesh.setup(vertices, indices);
    return mesh;
}

Mesh Mesh::plane() {
    const std::vector<Vertex> vertices = {
        {{-0.5f, 0, -0.5f}, {0, 1, 0}, {0, 0}},
        {{0.5f, 0, -0.5f}, {0, 1, 0}, {1, 0}},
        {{0.5f, 0, 0.5f}, {0, 1, 0}, {1, 1}},
        {{-0.5f, 0, 0.5f}, {0, 1, 0}, {0, 1}}
    };
    const std::vector<uint32_t> indices = {0, 2, 1, 0, 3, 2};
    Mesh mesh;
    mesh.setup(vertices, indices);
    return mesh;
}

Mesh Mesh::quad() {
    const std::vector<Vertex> vertices = {
        {{-0.5f, -0.5f, 0}, {0, 0, 1}, {0, 0}},
        {{0.5f, -0.5f, 0}, {0, 0, 1}, {1, 0}},
        {{0.5f, 0.5f, 0}, {0, 0, 1}, {1, 1}},
        {{-0.5f, 0.5f, 0}, {0, 0, 1}, {0, 1}}
    };
    const std::vector<uint32_t> indices = {0, 1, 2, 0, 2, 3};
    Mesh mesh;
    mesh.setup(vertices, indices);
    return mesh;
}

} // namespace VelvX