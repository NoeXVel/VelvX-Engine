/**
 * @file Panels.cpp
 * @brief Painéis docking do VelvX Editor: Hierarchy, Inspector, AssetBrowser,
 *        Console e SceneViewport.
 */

#include "EditorApp.hpp"
#include "Core/AssetBrowser.hpp"
#include <glad/gl.h>
#include <stb_image.h>
#include "Core/GameObject.hpp"
#include "Core/Inspector.hpp"
#include "Core/Log.hpp"
#include "Core/Scene.hpp"
#include "Core/SceneManager.hpp"
#include "Engine/Engine.hpp"
#include "Physics/PhysicsComponent.hpp"
#include "PluginAPI/ComponentRegistry.hpp"
#include "Renderer/RenderComponent.hpp"
#include "Renderer/Camera.hpp"
#include "Renderer/Renderer.hpp"
#include "Scripting/ScriptComponent.hpp"
#include "Utils/FileSystem.hpp"
#include <imgui.h>
#include <misc/cpp/imgui_stdlib.h>
#include <glm/gtc/quaternion.hpp>
#include <glm/gtc/matrix_inverse.hpp>
#include <glm/gtc/matrix_transform.hpp>
#include <algorithm>
#include <cctype>
#include <cmath>
#include <cstring>
#include <cstdlib>

using namespace VelvX;

/** @brief Extensões de imagem reconhecidas pelo editor. */
static bool isImageFile(const std::filesystem::path& path) {
    const std::string ext = path.extension().string();
    return ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".bmp" || ext == ".tga";
}

/** @brief Extensões de modelo 3D suportadas pelo Assimp. */
static bool isModelFile(const std::filesystem::path& path) {
    const std::string ext = path.extension().string();
    return ext == ".fbx" || ext == ".gltf" || ext == ".glb" || ext == ".obj";
}

/** @brief Caminho relativo ao projeto com barras normais. */
static std::string projectRelative(const std::filesystem::path& path) {
    std::error_code ec;
    const std::filesystem::path rel = std::filesystem::relative(path, FS::projectRoot(), ec);
    return ec ? path.generic_string() : rel.generic_string();
}

/** @brief Abre o gerenciador de arquivos do SO destacando o asset. */
static void revealInExplorer(const std::filesystem::path& path) {
    const std::string text = path.string();
    if (text.find('"') != std::string::npos) return;
#ifdef _WIN32
    const std::string cmd = "explorer /select,\"" + text + "\"";
    std::system(cmd.c_str());
#elif defined(__APPLE__)
    const std::string cmd = "open -R \"" + text + "\"";
    std::system(cmd.c_str());
#else
    const std::string cmd = "xdg-open \"" + path.parent_path().string() + "\"";
    std::system(cmd.c_str());
#endif
}

// ---------------------------------------------------------------------------
// Hierarchy
// ---------------------------------------------------------------------------

void EditorApp::drawHierarchy() {
    ImGui::Begin("Hierarchy");
    SceneManager& sm = Engine::getSceneManager();

    std::vector<std::string> sceneNames;
    for (const auto& [name, scene] : sm.scenes()) sceneNames.push_back(name);
    std::sort(sceneNames.begin(), sceneNames.end());
    if (!sceneNames.empty()) {
        std::vector<const char*> items;
        for (const auto& n : sceneNames) items.push_back(n.c_str());
        int current = 0;
        for (size_t i = 0; i < sceneNames.size(); ++i) {
            if (sceneNames[i] == sceneName_) current = static_cast<int>(i);
        }
        ImGui::SetNextItemWidth(-1);
        if (ImGui::Combo("##cena", &current, items.data(), static_cast<int>(items.size()))) {
            loadSceneByName(sceneNames[current]);
        }
    }

    Scene* scene = sm.getActiveScene();
    if (!scene) {
        ImGui::TextDisabled("Nenhuma cena ativa (abra um projeto)");
        ImGui::End();
        return;
    }

    ImGui::BeginChild("objetos", ImVec2(0, -30), ImGuiChildFlags_Borders);
    for (const auto& object : scene->gameObjects) {
        GameObject* obj = object.get();
        ImGui::PushID(obj);
        const bool isSelected = obj == selected_;
        if (ImGui::Selectable(obj->name.c_str(), isSelected)) {
            selected_ = obj;
        }
        if (ImGui::BeginPopupContextItem("objctx")) {
            selected_ = obj;
            if (ImGui::MenuItem("Renomear")) {
                renameTarget_ = obj;
                renameBuffer_ = obj->name;
                showRename_ = true;
            }
            if (ImGui::MenuItem("Duplicar", "Ctrl+D")) duplicateSelected();
            if (ImGui::MenuItem("Excluir", "Del")) deleteSelected();
            if (ImGui::MenuItem("Focar", "F")) frameSelected();
            ImGui::Separator();
            if (ImGui::MenuItem("Copiar", "Ctrl+C")) copySelected();
            if (ImGui::MenuItem("Colar", "Ctrl+V", false, !clipboardJson_.empty())) pasteClipboard();
            ImGui::Separator();
            if (ImGui::BeginMenu("Adicionar componente")) {
                if (ImGui::MenuItem("RigidBody", nullptr, false, !obj->getComponent<RigidBody>())) {
                    pushUndo(); obj->addComponent<RigidBody>();
                }
                if (ImGui::MenuItem("StaticBody", nullptr, false, !obj->getComponent<StaticBody>())) {
                    pushUndo(); obj->addComponent<StaticBody>();
                }
                if (ImGui::MenuItem("Collider", nullptr, false, !obj->getComponent<Collider>())) {
                    pushUndo(); obj->addComponent<Collider>();
                }
                if (ImGui::MenuItem("Camera", nullptr, false, !obj->getComponent<Camera>())) {
                    pushUndo(); obj->addComponent<Camera>();
                }
                if (ImGui::MenuItem("MeshRenderer", nullptr, false, !obj->getComponent<MeshRenderer>())) {
                    pushUndo(); obj->addComponent<MeshRenderer>();
                }
                ImGui::EndMenu();
            }
            if (ImGui::BeginMenu("Anexar script")) {
                bool any = false;
                for (const auto& entry : assetEntries_) {
                    if (entry.extension() != ".velv") continue;
                    any = true;
                    if (ImGui::MenuItem(entry.filename().string().c_str())) {
                        pushUndo();
                        obj->addScript(projectRelative(FS::projectRoot() / entry));
                    }
                }
                if (!any) ImGui::TextDisabled("Nenhum .velv no projeto");
                ImGui::EndMenu();
            }
            if (ImGui::BeginMenu("Criar filho")) {
                if (ImGui::MenuItem("Objeto vazio")) {
                    pushUndo();
                    auto child = std::make_unique<GameObject>("Filho");
                    child->transform.parent = &obj->transform;
                    selected_ = Engine::getSceneManager().getActiveScene()->addObject(std::move(child));
                }
                if (ImGui::MenuItem("Cubo")) addPrimitive("cube", obj);
                if (ImGui::MenuItem("Sprite")) addPrimitive("sprite", obj);
                if (ImGui::MenuItem("Camera")) addPrimitive("camera", obj);
                ImGui::EndMenu();
            }
            ImGui::EndPopup();
        }
        ImGui::PopID();
    }
    if (ImGui::BeginPopupContextWindow("hierctx",
            ImGuiPopupFlags_MouseButtonRight | ImGuiPopupFlags_NoOpenOverItems)) {
        if (ImGui::BeginMenu("Criar")) {
            if (ImGui::MenuItem("Objeto vazio (Transform)", "Ctrl+N")) createEmptyObject();
            ImGui::Separator();
            if (ImGui::BeginMenu("3D")) {
                if (ImGui::MenuItem("Cubo")) addPrimitive("cube");
                if (ImGui::MenuItem("Plano")) addPrimitive("plane");
                if (ImGui::MenuItem("Quad")) addPrimitive("quad");
                if (ImGui::MenuItem("Camera")) addPrimitive("camera");
                ImGui::EndMenu();
            }
            if (ImGui::BeginMenu("2D")) {
                if (ImGui::MenuItem("Sprite")) addPrimitive("sprite");
                if (ImGui::MenuItem("Quad 2D")) addPrimitive("quad2d");
                ImGui::EndMenu();
            }
            ImGui::EndMenu();
        }
        ImGui::Separator();
        if (ImGui::MenuItem("Colar", "Ctrl+V", false, !clipboardJson_.empty())) pasteClipboard();
        ImGui::EndPopup();
    }
    ImGui::EndChild();

    ImGui::TextDisabled("Botao direito na lista: criar e gerenciar objetos");
    ImGui::End();
}

