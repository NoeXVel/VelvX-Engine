#pragma once
/**
 * @file ModelImporter.hpp
 * @brief Importação de modelos 3D (Blender .fbx/.gltf/.obj) via Assimp.
 *
 * Suporta malhas, texturas, materiais, esqueletos (rig) e animações
 * (esqueleto com skinning em CPU).
 */

#include "Renderer/Mesh.hpp"
#include <glm/glm.hpp>
#include <glm/gtc/quaternion.hpp>
#include <map>
#include <string>
#include <utility>
#include <vector>

namespace VelvX {

/**
 * @brief Osso de um esqueleto importado.
 */
struct ImportedBone {
    std::string name; ///< Nome do nó/osso.
    glm::mat4 offset{1.0f}; ///< Matriz de offset (bind pose).
    std::vector<std::pair<int, float>> weights; ///< (índice de vértice, peso).
};

/**
 * @brief Material importado (cor difusa + textura albedo).
 */
struct ImportedMaterial {
    glm::vec3 color{1.0f, 1.0f, 1.0f}; ///< Cor difusa.
    std::string albedoTexture; ///< Caminho da textura albedo (vazio = sem).
};

/**
 * @brief Keyframe de posição/escala.
 */
struct AnimKeyVec3 {
    float time = 0.0f; ///< Tempo em ticks.
    glm::vec3 value{0.0f}; ///< Valor.
};

/**
 * @brief Keyframe de rotação.
 */
struct AnimKeyQuat {
    float time = 0.0f; ///< Tempo em ticks.
    glm::quat value{1.0f, 0.0f, 0.0f, 0.0f}; ///< Valor.
};

/**
 * @brief Canal de animação de um nó.
 */
struct AnimationChannel {
    std::string nodeName; ///< Nó animado.
    std::vector<AnimKeyVec3> positions; ///< Keys de posição.
    std::vector<AnimKeyQuat> rotations; ///< Keys de rotação.
    std::vector<AnimKeyVec3> scales; ///< Keys de escala.
};

/**
 * @brief Clipe de animação importado.
 */
struct ImportedAnimation {
    std::string name; ///< Nome do clipe.
    float durationTicks = 0.0f; ///< Duração em ticks.
    float ticksPerSecond = 25.0f; ///< Taxa de ticks.
    std::vector<AnimationChannel> channels; ///< Canais por nó.
};

/**
 * @brief Nó da hierarquia do modelo.
 */
struct SceneNode {
    std::string name; ///< Nome do nó.
    glm::mat4 transform{1.0f}; ///< Transformação local (bind).
    std::vector<int> meshIndices; ///< Malhas anexadas ao nó.
    std::vector<SceneNode> children; ///< Filhos.
};

/**
 * @brief Malha importada com dados de skinning.
 */
struct ImportedMesh {
    Mesh gpu; ///< Malha em GPU.
    std::vector<Vertex> vertices; ///< Vértices originais (CPU, para skinning).
    std::vector<uint32_t> indices; ///< Índices.
    std::vector<ImportedBone> bones; ///< Esqueleto da malha.
    std::map<std::string, int> boneMapping; ///< Nome do osso → índice.
    int materialIndex = 0; ///< Material usado.
};

/**
 * @brief Modelo completo importado.
 */
struct ImportedModel {
    std::vector<ImportedMesh> meshes; ///< Malhas.
    std::vector<ImportedMaterial> materials; ///< Materiais.
    SceneNode root; ///< Hierarquia de nós.
    glm::mat4 globalInverse{1.0f}; ///< Inversa da transformação raiz.
    std::vector<ImportedAnimation> animations; ///< Clipes de animação.
    std::string sourcePath; ///< Arquivo de origem.
};

/**
 * @brief Importador e reprodutor de modelos Assimp.
 */
class ModelImporter {
public:
    /**
     * @brief Importa um arquivo .fbx/.gltf/.glb/.obj.
     * @return Índice do modelo, ou -1 em falha.
     */
    static int import(const std::string& path);

    /** @brief Modelo pelo índice (nullptr se inválido). */
    static ImportedModel* model(int index);

    /** @brief Número de modelos importados. */
    static int modelCount();

    /** @brief Inicia a reprodução de um clipe de animação. */
    static void playAnimation(int modelIndex, int animationIndex, bool loop = true);

    /** @brief Para a animação do modelo (volta à bind pose). */
    static void stopAnimation(int modelIndex);

    /** @brief Avança animações e aplica skinning (chamado por frame). */
    static void update(float deltaTime);

    /** @brief Libera todos os modelos. */
    static void shutdown();

private:
    /** @brief Estado de reprodução de um modelo. */
    struct PlaybackState {
        int animationIndex = -1; ///< Clipe ativo (-1 = nenhum).
        float timeTicks = 0.0f; ///< Tempo atual em ticks.
        bool loop = true; ///< Repetição.
        bool playing = false; ///< Em reprodução.
    };

    static std::vector<ImportedModel> models_; ///< Modelos importados.
    static std::vector<PlaybackState> playback_; ///< Estados de animação.
};

} // namespace VelvX