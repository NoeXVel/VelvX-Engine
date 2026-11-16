#pragma once
/**
 * @file Component.hpp
 * @brief Base de todos os componentes de GameObject (incluindo plugins).
 */

#include "Utils/Json.hpp"
#include <memory>

namespace VelvX {

class GameObject;

/**
 * @brief Componente anexável a um GameObject.
 *
 * Plugins podem derivar desta classe e registrar fábricas via PluginManager.
 */
class Component {
public:
    /** @brief Componente pertence a @p owner. */
    explicit Component(GameObject* owner) : owner_(owner) {}
    virtual ~Component() = default;

    /** @brief Atualização por frame. */
    virtual void update(float deltaTime) { (void)deltaTime; }

    /** @brief Desenho opcional do componente. */
    virtual void render() {}

    /** @brief Hook de trigger 3D (colisão do tipo trigger com outro componente). */
    virtual void onTrigger3D(Component* other) { (void)other; }

    /** @brief Cópia profunda para Instantiate, vinculada a @p newOwner. */
    virtual std::unique_ptr<Component> clone(GameObject* newOwner) const = 0;

    /** @brief Nome do tipo (usado no Inspector e em serialização). */
    virtual const char* typeName() const = 0;

    /** @brief Serializa parâmetros do componente (plugins). */
    virtual Json::Value toJson() const { return Json::Value::object(); }

    /** @brief Lê parâmetros do componente (plugins). */
    virtual void fromJson(const Json::Value& json) { (void)json; }

    /** @brief GameObject dono. */
    GameObject* owner() const { return owner_; }

protected:
    GameObject* owner_; ///< Dono do componente.
};

} // namespace VelvX