void EditorApp::addPrimitive(const std::string& kind, GameObject* parent) {
    Scene* scene = Engine::getSceneManager().getActiveScene();
    if (!scene) return;
    pushUndo();
    auto object = std::make_unique<GameObject>("Objeto");
    if (kind == "camera") {
        object->name = "Camera";
        object->addComponent<Camera>();
        object->transform.setPosition(glm::vec3(0.0f, 3.0f, 8.0f));
    } else {
        auto* render = object->addComponent<MeshRenderer>();
        if (kind == "cube") {
            object->name = "Cubo";
            render->mesh = MeshType::Cube;
            object->addComponent<StaticBody>();
        } else if (kind == "plane") {
            object->name = "Plano";
            render->mesh = MeshType::Plane;
            render->color = glm::vec3(0.4f, 0.42f, 0.48f);
            object->addComponent<StaticBody>();
            object->transform.setScale(glm::vec3(8.0f, 1.0f, 8.0f));
        } else if (kind == "quad") {
            object->name = "Quad";
            render->mesh = MeshType::Quad;
            object->transform.setScale(glm::vec3(2.0f, 2.0f, 1.0f));
        } else if (kind == "quad2d") {
            object->name = "Quad2D";
            render->mesh = MeshType::Quad;
            object->transform.setScale(glm::vec3(64.0f, 64.0f, 1.0f));
        } else {
            object->name = "Sprite";
            render->mesh = MeshType::Sprite;
            object->transform.setScale(glm::vec3(64.0f, 64.0f, 1.0f));
        }
    }
    if (parent) object->transform.parent = &parent->transform;
    selected_ = scene->addObject(std::move(object));
}

void EditorApp::deleteSelected() {
    if (!selected_) return;
    Scene* scene = Engine::getSceneManager().getActiveScene();
    if (scene && scene->removeObject(selected_)) {
        Log::info("Editor: objeto removido");
    }
    selected_ = nullptr;
}

void EditorApp::duplicateSelected() {
    if (!selected_) return;
    Scene* scene = Engine::getSceneManager().getActiveScene();
    if (!scene) return;
    pushUndo();
    auto copy = selected_->clone();
    copy->name = selected_->name + " (copia)";
    copy->transform.setPosition(selected_->transform.position + glm::vec3(1.0f, 0.0f, 1.0f));
    selected_ = scene->addObject(std::move(copy));
    Log::info("Editor: objeto duplicado");
}

void EditorApp::createEmptyObject() {
    Scene* scene = Engine::getSceneManager().getActiveScene();
    if (!scene) return;
    pushUndo();
    selected_ = scene->addObject(std::make_unique<GameObject>("Vazio"));
    Log::info("Editor: objeto vazio criado");
}

void EditorApp::copySelected() {
    if (!selected_) return;
    Scene tmp("__clipboard__");
    tmp.gameObjects.push_back(selected_->clone());
    clipboardJson_ = tmp.serialize();
    Log::info("Editor: '" + selected_->name + "' copiado");
}

void EditorApp::pasteClipboard() {
    if (clipboardJson_.empty()) return;
    Scene* scene = Engine::getSceneManager().getActiveScene();
    if (!scene) return;
    Scene tmp("__paste__");
    if (!tmp.deserialize(clipboardJson_)) return;
    pushUndo();
    for (auto& obj : tmp.gameObjects) {
        if (!obj) continue;
        obj->name += " (copia)";
        selected_ = scene->addObject(std::move(obj));
    }
    Log::info("Editor: objeto colado");
}

void EditorApp::frameSelected() {
    Scene* scene = Engine::getSceneManager().getActiveScene();
    if (!scene || !selected_) return;
    const glm::vec3 target = selected_->transform.position;
    glm::vec3 offset = scene->cameraPosition - scene->cameraTarget;
    if (glm::length(offset) < 0.001f) offset = glm::vec3(0.0f, 8.5f, 15.0f);
    scene->cameraTarget = target;
    scene->cameraPosition = target + offset;
}

// ---------------------------------------------------------------------------
// Undo/redo (snapshots JSON inertes)
// ---------------------------------------------------------------------------

void EditorApp::pushUndo() {
    Scene* scene = Engine::getSceneManager().getActiveScene();
    if (!scene) return;
    Snapshot snap;
    snap.json = scene->serialize();
    snap.selectedName = selected_ ? selected_->name : "";
    undoStack_.push_back(std::move(snap));
    if (undoStack_.size() > 50) undoStack_.pop_front();
    redoStack_.clear();
}

void EditorApp::restoreSnapshot(const Snapshot& snap) {
    Scene* scene = Engine::getSceneManager().getActiveScene();
    if (!scene) return;
    scene->deserialize(snap.json);
    selected_ = snap.selectedName.empty() ? nullptr : scene->findGameObject(snap.selectedName);
}

void EditorApp::undo() {
    if (undoStack_.empty()) return;
    Scene* scene = Engine::getSceneManager().getActiveScene();
    if (!scene) return;
    Snapshot current;
    current.json = scene->serialize();
    current.selectedName = selected_ ? selected_->name : "";
    redoStack_.push_back(std::move(current));
    Snapshot snap = std::move(undoStack_.back());
    undoStack_.pop_back();
    restoreSnapshot(snap);
    Log::info("Editor: desfazer");
}

void EditorApp::redo() {
    if (redoStack_.empty()) return;
    Scene* scene = Engine::getSceneManager().getActiveScene();
    if (!scene) return;
    Snapshot current;
    current.json = scene->serialize();
    current.selectedName = selected_ ? selected_->name : "";
    undoStack_.push_back(std::move(current));
    Snapshot snap = std::move(redoStack_.back());
    redoStack_.pop_back();
    restoreSnapshot(snap);
    Log::info("Editor: refazer");
}

void EditorApp::drawRenameDialog() {
    if (showRename_) {
        ImGui::OpenPopup("Renomear objeto");
        showRename_ = false;
    }
    if (ImGui::BeginPopupModal("Renomear objeto", nullptr, ImGuiWindowFlags_AlwaysAutoResize)) {
        ImGui::SetNextItemWidth(260.0f);
        ImGui::InputText("Nome", &renameBuffer_);
        if (ImGui::Button("OK", ImVec2(120, 0)) && renameTarget_ && !renameBuffer_.empty()) {
            pushUndo();
            renameTarget_->name = renameBuffer_;
            selected_ = renameTarget_;
            ImGui::CloseCurrentPopup();
        }
        ImGui::SameLine();
        if (ImGui::Button("Cancelar", ImVec2(120, 0))) ImGui::CloseCurrentPopup();
        ImGui::EndPopup();
    }
}

void EditorApp::drawSaveAsDialog() {
    if (showSaveAs_) {
        ImGui::OpenPopup("Salvar cena como");
        showSaveAs_ = false;
    }
    if (ImGui::BeginPopupModal("Salvar cena como", nullptr, ImGuiWindowFlags_AlwaysAutoResize)) {
        ImGui::SetNextItemWidth(260.0f);
        ImGui::InputText("Nome da cena", &saveAsName_);
        if (ImGui::Button("Salvar", ImVec2(120, 0)) && !saveAsName_.empty()) {
            if (Scene* scene = Engine::getSceneManager().getActiveScene()) {
                const std::filesystem::path file =
                    FS::projectRoot() / "scenes" / (saveAsName_ + ".vscene");
                scene->save(file);
                Log::info("Editor: cena salva como " + file.string());
            }
            ImGui::CloseCurrentPopup();
        }
        ImGui::SameLine();
        if (ImGui::Button("Cancelar", ImVec2(120, 0))) ImGui::CloseCurrentPopup();
        ImGui::EndPopup();
    }
}

