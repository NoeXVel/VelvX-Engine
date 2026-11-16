#pragma once
/**
 * @file IPlugin.hpp
 * @brief Interface C++ para plugins da VelvX Engine.
 */

/**
 * @def VELVX_PLUGIN_API
 * @brief Marca a função de fábrica exportada pelo plugin.
 *
 * Todo plugin deve exportar @c CreatePlugin() retornando uma instância de
 * @c IPlugin (a engine assume a posse e chama init/shutdown).
 */
#if defined(_WIN32)
#define VELVX_PLUGIN_API extern "C" __declspec(dllexport)
#else
#define VELVX_PLUGIN_API extern "C" __attribute__((visibility("default")))
#endif

/**
 * @brief Interface base para todos os plugins.
 *
 * Plugins podem registrar componentes via @c VelvX::ComponentRegistry,
 * assets loaders e extensões de editor durante @c init().
 */
class IPlugin {
public:
    virtual ~IPlugin() = default;

    /** @brief Inicialização: registre componentes/extensões aqui. */
    virtual void init() = 0;

    /** @brief Encerramento: libere recursos do plugin. */
    virtual void shutdown() = 0;

    /** @brief Nome único do plugin. */
    virtual const char* getName() const = 0;

    /** @brief Versão do plugin. */
    virtual const char* getVersion() const { return "1.0"; }
};