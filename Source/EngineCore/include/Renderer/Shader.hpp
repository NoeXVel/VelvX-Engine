#pragma once
/**
 * @file Shader.hpp
 * @brief Programa GLSL compilado (vertex + fragment) com setters de uniforms.
 */

#include <glm/glm.hpp>
#include <string>

namespace VelvX {

/**
 * @brief Shader program OpenGL com cache de locations de uniforms.
 */
class Shader {
public:
    unsigned int id = 0; ///< Nome do programa GL.

    /** @brief Compila e linka a partir de código-fonte GLSL. */
    bool compile(const std::string& vertexSource, const std::string& fragmentSource);

    /** @brief Compila e linka a partir de arquivos .glsl em disco. */
    bool loadFiles(const std::string& vertexPath, const std::string& fragmentPath);

    /** @brief Ativa o programa. */
    void use() const;

    /** @brief Libera o programa. */
    void destroy();

    /** @brief Uniform mat4. */
    void setMat4(const std::string& name, const glm::mat4& value) const;
    /** @brief Uniform vec3. */
    void setVec3(const std::string& name, const glm::vec3& value) const;
    /** @brief Uniform vec4. */
    void setVec4(const std::string& name, const glm::vec4& value) const;
    /** @brief Uniform vec2. */
    void setVec2(const std::string& name, const glm::vec2& value) const;
    /** @brief Uniform float. */
    void setFloat(const std::string& name, float value) const;
    /** @brief Uniform int. */
    void setInt(const std::string& name, int value) const;

private:
    /** @brief Location de um uniform (sem cache: chamadas por frame são baratas). */
    int location(const std::string& name) const;
};

} // namespace VelvX