void EditorApp::drawAssetDialogs() {
    if (showAssetRename_) {
        ImGui::OpenPopup("Renomear asset");
        showAssetRename_ = false;
    }
    if (ImGui::BeginPopupModal("Renomear asset", nullptr, ImGuiWindowFlags_AlwaysAutoResize)) {
        ImGui::SetNextItemWidth(280.0f);
        ImGui::InputText("Novo nome", &assetRenameBuffer_);
        if (ImGui::Button("OK", ImVec2(120, 0)) && !assetRenameBuffer_.empty()) {
            const std::filesystem::path target = assetRenamePath_.parent_path() / assetRenameBuffer_;
            std::error_code ec;
            std::filesystem::rename(assetRenamePath_, target, ec);
            if (ec) Log::error("Editor: falha ao renomear asset: " + ec.message());
            else { refreshAssets(); Log::info("Editor: asset renomeado"); }
            ImGui::CloseCurrentPopup();
        }
        ImGui::SameLine();
        if (ImGui::Button("Cancelar", ImVec2(120, 0))) ImGui::CloseCurrentPopup();
        ImGui::EndPopup();
    }

    if (showAssetDelete_) {
        ImGui::OpenPopup("Excluir asset");
        showAssetDelete_ = false;
    }
    if (ImGui::BeginPopupModal("Excluir asset", nullptr, ImGuiWindowFlags_AlwaysAutoResize)) {
        ImGui::Text("Excluir '%s'?", assetDeletePath_.filename().string().c_str());
        ImGui::TextDisabled("Esta ação não pode ser desfeita.");
        if (ImGui::Button("Excluir", ImVec2(120, 0))) {
            std::error_code ec;
            std::filesystem::remove(assetDeletePath_, ec);
            if (ec) Log::error("Editor: falha ao excluir asset: " + ec.message());
            else { refreshAssets(); Log::info("Editor: asset excluído"); }
            ImGui::CloseCurrentPopup();
        }
        ImGui::SameLine();
        if (ImGui::Button("Cancelar", ImVec2(120, 0))) ImGui::CloseCurrentPopup();
        ImGui::EndPopup();
    }
}

void EditorApp::handleShortcuts() {
    ImGuiIO& io = ImGui::GetIO();
    if (io.WantTextInput) return;
    const bool ctrl = io.KeyCtrl;
    const bool shift = io.KeyShift;
    if (ctrl && ImGui::IsKeyPressed(ImGuiKey_Z)) { shift ? redo() : undo(); return; }
    if (ctrl && ImGui::IsKeyPressed(ImGuiKey_Y)) { redo(); return; }
    if (ctrl && shift && ImGui::IsKeyPressed(ImGuiKey_S)) {
        if (Scene* scene = Engine::getSceneManager().getActiveScene()) saveAsName_ = scene->getName();
        showSaveAs_ = true;
        return;
    }
    if (ctrl && ImGui::IsKeyPressed(ImGuiKey_S)) { saveProjectAndScene(); return; }
    if (ctrl && ImGui::IsKeyPressed(ImGuiKey_D)) { duplicateSelected(); return; }
    if (ctrl && ImGui::IsKeyPressed(ImGuiKey_C)) { copySelected(); return; }
    if (ctrl && ImGui::IsKeyPressed(ImGuiKey_V)) { pasteClipboard(); return; }
    if (ctrl && ImGui::IsKeyPressed(ImGuiKey_N)) { createEmptyObject(); return; }
    if (ctrl && ImGui::IsKeyPressed(ImGuiKey_O)) {
        showOpenProject_ = true;
        return;
    }
    if (ImGui::IsKeyPressed(ImGuiKey_Delete)) { deleteSelected(); return; }
    if (ImGui::IsKeyPressed(ImGuiKey_F)) { frameSelected(); return; }
}

// ---------------------------------------------------------------------------
// Inspector
// ---------------------------------------------------------------------------

void EditorApp::drawInspector() {
    ImGui::Begin("Inspector");
    Scene* scene = Engine::getSceneManager().getActiveScene();

    if (selected_) {
        ImGui::InputText("Nome", &selected_->name);
        ImGui::SeparatorText("Transform");
        drawTransformEditor(*selected_);
        ImGui::SeparatorText("Componentes");
        drawComponentEditors(*selected_);

        ImGui::SeparatorText("UNLOCKED (plugin)");
        bool any = false;
        for (const auto& component : selected_->components) {
            const std::string type = component->typeName();
            if (type == "MeshRenderer" || type == "RigidBody" || type == "StaticBody" ||
                type == "Collider") {
                continue;
            }
            any = true;
            ImGui::TextDisabled("%s", type.c_str());
            drawUnlockedFields(component.get());
        }
        if (!any) ImGui::TextDisabled("nenhum campo de plugin");
    } else if (scene) {
        ImGui::TextDisabled("Cena: %s", scene->getName().c_str());
        ImGui::SeparatorText("UNLOCKED da cena");
        drawUnlockedFields(scene);
        ImGui::SeparatorText("Camera");
        drawSceneCameraEditor(*scene);
    } else {
        ImGui::TextDisabled("Selecione um objeto na Hierarchy");
    }
    ImGui::End();
}

void EditorApp::drawTransformEditor(GameObject& object) {
    ImGui::DragFloat3("Posicao", &object.transform.position.x, 0.1f);
    glm::vec3 euler = glm::degrees(glm::eulerAngles(object.transform.rotation));
    if (ImGui::DragFloat3("Rotacao", &euler.x, 1.0f)) {
        object.transform.setRotation(glm::quat(glm::radians(euler)));
    }
    ImGui::DragFloat3("Escala", &object.transform.scale.x, 0.1f);
}

void EditorApp::drawComponentEditors(GameObject& object) {
    if (auto* render = object.getComponent<MeshRenderer>()) {
        if (ImGui::CollapsingHeader("MeshRenderer", ImGuiTreeNodeFlags_DefaultOpen)) {
            const char* meshes[] = {"cube", "plane", "quad", "sprite"};
            int meshIndex = static_cast<int>(render->mesh);
            if (ImGui::Combo("Malha", &meshIndex, meshes, 4)) {
                render->mesh = static_cast<MeshType>(meshIndex);
            }
            ImGui::ColorEdit3("Cor", &render->color.x);
            ImGui::Checkbox("Visivel", &render->visible);
            ImGui::InputText("Textura", &render->texturePath);
            if (render->importedModel >= 0) {
                ImGui::TextDisabled("Modelo importado #%d (malha %d)",
                                    render->importedModel, render->importedMesh);
            }
        }
    }
    if (auto* body = object.getComponent<RigidBody>()) {
        if (ImGui::CollapsingHeader("RigidBody", ImGuiTreeNodeFlags_DefaultOpen)) {
            ImGui::DragFloat("Massa", &body->mass, 0.1f, 0.01f, 1000.0f);
            ImGui::DragFloat("Restituicao", &body->restitution, 0.01f, 0.0f, 1.0f);
            ImGui::Checkbox("Gravidade", &body->useGravity);
            ImGui::Checkbox("Kinematic", &body->kinematic);
            ImGui::DragFloat3("Velocidade", &body->velocity.x, 0.1f);
        }
    }
    if (object.getComponent<StaticBody>()) {
        if (ImGui::CollapsingHeader("StaticBody", ImGuiTreeNodeFlags_DefaultOpen)) {
            ImGui::TextDisabled("Corpo estatico (nao simula)");
        }
    }
    if (auto* collider = object.getComponent<Collider>()) {
        if (ImGui::CollapsingHeader("Collider", ImGuiTreeNodeFlags_DefaultOpen)) {
            ImGui::DragFloat3("Tamanho", &collider->size.x, 0.1f);
            ImGui::Checkbox("Trigger", &collider->isTrigger);
        }
    }

    ImGui::Separator();
    const char* builtin[] = {"RigidBody", "StaticBody", "Collider"};
    static int comboIndex = 0;
    ImGui::SetNextItemWidth(140);
    ImGui::Combo("##addcomp", &comboIndex, builtin, 3);
    ImGui::SameLine();
    if (ImGui::Button("Adicionar")) {
        if (comboIndex == 0 && !object.getComponent<RigidBody>()) object.addComponent<RigidBody>();
        if (comboIndex == 1 && !object.getComponent<StaticBody>()) object.addComponent<StaticBody>();
        if (comboIndex == 2 && !object.getComponent<Collider>()) object.addComponent<Collider>();
    }
    const std::vector<std::string> pluginTypes = ComponentRegistry::names();
    if (!pluginTypes.empty()) {
        static int pluginIndex = 0;
        pluginIndex = std::clamp(pluginIndex, 0, static_cast<int>(pluginTypes.size()) - 1);
        std::vector<const char*> items;
        for (const auto& t : pluginTypes) items.push_back(t.c_str());
        ImGui::SetNextItemWidth(140);
        ImGui::Combo("##plugin", &pluginIndex, items.data(), static_cast<int>(items.size()));
        ImGui::SameLine();
        if (ImGui::Button("Add plugin")) {
            auto component = ComponentRegistry::create(pluginTypes[pluginIndex], &object);
            if (component) object.components.push_back(std::move(component));
        }
    }
}

