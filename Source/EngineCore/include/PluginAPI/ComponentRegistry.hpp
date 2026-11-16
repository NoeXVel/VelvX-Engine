#pragma once
/**
 * @file ComponentRegistry.hpp
 * @brief Fábricas de componentes por nome (built-in e plugins).
 */

#include "Core/Component.hpp"
#include <functional>
#include <memory>
#include <string>
#include <vector>

namespace VelvX {

/**
 * @brief Registro global de fábricas de componentes.
 *
 * Cenas JSON podem referenciar componentes pelo nome; plugins registram
 * novos tipos em @c IPlugin::init.
 */
class ComponentRegistry {
public:
    /** @brief Fábrica: cria um componente vinculado ao dono. */
    using Factory = std::function<std::unique_ptr<Component>(GameObject*)>;

    /** @brief Registra (ou substitui) a fábrica de um tipo. */
    static void registerFactory(const std::string& typeName, Factory factory);

    /** @brief Cria um componente pelo nome (nullptr se desconhecido). */
    static std::unique_ptr<Component> create(const std::string& typeName, GameObject* owner);

    /** @brief true se o tipo está registrado. */
    static bool contains(const std::string& typeName);

    /** @brief Nomes registrados (para o Editor). */
    static std::vector<std::string> names();
};

} // namespace VelvX