/**
 * @file Renderer.cpp
 * @brief Render 3D com sombras PCF, Ortho2D, sprites em pixels e FBO do Editor.
 */

#include "Renderer/Renderer.hpp"
#include "Core/AssetBrowser.hpp"
#include "Core/GameObject.hpp"
#include "Core/Log.hpp"
#include "Core/Scene.hpp"
#include "Renderer/Camera.hpp"
#include "Renderer/ModelImporter.hpp"
#include "Renderer/RenderComponent.hpp"
#include "Renderer/TextRenderer.hpp"
#include "UI/UIManager.hpp"
#include "Utils/FileSystem.hpp"
#include <glad/gl.h>
#include <glm/gtc/matrix_transform.hpp>
#include <algorithm>

namespace VelvX {

GLFWwindow* Renderer::window_ = nullptr;
std::filesystem::path Renderer::shaderRoot_;
Shader Renderer::program3D_;
Shader Renderer::spriteShader_;
Shader Renderer::depthShader_;
Mesh Renderer::cube_;
Mesh Renderer::plane_;
Mesh Renderer::quad_;
unsigned int Renderer::spriteVao_ = 0;
unsigned int Renderer::spriteVbo_ = 0;
Texture Renderer::white_;
std::map<std::string, std::unique_ptr<Texture>> Renderer::textures_;
unsigned int Renderer::shadowFbo_ = 0;
unsigned int Renderer::shadowDepth_ = 0;
int Renderer::shadowMapSize_ = 2048;
bool Renderer::shadowsEnabled_ = true;
unsigned int Renderer::targetFbo_ = 0;
unsigned int Renderer::targetColor_ = 0;
unsigned int Renderer::targetDepth_ = 0;
int Renderer::targetWidth_ = 0;
int Renderer::targetHeight_ = 0;
bool Renderer::targetActive_ = false;
glm::vec2 Renderer::viewport_{1280.0f, 720.0f};
glm::mat4 Renderer::lastView_{1.0f};
glm::mat4 Renderer::lastProj_{1.0f};
glm::vec3 Renderer::lastCameraPos_{0.0f};
bool Renderer::editorOverride_ = false;

glm::mat4 Renderer::viewMatrix() { return lastView_; }
glm::mat4 Renderer::projectionMatrix() { return lastProj_; }
glm::vec3 Renderer::cameraPosition() { return lastCameraPos_; }
void Renderer::setEditorCameraOverride(bool on) { editorOverride_ = on; }

namespace {

const char* kDepthVertex = R"(#version 330 core
layout(location = 0) in vec3 aPos;
uniform mat4 model;
uniform mat4 lightSpace;
void main() {
    gl_Position = lightSpace * model * vec4(aPos, 1.0);
}
)";

const char* kDepthFragment = R"(#version 330 core
void main() {}
)";

/** @brief Desenha a geometria de um MeshRenderer (built-in ou importada). */
void drawGeometry(const MeshRenderer& mr, const Mesh& cube, const Mesh& plane, const Mesh& quad) {
    if (mr.importedModel >= 0) {
        ImportedModel* model = ModelImporter::model(mr.importedModel);
        if (model && mr.importedMesh >= 0 && mr.importedMesh < static_cast<int>(model->meshes.size())) {
            model->meshes[mr.importedMesh].gpu.draw();
            return;
        }
    }
    switch (mr.mesh) {
        case MeshType::Plane: plane.draw(); break;
        case MeshType::Quad:
        case MeshType::Sprite: quad.draw(); break;
        default: cube.draw(); break;
    }
}

} // namespace