void EditorApp::drawSceneCameraEditor(Scene& scene) {
    const char* modes[] = {"Perspectiva 3D", "Ortho 2D"};
    int mode = scene.cameraMode == Scene::CameraMode::Ortho2D ? 1 : 0;
    if (ImGui::Combo("Modo", &mode, modes, 2)) {
        scene.cameraMode = mode == 1 ? Scene::CameraMode::Ortho2D : Scene::CameraMode::Perspective;
    }
    ImGui::DragFloat3("Posicao", &scene.cameraPosition.x, 0.1f);
    ImGui::DragFloat3("Alvo", &scene.cameraTarget.x, 0.1f);
    if (scene.cameraMode == Scene::CameraMode::Ortho2D) {
        ImGui::DragFloat("Altura ortho", &scene.orthoHeight, 0.5f, 1.0f, 500.0f);
    }
}

void EditorApp::drawUnlockedFields(const void* base) {
    for (const Inspector::Field& field : Inspector::fields()) {
        if (field.base != base) continue;
        ImGui::PushID(field.address);
        const std::string label = field.name + " (" + field.owner + ")";
        if (field.type == "float") {
            float value = *static_cast<const float*>(field.address);
            if (ImGui::DragFloat(label.c_str(), &value, 0.05f)) {
                *static_cast<float*>(const_cast<void*>(field.address)) = value;
            }
        } else if (field.type == "double") {
            double value = *static_cast<const double*>(field.address);
            float fv = static_cast<float>(value);
            if (ImGui::DragFloat(label.c_str(), &fv, 0.05f)) {
                *static_cast<double*>(const_cast<void*>(field.address)) = static_cast<double>(fv);
            }
        } else if (field.type == "bool") {
            bool value = *static_cast<const bool*>(field.address);
            if (ImGui::Checkbox(label.c_str(), &value)) {
                *static_cast<bool*>(const_cast<void*>(field.address)) = value;
            }
        } else if (field.type == "glm::vec3") {
            glm::vec3 value = *static_cast<const glm::vec3*>(field.address);
            if (ImGui::DragFloat3(label.c_str(), &value.x, 0.1f)) {
                *static_cast<glm::vec3*>(const_cast<void*>(field.address)) = value;
            }
        } else if (field.type == "int") {
            int value = *static_cast<const int*>(field.address);
            if (ImGui::InputInt(label.c_str(), &value)) {
                *static_cast<int*>(const_cast<void*>(field.address)) = value;
            }
        } else if (field.type == "std::string") {
            const std::string key = std::to_string(reinterpret_cast<uintptr_t>(field.address));
            auto it = stringEdits_.find(key);
            if (it == stringEdits_.end()) {
                it = stringEdits_.emplace(key, *static_cast<const std::string*>(field.address)).first;
            }
            if (ImGui::InputText(label.c_str(), &it->second)) {
                *static_cast<std::string*>(const_cast<void*>(field.address)) = it->second;
            }
        } else {
            ImGui::TextDisabled("%s : %s", label.c_str(), field.type.c_str());
        }
        ImGui::PopID();
    }
}

// ---------------------------------------------------------------------------
// AssetBrowser
// ---------------------------------------------------------------------------

unsigned int EditorApp::assetThumbnail(const std::filesystem::path& full) {
    const std::string key = full.string();
    auto it = assetThumbs_.find(key);
    if (it != assetThumbs_.end()) return it->second;
    unsigned int tex = 0;
    int w = 0, h = 0, c = 0;
    stbi_uc* px = stbi_load(key.c_str(), &w, &h, &c, 4);
    if (px && w > 0 && h > 0) {
        glGenTextures(1, &tex);
        glBindTexture(GL_TEXTURE_2D, tex);
        glTexImage2D(GL_TEXTURE_2D, 0, GL_RGBA8, w, h, 0, GL_RGBA, GL_UNSIGNED_BYTE, px);
        glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, GL_LINEAR);
        glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, GL_LINEAR);
        glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_S, GL_CLAMP_TO_EDGE);
        glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_T, GL_CLAMP_TO_EDGE);
    }
    if (px) stbi_image_free(px);
    assetThumbs_[key] = tex; // cacheia mesmo 0 para nao reabrir todo frame
    return tex;
}

void EditorApp::drawAssetCell(const std::filesystem::path& entry, const ImVec2& pos,
                              float cellW, float cellH, float thumbSize) {
    const std::filesystem::path full = FS::projectRoot() / entry;
    const std::string name = entry.filename().string();
    std::error_code ec;
    const bool isDir = std::filesystem::is_directory(full, ec);
    ImDrawList* dl = ImGui::GetWindowDrawList();

    const bool hovered = ImGui::IsMouseHoveringRect(pos, ImVec2(pos.x + cellW, pos.y + cellH));
    const bool selected = (assetSelected_ == entry);
    if (selected) {
        dl->AddRectFilled(ImVec2(pos.x + 1, pos.y + 1), ImVec2(pos.x + cellW - 1, pos.y + cellH - 1),
                          IM_COL32(90, 120, 200, 70), 6.0f);
    } else if (hovered) {
        dl->AddRectFilled(ImVec2(pos.x + 1, pos.y + 1), ImVec2(pos.x + cellW - 1, pos.y + cellH - 1),
                          IM_COL32(255, 255, 255, 12), 6.0f);
    }

    const ImVec2 imgMin(pos.x + (cellW - thumbSize) * 0.5f, pos.y + 4.0f);
    const ImVec2 imgMax(imgMin.x + thumbSize, imgMin.y + thumbSize);
    dl->AddRectFilled(imgMin, imgMax, IM_COL32(26, 26, 31, 255), 6.0f);

    if (isDir) {
        const ImU32 fc = IM_COL32(222, 178, 92, 255);
        const float pad = thumbSize * 0.16f;
        const ImVec2 b0(imgMin.x + pad, imgMin.y + pad + thumbSize * 0.12f);
        const ImVec2 b1(imgMax.x - pad, imgMax.y - pad);
        dl->AddRectFilled(ImVec2(b0.x, imgMin.y + pad),
                          ImVec2(b0.x + (b1.x - b0.x) * 0.45f, b0.y + 5), fc, 3.0f);
        dl->AddRectFilled(b0, b1, fc, 4.0f);
        dl->AddRectFilled(ImVec2(b0.x, b0.y + (b1.y - b0.y) * 0.30f), b1,
                          IM_COL32(238, 198, 118, 255), 4.0f);
    } else if (isImageFile(full)) {
        const unsigned int tex = assetThumbnail(full);
        if (tex) {
            dl->AddImage(static_cast<ImTextureID>(static_cast<intptr_t>(tex)), imgMin, imgMax,
                         ImVec2(0, 0), ImVec2(1, 1));
        } else {
            dl->AddRectFilled(imgMin, imgMax, IM_COL32(60, 90, 70, 255), 6.0f);
        }
    } else {
        const std::string ext = full.extension().string();
        ImU32 bc = IM_COL32(92, 92, 102, 255);
        std::string label = ext.empty() ? "?" : ext.substr(1);
        for (auto& ch : label) ch = static_cast<char>(std::toupper(static_cast<unsigned char>(ch)));
        if (ext == ".velv") bc = IM_COL32(150, 95, 200, 255);
        else if (ext == ".vscene") bc = IM_COL32(80, 130, 210, 255);
        else if (ext == ".ui") bc = IM_COL32(70, 160, 160, 255);
        else if (isModelFile(full)) bc = IM_COL32(210, 140, 70, 255);
        dl->AddRectFilled(imgMin, imgMax, bc, 6.0f);
        const ImVec2 ts = ImGui::CalcTextSize(label.c_str());
        dl->AddText(ImVec2(imgMin.x + (thumbSize - ts.x) * 0.5f, imgMin.y + (thumbSize - ts.y) * 0.5f),
                    IM_COL32(255, 255, 255, 235), label.c_str());
    }

    std::string disp = name;
    const float maxW = cellW - 8.0f;
    if (ImGui::CalcTextSize(disp.c_str()).x > maxW) {
        while (!disp.empty() && ImGui::CalcTextSize((disp + "...").c_str()).x > maxW) disp.pop_back();
        disp += "...";
    }
    const ImVec2 nts = ImGui::CalcTextSize(disp.c_str());
    dl->AddText(ImVec2(pos.x + (cellW - nts.x) * 0.5f, imgMax.y + 5.0f),
                IM_COL32(222, 222, 230, 255), disp.c_str());

    ImGui::SetCursorScreenPos(pos);
    ImGui::PushID(full.string().c_str());
    ImGui::InvisibleButton("cell", ImVec2(cellW, cellH));
    if (ImGui::IsItemClicked(ImGuiMouseButton_Left)) assetSelected_ = entry;
    if (hovered && ImGui::IsMouseDoubleClicked(ImGuiMouseButton_Left)) {
        if (isDir) { assetCurrentDir_ = entry; assetSelected_.clear(); }
        else openAsset(full);
    }
    if (!isDir && ImGui::BeginDragDropSource(ImGuiDragDropFlags_None)) {
        const std::string payload = full.string();
        ImGui::SetDragDropPayload("VELVX_ASSET", payload.c_str(), payload.size() + 1);
        ImGui::Text("%s", name.c_str());
        ImGui::EndDragDropSource();
    }
    if (ImGui::BeginPopupContextItem("assetcellctx")) {
        if (isDir) {
            if (ImGui::MenuItem("Abrir pasta")) { assetCurrentDir_ = entry; assetSelected_.clear(); }
        } else {
            if (ImGui::MenuItem("Abrir")) openAsset(full);
            if (ImGui::MenuItem("Renomear")) {
                assetRenamePath_ = full; assetRenameBuffer_ = name; showAssetRename_ = true;
            }
            if (ImGui::MenuItem("Duplicar")) {
                const std::filesystem::path dup =
                    full.parent_path() / (full.stem().string() + " (copia)" + full.extension().string());
                std::error_code copyEc;
                std::filesystem::copy_file(full, dup, std::filesystem::copy_options::skip_existing, copyEc);
                if (copyEc) Log::error("Editor: falha ao duplicar asset: " + copyEc.message());
                else { refreshAssets(); Log::info("Editor: asset duplicado"); }
            }
            if (ImGui::MenuItem("Excluir")) { assetDeletePath_ = full; showAssetDelete_ = true; }
            ImGui::Separator();
            if (ImGui::MenuItem("Copiar caminho")) ImGui::SetClipboardText(full.string().c_str());
        }
        if (ImGui::MenuItem("Revelar no Explorer")) revealInExplorer(full);
        ImGui::EndPopup();
    }
    ImGui::PopID();
}

