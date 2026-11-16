#include "Renderer/Camera.hpp"
#include "Core/GameObject.hpp"
#include <glm/gtc/matrix_transform.hpp>
#include <glm/gtc/quaternion.hpp>

namespace VelvX {

Camera::Camera(GameObject* owner) : Component(owner) {
    const std::string& n = owner->name;
    Inspector::expose(n, "fov", "float", &fov, this);
    Inspector::expose(n, "nearPlane", "float", &nearPlane, this);
    Inspector::expose(n, "farPlane", "float", &farPlane, this);
    Inspector::expose(n, "orthoHeight", "float", &orthoHeight, this);
    Inspector::expose(n, "active", "bool", &active, this);
}

glm::vec3 Camera::worldPosition() const {
    return glm::vec3(owner()->transform.worldMatrix()[3]);
}

glm::vec3 Camera::forward() const {
    glm::quat r = owner()->transform.rotation;
    for (const Transform* p = owner()->transform.parent; p; p = p->parent) {
        r = p->rotation * r;
    }
    return glm::normalize(r * glm::vec3(0.0f, 0.0f, -1.0f));
}

glm::mat4 Camera::viewMatrix() const {
    glm::quat r = owner()->transform.rotation;
    for (const Transform* p = owner()->transform.parent; p; p = p->parent) {
        r = p->rotation * r;
    }
    const glm::mat4 world = glm::translate(glm::mat4(1.0f), worldPosition()) * glm::mat4_cast(r);
    return glm::inverse(world);
}

glm::mat4 Camera::projectionMatrix(float aspect) const {
    if (projection == Projection::Orthographic) {
        const float halfH = orthoHeight * 0.5f;
        const float halfW = halfH * aspect;
        return glm::ortho(-halfW, halfW, -halfH, halfH, -100.0f, 100.0f);
    }
    return glm::perspective(glm::radians(fov), aspect, nearPlane, farPlane);
}

void Camera::lookAt(const glm::vec3& target) {
    const glm::vec3 pos = worldPosition();
    glm::vec3 dir = target - pos;
    if (glm::length(dir) < 1e-5f) {
        return;
    }
    const glm::mat4 look = glm::lookAt(pos, target, glm::vec3(0.0f, 1.0f, 0.0f));
    const glm::quat worldRot = glm::quat_cast(glm::transpose(glm::mat3(look)));
    owner()->transform.setRotation(worldRot);
}

std::unique_ptr<Component> Camera::clone(GameObject* newOwner) const {
    auto c = std::make_unique<Camera>(newOwner);
    c->fov = fov;
    c->nearPlane = nearPlane;
    c->farPlane = farPlane;
    c->orthoHeight = orthoHeight;
    c->active = active;
    c->projection = projection;
    return c;
}

} // namespace VelvX