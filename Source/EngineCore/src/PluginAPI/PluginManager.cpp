/**
 * @file PluginManager.cpp
 * @brief Carregamento dinâmico de plugins (LoadLibrary/dlopen).
 */

#include "PluginAPI/PluginManager.hpp"
#include "Core/Log.hpp"
#include "Utils/FileSystem.hpp"
#include <algorithm>
#include <filesystem>

#if defined(_WIN32)
#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#ifndef NOMINMAX
#define NOMINMAX
#endif
#include <windows.h>
#else
#include <dlfcn.h>
#endif

namespace VelvX {

std::vector<PluginManager::PluginHandle> PluginManager::s_loadedPlugins;

namespace {

/** @brief Abre a biblioteca e resolve CreatePlugin. */
void* openLibrary(const std::string& path) {
#if defined(_WIN32)
    return static_cast<void*>(LoadLibraryA(path.c_str()));
#else
    return dlopen(path.c_str(), RTLD_NOW | RTLD_LOCAL);
#endif
}

/** @brief Resolve o símbolo CreatePlugin na biblioteca. */
void* findSymbol(void* handle, const char* symbol) {
#if defined(_WIN32)
    return reinterpret_cast<void*>(GetProcAddress(static_cast<HMODULE>(handle), symbol));
#else
    return dlsym(handle, symbol);
#endif
}

/** @brief Extensão de biblioteca plugin na plataforma atual. */
std::string pluginExtension() {
#if defined(_WIN32)
    return ".dll";
#elif defined(__APPLE__)
    return ".dylib";
#else
    return ".so";
#endif
}

} // namespace

void PluginManager::loadPlugin(const std::string& filePath) {
    void* handle = openLibrary(filePath);
    if (!handle) {
        Log::error("PluginManager: falha ao carregar biblioteca: " + filePath);
        return;
    }
    using CreatePluginFn = IPlugin* (*)();
    auto create = reinterpret_cast<CreatePluginFn>(findSymbol(handle, "CreatePlugin"));
    if (!create) {
        Log::error("PluginManager: símbolo CreatePlugin ausente em " + filePath);
#if !defined(_WIN32)
        dlclose(handle);
#endif
        return;
    }
    IPlugin* plugin = create();
    if (!plugin) {
        Log::error("PluginManager: CreatePlugin retornou nulo em " + filePath);
        return;
    }
    const std::string name = plugin->getName();
    for (const auto& loaded : s_loadedPlugins) {
        if (loaded.plugin && loaded.plugin->getName() == name) {
            Log::warn("PluginManager: plugin '" + name + "' já carregado");
            delete plugin;
            return;
        }
    }
    plugin->init();
    Log::info("PluginManager: plugin '" + name + "' v" + plugin->getVersion() + " carregado");
    s_loadedPlugins.push_back({handle, std::unique_ptr<IPlugin>(plugin), filePath});
}

void PluginManager::unloadPlugin(const std::string& name) {
    for (auto it = s_loadedPlugins.begin(); it != s_loadedPlugins.end(); ++it) {
        if (it->plugin && it->plugin->getName() == name) {
            it->plugin->shutdown();
            Log::info("PluginManager: plugin '" + name + "' encerrado (biblioteca residente)");
            s_loadedPlugins.erase(it);
            return;
        }
    }
}

void PluginManager::initAllPlugins() {
    for (const auto& loaded : s_loadedPlugins) {
        if (loaded.plugin) {
            loaded.plugin->init();
        }
    }
}

void PluginManager::shutdownAllPlugins() {
    for (auto it = s_loadedPlugins.rbegin(); it != s_loadedPlugins.rend(); ++it) {
        if (it->plugin) {
            it->plugin->shutdown();
        }
    }
    s_loadedPlugins.clear();
}

void PluginManager::loadPluginsFromDirectory(const std::string& directory) {
    const std::filesystem::path dir(directory);
    std::error_code ec;
    if (!std::filesystem::is_directory(dir, ec)) {
        return;
    }
    for (const auto& entry : std::filesystem::directory_iterator(dir, ec)) {
        if (entry.is_regular_file(ec) && entry.path().extension() == pluginExtension()) {
            loadPlugin(entry.path().string());
        }
    }
}

std::vector<std::string> PluginManager::loadedPluginNames() {
    std::vector<std::string> names;
    names.reserve(s_loadedPlugins.size());
    for (const auto& loaded : s_loadedPlugins) {
        if (loaded.plugin) {
            names.emplace_back(loaded.plugin->getName());
        }
    }
    return names;
}

} // namespace VelvX