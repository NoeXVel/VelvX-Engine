#pragma once
/**
 * @file Inspector.hpp
 * @brief Palavra-chave UNLOCKED e registro de campos inspecionáveis da VelvX Engine.
 */

#include <string>
#include <vector>

/**
 * @def UNLOCKED
 * @brief Marca uma variável como exposta no inspector.
 *
 * Uso: @c UNLOCKED std::string nextScene;
 * Em GCC/Clang emite a anotação @c inspectable (consumível por ferramentas de
 * reflexão); em MSVC a macro é vazia, pois o compilador não suporta
 * @c __attribute__. O registro em tempo de execução é feito via
 * @c VelvX::Inspector::expose.
 */
#if defined(__clang__) || defined(__GNUC__)
#define UNLOCKED __attribute__((annotate("inspectable")))
#else
#define UNLOCKED
#endif

namespace VelvX {

/**
 * @brief Tipo marcador vazio para valores que podem ser inspecionados.
 */
struct Inspectable {};

/**
 * @brief Registro em tempo de execução dos campos marcados com UNLOCKED.
 */
class Inspector {
public:
    /** @brief Descrição de um campo exposto. */
    struct Field {
        std::string owner; ///< Objeto/classe dona do campo.
        std::string name; ///< Nome do campo.
        std::string type; ///< Nome do tipo declarado.
        const void* address; ///< Endereço do campo para leitura/escrita.
        const void* base = nullptr; ///< Instância dona (GameObject/Component/Scene).
    };

    /** @brief Registra um campo UNLOCKED no inspector. */
    static void expose(const std::string& owner, const std::string& name,
                       const std::string& type, const void* address);

    /** @brief Registra um campo UNLOCKED vinculado a uma instância. */
    static void expose(const std::string& owner, const std::string& name,
                       const std::string& type, const void* address, const void* base);

    /** @brief Remove todos os campos de uma instância (destruição). */
    static void removeFields(const void* base);

    /** @brief Todos os campos expostos até o momento. */
    static const std::vector<Field>& fields();

    /** @brief Escreve todos os campos expostos no log (nível info). */
    static void dump();
};

} // namespace VelvX