void Renderer::init(GLFWwindow* win) {
    window_ = win;
    glEnable(GL_MULTISAMPLE);
    glEnable(GL_DEPTH_TEST);
    glEnable(GL_CULL_FACE);
    glCullFace(GL_BACK);
    glFrontFace(GL_CCW);

    cube_ = Mesh::cube();
    plane_ = Mesh::plane();
    quad_ = Mesh::quad();

    const float spriteVerts[] = {
        0.0f, 0.0f, 0.0f, 0.0f,
        1.0f, 0.0f, 1.0f, 0.0f,
        1.0f, 1.0f, 1.0f, 1.0f,
        0.0f, 0.0f, 0.0f, 0.0f,
        1.0f, 1.0f, 1.0f, 1.0f,
        0.0f, 1.0f, 0.0f, 1.0f
    };
    glGenVertexArrays(1, &spriteVao_);
    glGenBuffers(1, &spriteVbo_);
    glBindVertexArray(spriteVao_);
    glBindBuffer(GL_ARRAY_BUFFER, spriteVbo_);
    glBufferData(GL_ARRAY_BUFFER, sizeof(spriteVerts), spriteVerts, GL_STATIC_DRAW);
    glEnableVertexAttribArray(0);
    glVertexAttribPointer(0, 2, GL_FLOAT, GL_FALSE, 4 * sizeof(float), nullptr);
    glEnableVertexAttribArray(1);
    glVertexAttribPointer(1, 2, GL_FLOAT, GL_FALSE, 4 * sizeof(float),
                          reinterpret_cast<void*>(2 * sizeof(float)));
    glBindVertexArray(0);

    white_.makeWhite();

    if (shaderRoot_.empty()) {
        shaderRoot_ = FS::resolveProject("shaders");
    }
    const std::string root = shaderRoot_.string();
    if (!program3D_.loadFiles(root + "/vertex.glsl", root + "/fragment.glsl")) {
        Log::error("Renderer: shader 3D não carregado");
    }
    if (!spriteShader_.loadFiles(root + "/sprite_vertex.glsl", root + "/sprite_fragment.glsl")) {
        Log::error("Renderer: shader 2D não carregado");
    }
    depthShader_.compile(kDepthVertex, kDepthFragment);

    setupShadowMap(shadowMapSize_);
    TextRenderer::init();
    Log::info("Renderer: OpenGL 3.3 core, 3D com sombras + Ortho2D + sprites prontos");
}

void Renderer::shutdown() {
    program3D_.destroy();
    spriteShader_.destroy();
    depthShader_.destroy();
    cube_.destroy();
    plane_.destroy();
    quad_.destroy();
    if (spriteVao_) glDeleteVertexArrays(1, &spriteVao_);
    if (spriteVbo_) glDeleteBuffers(1, &spriteVbo_);
    white_.destroy();
    for (auto& entry : textures_) {
        entry.second->destroy();
    }
    textures_.clear();
    if (shadowFbo_) glDeleteFramebuffers(1, &shadowFbo_);
    if (shadowDepth_) glDeleteTextures(1, &shadowDepth_);
    if (targetFbo_) glDeleteFramebuffers(1, &targetFbo_);
    if (targetColor_) glDeleteTextures(1, &targetColor_);
    if (targetDepth_) glDeleteRenderbuffers(1, &targetDepth_);
    TextRenderer::shutdown();
    window_ = nullptr;
}

void Renderer::setShaderRoot(const std::filesystem::path& root) {
    shaderRoot_ = root;
}

void Renderer::setupShadowMap(int size) {
    if (shadowFbo_ && size == shadowMapSize_) {
        return;
    }
    if (shadowFbo_) glDeleteFramebuffers(1, &shadowFbo_);
    if (shadowDepth_) glDeleteTextures(1, &shadowDepth_);
    shadowMapSize_ = size;
    glGenTextures(1, &shadowDepth_);
    glBindTexture(GL_TEXTURE_2D, shadowDepth_);
    glTexImage2D(GL_TEXTURE_2D, 0, GL_DEPTH_COMPONENT24, size, size, 0, GL_DEPTH_COMPONENT,
                 GL_FLOAT, nullptr);
    glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, GL_NEAREST);
    glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, GL_NEAREST);
    glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_S, GL_CLAMP_TO_BORDER);
    glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_T, GL_CLAMP_TO_BORDER);
    const float border[] = {1.0f, 1.0f, 1.0f, 1.0f};
    glTexParameterfv(GL_TEXTURE_2D, GL_TEXTURE_BORDER_COLOR, border);
    glGenFramebuffers(1, &shadowFbo_);
    glBindFramebuffer(GL_FRAMEBUFFER, shadowFbo_);
    glFramebufferTexture2D(GL_FRAMEBUFFER, GL_DEPTH_ATTACHMENT, GL_TEXTURE_2D, shadowDepth_, 0);
    glDrawBuffer(GL_NONE);
    glReadBuffer(GL_NONE);
    glBindFramebuffer(GL_FRAMEBUFFER, 0);
}

void Renderer::setShadowSettings(bool enabled, int mapSize) {
    shadowsEnabled_ = enabled;
    if (mapSize > 0) {
        setupShadowMap(mapSize);
    }
}

