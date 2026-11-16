/**
 * @file Shader.cpp
 * @brief Compilação/link de shaders com log de erros.
 */

#include "Renderer/Shader.hpp"
#include "Core/Log.hpp"
#include "Utils/FileSystem.hpp"
#include <glad/gl.h>
#include <glm/gtc/type_ptr.hpp>
#include <vector>

namespace VelvX {

namespace {

/** @brief Compila um estágio, logando erros. */
unsigned int compileStage(unsigned int type, const std::string& source) {
    const unsigned int shader = glCreateShader(type);
    const char* raw = source.c_str();
    glShaderSource(shader, 1, &raw, nullptr);
    glCompileShader(shader);
    int success = 0;
    glGetShaderiv(shader, GL_COMPILE_STATUS, &success);
    if (!success) {
        char info[1024];
        glGetShaderInfoLog(shader, sizeof(info), nullptr, info);
        Log::error(std::string("Shader: erro de compilação: ") + info);
        glDeleteShader(shader);
        return 0;
    }
    return shader;
}

} // namespace

bool Shader::compile(const std::string& vertexSource, const std::string& fragmentSource) {
    const unsigned int vs = compileStage(GL_VERTEX_SHADER, vertexSource);
    const unsigned int fs = compileStage(GL_FRAGMENT_SHADER, fragmentSource);
    if (!vs || !fs) {
        if (vs) glDeleteShader(vs);
        if (fs) glDeleteShader(fs);
        return false;
    }
    id = glCreateProgram();
    glAttachShader(id, vs);
    glAttachShader(id, fs);
    glLinkProgram(id);
    int success = 0;
    glGetProgramiv(id, GL_LINK_STATUS, &success);
    if (!success) {
        char info[1024];
        glGetProgramInfoLog(id, sizeof(info), nullptr, info);
        Log::error(std::string("Shader: erro de link: ") + info);
        glDeleteProgram(id);
        id = 0;
    }
    glDeleteShader(vs);
    glDeleteShader(fs);
    return id != 0;
}

bool Shader::loadFiles(const std::string& vertexPath, const std::string& fragmentPath) {
    const std::string vs = FS::readText(vertexPath);
    const std::string fs = FS::readText(fragmentPath);
    if (vs.empty() || fs.empty()) {
        Log::error("Shader: arquivos não encontrados: " + vertexPath + " / " + fragmentPath);
        return false;
    }
    return compile(vs, fs);
}

void Shader::use() const {
    glUseProgram(id);
}

void Shader::destroy() {
    if (id) {
        glDeleteProgram(id);
        id = 0;
    }
}

int Shader::location(const std::string& name) const {
    return glGetUniformLocation(id, name.c_str());
}

void Shader::setMat4(const std::string& name, const glm::mat4& value) const {
    glUniformMatrix4fv(location(name), 1, GL_FALSE, glm::value_ptr(value));
}

void Shader::setVec3(const std::string& name, const glm::vec3& value) const {
    glUniform3fv(location(name), 1, glm::value_ptr(value));
}

void Shader::setVec4(const std::string& name, const glm::vec4& value) const {
    glUniform4fv(location(name), 1, glm::value_ptr(value));
}

void Shader::setVec2(const std::string& name, const glm::vec2& value) const {
    glUniform2fv(location(name), 1, glm::value_ptr(value));
}

void Shader::setFloat(const std::string& name, float value) const {
    glUniform1f(location(name), value);
}

void Shader::setInt(const std::string& name, int value) const {
    glUniform1i(location(name), value);
}

} // namespace VelvX