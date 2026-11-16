#pragma once
/**
 * @file ScriptComponent.hpp
 * @brief Componente de script Velv (.velv) anexável a um GameObject.
 *
 * Cada ScriptComponent possui seu próprio estado de interpretador, permitindo
 * múltiplos scripts por GameObject. Variáveis @c unlock globais do script ficam
 * editáveis no Inspector (vinculadas a este componente). O identificador @c self
 * resolve para o GameObject dono.
 */

#include "Core/Component.hpp"
#include "Core/Inspector.hpp"
#include "Utils/Json.hpp"
#include <memory>
#include <string>

namespace VelvX {

struct ScriptState;

/**
 * @brief Anexa um script .velv a um GameObject (Main uma vez, InGame por frame).
 */
class ScriptComponent : public Component {
public:
    UNLOCKED std::string scriptPath; ///< Caminho do script .velv (relativo ao projeto).

    /** @brief Componente vinculado ao dono (registra scriptPath). */
    explicit ScriptComponent(GameObject* owner);

    /** @brief Destrói o estado do interpretador e remove campos unlock. */
    ~ScriptComponent() override;

    /** @brief Carrega (ou recarrega) o script de @p path. */
    void setScript(const std::string& path);

    /** @brief Executa Main (uma vez) e InGame (a cada frame). */
    void update(float deltaTime) override;

    /** @brief Executa um book nomeado deste script. */
    void callBook(const std::string& name);

    /** @brief true se o script carregado define o book. */
    bool hasBook(const std::string& name) const;

    /** @brief Cópia profunda (recarrega o mesmo script no novo dono). */
    std::unique_ptr<Component> clone(GameObject* newOwner) const override;

    /** @brief Nome do tipo. */
    const char* typeName() const override { return "ScriptComponent"; }

    /** @brief Serializa o caminho do script. */
    Json::Value toJson() const override;

    /** @brief Lê o caminho do script e carrega. */
    void fromJson(const Json::Value& json) override;

private:
    ScriptState* state_ = nullptr; ///< Estado independente do interpretador.
    bool mainCalled_ = false; ///< Main já executado neste ciclo de play.
};

} // namespace VelvX