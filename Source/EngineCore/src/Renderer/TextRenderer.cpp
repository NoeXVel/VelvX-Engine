/**
 * @file TextRenderer.cpp
 * @brief Atlas de glifos stb_truetype e desenho de texto 2D.
 */

#include "Renderer/TextRenderer.hpp"
#include "Renderer/Shader.hpp"
#include "Core/Log.hpp"
#include "Utils/FileSystem.hpp"
#include <glad/gl.h>
#include <glm/gtc/matrix_transform.hpp>
#include <algorithm>
#include <vector>

#define STB_TRUETYPE_IMPLEMENTATION
#include <stb_truetype.h>

namespace VelvX {

bool TextRenderer::ready_ = false;
unsigned int TextRenderer::texture_ = 0;
unsigned int TextRenderer::vao_ = 0;
unsigned int TextRenderer::vbo_ = 0;
float TextRenderer::bakedAscent_ = 0.0f;
glm::vec2 TextRenderer::screenSize_{1280.0f, 720.0f};

namespace {

constexpr int kAtlasSize = 512; ///< Dimensão do atlas.
constexpr float kBakedHeight = 48.0f; ///< Altura de bake em pixels.
constexpr int kFirstChar = 32; ///< Primeiro glifo (espaço).
constexpr int kCharCount = 224; ///< Latin-1 completo (32..255).

stbtt_bakedchar g_cdata[kCharCount]; ///< Métricas dos glifos.
Shader g_shader; ///< Programa de texto.
std::vector<unsigned char> g_fontData; ///< Bytes da fonte (mantidos vivos).

const char* kVertexSource = R"(#version 330 core
layout(location = 0) in vec2 aPos;
layout(location = 1) in vec2 aUV;
uniform mat4 projection;
out vec2 vUV;
void main() {
    vUV = aUV;
    gl_Position = projection * vec4(aPos, 0.0, 1.0);
}
)";

const char* kFragmentSource = R"(#version 330 core
in vec2 vUV;
uniform sampler2D atlas;
uniform vec4 color;
out vec4 FragColor;
void main() {
    float alpha = texture(atlas, vUV).r;
    FragColor = vec4(color.rgb, color.a * alpha);
}
)";

/** @brief Decodifica UTF-8 para codepoints Latin-1 (acentos PT-BR). */
std::vector<int> decodeLatin1(const std::string& text) {
    std::vector<int> codes;
    for (size_t i = 0; i < text.size();) {
        const unsigned char c = static_cast<unsigned char>(text[i]);
        if (c < 0x80) {
            codes.push_back(c);
            ++i;
        } else if ((c & 0xE0) == 0xC0 && i + 1 < text.size()) {
            const int cp = ((c & 0x1F) << 6) | (static_cast<unsigned char>(text[i + 1]) & 0x3F);
            codes.push_back(cp <= 255 ? cp : '?');
            i += 2;
        } else {
            ++i;
            while (i < text.size() && (static_cast<unsigned char>(text[i]) & 0xC0) == 0x80) {
                ++i;
            }
            codes.push_back('?');
        }
    }
    return codes;
}

/** @brief Candidatos de fonte TrueType do sistema. */
const char* kFontCandidates[] = {
    "C:/Windows/Fonts/arial.ttf",
    "C:/Windows/Fonts/calibri.ttf",
    "C:/Windows/Fonts/segoeui.ttf",
    "C:/Windows/Fonts/tahoma.ttf",
    "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",
    "/System/Library/Fonts/Supplemental/Arial.ttf"
};

} // namespace

bool TextRenderer::init() {
    if (ready_) {
        return true;
    }
    for (const char* candidate : kFontCandidates) {
        g_fontData = FS::readBinary(candidate);
        if (!g_fontData.empty()) {
            break;
        }
    }
    if (g_fontData.empty()) {
        Log::warn("TextRenderer: nenhuma fonte TrueType encontrada; texto desativado");
        return false;
    }

    std::vector<unsigned char> atlas(kAtlasSize * kAtlasSize);
    const int result = stbtt_BakeFontBitmap(g_fontData.data(), 0, kBakedHeight, atlas.data(),
                                            kAtlasSize, kAtlasSize, kFirstChar, kCharCount, g_cdata);
    if (result <= 0) {
        Log::warn("TextRenderer: falha ao gerar atlas de glifos");
        return false;
    }

    stbtt_fontinfo font;
    if (stbtt_InitFont(&font, g_fontData.data(), 0)) {
        int ascent = 0;
        int descent = 0;
        int lineGap = 0;
        stbtt_GetFontVMetrics(&font, &ascent, &descent, &lineGap);
        bakedAscent_ = ascent * stbtt_ScaleForPixelHeight(&font, kBakedHeight);
    } else {
        bakedAscent_ = kBakedHeight * 0.8f;
    }

    glGenTextures(1, &texture_);
    glBindTexture(GL_TEXTURE_2D, texture_);
    glPixelStorei(GL_UNPACK_ALIGNMENT, 1);
    glTexImage2D(GL_TEXTURE_2D, 0, GL_R8, kAtlasSize, kAtlasSize, 0, GL_RED, GL_UNSIGNED_BYTE,
                 atlas.data());
    glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, GL_LINEAR);
    glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, GL_LINEAR);
    glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_S, GL_CLAMP_TO_EDGE);
    glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_T, GL_CLAMP_TO_EDGE);

    glGenVertexArrays(1, &vao_);
    glGenBuffers(1, &vbo_);
    glBindVertexArray(vao_);
    glBindBuffer(GL_ARRAY_BUFFER, vbo_);
    glEnableVertexAttribArray(0);
    glVertexAttribPointer(0, 2, GL_FLOAT, GL_FALSE, 4 * sizeof(float), nullptr);
    glEnableVertexAttribArray(1);
    glVertexAttribPointer(1, 2, GL_FLOAT, GL_FALSE, 4 * sizeof(float),
                          reinterpret_cast<void*>(2 * sizeof(float)));
    glBindVertexArray(0);

    if (!g_shader.compile(kVertexSource, kFragmentSource)) {
        return false;
    }
    ready_ = true;
    Log::info("TextRenderer: atlas de texto pronto (Latin-1, 512x512)");
    return true;
}

