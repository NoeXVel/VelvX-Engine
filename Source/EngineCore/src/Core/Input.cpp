/**
 * @file Input.cpp
 * @brief Fachada legada delegando ao InputManager.
 */

#include "Core/Input.hpp"

namespace VelvX {

bool Input::isKeyPressed(int key) {
    return InputManager::isKeyPressed(static_cast<KeyCode>(key));
}

bool Input::isKeyDown(int key) {
    return InputManager::isKeyDown(static_cast<KeyCode>(key));
}

bool Input::isKeyUp(int key) {
    return InputManager::isKeyUp(static_cast<KeyCode>(key));
}

void Input::update() {
    // O InputManager::update() é chamado pela Engine após glfwPollEvents.
}

} // namespace VelvX