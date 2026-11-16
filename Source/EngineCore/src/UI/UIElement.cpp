/**
 * @file UIElement.cpp
 * @brief Elementos de UI: desenho via Renderer 2D e serialização JSON.
 */

#include "UI/UIElement.hpp"
#include "UI/UIManager.hpp"
#include "Renderer/Renderer.hpp"
#include "Renderer/TextRenderer.hpp"
#include "Utils/Json.hpp"
#include <utility>

namespace VelvX {

namespace {

/** @brief vec2 como array JSON. */
Json::Value vec2Json(const glm::vec2& v) {
    Json::Value arr = Json::Value::array();
    arr.push(Json::Value::ofNumber(v.x));
    arr.push(Json::Value::ofNumber(v.y));
    return arr;
}

/** @brief vec4 como array JSON. */
Json::Value vec4Json(const glm::vec4& v) {
    Json::Value arr = Json::Value::array();
    arr.push(Json::Value::ofNumber(v.x));
    arr.push(Json::Value::ofNumber(v.y));
    arr.push(Json::Value::ofNumber(v.z));
    arr.push(Json::Value::ofNumber(v.w));
    return arr;
}

/** @brief Lê vec2 de array JSON. */
glm::vec2 jsonVec2(const Json::Value* json, const glm::vec2& def) {
    if (!json || json->type != Json::Value::Type::Array || json->arr.size() < 2) {
        return def;
    }
    return glm::vec2(static_cast<float>(json->arr[0].asNumber(def.x)),
                     static_cast<float>(json->arr[1].asNumber(def.y)));
}

/** @brief Lê vec4 de array JSON. */
glm::vec4 jsonVec4(const Json::Value* json, const glm::vec4& def) {
    if (!json || json->type != Json::Value::Type::Array || json->arr.size() < 4) {
        return def;
    }
    return glm::vec4(static_cast<float>(json->arr[0].asNumber(def.x)),
                     static_cast<float>(json->arr[1].asNumber(def.y)),
                     static_cast<float>(json->arr[2].asNumber(def.z)),
                     static_cast<float>(json->arr[3].asNumber(def.w)));
}

} // namespace

UIElement* UIElement::addChild(std::unique_ptr<UIElement> child) {
    UIElement* raw = child.get();
    children.push_back(std::move(child));
    return raw;
}

UIElement* UIElement::findByName(const std::string& childName) {
    if (name == childName) {
        return this;
    }
    for (const auto& child : children) {
        if (UIElement* found = child->findByName(childName)) {
            return found;
        }
    }
    return nullptr;
}

glm::vec2 UIElement::resolvePosition(const glm::vec2& parentOrigin, const glm::vec2& parentSize) const {
    const glm::vec2 s = size();
    glm::vec2 anchorOffset(0.0f);
    switch (anchor) {
        case Anchor::TopCenter: case Anchor::Center: case Anchor::BottomCenter:
            anchorOffset.x = (parentSize.x - s.x) * 0.5f;
            break;
        case Anchor::TopRight: case Anchor::CenterRight: case Anchor::BottomRight:
            anchorOffset.x = parentSize.x - s.x;
            break;
        default:
            break;
    }
    switch (anchor) {
        case Anchor::CenterLeft: case Anchor::Center: case Anchor::CenterRight:
            anchorOffset.y = (parentSize.y - s.y) * 0.5f;
            break;
        case Anchor::BottomLeft: case Anchor::BottomCenter: case Anchor::BottomRight:
            anchorOffset.y = parentSize.y - s.y;
            break;
        default:
            break;
    }
    const glm::vec2 pos(transform.position.x, transform.position.y);
    const glm::vec2 offset = relativePosition ? pos * parentSize : pos;
    return parentOrigin + offset + anchorOffset;
}

bool UIElement::contains(const glm::vec2& point) const {
    const glm::vec2 s = size();
    return point.x >= resolvedPosition.x && point.x <= resolvedPosition.x + s.x &&
           point.y >= resolvedPosition.y && point.y <= resolvedPosition.y + s.y;
}

Json::Value UIElement::toJson() const {
    Json::Value json = Json::Value::object();
    json["type"] = Json::Value::ofString(typeName());
    json["name"] = Json::Value::ofString(name);
    json["position"] = vec2Json(glm::vec2(transform.position.x, transform.position.y));
    json["size"] = vec2Json(size());
    json["anchor"] = Json::Value::ofString(anchorToString(anchor));
    json["relative"] = Json::Value::ofBool(relativePosition);
    json["visible"] = Json::Value::ofBool(isVisible);
    json["interactable"] = Json::Value::ofBool(isInteractable);
    json["script"] = Json::Value::ofString(scriptBinding);
    json["color"] = vec4Json(color);
    Json::Value childArray = Json::Value::array();
    for (const auto& child : children) {
        childArray.push(child->toJson());
    }
    json["children"] = childArray;
    return json;
}

void UIElement::fromJson(const Json::Value& json) {
    name = json["name"].asString(name);
    const glm::vec2 pos = jsonVec2(json.find("position"), glm::vec2(transform.position.x, transform.position.y));
    transform.position = glm::vec3(pos.x, pos.y, 0.0f);
    const glm::vec2 s = jsonVec2(json.find("size"), size());
    transform.scale = glm::vec3(s.x, s.y, 1.0f);
    anchor = anchorFromString(json["anchor"].asString("TopLeft"));
    relativePosition = json["relative"].asBool(relativePosition);
    isVisible = json["visible"].asBool(isVisible);
    isInteractable = json["interactable"].asBool(isInteractable);
    scriptBinding = json["script"].asString(scriptBinding);
    color = jsonVec4(json.find("color"), color);
}

std::string UIElement::anchorToString(Anchor anchor) {
    switch (anchor) {
        case Anchor::TopLeft: return "TopLeft";
        case Anchor::TopCenter: return "TopCenter";
        case Anchor::TopRight: return "TopRight";
        case Anchor::CenterLeft: return "CenterLeft";
        case Anchor::Center: return "Center";
        case Anchor::CenterRight: return "CenterRight";
        case Anchor::BottomLeft: return "BottomLeft";
        case Anchor::BottomCenter: return "BottomCenter";
        case Anchor::BottomRight: return "BottomRight";
    }
    return "TopLeft";
}

UIElement::Anchor UIElement::anchorFromString(const std::string& name) {
    if (name == "TopCenter") return Anchor::TopCenter;
    if (name == "TopRight") return Anchor::TopRight;
    if (name == "CenterLeft") return Anchor::CenterLeft;
    if (name == "Center") return Anchor::Center;
    if (name == "CenterRight") return Anchor::CenterRight;
    if (name == "BottomLeft") return Anchor::BottomLeft;
    if (name == "BottomCenter") return Anchor::BottomCenter;
    if (name == "BottomRight") return Anchor::BottomRight;
    return Anchor::TopLeft;
}

void UIButton::render() {
    const glm::vec2 s = size();
    Renderer::drawSprite(resolvedPosition, s, glm::vec3(color), color.a);
    const glm::vec2 textSize = TextRenderer::measure(label, fontSize);
    const glm::vec2 textPos = resolvedPosition + (s - textSize) * 0.5f;
    TextRenderer::draw(label, textPos, fontSize, textColor);
}

void UIButton::onClick() {
    if (onButtonClicked) {
        onButtonClicked();
    }
    if (!scriptBinding.empty() && UIManager::onScriptEvent) {
        UIManager::onScriptEvent(scriptBinding);
    }
}

Json::Value UIButton::toJson() const {
    Json::Value json = UIElement::toJson();
    json["label"] = Json::Value::ofString(label);
    json["fontSize"] = Json::Value::ofNumber(fontSize);
    json["textColor"] = vec4Json(textColor);
    return json;
}

void UIButton::fromJson(const Json::Value& json) {
    UIElement::fromJson(json);
    label = json["label"].asString(label);
    fontSize = static_cast<float>(json["fontSize"].asNumber(fontSize));
    textColor = jsonVec4(json.find("textColor"), textColor);
}

void UIText::render() {
    glm::vec2 pos = resolvedPosition;
    if (anchor == Anchor::TopCenter || anchor == Anchor::Center || anchor == Anchor::BottomCenter) {
        const glm::vec2 textSize = TextRenderer::measure(text_, fontSize);
        pos.x += (size().x - textSize.x) * 0.5f;
    }
    TextRenderer::draw(text_, pos, fontSize, color);
}

Json::Value UIText::toJson() const {
    Json::Value json = UIElement::toJson();
    json["text"] = Json::Value::ofString(text_);
    json["fontSize"] = Json::Value::ofNumber(fontSize);
    return json;
}

void UIText::fromJson(const Json::Value& json) {
    UIElement::fromJson(json);
    text_ = json["text"].asString(text_);
    fontSize = static_cast<float>(json["fontSize"].asNumber(fontSize));
}

void UIImage::render() {
    const glm::vec2 s = size();
    if (!texturePath.empty()) {
        Renderer::drawSpriteTextured(resolvedPosition, s, texturePath, glm::vec3(color), color.a);
    } else {
        Renderer::drawSprite(resolvedPosition, s, glm::vec3(color), color.a);
    }
}

Json::Value UIImage::toJson() const {
    Json::Value json = UIElement::toJson();
    json["texture"] = Json::Value::ofString(texturePath);
    return json;
}

void UIImage::fromJson(const Json::Value& json) {
    UIElement::fromJson(json);
    texturePath = json["texture"].asString(texturePath);
}

void UIPanel::render() {
    Renderer::drawSprite(resolvedPosition, size(), glm::vec3(color), color.a);
}

} // namespace VelvX