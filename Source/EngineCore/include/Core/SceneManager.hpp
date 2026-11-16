#pragma once
/**
 * @file SceneManager.hpp
 * @brief Registro, carregamento síncrono/assíncrono e troca de cenas.
 */

#include "Scene.hpp"
#include "GameObject.hpp"
#include "Utils/ThreadPool.hpp"
#include <future>
#include <memory>
#include <string>
#include <unordered_map>
#include <vector>

namespace VelvX {

/**
 * @brief Gerencia cenas, protótipos e carregamentos assíncronos (ThreadPool).
 */
class SceneManager {
public:
    /** @brief Registra uma cena sob @p name (assume a posse). */
    void registerScene(const std::string& name, std::unique_ptr<Scene> scene);

    /**
     * @brief Carregamento síncrono: ativa e carrega a cena.
     * @throws std::runtime_error se não registrada.
     */
    void loadScene(const std::string& name);

    /**
     * @brief Carregamento assíncrono via ThreadPool.
     *
     * O future conclui quando a cena estiver carregada E ativada; ao terminar,
     * @c positionScene(Transform*) move a nova cena ao parentTransform.
     */
    std::future<void> loadSceneAsync(const std::string& name);

    /** @brief Move a cena ativa para a transformação @p target. */
    void positionScene(Transform* target);

    /** @brief Conveniência: move a cena ativa para @p position. */
    void positionScene(const glm::vec3& position);

    /** @brief Descarrega a ativa e carrega @p name (síncrono). */
    void swapTo(const std::string& name);

    /** @brief Remove todas as cenas e protótipos (usado ao trocar de projeto). */
    void clear();

    /** @brief Cena ativa (nula antes do primeiro load). */
    Scene* getActiveScene() const { return activeScene_; }

    /** @brief Cenas registradas (para o Editor). */
    const std::unordered_map<std::string, std::unique_ptr<Scene>>& scenes() const { return scenes_; }

    /** @brief Registra protótipo clonável por nome. */
    void registerPrototype(const std::string& name, std::unique_ptr<GameObject> prototype);

    /** @brief Protótipo registrado (ou nullptr). */
    GameObject* getPrototype(const std::string& name) const;

    /** @brief Protótipos registrados (para o Editor). */
    const std::unordered_map<std::string, std::unique_ptr<GameObject>>& prototypes() const { return prototypes_; }

    /** @brief Conclui carregamentos pendentes na thread principal (por frame). */
    void update();

private:
    /** @brief Estado de um loadSceneAsync em andamento. */
    struct PendingLoad {
        std::string name; ///< Cena em carregamento.
        std::shared_future<void> done; ///< Conclusão do load.
        std::promise<void> notify; ///< Future entregue ao chamador.
    };

    std::unordered_map<std::string, std::unique_ptr<Scene>> scenes_; ///< Cenas.
    Scene* activeScene_ = nullptr; ///< Cena ativa.
    std::vector<PendingLoad> pending_; ///< Async em voo.
    std::unordered_map<std::string, std::unique_ptr<GameObject>> prototypes_; ///< Protótipos.
    ThreadPool pool_{2}; ///< Workers de carregamento.
};

} // namespace VelvX