void Renderer::beginFrame() {
    if (window_ && !targetActive_) {
        int w = 0;
        int h = 0;
        glfwGetFramebufferSize(window_, &w, &h);
        viewport_ = glm::vec2(static_cast<float>(std::max(w, 1)), static_cast<float>(std::max(h, 1)));
        glViewport(0, 0, w, h);
    }
    TextRenderer::setScreenSize(viewport_.x, viewport_.y);
    UIManager::setScreenSize(viewport_.x, viewport_.y);
    clear();
}

void Renderer::clear() {
    glClearColor(0.07f, 0.08f, 0.11f, 1.0f);
    glClear(GL_COLOR_BUFFER_BIT | GL_DEPTH_BUFFER_BIT);
}

void Renderer::beginSceneTarget(int width, int height) {
    width = std::max(width, 1);
    height = std::max(height, 1);
    if (!targetFbo_ || width != targetWidth_ || height != targetHeight_) {
        if (targetFbo_) glDeleteFramebuffers(1, &targetFbo_);
        if (targetColor_) glDeleteTextures(1, &targetColor_);
        if (targetDepth_) glDeleteRenderbuffers(1, &targetDepth_);
        targetWidth_ = width;
        targetHeight_ = height;
        glGenTextures(1, &targetColor_);
        glBindTexture(GL_TEXTURE_2D, targetColor_);
        glTexImage2D(GL_TEXTURE_2D, 0, GL_RGBA8, width, height, 0, GL_RGBA, GL_UNSIGNED_BYTE, nullptr);
        glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, GL_LINEAR);
        glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, GL_LINEAR);
        glGenRenderbuffers(1, &targetDepth_);
        glBindRenderbuffer(GL_RENDERBUFFER, targetDepth_);
        glRenderbufferStorage(GL_RENDERBUFFER, GL_DEPTH_COMPONENT24, width, height);
        glGenFramebuffers(1, &targetFbo_);
        glBindFramebuffer(GL_FRAMEBUFFER, targetFbo_);
        glFramebufferTexture2D(GL_FRAMEBUFFER, GL_COLOR_ATTACHMENT0, GL_TEXTURE_2D, targetColor_, 0);
        glFramebufferRenderbuffer(GL_FRAMEBUFFER, GL_DEPTH_ATTACHMENT, GL_RENDERBUFFER, targetDepth_);
    }
    glBindFramebuffer(GL_FRAMEBUFFER, targetFbo_);
    glViewport(0, 0, width, height);
    viewport_ = glm::vec2(static_cast<float>(width), static_cast<float>(height));
    TextRenderer::setScreenSize(viewport_.x, viewport_.y);
    UIManager::setScreenSize(viewport_.x, viewport_.y);
    targetActive_ = true;
    clear();
}

unsigned int Renderer::endSceneTarget() {
    glBindFramebuffer(GL_FRAMEBUFFER, 0);
    targetActive_ = false;
    if (window_) {
        int w = 0;
        int h = 0;
        glfwGetFramebufferSize(window_, &w, &h);
        glViewport(0, 0, w, h);
        viewport_ = glm::vec2(static_cast<float>(std::max(w, 1)), static_cast<float>(std::max(h, 1)));
    }
    return targetColor_;
}

glm::vec2 Renderer::viewportSize() {
    return viewport_;
}

unsigned int Renderer::textureFor(const std::string& path) {
    if (path.empty()) {
        return white_.id;
    }
    std::string resolved = path;
    try {
        resolved = AssetBrowser::resolve(path).string();
    } catch (...) {
        resolved = path;
    }
    const auto it = textures_.find(resolved);
    if (it != textures_.end()) {
        return it->second->id;
    }
    auto texture = std::make_unique<Texture>();
    if (!texture->loadFromFile(resolved)) {
        textures_[resolved] = nullptr;
        return white_.id;
    }
    const unsigned int id = texture->id;
    textures_[resolved] = std::move(texture);
    return id;
}

