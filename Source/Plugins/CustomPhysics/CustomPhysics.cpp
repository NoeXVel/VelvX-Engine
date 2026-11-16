/**
 * @file CustomPhysics.cpp
 * @brief Implementação do plugin de física customizada.
 */

#include "CustomPhysics.hpp"
#include "Core/GameObject.hpp"
#include "Core/Log.hpp"
#include "PluginAPI/ComponentRegistry.hpp"
#include <cmath>
#include <glm/gtc/quaternion.hpp>

CustomPhysicsComponent::CustomPhysicsComponent(VelvX::GameObject* owner)
    : VelvX::Component(owner) {
    const std::string& n = owner->name;
    VelvX::Inspector::expose(n, "hoverAmplitude", "float", &hoverAmplitude, this);
    VelvX::Inspector::expose(n, "hoverSpeed", "float", &hoverSpeed, this);
    VelvX::Inspector::expose(n, "spinSpeed", "float", &spinSpeed, this);
}

void CustomPhysicsComponent::update(float deltaTime) {
    if (!owner()) {
        return;
    }
    if (!captured_) {
        baseY_ = owner()->transform.position.y;
        captured_ = true;
    }
    elapsed_ += deltaTime;
    owner()->transform.position.y = baseY_ + std::sin(elapsed_ * hoverSpeed) * hoverAmplitude;
    const float angle = glm::radians(spinSpeed * deltaTime);
    owner()->transform.rotation = glm::angleAxis(angle, glm::vec3(0.0f, 1.0f, 0.0f)) *
                                  owner()->transform.rotation;
}

void CustomPhysicsComponent::onTrigger3D(VelvX::Component* other) {
    VelvX::Log::info(std::string("CustomPhysics: trigger em '") + owner()->name + "' por " +
                     (other ? other->typeName() : "?"));
}

std::unique_ptr<VelvX::Component> CustomPhysicsComponent::clone(VelvX::GameObject* newOwner) const {
    auto copy = std::make_unique<CustomPhysicsComponent>(newOwner);
    copy->hoverAmplitude = hoverAmplitude;
    copy->hoverSpeed = hoverSpeed;
    copy->spinSpeed = spinSpeed;
    return copy;
}

VelvX::Json::Value CustomPhysicsComponent::toJson() const {
    VelvX::Json::Value json = VelvX::Json::Value::object();
    json["hoverAmplitude"] = VelvX::Json::Value::ofNumber(hoverAmplitude);
    json["hoverSpeed"] = VelvX::Json::Value::ofNumber(hoverSpeed);
    json["spinSpeed"] = VelvX::Json::Value::ofNumber(spinSpeed);
    return json;
}

void CustomPhysicsComponent::fromJson(const VelvX::Json::Value& json) {
    hoverAmplitude = static_cast<float>(json["hoverAmplitude"].asNumber(hoverAmplitude));
    hoverSpeed = static_cast<float>(json["hoverSpeed"].asNumber(hoverSpeed));
    spinSpeed = static_cast<float>(json["spinSpeed"].asNumber(spinSpeed));
}

void CustomPhysicsPlugin::init() {
    VelvX::ComponentRegistry::registerFactory("CustomPhysics", [](VelvX::GameObject* owner) {
        return std::make_unique<CustomPhysicsComponent>(owner);
    });
    VelvX::Log::info("CustomPhysics: componente 'CustomPhysics' registrado");
}

void CustomPhysicsPlugin::shutdown() {
    VelvX::Log::info("CustomPhysics: plugin encerrado");
}

VELVX_PLUGIN_API IPlugin* CreatePlugin() {
    return new CustomPhysicsPlugin();
}