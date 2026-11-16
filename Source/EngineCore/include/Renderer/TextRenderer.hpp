#pragma once
/**
 * @file TextRenderer.hpp
 * @brief Texto 2D via atlas stb_truetype (fonte do sistema).
 */

#include <glm/glm.hpp>
#include <string>

namespace VelvX {

/**
 * @brief Renderizador de texto em pixels de tela (pass 2D).
 *
 * O atlas é gerado uma vez a partir de uma fonte TrueType do sistema
 * (Arial/Calibri/Segoe UI/DejaVu), cobrindo Latin-1 (acentos do português).
 */
class TextRenderer {
public:
    /** @brief Gera o atlas de glifos; false se nenhuma fonte for encontrada. */
    static bool init();

    /** @brief Libera atlas e buffers. */
    static void shutdown();

    /** @brief true se o atlas está pronto. */
    static bool ready();

    /** @brief Desenha @p text com canto superior-esquerdo em @p pos. */
    static void draw(const std::string& text, const glm::vec2& pos, float fontSize,
                     const glm::vec4& color);

    /** @brief Mede o texto (largura, altura) em pixels. */
    static glm::vec2 measure(const std::string& text, float fontSize);

    /** @brief Define o tamanho da tela para a projeção ortográfica de pixels. */
    static void setScreenSize(float width, float height);

private:
    static bool ready_; ///< Atlas inicializado.
    static unsigned int texture_; ///< Textura do atlas (GL_R8).
    static unsigned int vao_; ///< VAO dos quads de texto.
    static unsigned int vbo_; ///< VBO dinâmico.
    static float bakedAscent_; ///< Ascent na altura de bake.
    static glm::vec2 screenSize_; ///< Tela atual.
};

} // namespace VelvX