/**
 * @file ProjectSettings.cpp
 * @brief Serialização JSON das configurações de projeto.
 */

#include "Core/ProjectSettings.hpp"
#include "Core/Log.hpp"
#include "Utils/FileSystem.hpp"
#include "Utils/Json.hpp"
#include <algorithm>

namespace VelvX {

namespace {

/** @brief Serializa um perfil de input no schema do InputManager. */
Json::Value profileToJson(const InputProfile& profile) {
    Json::Value json = Json::Value::object();
    json["name"] = Json::Value::ofString(profile.name);
    Json::Value bindings = Json::Value::array();
    for (const auto& binding : profile.bindings) {
        Json::Value item = Json::Value::object();
        item["action"] = Json::Value::ofString(binding.actionName);
        item["primary"] = Json::Value::ofString(InputManager::keyToString(binding.primaryKey));
        item["secondary"] = Json::Value::ofString(InputManager::keyToString(binding.secondaryKey));
        bindings.push(item);
    }
    json["bindings"] = bindings;
    Json::Value axes = Json::Value::array();
    for (const auto& axis : profile.axes) {
        Json::Value item = Json::Value::object();
        item["name"] = Json::Value::ofString(axis.name);
        item["positive"] = Json::Value::ofString(InputManager::keyToString(axis.positive));
        item["negative"] = Json::Value::ofString(InputManager::keyToString(axis.negative));
        axes.push(item);
    }
    json["axes"] = axes;
    return json;
}

/** @brief Lê um perfil de input do schema do InputManager. */
InputProfile profileFromJson(const Json::Value& json) {
    InputProfile profile;
    profile.name = json["name"].asString("Padrao");
    if (const Json::Value* bindings = json.find("bindings")) {
        for (const auto& item : bindings->arr) {
            InputBinding binding;
            binding.actionName = item["action"].asString();
            binding.primaryKey = InputManager::keyFromString(item["primary"].asString());
            binding.secondaryKey = InputManager::keyFromString(item["secondary"].asString());
            profile.bindings.push_back(binding);
        }
    }
    if (const Json::Value* axes = json.find("axes")) {
        for (const auto& item : axes->arr) {
            AxisBinding axis;
            axis.name = item["name"].asString();
            axis.positive = InputManager::keyFromString(item["positive"].asString());
            axis.negative = InputManager::keyFromString(item["negative"].asString());
            profile.axes.push_back(axis);
        }
    }
    return profile;
}

} // namespace

ProjectSettings ProjectSettings::defaults() {
    ProjectSettings settings;
    InputProfile keyboard;
    keyboard.name = "Padrao";
    keyboard.bindings = {
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
    keyboard.axes = {
        {"Horizontal", KeyCode::D, KeyCode::A},
        {"Vertical", KeyCode::W, KeyCode::S}
    };
    settings.inputProfiles.push_back(keyboard);
    settings.inputBindings = keyboard.bindings;
    settings.ensureCollisionMatrix();
    return settings;
}

ProjectSettings ProjectSettings::load(const std::filesystem::path& file) {
    const std::string text = FS::readText(file);
    if (text.empty()) {
        return defaults();
    }
    try {
        return fromJson(Json::Value::parse(text));
    } catch (const std::exception& e) {
        Log::warn(std::string("ProjectSettings: JSON inválido, usando defaults: ") + e.what());
        return defaults();
    }
}

ProjectSettings ProjectSettings::fromJson(const Json::Value& root) {
    ProjectSettings settings = defaults();
    {
        if (const Json::Value* window = root.find("window")) {
            settings.windowWidth = static_cast<int>((*window)["width"].asNumber(settings.windowWidth));
            settings.windowHeight = static_cast<int>((*window)["height"].asNumber(settings.windowHeight));
            settings.fullscreen = (*window)["fullscreen"].asBool(settings.fullscreen);
            settings.windowTitle = (*window)["title"].asString(settings.windowTitle);
        }
        if (const Json::Value* graphics = root.find("graphics")) {
            settings.antiAliasingSamples = static_cast<int>((*graphics)["antiAliasing"].asNumber(settings.antiAliasingSamples));
            settings.enableShadows = (*graphics)["shadows"].asBool(settings.enableShadows);
            settings.shadowQuality = static_cast<int>((*graphics)["shadowQuality"].asNumber(settings.shadowQuality));
            settings.vsync = (*graphics)["vsync"].asBool(settings.vsync);
        }
        if (const Json::Value* audio = root.find("audio")) {
            settings.masterVolume = static_cast<float>((*audio)["master"].asNumber(settings.masterVolume));
            settings.musicVolume = static_cast<float>((*audio)["music"].asNumber(settings.musicVolume));
            settings.sfxVolume = static_cast<float>((*audio)["sfx"].asNumber(settings.sfxVolume));
        }
        if (const Json::Value* ui = root.find("interface")) {
            settings.language = (*ui)["language"].asString(settings.language);
            settings.fontScale = static_cast<float>((*ui)["fontScale"].asNumber(settings.fontScale));
            settings.highContrast = (*ui)["highContrast"].asBool(settings.highContrast);
        }
        if (const Json::Value* input = root.find("input")) {
            settings.gamepadDeadzone = static_cast<float>((*input)["deadzone"].asNumber(settings.gamepadDeadzone));
            settings.activeInputProfile = static_cast<int>((*input)["activeProfile"].asNumber(0));
            if (const Json::Value* profiles = input->find("profiles")) {
                settings.inputProfiles.clear();
                for (const auto& profileJson : profiles->arr) {
                    settings.inputProfiles.push_back(profileFromJson(profileJson));
                }
            }
            if (const Json::Value* bindings = input->find("bindings")) {
                settings.inputBindings.clear();
                for (const auto& item : bindings->arr) {
                    InputBinding binding;
                    binding.actionName = item["action"].asString();
                    binding.primaryKey = InputManager::keyFromString(item["primary"].asString());
                    binding.secondaryKey = InputManager::keyFromString(item["secondary"].asString());
                    settings.inputBindings.push_back(binding);
                }
            }
        }
        if (settings.inputProfiles.empty() && !settings.inputBindings.empty()) {
            InputProfile profile;
            profile.name = "Padrao";
            profile.bindings = settings.inputBindings;
            settings.inputProfiles.push_back(profile);
        }
        settings.activeInputProfile = std::clamp(settings.activeInputProfile, 0,
                                                 std::max(0, static_cast<int>(settings.inputProfiles.size()) - 1));
        if (const Json::Value* physics = root.find("physics")) {
            settings.useGroundPlane = physics->find("groundPlane") ? physics->find("groundPlane")->asBool(true) : true;
            if (const Json::Value* layers = physics->find("layers")) {
                settings.collisionLayers.clear();
                for (const auto& layer : layers->arr) {
                    settings.collisionLayers.push_back(layer.asString("Layer"));
                }
            }
            settings.ensureCollisionMatrix();
            if (const Json::Value* matrix = physics->find("matrix")) {
                const int n = static_cast<int>(settings.collisionLayers.size());
                for (int i = 0; i < n && i < static_cast<int>(matrix->arr.size()); ++i) {
                    const auto& row = matrix->arr[i];
                    for (int j = 0; j < n && j < static_cast<int>(row.arr.size()); ++j) {
                        settings.collisionMatrix[i][j] = row.arr[j].asBool(true);
                    }
                }
            }
        }
    }
    return settings;
}

void ProjectSettings::save(const std::filesystem::path& file) const {
    FS::writeText(file, toJson().dump());
}

Json::Value ProjectSettings::toJson() const {
    Json::Value root = Json::Value::object();

    Json::Value window = Json::Value::object();
    window["width"] = Json::Value::ofNumber(windowWidth);
    window["height"] = Json::Value::ofNumber(windowHeight);
    window["fullscreen"] = Json::Value::ofBool(fullscreen);
    window["title"] = Json::Value::ofString(windowTitle);
    root["window"] = window;

    Json::Value graphics = Json::Value::object();
    graphics["antiAliasing"] = Json::Value::ofNumber(antiAliasingSamples);
    graphics["shadows"] = Json::Value::ofBool(enableShadows);
    graphics["shadowQuality"] = Json::Value::ofNumber(shadowQuality);
    graphics["vsync"] = Json::Value::ofBool(vsync);
    root["graphics"] = graphics;

    Json::Value audio = Json::Value::object();
    audio["master"] = Json::Value::ofNumber(masterVolume);
    audio["music"] = Json::Value::ofNumber(musicVolume);
    audio["sfx"] = Json::Value::ofNumber(sfxVolume);
    root["audio"] = audio;

    Json::Value ui = Json::Value::object();
    ui["language"] = Json::Value::ofString(language);
    ui["fontScale"] = Json::Value::ofNumber(fontScale);
    ui["highContrast"] = Json::Value::ofBool(highContrast);
    root["interface"] = ui;

    Json::Value input = Json::Value::object();
    input["deadzone"] = Json::Value::ofNumber(gamepadDeadzone);
    input["activeProfile"] = Json::Value::ofNumber(activeInputProfile);
    Json::Value profiles = Json::Value::array();
    for (const auto& profile : inputProfiles) {
        profiles.push(profileToJson(profile));
    }
    input["profiles"] = profiles;
    Json::Value bindings = Json::Value::array();
    for (const auto& binding : inputBindings) {
        Json::Value item = Json::Value::object();
        item["action"] = Json::Value::ofString(binding.actionName);
        item["primary"] = Json::Value::ofString(InputManager::keyToString(binding.primaryKey));
        item["secondary"] = Json::Value::ofString(InputManager::keyToString(binding.secondaryKey));
        bindings.push(item);
    }
    input["bindings"] = bindings;
    root["input"] = input;

    Json::Value physics = Json::Value::object();
    Json::Value layers = Json::Value::array();
    for (const auto& layer : collisionLayers) {
        layers.push(Json::Value::ofString(layer));
    }
    physics["layers"] = layers;
    Json::Value matrix = Json::Value::array();
    for (const auto& row : collisionMatrix) {
        Json::Value rowJson = Json::Value::array();
        for (bool cell : row) {
            rowJson.push(Json::Value::ofBool(cell));
        }
        matrix.push(rowJson);
    }
    physics["matrix"] = matrix;
    physics["groundPlane"] = Json::Value::ofBool(useGroundPlane);
    root["physics"] = physics;

    return root;
}

void ProjectSettings::applyInput() const {
    if (!inputProfiles.empty()) {
        InputManager::setProfiles(inputProfiles);
        InputManager::setActiveProfile(activeInputProfile);
    } else if (!inputBindings.empty()) {
        InputProfile profile;
        profile.name = "Padrao";
        profile.bindings = inputBindings;
        InputManager::setProfiles({profile});
        InputManager::setActiveProfile(0);
    }
    InputManager::setGamepadDeadzone(gamepadDeadzone);
}

int ProjectSettings::shadowMapSize() const {
    switch (std::clamp(shadowQuality, 0, 3)) {
        case 0: return 512;
        case 1: return 1024;
        case 3: return 4096;
        default: return 2048;
    }
}

void ProjectSettings::ensureCollisionMatrix() {
    const size_t n = collisionLayers.size();
    collisionMatrix.resize(n);
    for (auto& row : collisionMatrix) {
        row.resize(n, true);
    }
}

bool ProjectSettings::layerCollides(int a, int b) const {
    const int n = static_cast<int>(collisionLayers.size());
    if (a < 0 || b < 0 || a >= n || b >= n) {
        return true;
    }
    if (a >= static_cast<int>(collisionMatrix.size()) ||
        b >= static_cast<int>(collisionMatrix[a].size())) {
        return true;
    }
    return collisionMatrix[a][b];
}

int ProjectSettings::layerIndex(const std::string& name) const {
    for (size_t i = 0; i < collisionLayers.size(); ++i) {
        if (collisionLayers[i] == name) {
            return static_cast<int>(i);
        }
    }
    return -1;
}

} // namespace VelvX