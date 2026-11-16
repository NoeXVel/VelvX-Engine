#pragma once
/**
 * @file PhysicsComponent.hpp
 * @brief RigidBody (dinâmico), StaticBody (estático), Collider e PhysicsWorld.
 */

#include "Core/Component.hpp"
#include "Core/Inspector.hpp"
#include "Core/Transform.hpp"
#include <glm/glm.hpp>
#include <memory>
#include <vector>

namespace VelvX {

/**
 * @brief Corpo rígido dinâmico: gravidade, velocidade e colisão com o chão.
 */
class RigidBody : public Component {
public:
    UNLOCKED float mass = 1.0f; ///< Massa em kg.
    UNLOCKED glm::vec3 velocity{0.0f, 0.0f, 0.0f}; ///< Velocidade linear.
    UNLOCKED bool useGravity = true; ///< Aplica gravidade global (por objeto).
    UNLOCKED float restitution = 0.35f; ///< Coeficiente de quique (0..1).
    UNLOCKED float friction = 0.15f; ///< Atrito horizontal ao tocar o chão (0..1).
    UNLOCKED bool kinematic = false; ///< Se true, não integra (movido por script).
    bool grounded = false; ///< true se apoiado no chão/plataforma neste frame.
    glm::vec3 prevPos{0.0f}; ///< Posição no passo anterior (estimativa de vel. kinematic).
    bool hasPrevPos = false; ///< prevPos já inicializado.

    /** @brief Corpo vinculado ao dono (registra campos UNLOCKED). */
    explicit RigidBody(GameObject* owner);

    /** @brief Cópia profunda do estado dinâmico. */
    std::unique_ptr<Component> clone(GameObject* newOwner) const override {
        auto c = std::make_unique<RigidBody>(newOwner);
        c->mass = mass;
        c->velocity = velocity;
        c->useGravity = useGravity;
        c->restitution = restitution;
        c->friction = friction;
        c->kinematic = kinematic;
        return c;
    }

    /** @brief Nome do tipo. */
    const char* typeName() const override { return "RigidBody"; }
};

/**
 * @brief Corpo estático: nunca se move (piso, paredes, plataformas).
 */
class StaticBody : public Component {
public:
    /** @brief Corpo vinculado ao dono. */
    explicit StaticBody(GameObject* owner) : Component(owner) {}

    /** @brief Cópia (sem estado). */
    std::unique_ptr<Component> clone(GameObject* newOwner) const override {
        return std::make_unique<StaticBody>(newOwner);
    }

    /** @brief Nome do tipo. */
    const char* typeName() const override { return "StaticBody"; }
};

/**
 * @brief Volume de colisão AABB (opcionalmente trigger).
 */
class Collider : public Component {
public:
    UNLOCKED glm::vec3 size{1.0f, 1.0f, 1.0f}; ///< Tamanho local da caixa (multiplica a escala).
    UNLOCKED bool isTrigger = false; ///< Se true, gera eventos sem bloquear.

    /** @brief Collider vinculado ao dono (registra campos UNLOCKED). */
    explicit Collider(GameObject* owner);

    /** @brief Cópia profunda. */
    std::unique_ptr<Component> clone(GameObject* newOwner) const override {
        auto c = std::make_unique<Collider>(newOwner);
        c->size = size;
        c->isTrigger = isTrigger;
        return c;
    }

    /** @brief Nome do tipo. */
    const char* typeName() const override { return "Collider"; }
};

/**
 * @brief Mundo de física mínimo: integra corpos dinâmicos e resolve o chão.
 */
class PhysicsWorld {
public:
    /** @brief Registra um corpo para simulação. */
    static void add(RigidBody* body);

    /** @brief Remove um corpo (seguro para nulos). */
    static void remove(RigidBody* body);

    /** @brief Avança a simulação em @p dt segundos. */
    static void step(float dt);

    /** @brief Aceleração da gravidade global. */
    static glm::vec3 getGravity();

    /** @brief Define a aceleração da gravidade global. */
    static void setGravity(const glm::vec3& g);

private:
    static glm::vec3 gravity; ///< Gravidade global (acessada via get/set).
};

} // namespace VelvX