void EditorApp::drawAssets() {
    ImGui::Begin("Assets");
    if (ImGui::Button("Atualizar")) refreshAssets();
    ImGui::SameLine();
    if (ImGui::Button(assetGridView_ ? "Ver lista" : "Ver thumbnails")) assetGridView_ = !assetGridView_;
    if (assetGridView_) {
        ImGui::SameLine();
        ImGui::SetNextItemWidth(110);
        ImGui::SliderFloat("##tam", &assetThumbSize_, 56.0f, 160.0f, "%.0f px");
    }
    ImGui::SameLine();
    ImGui::TextDisabled("%s", FS::projectRoot().string().c_str());

    if (!assetGridView_) {
        ImGui::BeginChild("arvore", ImVec2(0, 0), ImGuiChildFlags_Borders);
        drawAssetTree(std::filesystem::path());
        ImGui::EndChild();
        ImGui::End();
        return;
    }

    if (!assetCurrentDir_.empty()) {
        if (ImGui::Button(".. Subir")) { assetCurrentDir_ = assetCurrentDir_.parent_path(); assetSelected_.clear(); }
        ImGui::SameLine();
        if (ImGui::Button("Raiz")) { assetCurrentDir_.clear(); assetSelected_.clear(); }
        ImGui::SameLine();
    }
    ImGui::TextDisabled("/%s", assetCurrentDir_.generic_string().c_str());

    std::vector<std::filesystem::path> dirs, files;
    for (const auto& e : assetEntries_) {
        if (e.parent_path() != assetCurrentDir_) continue;
        std::error_code ec;
        if (std::filesystem::is_directory(FS::projectRoot() / e, ec)) dirs.push_back(e);
        else files.push_back(e);
    }
    std::sort(dirs.begin(), dirs.end());
    std::sort(files.begin(), files.end());
    std::vector<std::filesystem::path> entries;
    entries.insert(entries.end(), dirs.begin(), dirs.end());
    entries.insert(entries.end(), files.begin(), files.end());

    ImGui::BeginChild("grid", ImVec2(0, 0), ImGuiChildFlags_Borders);
    const float cellW = assetThumbSize_ + 18.0f;
    const float cellH = assetThumbSize_ + 30.0f;
    const float availW = ImGui::GetContentRegionAvail().x;
    const int cols = std::max(1, static_cast<int>(availW / cellW));
    const ImVec2 start = ImGui::GetCursorScreenPos();
    if (entries.empty()) {
        ImGui::TextDisabled("Pasta vazia. Botao direito: nova pasta / revelar.");
    } else {
        const int rows = (static_cast<int>(entries.size()) + cols - 1) / cols;
        ImGui::Dummy(ImVec2(cols * cellW, rows * cellH));
        for (size_t i = 0; i < entries.size(); ++i) {
            const int c = static_cast<int>(i) % cols;
            const int r = static_cast<int>(i) / cols;
            drawAssetCell(entries[i], ImVec2(start.x + c * cellW, start.y + r * cellH),
                          cellW, cellH, assetThumbSize_);
        }
    }
    if (ImGui::BeginPopupContextWindow("assetgridctx",
            ImGuiPopupFlags_MouseButtonRight | ImGuiPopupFlags_NoOpenOverItems)) {
        if (ImGui::MenuItem("Nova pasta")) {
            std::error_code ec;
            const std::filesystem::path base = FS::projectRoot() / assetCurrentDir_;
            std::filesystem::path np = base / "NovaPasta";
            int n = 1;
            while (std::filesystem::exists(np, ec)) np = base / ("NovaPasta" + std::to_string(n++));
            std::filesystem::create_directories(np, ec);
            refreshAssets();
        }
        if (ImGui::MenuItem("Revelar no Explorer")) revealInExplorer(FS::projectRoot() / assetCurrentDir_);
        if (ImGui::MenuItem("Atualizar")) refreshAssets();
        ImGui::EndPopup();
    }
    ImGui::EndChild();
    ImGui::End();
}

void EditorApp::drawAssetTree(const std::filesystem::path& dir) {
    for (const auto& entry : assetEntries_) {
        if (entry.parent_path() != dir) continue;
        const std::filesystem::path full = FS::projectRoot() / entry;
        const std::string name = entry.filename().string();
        std::error_code ec;
        if (std::filesystem::is_directory(full, ec)) {
            if (ImGui::TreeNode(name.c_str())) {
                drawAssetTree(entry);
                ImGui::TreePop();
            }
        } else {
            ImGui::PushID(full.string().c_str());
            ImGui::Selectable(name.c_str());
            if (ImGui::BeginDragDropSource(ImGuiDragDropFlags_None)) {
                const std::string payload = full.string();
                ImGui::SetDragDropPayload("VELVX_ASSET", payload.c_str(), payload.size() + 1);
                ImGui::Text("%s", name.c_str());
                ImGui::EndDragDropSource();
            }
            if (ImGui::IsItemHovered() && ImGui::IsMouseDoubleClicked(ImGuiMouseButton_Left)) {
                openAsset(full);
            }
            if (ImGui::BeginPopupContextItem("assetctx")) {
                if (ImGui::MenuItem("Abrir")) openAsset(full);
                if (ImGui::MenuItem("Renomear")) {
                    assetRenamePath_ = full;
                    assetRenameBuffer_ = name;
                    showAssetRename_ = true;
                }
                if (ImGui::MenuItem("Duplicar")) {
                    const std::filesystem::path dup =
                        full.parent_path() / (full.stem().string() + " (copia)" + full.extension().string());
                    std::error_code copyEc;
                    std::filesystem::copy_file(full, dup, std::filesystem::copy_options::skip_existing, copyEc);
                    if (copyEc) Log::error("Editor: falha ao duplicar asset: " + copyEc.message());
                    else { refreshAssets(); Log::info("Editor: asset duplicado"); }
                }
                if (ImGui::MenuItem("Excluir")) {
                    assetDeletePath_ = full;
                    showAssetDelete_ = true;
                }
                ImGui::Separator();
                if (ImGui::MenuItem("Copiar caminho")) ImGui::SetClipboardText(full.string().c_str());
                if (ImGui::MenuItem("Revelar no Explorer")) revealInExplorer(full);
                ImGui::EndPopup();
            }
            ImGui::PopID();
        }
    }
}

