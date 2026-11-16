#include "Core/GameObject.hpp"
#include "Core/Inspector.hpp"
#include "Core/Log.hpp"
#include "Engine/Engine.hpp"
#include "Physics/PhysicsComponent.hpp"
#include "PluginAPI/ComponentRegistry.hpp"
#include "Scripting/ScriptComponent.hpp"
#include <utility>

namespace VelvX {

GameObject::GameObject(std::string name) : name(std::move(name)) {}

GameObject::~GameObject() {
    for (const auto& comp : components) {
        if (auto* rb = dynamic_cast<RigidBody*>(comp.get())) {
            PhysicsWorld::remove(rb);
        }
        Inspector::removeFields(comp.get());
    }
    Inspector::removeFields(this);
}

void GameObject::update(float deltaTime) {
    for (const auto& comp : components) {
        comp->update(deltaTime);
    }
}

void GameObject::render() {
    for (const auto& comp : components) {
        comp->render();
    }
}

std::unique_ptr<GameObject> GameObject::clone() const {
    auto copy = std::make_unique<GameObject>(name);
    copy->tag = tag;
    copy->layer = layer;
    copy->transform = transform;
    copy->transform.parent = nullptr;
    for (const auto& comp : components) {
        copy->components.push_back(comp->clone(copy.get()));
    }
    return copy;
}

void GameObject::setParent(GameObject* newParent) {
    parent = newParent;
    transform.parent = newParent ? &newParent->transform : nullptr;
}

ScriptComponent* GameObject::addScript(const std::string& scriptPath) {
    auto script = std::make_unique<ScriptComponent>(this);
    ScriptComponent* raw = script.get();
    components.push_back(std::move(script));
    raw->setScript(scriptPath);
    return raw;
}

std::vector<ScriptComponent*> GameObject::getScripts() const {
    std::vector<ScriptComponent*> scripts;
    for (const auto& comp : components) {
        if (auto* script = dynamic_cast<ScriptComponent*>(comp.get())) {
            scripts.push_back(script);
        }
    }
    return scripts;
}

Component* GameObject::addComponentByName(const std::string& typeName) {
    auto component = ComponentRegistry::create(typeName, this);
    if (!component) {
        return nullptr;
    }
    Component* raw = component.get();
    if (auto* rb = dynamic_cast<RigidBody*>(raw)) {
        PhysicsWorld::add(rb);
    }
    components.push_back(std::move(component));
    return raw;
}

Component* GameObject::getComponentByName(const std::string& typeName) const {
    for (const auto& comp : components) {
        if (typeName == comp->typeName()) {
            return comp.get();
        }
    }
    return nullptr;
}

void GameObject::removeComponent(Component* component) {
    if (!component) {
        return;
    }
    for (auto it = components.begin(); it != components.end(); ++it) {
        if (it->get() == component) {
            if (auto* rb = dynamic_cast<RigidBody*>(component)) {
                PhysicsWorld::remove(rb);
            }
            Inspector::removeFields(component);
            components.erase(it);
            return;
        }
    }
}

GameObject* Instantiate(GameObject* prototype, const glm::vec3& pos,
                        const glm::quat& rot, const glm::vec3& scale) {
    if (!prototype) {
        Log::warn("Instantiate: protótipo nulo");
        return nullptr;
    }
    Scene* scene = Engine::getSceneManager().getActiveScene();
    if (!scene) {
        Log::warn("Instantiate: nenhuma cena ativa");
        return nullptr;
    }

    std::unique_ptr<GameObject> clone = prototype->clone();
    clone->transform.setPosition(pos);
    clone->transform.setRotation(rot);
    clone->transform.setScale(scale);
    GameObject* raw = clone.get();
    scene->addObject(std::move(clone));
    Log::info("Instantiate: '" + raw->name + "' em (" +
              std::to_string(pos.x) + ", " + std::to_string(pos.y) + ", " +
              std::to_string(pos.z) + ")");
    return raw;
}

GameObject* Instantiate(const std::string& prototypeName, const glm::vec3& pos,
                        const glm::vec3& scale) {
    GameObject* prototype = Engine::getSceneManager().getPrototype(prototypeName);
    if (!prototype) {
        Log::warn("Instantiate: protótipo desconhecido: " + prototypeName);
        return nullptr;
    }
    return Instantiate(prototype, pos, glm::quat(1.0f, 0.0f, 0.0f, 0.0f), scale);
}

void Destroy(GameObject* object) {
    if (!object) {
        return;
    }
    Scene* scene = Engine::getSceneManager().getActiveScene();
    if (scene && scene->removeObject(object)) {
        Log::info("Destroy: '" + object->name + "' removido da cena");
        return;
    }
    Log::warn("Destroy: objeto '" + object->name + "' não pertence à cena ativa");
}

} // namespace VelvX