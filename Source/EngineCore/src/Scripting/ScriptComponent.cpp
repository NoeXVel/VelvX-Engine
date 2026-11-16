#include "Scripting/ScriptComponent.hpp"
#include "Core/GameObject.hpp"
#include "Core/Log.hpp"
#include "PluginAPI/ComponentRegistry.hpp"
#include "Scripting/ScriptEngine.hpp"
#include <utility>

namespace VelvX {

ScriptComponent::ScriptComponent(GameObject* owner) : Component(owner) {
    Inspector::expose(owner->name, "scriptPath", "std::string", &scriptPath, this);
}

ScriptComponent::~ScriptComponent() {
    ScriptEngine::destroyState(state_);
    state_ = nullptr;
}

void ScriptComponent::setScript(const std::string& path) {
    scriptPath = path;
    mainCalled_ = false;
    if (!state_) {
        state_ = ScriptEngine::createState();
    }
    if (scriptPath.empty()) {
        return;
    }
    ScriptEngine::loadStateFile(state_, scriptPath, owner_, this, owner_->name);
}

void ScriptComponent::update(float deltaTime) {
    (void)deltaTime;
    if (!state_) {
        return;
    }
    if (!mainCalled_) {
        ScriptEngine::callStateBook(state_, "Main");
        mainCalled_ = true;
    }
    ScriptEngine::callStateBook(state_, "InGame");
}

void ScriptComponent::callBook(const std::string& name) {
    ScriptEngine::callStateBook(state_, name);
}

bool ScriptComponent::hasBook(const std::string& name) const {
    return ScriptEngine::stateHasBook(state_, name);
}

std::unique_ptr<Component> ScriptComponent::clone(GameObject* newOwner) const {
    auto c = std::make_unique<ScriptComponent>(newOwner);
    if (!scriptPath.empty()) {
        c->setScript(scriptPath);
    }
    return c;
}

Json::Value ScriptComponent::toJson() const {
    Json::Value j = Json::Value::object();
    j["scriptPath"] = Json::Value::ofString(scriptPath);
    return j;
}

void ScriptComponent::fromJson(const Json::Value& json) {
    if (const Json::Value* p = json.find("scriptPath")) {
        setScript(p->asString());
    }
}

namespace {
/** @brief Registra a fábrica de ScriptComponent no carregamento da DLL. */
struct ScriptComponentRegistrar {
    ScriptComponentRegistrar() {
        ComponentRegistry::registerFactory(
            "ScriptComponent",
            [](GameObject* owner) -> std::unique_ptr<Component> {
                return std::make_unique<ScriptComponent>(owner);
            });
    }
};
static ScriptComponentRegistrar g_scriptComponentRegistrar;
} // namespace

} // namespace VelvX