void EditorApp::openScriptInPanel(const std::filesystem::path& path) {
    if (scriptEditor_.load(path)) {
        focusScriptPanel_ = true;
    } else {
        Log::error("Editor: " + scriptEditor_.status);
    }
}

void EditorApp::drawScriptPanel() {
    if (focusScriptPanel_) {
        ImGui::SetNextWindowFocus();
        focusScriptPanel_ = false;
    }
    if (!ImGui::Begin("Script")) { ImGui::End(); return; }

    if (ImGui::Button("Novo")) scriptEditor_.newScript();
    ImGui::SameLine();
    if (ImGui::Button("Salvar")) {
        if (scriptEditor_.path().empty()) {
            const std::filesystem::path dir = FS::projectRoot() / "Scripts";
            std::error_code ec;
            std::filesystem::create_directories(dir, ec);
            scriptEditor_.setPath(dir / "script.velv");
        }
        scriptEditor_.save();
    }
    ImGui::SameLine();
    if (ImGui::Button("Do objeto")) {
        if (selected_) {
            if (auto* script = selected_->getComponent<ScriptComponent>()) {
                if (!script->scriptPath.empty()) {
                    openScriptInPanel(FS::projectRoot() / script->scriptPath);
                }
            }
        }
    }
    if (ImGui::IsItemHovered()) ImGui::SetTooltip("Edita o script .velv do objeto selecionado.");
    ImGui::SameLine();
    if (ImGui::Button("Buscar")) scriptEditor_.openSearch();
    ImGui::SameLine();
    const std::string fileLabel = scriptEditor_.path().empty()
        ? std::string("(sem arquivo)")
        : scriptEditor_.path().filename().string();
    ImGui::TextDisabled("%s%s", fileLabel.c_str(), scriptEditor_.dirty() ? " *" : "");
    ImGui::SameLine();
    ImGui::TextDisabled("%s", scriptEditor_.status.c_str());

    scriptEditor_.drawSearchBar();
    const std::string hint = scriptEditor_.activeCallHint();
    if (!hint.empty()) {
        ImGui::TextColored(ImVec4(0.55f, 0.8f, 1.0f, 1.0f), "%s", hint.c_str());
    }
    scriptEditor_.draw(ImGui::GetContentRegionAvail());
    ImGui::End();
}

void EditorApp::openAsset(const std::filesystem::path& path) {
    const std::string ext = path.extension().string();
    if (ext == ".velv") {
        openScriptInPanel(path);
    } else if (ext == ".ui") {
        spawnTool("VelvXUIEditor", path);
    } else if (ext == ".vscene") {
        loadSceneByName(path.stem().string());
    } else {
        assignAsset(path);
    }
}

void EditorApp::assignAsset(const std::filesystem::path& path) {
    Scene* scene = Engine::getSceneManager().getActiveScene();
    if (isImageFile(path)) {
        if (selected_) {
            if (auto* render = selected_->getComponent<MeshRenderer>()) {
                render->texturePath = projectRelative(path);
                Log::info("Editor: textura aplicada a '" + selected_->name + "'");
                return;
            }
        }
        if (scene) {
            auto object = std::make_unique<GameObject>(path.stem().string());
            auto* render = object->addComponent<MeshRenderer>();
            render->mesh = MeshType::Sprite;
            render->texturePath = projectRelative(path);
            object->transform.setScale(glm::vec3(128.0f, 128.0f, 1.0f));
            selected_ = scene->addObject(std::move(object));
        }
    } else if (isModelFile(path)) {
        const int model = AssetBrowser::loadModel(path.string());
        if (model < 0) {
            Log::error("Editor: falha ao importar modelo " + path.string());
            return;
        }
        if (scene) {
            auto object = std::make_unique<GameObject>(path.stem().string());
            auto* render = object->addComponent<MeshRenderer>();
            render->importedModel = model;
            render->importedMesh = 0;
            selected_ = scene->addObject(std::move(object));
            Log::info("Editor: modelo importado como objeto '" + selected_->name + "'");
        }
    } else {
        Log::warn("Editor: tipo de asset nao suportado: " + path.extension().string());
    }
}

// ---------------------------------------------------------------------------
// Console
// ---------------------------------------------------------------------------

void EditorApp::drawConsole() {
    ImGui::Begin("Console");
    if (ImGui::Button("Limpar")) {
        std::lock_guard<std::mutex> lock(consoleMutex_);
        console_.clear();
    }
    ImGui::SameLine();
    ImGui::Checkbox("Auto-scroll", &consoleAutoScroll_);
    ImGui::SameLine();
    ImGui::SetNextItemWidth(200);
    ImGui::InputText("Filtro", &consoleFilter_);
    ImGui::BeginChild("log", ImVec2(0, 0), ImGuiChildFlags_Borders);
    {
        std::lock_guard<std::mutex> lock(consoleMutex_);
        for (const auto& line : console_) {
            if (!consoleFilter_.empty() && line.message.find(consoleFilter_) == std::string::npos) {
                continue;
            }
            ImVec4 color(0.85f, 0.85f, 0.85f, 1.0f);
            if (line.level == "warn") color = ImVec4(0.95f, 0.8f, 0.3f, 1.0f);
            if (line.level == "error") color = ImVec4(0.95f, 0.35f, 0.3f, 1.0f);
            ImGui::PushStyleColor(ImGuiCol_Text, color);
            ImGui::TextWrapped("[%s] %s", line.level.c_str(), line.message.c_str());
            ImGui::PopStyleColor();
        }
    }
    if (consoleAutoScroll_ && ImGui::GetScrollY() >= ImGui::GetScrollMaxY() - 4.0f) {
        ImGui::SetScrollHereY(1.0f);
    }
    ImGui::EndChild();
    ImGui::End();
}

// ---------------------------------------------------------------------------
// SceneViewport
// ---------------------------------------------------------------------------

namespace {

/** @brief Meia-extensão AABB mundial (Collider local ou escala do mesh). */
glm::vec3 editorHalfExtent(GameObject* obj) {
    if (const auto* col = obj->getComponent<Collider>()) {
        return obj->transform.scale * col->size * 0.5f;
    }
    return obj->transform.scale * 0.5f;
}

glm::vec3 editorWorldPos(GameObject* obj) {
    return glm::vec3(obj->transform.worldMatrix()[3]);
}

/** @brief Projeta um ponto mundial para pixels do viewport (y para baixo). */
bool worldToScreen(const glm::vec3& w, const glm::mat4& vp, const ImVec2& mn,
                   const ImVec2& mx, ImVec2& out) {
    const glm::vec4 c = vp * glm::vec4(w, 1.0f);
    if (c.w <= 0.0001f) return false;
    const glm::vec3 ndc = glm::vec3(c) / c.w;
    if (ndc.z < -1.0f || ndc.z > 1.0f) return false;
    out.x = mn.x + (ndc.x * 0.5f + 0.5f) * (mx.x - mn.x);
    out.y = mn.y + (0.5f - ndc.y * 0.5f) * (mx.y - mn.y);
    return true;
}

/** @brief Raio mundo a partir de um pixel do viewport (perspectiva ou ortho). */
void screenRay(const ImVec2& mouse, const glm::mat4& vp, const ImVec2& mn,
               const ImVec2& mx, glm::vec3& origin, glm::vec3& dir) {
    const glm::mat4 inv = glm::inverse(vp);
    const float nx = 2.0f * (mouse.x - mn.x) / std::max(1.0f, mx.x - mn.x) - 1.0f;
    const float ny = 1.0f - 2.0f * (mouse.y - mn.y) / std::max(1.0f, mx.y - mn.y);
    glm::vec4 pn = inv * glm::vec4(nx, ny, -1.0f, 1.0f);
    glm::vec4 pf = inv * glm::vec4(nx, ny, 1.0f, 1.0f);
    pn /= pn.w;
    pf /= pf.w;
    origin = glm::vec3(pn);
    dir = glm::normalize(glm::vec3(pf) - origin);
}

/** @brief Interseção raio-AABB (slab); retorna t>0 ou -1. */
float rayAABB(const glm::vec3& o, const glm::vec3& d, const glm::vec3& center,
              const glm::vec3& half) {
    float tmin = 0.0f, tmax = 1e9f;
    for (int i = 0; i < 3; ++i) {
        if (std::fabs(d[i]) < 1e-8f) {
            if (o[i] < center[i] - half[i] || o[i] > center[i] + half[i]) return -1.0f;
        } else {
            float t1 = (center[i] - half[i] - o[i]) / d[i];
            float t2 = (center[i] + half[i] - o[i]) / d[i];
            if (t1 > t2) std::swap(t1, t2);
            tmin = std::max(tmin, t1);
            tmax = std::min(tmax, t2);
            if (tmin > tmax) return -1.0f;
        }
    }
    return tmin;
}

const glm::vec3 kAxis[3] = {{1, 0, 0}, {0, 1, 0}, {0, 0, 1}};
const ImU32 kAxisColor[3] = {IM_COL32(230, 60, 60, 255), IM_COL32(80, 200, 90, 255),
                             IM_COL32(70, 130, 230, 255)};

} // namespace

