#pragma once
/**
 * @file Texture.hpp
 * @brief Textura 2D carregada via stb_image (disco ou pack de exportação).
 */

#include <string>

namespace VelvX {

/**
 * @brief Textura OpenGL 2D (RGBA8).
 */
class Texture {
public:
    unsigned int id = 0; ///< Nome da textura GL.
    int width = 0; ///< Largura em pixels.
    int height = 0; ///< Altura em pixels.

    /** @brief Carrega de arquivo (png/jpg/tga/bmp); false em falha. */
    bool loadFromFile(const std::string& path);

    /** @brief Cria textura sólida 1x1 (fallback branco). */
    void makeWhite();

    /** @brief Vincula à unidade de textura dada. */
    void bind(unsigned int unit = 0) const;

    /** @brief Libera a textura. */
    void destroy();

    /** @brief true se válida. */
    bool valid() const { return id != 0; }
};

} // namespace VelvX