void Renderer::drawMeshRenderer(GameObject& object, const glm::mat4& lightSpace,
                                const glm::vec3& lightDir, bool unlit) {
    auto* mr = object.getComponent<MeshRenderer>();
    if (!mr || !mr->visible) {
        return;
    }
    const glm::mat4 model = object.transform.worldMatrix();
    program3D_.setMat4("model", model);

    glm::vec3 color = mr->color;
    std::string texturePath = mr->texturePath;
    if (mr->importedModel >= 0) {
        ImportedModel* imported = ModelImporter::model(mr->importedModel);
        if (imported && mr->importedMesh >= 0 &&
            mr->importedMesh < static_cast<int>(imported->meshes.size())) {
            const ImportedMesh& mesh = imported->meshes[mr->importedMesh];
            if (mesh.materialIndex >= 0 &&
                mesh.materialIndex < static_cast<int>(imported->materials.size())) {
                const ImportedMaterial& material = imported->materials[mesh.materialIndex];
                color *= material.color;
                if (texturePath.empty()) {
                    texturePath = material.albedoTexture;
                }
            }
        }
    }
    program3D_.setVec3("color", color);
    const bool hasTexture = !texturePath.empty();
    program3D_.setInt("useTexture", hasTexture ? 1 : 0);
    glActiveTexture(GL_TEXTURE0);
    glBindTexture(GL_TEXTURE_2D, hasTexture ? textureFor(texturePath) : white_.id);
    program3D_.setFloat("unlit", unlit ? 1.0f : 0.0f);
    (void)lightSpace;
    (void)lightDir;
    drawGeometry(*mr, cube_, plane_, quad_);
}

void Renderer::renderScene(Scene* scene, float time) {
    (void)time;
    clear();
    if (!scene) {
        return;
    }
    glEnable(GL_DEPTH_TEST);
    glDepthFunc(GL_LESS);
    glEnable(GL_CULL_FACE);
    glCullFace(GL_BACK);
    glDisable(GL_BLEND);

    const float aspect = viewport_.x / std::max(viewport_.y, 1.0f);

    // Câmera ativa: componente Camera (GameObject) ou campos legados da cena.
    // No viewport de edição ignoramos o Camera para não misturar com o jogo.
    const Camera* cam = editorOverride_ ? nullptr : scene->getActiveCamera();
    const bool ortho = cam ? cam->projection == Camera::Projection::Orthographic
                           : scene->cameraMode == Scene::CameraMode::Ortho2D;
    glm::mat4 view;
    glm::mat4 proj;
    glm::vec3 cameraPos;
    if (cam) {
        view = cam->viewMatrix();
        proj = cam->projectionMatrix(aspect);
        cameraPos = cam->worldPosition();
    } else if (ortho) {
        const float halfH = scene->orthoHeight * 0.5f;
        const float halfW = halfH * aspect;
        proj = glm::ortho(-halfW, halfW, -halfH, halfH, -100.0f, 100.0f);
        view = glm::translate(glm::mat4(1.0f),
                              glm::vec3(-scene->cameraTarget.x, -scene->cameraTarget.y, -10.0f));
        cameraPos = glm::vec3(scene->cameraTarget.x, scene->cameraTarget.y, 10.0f);
    } else {
        view = glm::lookAt(scene->cameraPosition, scene->cameraTarget, glm::vec3(0.0f, 1.0f, 0.0f));
        proj = glm::perspective(glm::radians(45.0f), aspect, 0.1f, 500.0f);
        cameraPos = scene->cameraPosition;
    }
    lastView_ = view;
    lastProj_ = proj;
    lastCameraPos_ = cameraPos;

    if (!ortho) {
        const glm::vec3 lightDir = glm::normalize(glm::vec3(-0.45f, -1.0f, -0.35f));
        const glm::vec3 center(0.0f, 0.0f, 0.0f);
        const glm::mat4 lightView = glm::lookAt(center - lightDir * 45.0f, center, glm::vec3(0.0f, 1.0f, 0.0f));
        const glm::mat4 lightProj = glm::ortho(-28.0f, 28.0f, -28.0f, 28.0f, 1.0f, 120.0f);
        const glm::mat4 lightSpace = lightProj * lightView;

        if (shadowsEnabled_) {
            glViewport(0, 0, shadowMapSize_, shadowMapSize_);
            glBindFramebuffer(GL_FRAMEBUFFER, shadowFbo_);
            glClear(GL_DEPTH_BUFFER_BIT);
            glEnable(GL_POLYGON_OFFSET_FILL);
            glPolygonOffset(2.0f, 4.0f);
            glCullFace(GL_FRONT);
            depthShader_.use();
            depthShader_.setMat4("lightSpace", lightSpace);
            for (const auto& object : scene->gameObjects) {
                const auto* mr = object->getComponent<MeshRenderer>();
                if (!mr || !mr->visible || mr->mesh == MeshType::Sprite) {
                    continue;
                }
                depthShader_.setMat4("model", object->transform.worldMatrix());
                drawGeometry(*mr, cube_, plane_, quad_);
            }
            glDisable(GL_POLYGON_OFFSET_FILL);
            glCullFace(GL_BACK);
            glBindFramebuffer(GL_FRAMEBUFFER, targetActive_ ? targetFbo_ : 0);
            glViewport(0, 0, static_cast<int>(viewport_.x), static_cast<int>(viewport_.y));
        }

        program3D_.use();
        program3D_.setMat4("view", view);
        program3D_.setMat4("projection", proj);
        program3D_.setMat4("lightSpace", lightSpace);
        program3D_.setVec3("lightDir", lightDir);
        program3D_.setVec3("viewPos", cameraPos);
        program3D_.setInt("shadowsEnabled", shadowsEnabled_ ? 1 : 0);
        program3D_.setInt("albedo", 0);
        program3D_.setInt("shadowMap", 1);
        glActiveTexture(GL_TEXTURE1);
        glBindTexture(GL_TEXTURE_2D, shadowDepth_);
        for (const auto& object : scene->gameObjects) {
            const auto* mr = object->getComponent<MeshRenderer>();
            if (mr && mr->mesh == MeshType::Sprite) {
                continue;
            }
            drawMeshRenderer(*object, lightSpace, lightDir, false);
        }
    } else {
        program3D_.use();
        program3D_.setMat4("view", view);
        program3D_.setMat4("projection", proj);
        program3D_.setMat4("lightSpace", glm::mat4(1.0f));
        program3D_.setVec3("lightDir", glm::vec3(0.0f, -1.0f, 0.0f));
        program3D_.setVec3("viewPos", cameraPos);
        program3D_.setInt("shadowsEnabled", 0);
        program3D_.setInt("albedo", 0);
        program3D_.setInt("shadowMap", 1);
        glDisable(GL_CULL_FACE);
        for (const auto& object : scene->gameObjects) {
            drawMeshRenderer(*object, glm::mat4(1.0f), glm::vec3(0.0f), true);
        }
        glEnable(GL_CULL_FACE);
    }

    glDisable(GL_DEPTH_TEST);
    glDisable(GL_CULL_FACE);
    glEnable(GL_BLEND);
    glBlendFunc(GL_SRC_ALPHA, GL_ONE_MINUS_SRC_ALPHA);
    UIManager::render();
    glDisable(GL_BLEND);
    glEnable(GL_DEPTH_TEST);
    glEnable(GL_CULL_FACE);
}

