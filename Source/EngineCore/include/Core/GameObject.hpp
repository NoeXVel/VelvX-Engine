#pragma once
/**
 * @file GameObject.hpp
 * @brief GameObject baseado em componentes + Instantiate/Destroy globais.
 */

#include "Transform.hpp"
#include "Component.hpp"
#include <glm/glm.hpp>
#include <glm/gtc/quaternion.hpp>
#include <memory>
#include <string>
#include <type_traits>
#include <vector>

namespace VelvX {

class ScriptComponent;

/**
 * @brief Entidade da engine: nome, Transform e lista de componentes.
 */
class GameObject {
public:
    std::string name; ///< Nome identificador.
    std::string tag; ///< Tag livre (agrupamento/filtragem de colisão).
    int layer = 0; ///< Camada de colisão (índice em ProjectSettings::collisionLayers).
    Transform transform; ///< Transformação espacial.
    GameObject* parent = nullptr; ///< Objeto pai na hierarquia (nulo = raiz).
    std::vector<std::unique_ptr<Component>> components; ///< Componentes anexados.

    /** @brief Cria um objeto nomeado. */
    explicit GameObject(std::string name = "GameObject");

    /** @brief Destrói componentes (remove corpos de física do mundo). */
    ~GameObject();

    GameObject(const GameObject&) = delete;
    GameObject& operator=(const GameObject&) = delete;

    /**
     * @brief Cria e anexa um componente do tipo T.
     * @tparam T Derivado de Component.
     * @return Ponteiro para o componente criado.
     */
    template <typename T, typename... Args>
    T* addComponent(Args&&... args) {
        static_assert(std::is_base_of<Component, T>::value, "T must derive from Component");
        components.emplace_back(std::make_unique<T>(this, std::forward<Args>(args)...));
        return static_cast<T*>(components.back().get());
    }

    /** @brief Primeiro componente do tipo T, ou nullptr. */
    template <typename T>
    T* getComponent() const {
        for (const auto& comp : components) {
            if (T* casted = dynamic_cast<T*>(comp.get())) {
                return casted;
            }
        }
        return nullptr;
    }

    /** @brief Remove e destrói o primeiro componente do tipo T. */
    template <typename T>
    void removeComponent() {
        for (auto it = components.begin(); it != components.end(); ++it) {
            if (dynamic_cast<T*>(it->get())) {
                components.erase(it);
                return;
            }
        }
    }

    /** @brief Define o pai na hierarquia (atualiza transform.parent). */
    void setParent(GameObject* newParent);

    /** @brief Anexa um script .velv e retorna o componente criado. */
    ScriptComponent* addScript(const std::string& scriptPath);

    /** @brief Todos os ScriptComponent anexados. */
    std::vector<ScriptComponent*> getScripts() const;

    /** @brief Cria um componente pelo nome registrado (nullptr se desconhecido). */
    Component* addComponentByName(const std::string& typeName);

    /** @brief Primeiro componente cujo typeName() casa (nullptr se ausente). */
    Component* getComponentByName(const std::string& typeName) const;

    /** @brief Remove e destrói um componente específico (seguro para nulos). */
    void removeComponent(Component* component);

    /** @brief Atualiza todos os componentes. */
    void update(float deltaTime);

    /** @brief Renderiza todos os componentes. */
    void render();

    /** @brief Cópia profunda (transform + componentes) com o mesmo nome. */
    std::unique_ptr<GameObject> clone() const;
};

/**
 * @brief Instancia um protótipo na cena ativa com pos/rot/scale.
 * @return O novo GameObject, ou nullptr sem cena ativa/protótipo nulo.
 */
GameObject* Instantiate(GameObject* prototype, const glm::vec3& pos,
                        const glm::quat& rot = glm::quat(1.0f, 0.0f, 0.0f, 0.0f),
                        const glm::vec3& scale = glm::vec3(1.0f));

/** @brief Instancia um protótipo registrado pelo nome (Velv Language). */
GameObject* Instantiate(const std::string& prototypeName, const glm::vec3& pos,
                        const glm::vec3& scale = glm::vec3(1.0f));

/** @brief Destrói um objeto da cena ativa (seguro para nulos). */
void Destroy(GameObject* object);

} // namespace VelvX