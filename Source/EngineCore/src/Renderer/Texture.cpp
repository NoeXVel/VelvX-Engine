/**
 * @file Texture.cpp
 * @brief Upload de texturas via stb_image.
 */

#include "Renderer/Texture.hpp"
#include "Core/Log.hpp"
#include "Utils/FileSystem.hpp"
#include <glad/gl.h>

#define STB_IMAGE_IMPLEMENTATION
#include <stb_image.h>

namespace VelvX {

bool Texture::loadFromFile(const std::string& path) {
    const std::vector<uint8_t> bytes = FS::readBinary(path);
    if (bytes.empty()) {
        Log::warn("Texture: arquivo não encontrado: " + path);
        return false;
    }
    int channels = 0;
    stbi_uc* pixels = stbi_load_from_memory(bytes.data(), static_cast<int>(bytes.size()),
                                            &width, &height, &channels, 4);
    if (!pixels) {
        Log::warn("Texture: falha ao decodificar: " + path);
        return false;
    }
    glGenTextures(1, &id);
    glBindTexture(GL_TEXTURE_2D, id);
    glTexImage2D(GL_TEXTURE_2D, 0, GL_RGBA8, width, height, 0, GL_RGBA, GL_UNSIGNED_BYTE, pixels);
    glGenerateMipmap(GL_TEXTURE_2D);
    glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_S, GL_REPEAT);
    glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_T, GL_REPEAT);
    glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, GL_LINEAR_MIPMAP_LINEAR);
    glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, GL_LINEAR);
    stbi_image_free(pixels);
    return true;
}

void Texture::makeWhite() {
    const unsigned char white[4] = {255, 255, 255, 255};
    width = 1;
    height = 1;
    glGenTextures(1, &id);
    glBindTexture(GL_TEXTURE_2D, id);
    glTexImage2D(GL_TEXTURE_2D, 0, GL_RGBA8, 1, 1, 0, GL_RGBA, GL_UNSIGNED_BYTE, white);
    glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, GL_NEAREST);
    glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, GL_NEAREST);
}

void Texture::bind(unsigned int unit) const {
    glActiveTexture(GL_TEXTURE0 + unit);
    glBindTexture(GL_TEXTURE_2D, id);
}

void Texture::destroy() {
    if (id) {
        glDeleteTextures(1, &id);
        id = 0;
    }
}

} // namespace VelvX