void Renderer::drawQuad2D(const glm::vec2& pos, const glm::vec2& size, const glm::vec4& color,
                          unsigned int texture) {
    spriteShader_.use();
    spriteShader_.setMat4("projection", glm::ortho(0.0f, viewport_.x, viewport_.y, 0.0f, -1.0f, 1.0f));
    const glm::mat4 model = glm::scale(glm::translate(glm::mat4(1.0f), glm::vec3(pos, 0.0f)),
                                       glm::vec3(size, 1.0f));
    spriteShader_.setMat4("model", model);
    spriteShader_.setVec4("color", color);
    spriteShader_.setInt("useTexture", texture != white_.id ? 1 : 0);
    spriteShader_.setInt("tex", 0);
    glActiveTexture(GL_TEXTURE0);
    glBindTexture(GL_TEXTURE_2D, texture);
    glBindVertexArray(spriteVao_);
    glDrawArrays(GL_TRIANGLES, 0, 6);
    glBindVertexArray(0);
}

void Renderer::drawSprite(const glm::vec2& pos, const glm::vec2& size, const glm::vec3& color,
                          float alpha) {
    drawQuad2D(pos, size, glm::vec4(color, alpha), white_.id);
}

void Renderer::drawSpriteTextured(const glm::vec2& pos, const glm::vec2& size,
                                  const std::string& texturePath, const glm::vec3& tint,
                                  float alpha) {
    drawQuad2D(pos, size, glm::vec4(tint, alpha), textureFor(texturePath));
}

void Renderer::draw(const Transform& tf, const std::string& shaderPath) {
    (void)shaderPath;
    program3D_.use();
    program3D_.setMat4("view", lastView_);
    program3D_.setMat4("projection", lastProj_);
    program3D_.setMat4("model", tf.worldMatrix());
    program3D_.setVec3("color", glm::vec3(1.0f));
    program3D_.setInt("useTexture", 0);
    program3D_.setFloat("unlit", 0.0f);
    glActiveTexture(GL_TEXTURE0);
    glBindTexture(GL_TEXTURE_2D, white_.id);
    cube_.draw();
}

} // namespace VelvX