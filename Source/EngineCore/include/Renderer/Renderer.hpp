#pragma once
/**
 * @file Renderer.hpp
 * @brief Renderer OpenGL 3.3 core: 3D com sombras, Ortho2D e sprites em pixels.
 *
 * Duas passadas sobre um único framebuffer (janela ou FBO do Editor):
 *  1. 3D perspectiva com shadow map direcional + PCF (ou Ortho2D para cenas 2D).
 *  2. 2D ortho em pixels: sprites, HUD e UIManager.
 */

#include "Core/Transform.hpp"
#include "Renderer/Shader.hpp"
#include "Renderer/Mesh.hpp"
#include "Renderer/Texture.hpp"
#include <GLFW/glfw3.h>
#include <glm/glm.hpp>
#include <filesystem>
#include <map>
#include <memory>
#include <string>

namespace VelvX {

class Scene;

/**
 * @brief Fachada estática de renderização da engine.
 */
class Renderer {
public:
    /** @brief Inicializa GL, shaders, malhas built-in, shadow map e texto. */
    static void init(GLFWwindow* win);

    /** @brief Libera programas, buffers, texturas e FBOs. */
    static void shutdown();

    /** @brief Diretório de onde os shaders .glsl são carregados. */
    static void setShaderRoot(const std::filesystem::path& root);

    /** @brief Configura sombras (ativadas + resolução do shadow map). */
    static void setShadowSettings(bool enabled, int mapSize);

    /** @brief Prepara o frame: viewport do framebuffer atual + clear. */
    static void beginFrame();

    /** @brief Limpa cor e profundidade do alvo atual. */
    static void clear();

    /**
     * @brief Renderiza a cena (3D com sombras ou Ortho2D) + HUD/UI.
     * @param scene Cena a desenhar (nula = apenas clear).
     * @param time Tempo total em segundos.
     */
    static void renderScene(Scene* scene, float time);

    /** @brief Sprite sólido em pixels de tela (origem superior-esquerda). */
    static void drawSprite(const glm::vec2& pos, const glm::vec2& size,
                           const glm::vec3& color, float alpha = 1.0f);

    /** @brief Sprite texturizado em pixels de tela. */
    static void drawSpriteTextured(const glm::vec2& pos, const glm::vec2& size,
                                   const std::string& texturePath,
                                   const glm::vec3& tint, float alpha = 1.0f);

    /** @brief Desenha um cubo com a transformação dada (API legada). */
    static void draw(const Transform& tf, const std::string& shaderPath);

    /** @brief Textura do cache (carrega sob demanda; branco em falha). */
    static unsigned int textureFor(const std::string& path);

    /** @brief Inicia render para textura (viewport do Editor). */
    static void beginSceneTarget(int width, int height);

    /** @brief Finaliza o target e retorna a textura de cor. */
    static unsigned int endSceneTarget();

    /** @brief Tamanho do framebuffer atual em pixels. */
    static glm::vec2 viewportSize();

    /** @brief Última view 3D usada por renderScene (para gizmos/picking). */
    static glm::mat4 viewMatrix();

    /** @brief Última projeção 3D usada por renderScene. */
    static glm::mat4 projectionMatrix();

    /** @brief Posição da câmera usada no último renderScene. */
    static glm::vec3 cameraPosition();

    /** @brief Ignora o componente Camera e usa a câmera de edição da cena. */
    static void setEditorCameraOverride(bool on);

private:
    static GLFWwindow* window_; ///< Janela principal.
    static std::filesystem::path shaderRoot_; ///< Raiz dos shaders.
    static Shader program3D_; ///< Programa 3D lit + sombra.
    static Shader spriteShader_; ///< Programa 2D (sprites/HUD/UI).
    static Shader depthShader_; ///< Programa do shadow pass.
    static Mesh cube_; ///< Malha built-in Cube.
    static Mesh plane_; ///< Malha built-in Plane.
    static Mesh quad_; ///< Malha built-in Quad.
    static unsigned int spriteVao_; ///< VAO do quad unitário 2D.
    static unsigned int spriteVbo_; ///< VBO do quad unitário 2D.
    static Texture white_; ///< Textura branca 1x1 (fallback).
    static std::map<std::string, std::unique_ptr<Texture>> textures_; ///< Cache de texturas.

    static unsigned int shadowFbo_; ///< FBO do shadow map.
    static unsigned int shadowDepth_; ///< Textura de profundidade do shadow map.
    static int shadowMapSize_; ///< Resolução atual do shadow map.
    static bool shadowsEnabled_; ///< Sombras ativas.

    static unsigned int targetFbo_; ///< FBO do viewport do Editor.
    static unsigned int targetColor_; ///< Textura de cor do target.
    static unsigned int targetDepth_; ///< Renderbuffer de profundidade do target.
    static int targetWidth_; ///< Largura do target.
    static int targetHeight_; ///< Altura do target.
    static bool targetActive_; ///< Renderizando para o target.

    static glm::vec2 viewport_; ///< Viewport atual.
    static glm::mat4 lastView_; ///< Última view 3D (API legada).
    static glm::mat4 lastProj_; ///< Última projeção 3D (API legada).
    static glm::vec3 lastCameraPos_; ///< Posição da câmera do último render.
    static bool editorOverride_; ///< Força câmera de edição (ignora Camera).

    /** @brief Cria/recria o shadow map. */
    static void setupShadowMap(int size);

    /** @brief Desenha um MeshRenderer no pass 3D. */
    static void drawMeshRenderer(class GameObject& object, const glm::mat4& lightSpace,
                                 const glm::vec3& lightDir, bool unlit);

    /** @brief Desenha o quad 2D unitário com cor/textura. */
    static void drawQuad2D(const glm::vec2& pos, const glm::vec2& size, const glm::vec4& color,
                           unsigned int texture);
};

} // namespace VelvX