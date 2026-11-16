/**
 * @file InputManager.cpp
 * @brief Implementação da entrada unificada com perfis e bindings JSON.
 */

#include "Input/InputManager.hpp"
#include "Core/Log.hpp"
#include "Utils/FileSystem.hpp"
#include "Utils/Json.hpp"
#include <algorithm>
#include <cctype>

namespace VelvX {

GLFWwindow* InputManager::window_ = nullptr;
std::array<bool, InputManager::kMaxCode> InputManager::current_{};
std::array<bool, InputManager::kMaxCode> InputManager::previous_{};
std::array<float, 6> InputManager::gamepadAxes_{};
glm::vec2 InputManager::mousePosition_{0.0f};
glm::vec2 InputManager::mouseDelta_{0.0f};
bool InputManager::firstMouseSample_ = true;
float InputManager::deadzone_ = 0.15f;
std::vector<InputProfile> InputManager::profiles_;
int InputManager::activeProfile_ = 0;
static bool g_cursorCaptured = false;

namespace {

/** @brief Par código/nome para serialização. */
struct KeyName {
    KeyCode code;
    const char* name;
};

const KeyName kKeyNames[] = {
    {KeyCode::Space, "Space"}, {KeyCode::Enter, "Enter"}, {KeyCode::Escape, "Escape"},
    {KeyCode::Backspace, "Backspace"}, {KeyCode::Tab, "Tab"}, {KeyCode::Delete, "Delete"},
    {KeyCode::Insert, "Insert"}, {KeyCode::Home, "Home"}, {KeyCode::End, "End"},
    {KeyCode::PageUp, "PageUp"}, {KeyCode::PageDown, "PageDown"},
    {KeyCode::Left, "Left"}, {KeyCode::Right, "Right"}, {KeyCode::Up, "Up"}, {KeyCode::Down, "Down"},
    {KeyCode::A, "A"}, {KeyCode::B, "B"}, {KeyCode::C, "C"}, {KeyCode::D, "D"},
    {KeyCode::E, "E"}, {KeyCode::F, "F"}, {KeyCode::G, "G"}, {KeyCode::H, "H"},
    {KeyCode::I, "I"}, {KeyCode::J, "J"}, {KeyCode::K, "K"}, {KeyCode::L, "L"},
    {KeyCode::M, "M"}, {KeyCode::N, "N"}, {KeyCode::O, "O"}, {KeyCode::P, "P"},
    {KeyCode::Q, "Q"}, {KeyCode::R, "R"}, {KeyCode::S, "S"}, {KeyCode::T, "T"},
    {KeyCode::U, "U"}, {KeyCode::V, "V"}, {KeyCode::W, "W"}, {KeyCode::X, "X"},
    {KeyCode::Y, "Y"}, {KeyCode::Z, "Z"},
    {KeyCode::Num0, "0"}, {KeyCode::Num1, "1"}, {KeyCode::Num2, "2"}, {KeyCode::Num3, "3"},
    {KeyCode::Num4, "4"}, {KeyCode::Num5, "5"}, {KeyCode::Num6, "6"}, {KeyCode::Num7, "7"},
    {KeyCode::Num8, "8"}, {KeyCode::Num9, "9"},
    {KeyCode::F1, "F1"}, {KeyCode::F2, "F2"}, {KeyCode::F3, "F3"}, {KeyCode::F4, "F4"},
    {KeyCode::F5, "F5"}, {KeyCode::F6, "F6"}, {KeyCode::F7, "F7"}, {KeyCode::F8, "F8"},
    {KeyCode::F9, "F9"}, {KeyCode::F10, "F10"}, {KeyCode::F11, "F11"}, {KeyCode::F12, "F12"},
    {KeyCode::LeftShift, "LeftShift"}, {KeyCode::RightShift, "RightShift"},
    {KeyCode::LeftControl, "LeftControl"}, {KeyCode::RightControl, "RightControl"},
    {KeyCode::LeftAlt, "LeftAlt"}, {KeyCode::RightAlt, "RightAlt"},
    {KeyCode::MouseLeft, "MouseLeft"}, {KeyCode::MouseRight, "MouseRight"},
    {KeyCode::MouseMiddle, "MouseMiddle"}, {KeyCode::Mouse4, "Mouse4"}, {KeyCode::Mouse5, "Mouse5"},
    {KeyCode::GamepadA, "GamepadA"}, {KeyCode::GamepadB, "GamepadB"},
    {KeyCode::GamepadX, "GamepadX"}, {KeyCode::GamepadY, "GamepadY"},
    {KeyCode::GamepadLeftBumper, "GamepadLeftBumper"}, {KeyCode::GamepadRightBumper, "GamepadRightBumper"},
    {KeyCode::GamepadBack, "GamepadBack"}, {KeyCode::GamepadStart, "GamepadStart"},
    {KeyCode::GamepadGuide, "GamepadGuide"}, {KeyCode::GamepadLeftThumb, "GamepadLeftThumb"},
    {KeyCode::GamepadRightThumb, "GamepadRightThumb"},
    {KeyCode::GamepadDPadUp, "GamepadDPadUp"}, {KeyCode::GamepadDPadRight, "GamepadDPadRight"},
    {KeyCode::GamepadDPadDown, "GamepadDPadDown"}, {KeyCode::GamepadDPadLeft, "GamepadDPadLeft"},
    {KeyCode::GamepadLStickX, "GamepadLStickX"}, {KeyCode::GamepadLStickY, "GamepadLStickY"},
    {KeyCode::GamepadRStickX, "GamepadRStickX"}, {KeyCode::GamepadRStickY, "GamepadRStickY"},
    {KeyCode::GamepadLTrigger, "GamepadLTrigger"}, {KeyCode::GamepadRTrigger, "GamepadRTrigger"}
};

/** @brief Perfil padrão: teclado + mouse com alternativas de gamepad. */
InputProfile makeDefaultProfile() {
    InputProfile p;
    p.name = "Padrao";
    p.bindings = {
        {"MoveLeft", KeyCode::A, KeyCode::Left},
        {"MoveRight", KeyCode::D, KeyCode::Right},
        {"MoveUp", KeyCode::W, KeyCode::Up},
        {"MoveDown", KeyCode::S, KeyCode::Down},
        {"Jump", KeyCode::Space, KeyCode::GamepadA},
        {"Fire", KeyCode::MouseLeft, KeyCode::GamepadRTrigger},
        {"Interact", KeyCode::E, KeyCode::GamepadX},
        {"Pause", KeyCode::Escape, KeyCode::GamepadStart},
        {"SceneInfo", KeyCode::R, KeyCode::GamepadY}
    };
    p.axes = {
        {"Horizontal", KeyCode::D, KeyCode::A},
        {"Vertical", KeyCode::W, KeyCode::S}
    };
    return p;
}

/** @brief Perfil alternativo focado em gamepad. */
InputProfile makeGamepadProfile() {
    InputProfile p;
    p.name = "Gamepad";
    p.bindings = {
        {"MoveLeft", KeyCode::GamepadDPadLeft, KeyCode::A},
        {"MoveRight", KeyCode::GamepadDPadRight, KeyCode::D},
        {"MoveUp", KeyCode::GamepadDPadUp, KeyCode::W},
        {"MoveDown", KeyCode::GamepadDPadDown, KeyCode::S},
        {"Jump", KeyCode::GamepadA, KeyCode::Space},
        {"Fire", KeyCode::GamepadRTrigger, KeyCode::MouseLeft},
        {"Interact", KeyCode::GamepadX, KeyCode::E},
        {"Pause", KeyCode::GamepadStart, KeyCode::Escape},
        {"SceneInfo", KeyCode::GamepadY, KeyCode::R}
    };
    p.axes = {
        {"Horizontal", KeyCode::GamepadLStickX, KeyCode::Unknown},
        {"Vertical", KeyCode::GamepadLStickY, KeyCode::Unknown}
    };
    return p;
}

/** @brief Eixo de gamepad com zona morta aplicada. */
float applyDeadzone(float value, float deadzone) {
    if (std::abs(value) < deadzone) {
        return 0.0f;
    }
    return value;
}

} // namespace

void InputManager::init(GLFWwindow* window) {
    window_ = window;
    current_.fill(false);
    previous_.fill(false);
    gamepadAxes_.fill(0.0f);
    firstMouseSample_ = true;
    mouseDelta_ = glm::vec2(0.0f);
    g_cursorCaptured = false;
    if (profiles_.empty()) {
        profiles_.push_back(makeDefaultProfile());
        profiles_.push_back(makeGamepadProfile());
        activeProfile_ = 0;
    }
}

void InputManager::shutdown() {
    if (window_ && g_cursorCaptured) {
        glfwSetInputMode(window_, GLFW_CURSOR, GLFW_CURSOR_NORMAL);
    }
    g_cursorCaptured = false;
    window_ = nullptr;
}

bool InputManager::sampleKey(KeyCode key) {
    const int code = static_cast<int>(key);
    if (!window_) {
        return false;
    }
    if (code >= 0 && code <= GLFW_KEY_LAST) {
        return glfwGetKey(window_, code) == GLFW_PRESS;
    }
    if (code >= static_cast<int>(KeyCode::MouseLeft) && code <= static_cast<int>(KeyCode::Mouse5)) {
        return glfwGetMouseButton(window_, code - static_cast<int>(KeyCode::MouseLeft)) == GLFW_PRESS;
    }
    if (code >= static_cast<int>(KeyCode::GamepadA) && code <= static_cast<int>(KeyCode::GamepadDPadLeft)) {
        GLFWgamepadstate state;
        if (glfwGetGamepadState(GLFW_JOYSTICK_1, &state)) {
            const int button = code - static_cast<int>(KeyCode::GamepadA);
            return state.buttons[button] == GLFW_PRESS;
        }
        return false;
    }
    if (code >= static_cast<int>(KeyCode::GamepadLStickX) && code <= static_cast<int>(KeyCode::GamepadRTrigger)) {
        return std::abs(getAxisValue(key)) > 0.5f;
    }
    return false;
}

void InputManager::update() {
    previous_ = current_;
    if (!window_) {
        return;
    }
    for (int code = GLFW_KEY_SPACE; code <= GLFW_KEY_LAST; ++code) {
        current_[code] = glfwGetKey(window_, code) == GLFW_PRESS;
    }
    for (int i = 0; i < 5; ++i) {
        current_[static_cast<int>(KeyCode::MouseLeft) + i] = glfwGetMouseButton(window_, i) == GLFW_PRESS;
    }
    GLFWgamepadstate state;
    if (glfwGetGamepadState(GLFW_JOYSTICK_1, &state)) {
        for (int i = 0; i <= GLFW_GAMEPAD_BUTTON_DPAD_LEFT; ++i) {
            current_[static_cast<int>(KeyCode::GamepadA) + i] = state.buttons[i] == GLFW_PRESS;
        }
        for (int i = 0; i < 6; ++i) {
            gamepadAxes_[i] = state.axes[i];
        }
    } else {
        gamepadAxes_.fill(0.0f);
    }

    double mx = 0.0;
    double my = 0.0;
    glfwGetCursorPos(window_, &mx, &my);
    const glm::vec2 pos(static_cast<float>(mx), static_cast<float>(my));
    mouseDelta_ = firstMouseSample_ ? glm::vec2(0.0f) : pos - mousePosition_;
    mousePosition_ = pos;
    firstMouseSample_ = false;
}

bool InputManager::isKeyPressed(KeyCode key) {
    const int code = static_cast<int>(key);
    if (code < 0 || code >= kMaxCode) {
        return false;
    }
    return current_[code] && !previous_[code];
}

bool InputManager::isKeyDown(KeyCode key) {
    const int code = static_cast<int>(key);
    if (code < 0 || code >= kMaxCode) {
        return false;
    }
    return current_[code];
}

bool InputManager::isKeyUp(KeyCode key) {
    const int code = static_cast<int>(key);
    if (code < 0 || code >= kMaxCode) {
        return false;
    }
    return !current_[code] && previous_[code];
}

bool InputManager::isActionDown(const std::string& actionName) {
    for (const auto& binding : bindings()) {
        if (binding.actionName == actionName &&
            (isKeyDown(binding.primaryKey) || isKeyDown(binding.secondaryKey))) {
            return true;
        }
    }
    return false;
}

bool InputManager::isActionPressed(const std::string& actionName) {
    for (const auto& binding : bindings()) {
        if (binding.actionName == actionName &&
            (isKeyPressed(binding.primaryKey) || isKeyPressed(binding.secondaryKey))) {
            return true;
        }
    }
    return false;
}

glm::vec2 InputManager::getMousePosition() {
    return mousePosition_;
}

glm::vec2 InputManager::getMouseDelta() {
    return mouseDelta_;
}

void InputManager::setCursorCaptured(bool captured) {
    g_cursorCaptured = captured;
    if (!window_) {
        return;
    }
    glfwSetInputMode(window_, GLFW_CURSOR,
                     captured ? GLFW_CURSOR_DISABLED : GLFW_CURSOR_NORMAL);
    // Evita um salto de delta ao alternar o modo do cursor.
    firstMouseSample_ = true;
    mouseDelta_ = glm::vec2(0.0f);
}

bool InputManager::isCursorCaptured() {
    return g_cursorCaptured;
}

float InputManager::getAxisValue(KeyCode axis) {
    const int index = static_cast<int>(axis) - static_cast<int>(KeyCode::GamepadLStickX);
    if (index < 0 || index >= 6) {
        return 0.0f;
    }
    float value = gamepadAxes_[index];
    if (index == GLFW_GAMEPAD_AXIS_LEFT_Y || index == GLFW_GAMEPAD_AXIS_RIGHT_Y) {
        value = -value; // GLFW: Y positivo para baixo; convenção da engine: cima = +1.
    }
    if (index == GLFW_GAMEPAD_AXIS_LEFT_TRIGGER || index == GLFW_GAMEPAD_AXIS_RIGHT_TRIGGER) {
        return std::clamp((value + 1.0f) * 0.5f, 0.0f, 1.0f);
    }
    return applyDeadzone(value, deadzone_);
}

float InputManager::getAxis(const std::string& axisName) {
    float value = 0.0f;
    if (axisName == "Horizontal") {
        if (isKeyDown(KeyCode::D) || isKeyDown(KeyCode::Right)) value += 1.0f;
        if (isKeyDown(KeyCode::A) || isKeyDown(KeyCode::Left)) value -= 1.0f;
        value += getAxisValue(KeyCode::GamepadLStickX);
        return std::clamp(value, -1.0f, 1.0f);
    }
    if (axisName == "Vertical") {
        if (isKeyDown(KeyCode::W) || isKeyDown(KeyCode::Up)) value += 1.0f;
        if (isKeyDown(KeyCode::S) || isKeyDown(KeyCode::Down)) value -= 1.0f;
        value += getAxisValue(KeyCode::GamepadLStickY);
        return std::clamp(value, -1.0f, 1.0f);
    }
    for (const auto& axis : profiles_[activeProfile_].axes) {
        if (axis.name != axisName) {
            continue;
        }
        if (isKeyDown(axis.positive)) value += 1.0f;
        if (isKeyDown(axis.negative)) value -= 1.0f;
        const int posCode = static_cast<int>(axis.positive);
        if (posCode >= static_cast<int>(KeyCode::GamepadLStickX) &&
            posCode <= static_cast<int>(KeyCode::GamepadRTrigger)) {
            value += getAxisValue(axis.positive);
        }
        return std::clamp(value, -1.0f, 1.0f);
    }
    return 0.0f;
}

void InputManager::addBinding(const std::string& actionName, KeyCode primary, KeyCode secondary) {
    auto& list = profiles_[activeProfile_].bindings;
    for (auto& binding : list) {
        if (binding.actionName == actionName) {
            binding.primaryKey = primary;
            binding.secondaryKey = secondary;
            return;
        }
    }
    list.push_back({actionName, primary, secondary});
}

void InputManager::addAxis(const std::string& axisName, KeyCode positive, KeyCode negative) {
    auto& list = profiles_[activeProfile_].axes;
    for (auto& axis : list) {
        if (axis.name == axisName) {
            axis.positive = positive;
            axis.negative = negative;
            return;
        }
    }
    list.push_back({axisName, positive, negative});
}

const std::vector<InputBinding>& InputManager::bindings() {
    return profiles_[activeProfile_].bindings;
}

const std::vector<InputProfile>& InputManager::profiles() {
    return profiles_;
}

void InputManager::setProfiles(std::vector<InputProfile> profiles) {
    profiles_ = std::move(profiles);
    if (profiles_.empty()) {
        profiles_.push_back(makeDefaultProfile());
    }
    activeProfile_ = std::clamp(activeProfile_, 0, static_cast<int>(profiles_.size()) - 1);
}

int InputManager::activeProfile() {
    return activeProfile_;
}

void InputManager::setActiveProfile(int index) {
    if (index >= 0 && index < static_cast<int>(profiles_.size())) {
        activeProfile_ = index;
        Log::info("InputManager: perfil ativo = '" + profiles_[index].name + "'");
    }
}

void InputManager::loadBindingsFromFile(const std::string& filePath) {
    const std::string text = FS::readText(filePath);
    if (text.empty()) {
        return;
    }
    try {
        const Json::Value root = Json::Value::parse(text);
        std::vector<InputProfile> loaded;
        if (const Json::Value* profiles = root.find("profiles")) {
            for (const auto& profileJson : profiles->arr) {
                InputProfile profile;
                profile.name = profileJson["name"].asString("Padrao");
                if (const Json::Value* bindingsJson = profileJson.find("bindings")) {
                    for (const auto& bindingJson : bindingsJson->arr) {
                        InputBinding binding;
                        binding.actionName = bindingJson["action"].asString();
                        binding.primaryKey = keyFromString(bindingJson["primary"].asString());
                        binding.secondaryKey = keyFromString(bindingJson["secondary"].asString());
                        profile.bindings.push_back(binding);
                    }
                }
                if (const Json::Value* axesJson = profileJson.find("axes")) {
                    for (const auto& axisJson : axesJson->arr) {
                        AxisBinding axis;
                        axis.name = axisJson["name"].asString();
                        axis.positive = keyFromString(axisJson["positive"].asString());
                        axis.negative = keyFromString(axisJson["negative"].asString());
                        profile.axes.push_back(axis);
                    }
                }
                loaded.push_back(std::move(profile));
            }
        }
        if (!loaded.empty()) {
            profiles_ = std::move(loaded);
            activeProfile_ = std::clamp(static_cast<int>(root["activeProfile"].asNumber(0)),
                                        0, static_cast<int>(profiles_.size()) - 1);
            Log::info("InputManager: bindings carregados de " + filePath);
        }
    } catch (const std::exception& e) {
        Log::warn(std::string("InputManager: falha ao ler bindings: ") + e.what());
    }
}

void InputManager::saveBindingsToFile(const std::string& filePath) {
    Json::Value root = Json::Value::object();
    root["activeProfile"] = Json::Value::ofNumber(activeProfile_);
    Json::Value profilesJson = Json::Value::array();
    for (const auto& profile : profiles_) {
        Json::Value profileJson = Json::Value::object();
        profileJson["name"] = Json::Value::ofString(profile.name);
        Json::Value bindingsJson = Json::Value::array();
        for (const auto& binding : profile.bindings) {
            Json::Value bindingJson = Json::Value::object();
            bindingJson["action"] = Json::Value::ofString(binding.actionName);
            bindingJson["primary"] = Json::Value::ofString(keyToString(binding.primaryKey));
            bindingJson["secondary"] = Json::Value::ofString(keyToString(binding.secondaryKey));
            bindingsJson.push(bindingJson);
        }
        profileJson["bindings"] = bindingsJson;
        Json::Value axesJson = Json::Value::array();
        for (const auto& axis : profile.axes) {
            Json::Value axisJson = Json::Value::object();
            axisJson["name"] = Json::Value::ofString(axis.name);
            axisJson["positive"] = Json::Value::ofString(keyToString(axis.positive));
            axisJson["negative"] = Json::Value::ofString(keyToString(axis.negative));
            axesJson.push(axisJson);
        }
        profileJson["axes"] = axesJson;
        profilesJson.push(profileJson);
    }
    root["profiles"] = profilesJson;
    FS::writeText(filePath, root.dump());
}

void InputManager::setGamepadDeadzone(float deadzone) {
    deadzone_ = std::clamp(deadzone, 0.0f, 0.9f);
}

bool InputManager::isGamepadConnected() {
    return glfwJoystickPresent(GLFW_JOYSTICK_1) == GLFW_TRUE &&
           glfwJoystickIsGamepad(GLFW_JOYSTICK_1) == GLFW_TRUE;
}

std::string InputManager::gamepadLabel() {
    if (!isGamepadConnected()) {
        return "Desconectado";
    }
    const char* rawName = glfwGetGamepadName(GLFW_JOYSTICK_1);
    std::string name = rawName ? rawName : "";
    std::string lower = name;
    std::transform(lower.begin(), lower.end(), lower.begin(),
                   [](unsigned char c) { return static_cast<char>(std::tolower(c)); });
    if (lower.find("xbox") != std::string::npos || lower.find("xinput") != std::string::npos) {
        return "Xbox";
    }
    if (lower.find("playstation") != std::string::npos || lower.find("dualshock") != std::string::npos ||
        lower.find("dualsense") != std::string::npos || lower.find("sony") != std::string::npos ||
        lower.find("ps4") != std::string::npos || lower.find("ps5") != std::string::npos) {
        return "PlayStation";
    }
    return "Generico";
}

std::string InputManager::keyToString(KeyCode key) {
    for (const auto& entry : kKeyNames) {
        if (entry.code == key) {
            return entry.name;
        }
    }
    return "Unknown";
}

KeyCode InputManager::keyFromString(const std::string& name) {
    for (const auto& entry : kKeyNames) {
        if (name == entry.name) {
            return entry.code;
        }
    }
    return KeyCode::Unknown;
}

} // namespace VelvX