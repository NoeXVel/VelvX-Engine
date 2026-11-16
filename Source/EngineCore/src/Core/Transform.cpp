#include "Core/Transform.hpp"
#include <glm/gtc/matrix_transform.hpp>

namespace VelvX {

glm::mat4 Transform::localMatrix() const {
    glm::mat4 t = glm::translate(glm::mat4(1.0f), position);
    glm::mat4 r = glm::mat4_cast(rotation);
    glm::mat4 s = glm::scale(glm::mat4(1.0f), scale);
    return t * r * s;
}

glm::mat4 Transform::worldMatrix() const {
    glm::mat4 result = localMatrix();
    const Transform* p = parent;
    while (p) {
        result = p->localMatrix() * result;
        p = p->parent;
    }
    return result;
}

} // namespace VelvX