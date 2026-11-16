#pragma once
/**
 * @file Trigger.hpp
 * @brief Sistema de triggers: associa teclas a books da Velv Language.
 */

#include <string>
#include <vector>

namespace VelvX {

/**
 * @brief Triggers nomeados disparados por teclas.
 *
 * Um trigger liga uma tecla a um @c book do script (ex.:
 * @c Trigger.bindKey(Key.Space, "OnChangeScene")). A Engine chama
 * @c Trigger::update() a cada frame; quando a tecla desce, o book associado
 * é executado uma única vez.
 */
class Trigger {
public:
    /** @brief Associa a tecla @p key ao book @p bookName. */
    static void bindKey(int key, const std::string& bookName);

    /** @brief Remove todos os triggers registrados. */
    static void clear();

    /** @brief Dispara manualmente o book @p bookName. */
    static void fire(const std::string& bookName);

    /** @brief Avalia os triggers do frame (chamado pela Engine). */
    static void update();

private:
    /** @brief Par tecla → book registrado. */
    struct Binding {
        int key; ///< Tecla observada.
        std::string bookName; ///< Book executado no disparo.
    };

    static std::vector<Binding> bindings_; ///< Triggers ativos.
};

} // namespace VelvX