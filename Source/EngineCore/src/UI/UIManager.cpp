/**
 * @file UIManager.cpp
 * @brief Runtime de UI: layout, cliques e prefabs .ui.
 */

#include "UI/UIManager.hpp"
#include "Core/Log.hpp"
#include "Input/InputManager.hpp"
#include "Utils/FileSystem.hpp"
#include "Utils/Json.hpp"
#include <algorithm>

namespace VelvX {

std::vector<std::unique_ptr<UIElement>> UIManager::uiElements_;
glm::vec2 UIManager::screenSize_{1280.0f, 720.0f};
std::function<void(const std::string&)> UIManager::onScriptEvent;

namespace {

/** @brief Fábrica de elementos por nome de tipo. */
std::unique_ptr<UIElement> createElement(const std::string& type) {
    if (type == "Button") return std::make_unique<UIButton>();
    if (type == "Text") return std::make_unique<UIText>();
    if (type == "Image") return std::make_unique<UIImage>();
    if (type == "Panel") return std::make_unique<UIPanel>();
    return std::make_unique<UIElement>();
}

/** @brief Constrói a árvore de UI a partir do JSON do prefab. */
std::unique_ptr<UIElement> elementFromJson(const Json::Value& json) {
    auto element = createElement(json["type"].asString("Element"));
    element->fromJson(json);
    if (const Json::Value* children = json.find("children")) {
        for (const auto& childJson : children->arr) {
            element->addChild(elementFromJson(childJson));
        }
    }
    return element;
}

} // namespace

void UIManager::init() {
    uiElements_.clear();
}

void UIManager::shutdown() {
    uiElements_.clear();
    onScriptEvent = nullptr;
}

void UIManager::update(float deltaTime) {
    for (const auto& element : uiElements_) {
        updateElement(*element, deltaTime);
    }
    if (InputManager::isKeyPressed(KeyCode::MouseLeft)) {
        handleClick(InputManager::getMousePosition());
    }
}

void UIManager::updateElement(UIElement& element, float deltaTime) {
    element.update(deltaTime);
    for (const auto& child : element.children) {
        updateElement(*child, deltaTime);
    }
}

void UIManager::render() {
    for (const auto& element : uiElements_) {
        renderElement(*element, glm::vec2(0.0f), screenSize_);
    }
}

void UIManager::renderElement(UIElement& element, const glm::vec2& origin, const glm::vec2& area) {
    if (!element.isVisible) {
        return;
    }
    element.resolvedPosition = element.resolvePosition(origin, area);
    element.render();
    for (const auto& child : element.children) {
        renderElement(*child, element.resolvedPosition, element.size());
    }
}

void UIManager::addUI(std::unique_ptr<UIElement> ui) {
    if (ui) {
        uiElements_.push_back(std::move(ui));
    }
}

void UIManager::removeUI(UIElement* ui) {
    uiElements_.erase(std::remove_if(uiElements_.begin(), uiElements_.end(),
                                     [ui](const std::unique_ptr<UIElement>& element) {
                                         return element.get() == ui;
                                     }),
                      uiElements_.end());
}

const std::vector<std::unique_ptr<UIElement>>& UIManager::elements() {
    return uiElements_;
}

UIElement* UIManager::findByName(const std::string& name) {
    for (const auto& element : uiElements_) {
        if (UIElement* found = element->findByName(name)) {
            return found;
        }
    }
    return nullptr;
}

void UIManager::setScreenSize(float width, float height) {
    screenSize_ = glm::vec2(width, height);
}

glm::vec2 UIManager::screenSize() {
    return screenSize_;
}

bool UIManager::handleClick(const glm::vec2& point) {
    for (auto it = uiElements_.rbegin(); it != uiElements_.rend(); ++it) {
        if (clickElement(**it, point)) {
            return true;
        }
    }
    return false;
}

bool UIManager::clickElement(UIElement& element, const glm::vec2& point) {
    if (!element.isVisible) {
        return false;
    }
    for (auto it = element.children.rbegin(); it != element.children.rend(); ++it) {
        if (clickElement(**it, point)) {
            return true;
        }
    }
    if (element.isInteractable && element.contains(point)) {
        element.onClick();
        return true;
    }
    return false;
}

std::unique_ptr<UIElement> UIManager::loadUIPrefab(const std::string& filePath) {
    std::filesystem::path path(filePath);
    if (path.is_relative()) path = FS::resolveProject(filePath);
    const std::string text = FS::readText(path);
    if (text.empty()) {
        Log::warn("UIManager: prefab não encontrado: " + path.string());
        return nullptr;
    }
    try {
        auto element = elementFromJson(Json::Value::parse(text));
        Log::info("UIManager: prefab '" + element->name + "' carregado de " + filePath);
        return element;
    } catch (const std::exception& e) {
        Log::error(std::string("UIManager: prefab inválido: ") + e.what());
        return nullptr;
    }
}

void UIManager::saveUIPrefab(const UIElement& ui, const std::string& filePath) {
    FS::writeText(filePath, ui.toJson().dump());
    Log::info("UIManager: prefab salvo em " + filePath);
}

} // namespace VelvX