void EditorApp::drawViewport() {
    if (!ImGui::Begin("Scene")) {
        ImGui::End();
        return;
    }
    Scene* scene = Engine::getSceneManager().getActiveScene();

    if (ImGui::Button(scene && scene->cameraMode == Scene::CameraMode::Ortho2D ? "2D" : "3D")) {
        if (scene) {
            scene->cameraMode = scene->cameraMode == Scene::CameraMode::Ortho2D
                                    ? Scene::CameraMode::Perspective
                                    : Scene::CameraMode::Ortho2D;
        }
    }
    ImGui::SameLine();
    const char* modes[] = {"Mover (W)", "Rotacionar (E)", "Escala (R)"};
    for (int m = 0; m < 3; ++m) {
        ImGui::SameLine();
        if (gizmoMode_ == m) ImGui::PushStyleColor(ImGuiCol_Button, ImGui::GetStyleColorVec4(ImGuiCol_ButtonActive));
        if (ImGui::SmallButton(modes[m])) gizmoMode_ = m;
        if (gizmoMode_ == m) ImGui::PopStyleColor();
    }
    ImGui::SameLine();
    ImGui::TextDisabled("%s | %d objetos | esq: selecionar/gizmo | dir: orbitar | meio: pan | roda: zoom",
                        scene ? scene->getName().c_str() : "-",
                        scene ? static_cast<int>(scene->gameObjects.size()) : 0);

    const ImVec2 avail = ImGui::GetContentRegionAvail();
    const int width = std::max(1, static_cast<int>(avail.x));
    const int height = std::max(1, static_cast<int>(avail.y));

    unsigned int texture = 0;
    if (scene) {
        Renderer::setEditorCameraOverride(true);
        Renderer::beginSceneTarget(width, height);
        Renderer::renderScene(scene, time_);
        texture = Renderer::endSceneTarget();
        Renderer::setEditorCameraOverride(false);
    }
    ImVec2 rectMin(0, 0), rectMax(0, 0);
    if (texture) {
        ImGui::Image(static_cast<ImTextureID>(static_cast<intptr_t>(texture)),
                     ImVec2(static_cast<float>(width), static_cast<float>(height)),
                     ImVec2(0, 1), ImVec2(1, 0));
        rectMin = ImGui::GetItemRectMin();
        rectMax = ImGui::GetItemRectMax();
    } else {
        ImGui::TextDisabled("Abra um projeto para visualizar a cena");
    }

    if (scene && ImGui::IsItemHovered()) {
        ImGuiIO& io = ImGui::GetIO();
        const bool orbit = ImGui::IsMouseDown(ImGuiMouseButton_Right);
        const bool pan = ImGui::IsMouseDown(ImGuiMouseButton_Middle);
        if (scene->cameraMode == Scene::CameraMode::Perspective) {
            glm::vec3 offset = scene->cameraPosition - scene->cameraTarget;
            float dist = std::max(0.5f, glm::length(offset));
            float yaw = std::atan2(offset.x, offset.z);
            float pitch = std::asin(std::clamp(offset.y / dist, -1.0f, 1.0f));
            if (orbit) {
                yaw -= io.MouseDelta.x * 0.005f;
                pitch = std::clamp(pitch + io.MouseDelta.y * 0.005f, -1.45f, 1.45f);
            }
            if (io.MouseWheel != 0.0f) {
                dist = std::clamp(dist * (1.0f - io.MouseWheel * 0.1f), 1.0f, 300.0f);
            }
            const glm::vec3 forward = glm::normalize(scene->cameraTarget - scene->cameraPosition);
            const glm::vec3 right = glm::normalize(glm::cross(forward, glm::vec3(0, 1, 0)));
            const glm::vec3 up = glm::cross(right, forward);
            if (pan) {
                scene->cameraTarget -= right * io.MouseDelta.x * 0.002f * dist;
                scene->cameraTarget += up * io.MouseDelta.y * 0.002f * dist;
            }
            scene->cameraPosition = scene->cameraTarget +
                                    dist * glm::vec3(std::sin(yaw) * std::cos(pitch),
                                                     std::sin(pitch),
                                                     std::cos(yaw) * std::cos(pitch));
        } else {
            if (pan || orbit) {
                scene->cameraTarget.x -= io.MouseDelta.x * 0.02f;
                scene->cameraTarget.y += io.MouseDelta.y * 0.02f;
                scene->cameraPosition = scene->cameraTarget + glm::vec3(0, 0, 10);
            }
            if (io.MouseWheel != 0.0f) {
                scene->orthoHeight = std::clamp(scene->orthoHeight * (1.0f - io.MouseWheel * 0.1f),
                                                1.0f, 500.0f);
            }
        }
    }

    if (scene && texture) {
        ImGuiIO& io = ImGui::GetIO();
        if (ImGui::IsWindowFocused(ImGuiFocusedFlags_RootAndChildWindows) && !io.WantTextInput) {
            if (ImGui::IsKeyPressed(ImGuiKey_W)) gizmoMode_ = 0;
            if (ImGui::IsKeyPressed(ImGuiKey_E)) gizmoMode_ = 1;
            if (ImGui::IsKeyPressed(ImGuiKey_R)) gizmoMode_ = 2;
        }
        drawViewportOverlay(*scene, rectMin, rectMax);
    }

    if (texture && ImGui::BeginDragDropTarget()) {
        if (const ImGuiPayload* payload = ImGui::AcceptDragDropPayload("VELVX_ASSET")) {
            const std::string dropped(static_cast<const char*>(payload->Data));
            assignAsset(std::filesystem::path(dropped));
        }
        ImGui::EndDragDropTarget();
    }
    ImGui::End();
}

void EditorApp::drawGameView() {
    if (!ImGui::Begin("Game")) {
        ImGui::End();
        return;
    }
    Scene* scene = Engine::getSceneManager().getActiveScene();
    const Camera* activeCam = scene ? scene->getActiveCamera() : nullptr;
    ImGui::TextDisabled("Camera ativa: %s | Play inicia o VelvX Player",
                        activeCam ? "Camera (game)" : "legada da cena");

    const ImVec2 avail = ImGui::GetContentRegionAvail();
    const int width = std::max(1, static_cast<int>(avail.x));
    const int height = std::max(1, static_cast<int>(avail.y));

    if (scene) {
        Renderer::setEditorCameraOverride(false);
        Renderer::beginSceneTarget(width, height);
        Renderer::renderScene(scene, time_);
        // Copia o resultado para uma textura própria (independente do Scene view).
        if (!gameTex_) glGenTextures(1, &gameTex_);
        glBindTexture(GL_TEXTURE_2D, gameTex_);
        glCopyTexImage2D(GL_TEXTURE_2D, 0, GL_RGBA8, 0, 0, width, height, 0);
        glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, GL_LINEAR);
        glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, GL_LINEAR);
        Renderer::endSceneTarget();
        Renderer::setEditorCameraOverride(true);
        ImGui::Image(static_cast<ImTextureID>(static_cast<intptr_t>(gameTex_)),
                     ImVec2(static_cast<float>(width), static_cast<float>(height)),
                     ImVec2(0, 1), ImVec2(1, 0));
    } else {
        ImGui::TextDisabled("Abra um projeto para visualizar o jogo");
    }
    ImGui::End();
}

