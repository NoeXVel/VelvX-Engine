#pragma once
/**
 * @file Scene.hpp
 * @brief Cena: parent vazio com Transform, GameObjects e câmera.
 */

#include "Transform.hpp"
#include "GameObject.hpp"
#include "Inspector.hpp"
#include <filesystem>
#include <future>
#include <memory>
#include <string>
#include <vector>

namespace VelvX {

class Camera;

/**
 * @brief Uma cena da engine: raiz vazia (@c parentTransform) + objetos.
 *
 * Campos UNLOCKED ficam expostos e editáveis no Inspector do Editor.
 */
class Scene {
public:
    /** @brief Modo de projeção da câmera da cena. */
    enum class CameraMode { Perspective, Ortho2D };

    /** @brief Cria uma cena nomeada (não carregada). */
    explicit Scene(std::string name);

    /** @brief Remove os campos UNLOCKED do inspector. */
    ~Scene();

    Transform parentTransform; ///< Transform raiz (parent vazio).

    UNLOCKED std::string nextScene; ///< Próxima cena (inspector).
    UNLOCKED glm::vec3 scenePosition{0.0f, 0.0f, 0.0f}; ///< Posição alvo (inspector).

    CameraMode cameraMode = CameraMode::Perspective; ///< Projeção usada no render.
    glm::vec3 cameraPosition{0.0f, 8.5f, 15.0f}; ///< Posição da câmera (perspectiva).
    glm::vec3 cameraTarget{0.0f, 1.0f, 0.0f}; ///< Alvo da câmera.
    float orthoHeight = 12.0f; ///< Altura do mundo visível no modo Ortho2D.

    std::vector<std::unique_ptr<GameObject>> gameObjects; ///< Objetos da cena.

    /**
     * @brief Carregamento síncrono (JSON .vscene ou conteúdo procedural).
     * @param path Nome da cena ou caminho do arquivo.
     */
    void load(const std::string& path);

    /** @brief Carregamento assíncrono em thread de background. */
    std::future<void> loadAsync(const std::string& path);

    /** @brief Descarrega destruindo todos os objetos. */
    void unload();

    /** @brief Adiciona objeto parenteando ao transform raiz. */
    GameObject* addObject(std::unique_ptr<GameObject> object);

    /** @brief Remove e destrói um objeto. @return true se removido. */
    bool removeObject(GameObject* object);

    /** @brief Busca objeto pelo nome (nullptr se ausente). */
    GameObject* findGameObject(const std::string& name);

    /**
     * @brief Câmera ativa: primeiro componente Camera marcado active, ou o
     * primeiro Camera da cena. nullptr se a cena não tem câmera (usa campos
     * legados cameraPosition/cameraTarget no render).
     */
    Camera* getActiveCamera() const;

    /** @brief Nome da cena. */
    const std::string& getName() const { return name_; }

    /** @brief true após load() concluir. */
    bool isLoaded() const { return loaded_; }

    /** @brief Serializa a cena em JSON .vscene. */
    void save(const std::filesystem::path& file) const;

    /** @brief Serializa a cena em uma string JSON (sem tocar em disco). */
    std::string serialize() const;

    /**
     * @brief Restaura a cena a partir de uma string JSON (descarrega antes).
     * @return true se o JSON foi aplicado; false se inválido (cena intacta).
     */
    bool deserialize(const std::string& text);

private:
    std::string name_; ///< Nome identificador.
    bool loaded_ = false; ///< Estado de carregamento.

    /** @brief Conteúdo procedural padrão quando não há arquivo. */
    void loadBuiltIn();
};

} // namespace VelvX