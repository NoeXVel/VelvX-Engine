#pragma once
/**
 * @file PluginManager.hpp
 * @brief Carregamento de plugins dinâmicos (.dll/.so/.dylib) em runtime.
 */

#include "PluginAPI/IPlugin.hpp"
#include <memory>
#include <string>
#include <vector>

namespace VelvX {

/**
 * @brief Carrega, inicializa e encerra plugins da engine.
 */
class PluginManager {
public:
    /**
     * @brief Carrega uma biblioteca de plugin e chama init().
     * @param filePath Caminho do .dll/.so/.dylib exportando CreatePlugin().
     */
    static void loadPlugin(const std::string& filePath);

    /**
     * @brief Encerra um plugin pelo nome.
     *
     * A biblioteca permanece residente no processo até o exit: componentes
     * criados pelo plugin podem continuar vivos em cenas ativas.
     */
    static void unloadPlugin(const std::string& name);

    /** @brief Inicializa todos os plugins carregados. */
    static void initAllPlugins();

    /** @brief Encerra todos os plugins (chamado no shutdown da engine). */
    static void shutdownAllPlugins();

    /** @brief Carrega todos os plugins de um diretório. */
    static void loadPluginsFromDirectory(const std::string& directory);

    /** @brief Nomes dos plugins ativos. */
    static std::vector<std::string> loadedPluginNames();

private:
    /** @brief Biblioteca + instância de um plugin carregado. */
    struct PluginHandle {
        void* handle = nullptr; ///< Handle da biblioteca (HMODULE/dlopen).
        std::unique_ptr<IPlugin> plugin; ///< Instância criada por CreatePlugin.
        std::string path; ///< Caminho de origem.
    };

    static std::vector<PluginHandle> s_loadedPlugins; ///< Plugins ativos.
};

} // namespace VelvX