void EditorApp::drawViewportOverlay(Scene& scene, const ImVec2& rectMin, const ImVec2& rectMax) {
    // Valida seleção contra a cena atual (evita ponteiro pendurado pós-delete).
    bool found = false;
    for (const auto& obj : scene.gameObjects) {
        if (obj.get() == selected_) { found = true; break; }
    }
    if (!found) selected_ = nullptr;

    ImDrawList* dl = ImGui::GetWindowDrawList();
    const glm::mat4 vp = Renderer::projectionMatrix() * Renderer::viewMatrix();
    const glm::vec3 camPos = Renderer::cameraPosition();
    const bool ortho = scene.cameraMode == Scene::CameraMode::Ortho2D;
    const float rectH = std::max(1.0f, rectMax.y - rectMin.y);
    ImGuiIO& io = ImGui::GetIO();
    const bool hovered = ImGui::IsWindowHovered(ImGuiHoveredFlags_None);

    // Geometria do gizmo para o objeto selecionado (recomputada a cada frame).
    glm::vec3 center(0.0f), half(0.0f);
    ImVec2 cSc(0, 0), endSc[3], axisDir[3];
    float ringR[3] = {0, 0, 0};
    float upp = 1.0f;
    bool haveGeom = false;
    const auto computeGeom = [&]() {
        haveGeom = false;
        if (!selected_) return;
        center = editorWorldPos(selected_);
        half = editorHalfExtent(selected_);
        const float dist = std::max(0.5f, glm::length(camPos - center));
        upp = ortho ? (scene.orthoHeight / rectH)
                    : (2.0f * dist * std::tan(glm::radians(45.0f) * 0.5f) / rectH);
        const float lenWorld = std::clamp(ortho ? scene.orthoHeight * 0.18f : dist * 0.18f, 0.3f, 8.0f);
        if (!worldToScreen(center, vp, rectMin, rectMax, cSc)) return;
        for (int a = 0; a < 3; ++a) {
            const glm::vec3 end = center + kAxis[a] * lenWorld;
            if (!worldToScreen(end, vp, rectMin, rectMax, endSc[a])) endSc[a] = cSc;
            ImVec2 dv(endSc[a].x - cSc.x, endSc[a].y - cSc.y);
            const float l = std::max(1e-3f, std::sqrt(dv.x * dv.x + dv.y * dv.y));
            axisDir[a] = ImVec2(dv.x / l, dv.y / l);
            ringR[a] = (lenWorld / upp) * (1.0f - 0.18f * a);
        }
        haveGeom = true;
    };
    computeGeom();

    // Clique: primeiro tenta agarrar um handle do gizmo; só então seleciona por
    // raio. (Antes a seleção vinha primeiro e o clique na seta — fora da AABB —
    // desselecionava o objeto, impedindo qualquer arraste.)
    if (hovered && ImGui::IsMouseClicked(ImGuiMouseButton_Left) && !gizmoDragging_) {
        int bestAxis = -1;
        if (haveGeom) {
            float bestD = 12.0f;
            for (int a = 0; a < 3; ++a) {
                float d;
                if (gizmoMode_ == 1) {
                    const ImVec2 mv(io.MousePos.x - cSc.x, io.MousePos.y - cSc.y);
                    d = std::fabs(std::sqrt(mv.x * mv.x + mv.y * mv.y) - ringR[a]);
                } else {
                    const ImVec2 mv(io.MousePos.x - endSc[a].x, io.MousePos.y - endSc[a].y);
                    d = std::sqrt(mv.x * mv.x + mv.y * mv.y);
                }
                if (d < bestD) { bestD = d; bestAxis = a; }
            }
        }
        if (bestAxis >= 0) {
            pushUndo();
            dragAxis_ = bestAxis;
            gizmoDragging_ = true;
        } else {
            glm::vec3 o, d;
            screenRay(io.MousePos, vp, rectMin, rectMax, o, d);
            GameObject* best = nullptr;
            float bestT = 1e9f;
            for (const auto& obj : scene.gameObjects) {
                const auto* col = obj->getComponent<Collider>();
                const auto* mesh = obj->getComponent<MeshRenderer>();
                if (!col && !mesh) continue;
                const float t = rayAABB(o, d, editorWorldPos(obj.get()), editorHalfExtent(obj.get()));
                if (t > 0.0f && t < bestT) { bestT = t; best = obj.get(); }
            }
            selected_ = best;
            computeGeom();
        }
    }

    // Arraste do gizmo (move/rotate/scale) no eixo agarrado.
    if (gizmoDragging_ && ImGui::IsMouseDown(ImGuiMouseButton_Left) && dragAxis_ >= 0 && haveGeom) {
        const int a = dragAxis_;
        const float along = io.MouseDelta.x * axisDir[a].x + io.MouseDelta.y * axisDir[a].y;
        Transform& tf = selected_->transform;
        const glm::mat4 pw = tf.parent ? tf.parent->worldMatrix() : glm::mat4(1.0f);
        if (gizmoMode_ == 0) {
            const glm::vec3 newWorld = center + kAxis[a] * (along * upp);
            tf.position = glm::vec3(glm::inverse(pw) * glm::vec4(newWorld, 1.0f));
        } else if (gizmoMode_ == 2) {
            const float factor = std::clamp(1.0f + along * 0.01f, 0.5f, 2.0f);
            tf.scale[a] = std::max(0.01f, tf.scale[a] * factor);
        } else {
            const ImVec2 prev(io.MousePos.x - io.MouseDelta.x - cSc.x,
                              io.MousePos.y - io.MouseDelta.y - cSc.y);
            const ImVec2 cur(io.MousePos.x - cSc.x, io.MousePos.y - cSc.y);
            float dAng = std::atan2(cur.y, cur.x) - std::atan2(prev.y, prev.x);
            while (dAng > 3.14159265f) dAng -= 6.2831853f;
            while (dAng < -3.14159265f) dAng += 6.2831853f;
            const glm::quat worldQ = glm::quat_cast(glm::mat3(tf.worldMatrix()));
            const glm::quat parentQ = glm::quat_cast(glm::mat3(pw));
            tf.rotation = glm::inverse(parentQ) * (glm::angleAxis(dAng, kAxis[a]) * worldQ);
        }
    }
    if (ImGui::IsMouseReleased(ImGuiMouseButton_Left)) { gizmoDragging_ = false; dragAxis_ = -1; }

    if (!selected_ || !haveGeom) return;

    // Caixa de destaque (12 arestas) sobre o objeto selecionado.
    glm::vec3 corners[8];
    int ci = 0;
    for (int x = 0; x < 2; ++x)
        for (int y = 0; y < 2; ++y)
            for (int z = 0; z < 2; ++z)
                corners[ci++] = center + glm::vec3(x ? half.x : -half.x, y ? half.y : -half.y,
                                                   z ? half.z : -half.z);
    ImVec2 sc[8];
    bool ok[8];
    for (int i = 0; i < 8; ++i) ok[i] = worldToScreen(corners[i], vp, rectMin, rectMax, sc[i]);
    const int edges[12][2] = {{0,1},{2,3},{4,5},{6,7},{0,2},{1,3},{4,6},{5,7},
                              {0,4},{1,5},{2,6},{3,7}};
    for (const auto& e : edges) {
        if (ok[e[0]] && ok[e[1]]) dl->AddLine(sc[e[0]], sc[e[1]], IM_COL32(255, 200, 40, 255), 1.5f);
    }

    // Desenho dos gizmos (overlay do editor, fora da cena do jogo).
    for (int a = 0; a < 3; ++a) {
        const bool active = (dragAxis_ == a);
        const ImU32 col = active ? IM_COL32(255, 255, 255, 255) : kAxisColor[a];
        const float thick = active ? 3.0f : 2.0f;
        if (gizmoMode_ == 1) {
            dl->AddCircle(cSc, ringR[a], col, 40, thick);
        } else {
            dl->AddLine(cSc, endSc[a], col, thick);
            if (gizmoMode_ == 0) {
                dl->AddTriangleFilled(endSc[a],
                                      ImVec2(endSc[a].x - axisDir[a].x * 10 - axisDir[a].y * 5,
                                             endSc[a].y - axisDir[a].y * 10 + axisDir[a].x * 5),
                                      ImVec2(endSc[a].x - axisDir[a].x * 10 + axisDir[a].y * 5,
                                             endSc[a].y - axisDir[a].y * 10 - axisDir[a].x * 5),
                                      col);
            } else {
                dl->AddRectFilled(ImVec2(endSc[a].x - 4, endSc[a].y - 4),
                                  ImVec2(endSc[a].x + 4, endSc[a].y + 4), col);
            }
        }
    }
    dl->AddCircleFilled(cSc, 4.0f, IM_COL32(255, 255, 255, 255));
}