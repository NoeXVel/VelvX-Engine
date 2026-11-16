/**
 * @file ComponentRegistry.cpp
 * @brief Registro global de fábricas de componentes.
 */

#include "PluginAPI/ComponentRegistry.hpp"
#include <algorithm>
#include <unordered_map>

namespace VelvX {

namespace {

/** @brief Mapa tipo → fábrica. */
std::unordered_map<std::string, ComponentRegistry::Factory>& registry() {
    static std::unordered_map<std::string, ComponentRegistry::Factory> factories;
    return factories;
}

} // namespace

void ComponentRegistry::registerFactory(const std::string& typeName, Factory factory) {
    registry()[typeName] = std::move(factory);
}

std::unique_ptr<Component> ComponentRegistry::create(const std::string& typeName, GameObject* owner) {
    const auto it = registry().find(typeName);
    if (it == registry().end() || !it->second) {
        return nullptr;
    }
    return it->second(owner);
}

bool ComponentRegistry::contains(const std::string& typeName) {
    return registry().find(typeName) != registry().end();
}

std::vector<std::string> ComponentRegistry::names() {
    std::vector<std::string> result;
    result.reserve(registry().size());
    for (const auto& entry : registry()) {
        result.push_back(entry.first);
    }
    std::sort(result.begin(), result.end());
    return result;
}

} // namespace VelvX