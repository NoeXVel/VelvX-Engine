#pragma once
/**
 * @file CustomPhysics.hpp
 * @brief Plugin de exemplo: componente de física customizada (hover + spin).
 */

#include "Core/Component.hpp"
#include "Core/Inspector.hpp"
#include "PluginAPI/IPlugin.hpp"
#include <memory>

/**
 * @brief Componente de exemplo: flutuação senoidal + rotação contínua.
 *
 * Registrado pelo plugin como "CustomPhysics" e utilizável em cenas JSON
 * ou via GameObject::addComponent.
 */
class CustomPhysicsComponent : public VelvX::Component {
public:
    UNLOCKED float hoverAmplitude = 0.5f; ///< Amplitude da flutuação (unidades).
    UNLOCKED float hoverSpeed = 2.0f; ///< Velocidade da flutuação (rad/s).
    UNLOCKED float spinSpeed = 90.0f; ///< Rotação em torno de Y (graus/s).

    /** @brief Componente vinculado ao dono (registra campos UNLOCKED). */
    explicit CustomPhysicsComponent(VelvX::GameObject* owner);

    /** @brief Aplica flutuação e rotação por frame. */
    void update(float deltaTime) override;

    /** @brief Loga o contato trigger. */
    void onTrigger3D(VelvX::Component* other) override;

    /** @brief Cópia profunda dos parâmetros. */
    std::unique_ptr<VelvX::Component> clone(VelvX::GameObject* newOwner) const override;

    /** @brief Nome do tipo. */
    const char* typeName() const override { return "CustomPhysics"; }

    /** @brief Serializa parâmetros. */
    VelvX::Json::Value toJson() const override;

    /** @brief Lê parâmetros. */
    void fromJson(const VelvX::Json::Value& json) override;

private:
    float elapsed_ = 0.0f; ///< Tempo acumulado.
    float baseY_ = 0.0f; ///< Altura base capturada no primeiro frame.
    bool captured_ = false; ///< baseY_ já capturada.
};

/**
 * @brief Plugin que registra o CustomPhysicsComponent na engine.
 */
class CustomPhysicsPlugin : public IPlugin {
public:
    /** @brief Registra a fábrica "CustomPhysics". */
    void init() override;

    /** @brief Encerramento (nada a liberar). */
    void shutdown() override;

    /** @brief Nome do plugin. */
    const char* getName() const override { return "CustomPhysics"; }

    /** @brief Versão do plugin. */
    const char* getVersion() const override { return "1.0"; }
};