void TextRenderer::shutdown() {
    if (texture_) glDeleteTextures(1, &texture_);
    if (vao_) glDeleteVertexArrays(1, &vao_);
    if (vbo_) glDeleteBuffers(1, &vbo_);
    g_shader.destroy();
    texture_ = vao_ = vbo_ = 0;
    ready_ = false;
}

bool TextRenderer::ready() {
    return ready_;
}

void TextRenderer::setScreenSize(float width, float height) {
    screenSize_ = glm::vec2(width, height);
}

void TextRenderer::draw(const std::string& text, const glm::vec2& pos, float fontSize,
                        const glm::vec4& color) {
    if (!ready_ || text.empty() || fontSize <= 0.0f) {
        return;
    }
    const float scale = fontSize / kBakedHeight;
    const std::vector<int> codes = decodeLatin1(text);
    std::vector<float> vertices;
    vertices.reserve(codes.size() * 24);
    float penX = 0.0f;
    float penY = 0.0f;
    const float baseline = bakedAscent_ * scale;
    for (const int code : codes) {
        if (code == '\n') {
            penX = 0.0f;
            penY += fontSize * 1.2f;
            continue;
        }
        const int index = (code >= kFirstChar && code < kFirstChar + kCharCount) ? code - kFirstChar : '?' - kFirstChar;
        const stbtt_bakedchar& glyph = g_cdata[index];
        const float x0 = pos.x + penX + glyph.xoff * scale;
        const float y0 = pos.y + penY + baseline + glyph.yoff * scale;
        const float x1 = x0 + (glyph.x1 - glyph.x0) * scale;
        const float y1 = y0 + (glyph.y1 - glyph.y0) * scale;
        const float u0 = glyph.x0 / static_cast<float>(kAtlasSize);
        const float v0 = glyph.y0 / static_cast<float>(kAtlasSize);
        const float u1 = glyph.x1 / static_cast<float>(kAtlasSize);
        const float v1 = glyph.y1 / static_cast<float>(kAtlasSize);
        vertices.insert(vertices.end(), {
            x0, y0, u0, v0, x1, y0, u1, v0, x1, y1, u1, v1,
            x0, y0, u0, v0, x1, y1, u1, v1, x0, y1, u0, v1
        });
        penX += glyph.xadvance * scale;
    }
    if (vertices.empty()) {
        return;
    }

    glDisable(GL_DEPTH_TEST);
    glEnable(GL_BLEND);
    glBlendFunc(GL_SRC_ALPHA, GL_ONE_MINUS_SRC_ALPHA);

    g_shader.use();
    g_shader.setMat4("projection", glm::ortho(0.0f, screenSize_.x, screenSize_.y, 0.0f, -1.0f, 1.0f));
    g_shader.setVec4("color", color);
    g_shader.setInt("atlas", 0);
    glActiveTexture(GL_TEXTURE0);
    glBindTexture(GL_TEXTURE_2D, texture_);

    glBindVertexArray(vao_);
    glBindBuffer(GL_ARRAY_BUFFER, vbo_);
    glBufferData(GL_ARRAY_BUFFER, static_cast<GLsizeiptr>(vertices.size() * sizeof(float)),
                 vertices.data(), GL_DYNAMIC_DRAW);
    glDrawArrays(GL_TRIANGLES, 0, static_cast<int>(vertices.size() / 4));
    glBindVertexArray(0);
}

glm::vec2 TextRenderer::measure(const std::string& text, float fontSize) {
    if (!ready_ || text.empty()) {
        return glm::vec2(0.0f);
    }
    const float scale = fontSize / kBakedHeight;
    const std::vector<int> codes = decodeLatin1(text);
    float width = 0.0f;
    float maxWidth = 0.0f;
    int lines = 1;
    for (const int code : codes) {
        if (code == '\n') {
            maxWidth = std::max(maxWidth, width);
            width = 0.0f;
            ++lines;
            continue;
        }
        const int index = (code >= kFirstChar && code < kFirstChar + kCharCount) ? code - kFirstChar : '?' - kFirstChar;
        width += g_cdata[index].xadvance * scale;
    }
    maxWidth = std::max(maxWidth, width);
    return glm::vec2(maxWidth, fontSize * (lines > 1 ? 1.2f * lines : 1.0f));
}

} // namespace VelvX