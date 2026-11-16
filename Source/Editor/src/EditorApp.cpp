/**
 * @file EditorApp.cpp
 * @brief Ciclo de vida, toolbar, diálogos e exportação do VelvX Editor.
 */

#ifdef _WIN32
#ifndef NOMINMAX
#define NOMINMAX
#endif
#include <windows.h>
#else
#include <csignal>
#include <sys/types.h>
#include <sys/wait.h>
#include <unistd.h>
#endif

#include "EditorApp.hpp"
#include "Core/GameObject.hpp"
#include "Core/Log.hpp"
#include "Core/Scene.hpp"
#include "Core/SceneManager.hpp"
#include "Engine/Engine.hpp"
#include "Input/InputManager.hpp"
#include "PluginAPI/PluginManager.hpp"
#include "Renderer/ModelImporter.hpp"
#include "Renderer/Renderer.hpp"
#include "Utils/FileSystem.hpp"
#include "Utils/PackFile.hpp"
#include <imgui.h>
#include <imgui_internal.h>
#include <misc/cpp/imgui_stdlib.h>
#include <algorithm>
#include <cstdlib>
#include <fstream>
#include <sstream>
#include <system_error>

using namespace VelvX;

/** @brief Diretório padrão para novos projetos (Documentos/VelvXProjects). */
static std::filesystem::path defaultProjectsDir() {
#ifdef _WIN32
    const char* profile = std::getenv("USERPROFILE");
    std::filesystem::path home = profile ? profile : "C:";
#else
    const char* homeEnv = std::getenv("HOME");
    std::filesystem::path home = homeEnv ? homeEnv : ".";
#endif
    return home / "Documents" / "VelvXProjects";
}

#ifdef _WIN32
static void* spawnProcess(const std::filesystem::path& exe, const std::string& args,
                          const std::filesystem::path& workdir) {
    std::string cmd = "\"" + exe.string() + "\" " + args;
    std::vector<char> buf(cmd.begin(), cmd.end());
    buf.push_back('\0');
    STARTUPINFOA si{};
    si.cb = sizeof(si);
    PROCESS_INFORMATION pi{};
    if (!CreateProcessA(nullptr, buf.data(), nullptr, nullptr, FALSE, 0, nullptr,
                        workdir.string().c_str(), &si, &pi)) {
        return nullptr;
    }
    CloseHandle(pi.hThread);
    return pi.hProcess;
}
#else
static std::vector<std::string> splitArgs(const std::string& args) {
    std::vector<std::string> out;
    std::string current;
    bool quoted = false;
    for (char c : args) {
        if (c == '"') { quoted = !quoted; continue; }
        if (c == ' ' && !quoted) {
            if (!current.empty()) out.push_back(current);
            current.clear();
            continue;
        }
        current += c;
    }
    if (!current.empty()) out.push_back(current);
    return out;
}

static int spawnProcess(const std::filesystem::path& exe, const std::string& args,
                        const std::filesystem::path& workdir) {
    const pid_t pid = fork();
    if (pid != 0) return pid;
    std::error_code ec;
    std::filesystem::current_path(workdir, ec);
    std::vector<std::string> parts = splitArgs(args);
    std::vector<char*> argv;
    argv.push_back(const_cast<char*>(exe.c_str()));
    for (auto& p : parts) argv.push_back(p.data());
    argv.push_back(nullptr);
    execv(exe.c_str(), argv.data());
    _exit(127);
}
#endif

bool EditorApp::init(GLFWwindow* window, const std::filesystem::path& projectFile) {
    window_ = window;
    Log::setConsoleSink([this](const std::string& level, const std::string& message) {
        std::lock_guard<std::mutex> lock(consoleMutex_);
        console_.push_back({level, message});
        if (console_.size() > 2000) console_.pop_front();
    });

    const std::filesystem::path templatesRoot = Engine::getExecutableDir() / "templates";
    std::error_code ec;
    for (const auto& entry : std::filesystem::directory_iterator(templatesRoot, ec)) {
        if (entry.is_directory()) templates_.push_back(entry.path().filename().string());
    }
    std::sort(templates_.begin(), templates_.end());

    newProjectDir_ = defaultProjectsDir().string();
    if (!projectFile.empty()) openProject(projectFile);
    return true;
}

void EditorApp::shutdown() {
    stopPlay();
    Log::setConsoleSink(nullptr);
}

void EditorApp::frame(float deltaTime) {
    time_ += deltaTime;
    assetRefreshTimer_ += deltaTime;
    if (assetRefreshTimer_ > 3.0f) refreshAssets();
    ModelImporter::update(deltaTime);

    const ImGuiID dockspace = ImGui::DockSpaceOverViewport(0, ImGui::GetMainViewport());
    static bool firstFrame = true;
    bool buildLayout = resetLayout_;
    if (firstFrame) {
        firstFrame = false;
        std::error_code ec;
        const char* ini = ImGui::GetIO().IniFilename;
        const bool savedLayout = ini && std::filesystem::exists(ini, ec);
        const auto iniSize = savedLayout ? std::filesystem::file_size(ini, ec) : 0;
        // Self-heal: ini ausente ou degenerado (vazio/minúsculo) => layout padrão.
        if (!savedLayout || iniSize < 200) buildLayout = true;
        // Migracao: layout antigo sem o painel Script => reconstruir uma vez.
        if (savedLayout && !buildLayout) {
            std::ifstream in(ini);
            std::ostringstream buf;
            buf << in.rdbuf();
            if (buf.str().find("[Window][Script]") == std::string::npos) buildLayout = true;
        }
    }
    if (buildLayout) {
        resetLayout_ = false;
        ImGui::DockBuilderRemoveNode(dockspace);
        ImGui::DockBuilderAddNode(dockspace, ImGuiDockNodeFlags_DockSpace);
        ImGui::DockBuilderSetNodeSize(dockspace, ImGui::GetMainViewport()->WorkSize);
        ImGuiID top = 0;
        ImGuiID rest = 0;
        ImGuiID left = 0;
        ImGuiID right = 0;
        ImGuiID bottom = 0;
        ImGuiID center = 0;
        ImGui::DockBuilderSplitNode(dockspace, ImGuiDir_Up, 0.055f, &top, &rest);
        ImGui::DockBuilderSplitNode(rest, ImGuiDir_Left, 0.18f, &left, &center);
        ImGui::DockBuilderSplitNode(center, ImGuiDir_Right, 0.24f, &right, &center);
        ImGui::DockBuilderSplitNode(center, ImGuiDir_Down, 0.30f, &bottom, &center);
        ImGui::DockBuilderDockWindow("Toolbar", top);
        ImGui::DockBuilderDockWindow("Hierarchy", left);
        ImGui::DockBuilderDockWindow("Inspector", right);
        ImGui::DockBuilderDockWindow("Assets", bottom);
        ImGui::DockBuilderDockWindow("Console", bottom);
        ImGui::DockBuilderDockWindow("Script", bottom);
        ImGui::DockBuilderDockWindow("Scene", center);
        ImGui::DockBuilderDockWindow("Game", center);
        ImGui::DockBuilderFinish(dockspace);
    }
    drawMenuBar();
    drawToolbar();
    drawHierarchy();
    drawInspector();
    drawAssets();
    drawConsole();
    drawScriptPanel();
    drawGameView();
    drawViewport();

    if (showNewProject_) drawNewProjectDialog();
    if (showOpenProject_) drawOpenProjectDialog();
    if (showSettings_) drawSettingsWindow();
    if (showExport_) drawExportDialog();
    drawRenameDialog();
    drawSaveAsDialog();
    drawAssetDialogs();
    handleShortcuts();
}

// ---------------------------------------------------------------------------
// Menu / toolbar
// ---------------------------------------------------------------------------

void EditorApp::drawMenuBar() {
    if (!ImGui::BeginMainMenuBar()) return;
    if (ImGui::BeginMenu("Projeto")) {
        if (ImGui::MenuItem("Novo projeto...")) showNewProject_ = true;
        if (ImGui::MenuItem("Abrir projeto...")) {
            foundProjects_ = Project::findProjects(defaultProjectsDir());
            showOpenProject_ = true;
        }
        if (ImGui::MenuItem("Salvar projeto + cena")) saveProjectAndScene();
        ImGui::Separator();
        if (ImGui::MenuItem("Sair")) glfwSetWindowShouldClose(window_, GLFW_TRUE);
        ImGui::EndMenu();
    }
    if (ImGui::BeginMenu("Ferramentas")) {
        if (ImGui::MenuItem("VelvX Script IDE")) spawnTool("VelvXScriptIDE", {});
        if (ImGui::MenuItem("VelvX UI Editor")) spawnTool("VelvXUIEditor", {});
        if (ImGui::MenuItem("VelvX Launcher")) spawnTool("VelvXLauncher", {});
        ImGui::Separator();
        if (ImGui::MenuItem("Project Settings")) showSettings_ = true;
        if (ImGui::MenuItem("Exportar jogo...")) showExport_ = true;
        ImGui::EndMenu();
    }
    if (ImGui::BeginMenu("Janela")) {
        if (ImGui::MenuItem("Restaurar layout padrão")) resetLayout_ = true;
        ImGui::EndMenu();
    }
    ImGui::EndMainMenuBar();
}

void EditorApp::drawToolbar() {
    ImGui::Begin("Toolbar", nullptr,
                 ImGuiWindowFlags_NoCollapse | ImGuiWindowFlags_NoScrollbar);
    if (ImGui::Button("Novo")) showNewProject_ = true;
    ImGui::SameLine();
    if (ImGui::Button("Abrir")) {
        foundProjects_ = Project::findProjects(defaultProjectsDir());
        showOpenProject_ = true;
    }
    ImGui::SameLine();
    if (ImGui::Button("Salvar")) saveProjectAndScene();
    ImGui::SameLine();
    ImGui::TextDisabled("|");
    ImGui::SameLine();
    if (isPlaying()) {
        ImGui::PushStyleColor(ImGuiCol_Button, ImVec4(0.7f, 0.2f, 0.2f, 1.0f));
        if (ImGui::Button("Stop")) stopPlay();
        ImGui::PopStyleColor();
    } else {
        ImGui::PushStyleColor(ImGuiCol_Button, ImVec4(0.2f, 0.6f, 0.3f, 1.0f));
        if (ImGui::Button("Play")) play();
        ImGui::PopStyleColor();
    }
    ImGui::SameLine();
    ImGui::TextDisabled("|");
    ImGui::SameLine();
    if (ImGui::Button("Settings")) showSettings_ = true;
    ImGui::SameLine();
    if (ImGui::Button("Exportar")) showExport_ = true;
    ImGui::SameLine();
    ImGui::TextDisabled("%s", Engine::project().file.empty()
                                 ? "sem projeto"
                                 : Engine::project().name.c_str());
    ImGui::End();
}

// ---------------------------------------------------------------------------
// Projeto
// ---------------------------------------------------------------------------

void EditorApp::openProject(const std::filesystem::path& file) {
    selected_ = nullptr;
    Engine::getSceneManager().clear();
    if (!Engine::loadProject(file)) {
        Log::error("Editor: falha ao abrir projeto: " + file.string());
        return;
    }
    Project& project = Engine::project();
    Renderer::setShadowSettings(project.settings.enableShadows, project.settings.shadowMapSize());
    project.settings.applyInput();
    PluginManager::loadPluginsFromDirectory((project.directory / "plugins").string());
    loadSceneByName(project.mainScene);
    refreshAssets();
    exportDir_ = (project.directory / "Export").string();
    Log::info("Editor: projeto '" + project.name + "' aberto");
}

void EditorApp::saveProjectAndScene() {
    Project& project = Engine::project();
    if (project.file.empty()) {
        Log::warn("Editor: nenhum projeto aberto para salvar");
        return;
    }
    if (Scene* scene = Engine::getSceneManager().getActiveScene()) {
        scene->save(project.directory / "scenes" / (scene->getName() + ".vscene"));
    }
    if (project.save()) {
        Log::info("Editor: projeto salvo (" + project.file.string() + ")");
    } else {
        Log::error("Editor: falha ao salvar projeto");
    }
}

void EditorApp::loadSceneByName(const std::string& name) {
    SceneManager& sm = Engine::getSceneManager();
    if (sm.scenes().find(name) == sm.scenes().end()) {
        sm.registerScene(name, std::make_unique<Scene>(name));
    }
    sm.loadScene(name);
    sceneName_ = name;
    selected_ = nullptr;
}

void EditorApp::refreshAssets() {
    assetRefreshTimer_ = 0.0f;
    assetEntries_.clear();
    const std::filesystem::path root = FS::projectRoot();
    std::error_code ec;
    if (!std::filesystem::is_directory(root, ec)) return;
    for (const auto& entry : std::filesystem::recursive_directory_iterator(root, ec)) {
        const std::filesystem::path rel = std::filesystem::relative(entry.path(), root, ec);
        if (rel.empty()) continue;
        const std::string first = rel.begin()->string();
        if (first == "Export" || first == ".git") continue;
        assetEntries_.push_back(rel);
    }
    std::sort(assetEntries_.begin(), assetEntries_.end());
}

// ---------------------------------------------------------------------------
// Play (subprocesso VelvX Player)
// ---------------------------------------------------------------------------

void EditorApp::play() {
    Project& project = Engine::project();
    if (project.file.empty()) {
        Log::warn("Editor: abra um projeto antes de usar o Play");
        return;
    }
    saveProjectAndScene();
    const std::filesystem::path exe = Engine::getExecutableDir() / "VelvXPlayer.exe";
    const std::string args = "--project \"" + project.file.string() + "\"";
#ifdef _WIN32
    playProcess_ = spawnProcess(exe, args, project.directory);
    if (!playProcess_) Log::error("Editor: falha ao iniciar o VelvX Player");
#else
    playPid_ = spawnProcess(exe, args, project.directory);
    if (playPid_ <= 0) Log::error("Editor: falha ao iniciar o VelvX Player");
#endif
    Log::info("Editor: Play iniciado (VelvX Player)");
}

void EditorApp::stopPlay() {
#ifdef _WIN32
    if (playProcess_) {
        TerminateProcess(static_cast<HANDLE>(playProcess_), 0);
        CloseHandle(static_cast<HANDLE>(playProcess_));
        playProcess_ = nullptr;
        Log::info("Editor: Play encerrado");
    }
#else
    if (playPid_ > 0) {
        kill(playPid_, SIGTERM);
        waitpid(playPid_, nullptr, 0);
        playPid_ = -1;
        Log::info("Editor: Play encerrado");
    }
#endif
}

bool EditorApp::isPlaying() {
#ifdef _WIN32
    if (!playProcess_) return false;
    if (WaitForSingleObject(static_cast<HANDLE>(playProcess_), 0) == WAIT_TIMEOUT) return true;
    CloseHandle(static_cast<HANDLE>(playProcess_));
    playProcess_ = nullptr;
    return false;
#else
    if (playPid_ <= 0) return false;
    if (waitpid(playPid_, nullptr, WNOHANG) == 0) return true;
    playPid_ = -1;
    return false;
#endif
}

void EditorApp::spawnTool(const std::string& exeName, const std::filesystem::path& argument) {
    const std::filesystem::path exe = Engine::getExecutableDir() / (exeName + ".exe");
    std::error_code ec;
    if (!std::filesystem::exists(exe, ec)) {
        Log::error("Editor: ferramenta não encontrada: " + exe.string());
        return;
    }
    const std::string args = argument.empty() ? "" : "\"" + argument.string() + "\"";
#ifdef _WIN32
    void* handle = spawnProcess(exe, args, FS::projectRoot());
    if (handle) CloseHandle(static_cast<HANDLE>(handle));
#else
    spawnProcess(exe, args, FS::projectRoot());
#endif
}

// ---------------------------------------------------------------------------
// Diálogos: novo / abrir projeto
// ---------------------------------------------------------------------------

void EditorApp::drawNewProjectDialog() {
    ImGui::OpenPopup("Novo Projeto");
    showNewProject_ = false;
    if (!ImGui::BeginPopupModal("Novo Projeto", nullptr, ImGuiWindowFlags_AlwaysAutoResize)) return;

    ImGui::InputText("Nome", &newProjectName_);
    ImGui::InputText("Diretorio", &newProjectDir_);
    if (!templates_.empty()) {
        ImGui::Combo(
            "Template", &newProjectTemplate_,
            [](void* data, int idx) -> const char* {
                auto& list = *static_cast<std::vector<std::string>*>(data);
                if (idx < 0 || idx >= static_cast<int>(list.size())) return "";
                return list[idx].c_str();
            },
            &templates_, static_cast<int>(templates_.size()));
    } else {
        ImGui::TextDisabled("Nenhum template em Build Release/templates");
    }
    ImGui::Separator();
    if (ImGui::Button("Criar") && !templates_.empty() && !newProjectName_.empty()) {
        const std::filesystem::path created = Project::createFromTemplate(
            newProjectDir_, newProjectName_, templates_[newProjectTemplate_],
            Engine::getExecutableDir() / "templates");
        if (!created.empty()) {
            openProject(created);
            ImGui::CloseCurrentPopup();
        }
    }
    ImGui::SameLine();
    if (ImGui::Button("Cancelar")) ImGui::CloseCurrentPopup();
    ImGui::EndPopup();
}

void EditorApp::drawOpenProjectDialog() {
    ImGui::OpenPopup("Abrir Projeto");
    showOpenProject_ = false;
    if (!ImGui::BeginPopupModal("Abrir Projeto", nullptr, ImGuiWindowFlags_AlwaysAutoResize)) return;

    ImGui::InputText("Arquivo .velvproj", &openProjectPath_);
    if (ImGui::Button("Abrir caminho") && !openProjectPath_.empty()) {
        openProject(openProjectPath_);
        ImGui::CloseCurrentPopup();
    }
    ImGui::SameLine();
    if (ImGui::Button("Atualizar lista")) {
        foundProjects_ = Project::findProjects(defaultProjectsDir());
    }
    ImGui::Separator();
    ImGui::TextDisabled("Projetos em %s", defaultProjectsDir().string().c_str());
    ImGui::BeginChild("listaProjetos", ImVec2(420, 180), ImGuiChildFlags_Borders);
    for (const auto& projectFile : foundProjects_) {
        if (ImGui::Selectable(projectFile.string().c_str())) {
            openProject(projectFile);
            ImGui::CloseCurrentPopup();
        }
    }
    ImGui::EndChild();
    if (ImGui::Button("Fechar")) ImGui::CloseCurrentPopup();
    ImGui::EndPopup();
}

// ---------------------------------------------------------------------------
// Project Settings
// ---------------------------------------------------------------------------

void EditorApp::drawSettingsWindow() {
    Project& project = Engine::project();
    ProjectSettings& s = project.settings;
    if (!ImGui::Begin("Project Settings", &showSettings_)) {
        ImGui::End();
        return;
    }

    if (ImGui::CollapsingHeader("Janela", ImGuiTreeNodeFlags_DefaultOpen)) {
        ImGui::InputInt("Largura", &s.windowWidth);
        ImGui::InputInt("Altura", &s.windowHeight);
        ImGui::Checkbox("Tela cheia", &s.fullscreen);
        ImGui::InputText("Titulo", &s.windowTitle);
    }
    if (ImGui::CollapsingHeader("Graficos", ImGuiTreeNodeFlags_DefaultOpen)) {
        const char* aa[] = {"0 (off)", "2x", "4x", "8x"};
        int aaIndex = s.antiAliasingSamples == 0 ? 0 : s.antiAliasingSamples == 2 ? 1
                      : s.antiAliasingSamples == 8 ? 3 : 2;
        if (ImGui::Combo("Anti-aliasing", &aaIndex, aa, 4)) {
            s.antiAliasingSamples = aaIndex == 0 ? 0 : aaIndex == 1 ? 2 : aaIndex == 2 ? 4 : 8;
        }
        ImGui::Checkbox("Sombras", &s.enableShadows);
        const char* qualities[] = {"Baixa (512)", "Media (1024)", "Alta (2048)", "Ultra (4096)"};
        ImGui::Combo("Qualidade da sombra", &s.shadowQuality, qualities, 4);
        ImGui::Checkbox("VSync", &s.vsync);
    }
    if (ImGui::CollapsingHeader("Audio")) {
        ImGui::SliderFloat("Master", &s.masterVolume, 0.0f, 1.0f);
        ImGui::SliderFloat("Musica", &s.musicVolume, 0.0f, 1.0f);
        ImGui::SliderFloat("SFX", &s.sfxVolume, 0.0f, 1.0f);
    }
    if (ImGui::CollapsingHeader("Interface / Acessibilidade")) {
        const char* languages[] = {"pt-BR", "en-US", "es-ES"};
        int langIndex = s.language == "en-US" ? 1 : s.language == "es-ES" ? 2 : 0;
        if (ImGui::Combo("Idioma", &langIndex, languages, 3)) s.language = languages[langIndex];
        ImGui::SliderFloat("Escala de fonte", &s.fontScale, 0.8f, 2.0f);
        ImGui::Checkbox("Alto contraste", &s.highContrast);
    }
    if (ImGui::CollapsingHeader("Input", ImGuiTreeNodeFlags_DefaultOpen)) {
        ImGui::SliderFloat("Deadzone gamepad", &s.gamepadDeadzone, 0.0f, 0.9f);
        ImGui::TextDisabled("Gamepad: %s", InputManager::isGamepadConnected()
                                               ? InputManager::gamepadLabel().c_str()
                                               : "desconectado");
        if (!s.inputProfiles.empty()) {
            std::vector<std::string> names;
            for (const auto& profile : s.inputProfiles) names.push_back(profile.name);
            std::vector<const char*> items;
            for (const auto& n : names) items.push_back(n.c_str());
            settingsProfile_ = std::clamp(settingsProfile_, 0, static_cast<int>(names.size()) - 1);
            ImGui::Combo("Perfil em edicao", &settingsProfile_, items.data(),
                         static_cast<int>(items.size()));
            s.activeInputProfile = std::clamp(s.activeInputProfile, 0,
                                              static_cast<int>(names.size()) - 1);
            ImGui::Combo("Perfil ativo no jogo", &s.activeInputProfile, items.data(),
                         static_cast<int>(items.size()));
            drawBindingEditor();
        }
        if (ImGui::Button("Novo perfil")) {
            InputProfile profile;
            profile.name = "Perfil " + std::to_string(s.inputProfiles.size() + 1);
            s.inputProfiles.push_back(profile);
            settingsProfile_ = static_cast<int>(s.inputProfiles.size()) - 1;
        }
        ImGui::SameLine();
        if (ImGui::Button("Remover perfil") && !s.inputProfiles.empty()) {
            s.inputProfiles.erase(s.inputProfiles.begin() + settingsProfile_);
            settingsProfile_ = 0;
        }
    }

    if (ImGui::CollapsingHeader("Fisica / Colisao", ImGuiTreeNodeFlags_DefaultOpen)) {
        s.ensureCollisionMatrix();
        ImGui::Checkbox("Plano de chao global (y=0)", &s.useGroundPlane);
        ImGui::TextDisabled("Camadas (indice = GameObject::layer). Renomeie ou adicione:");
        for (size_t i = 0; i < s.collisionLayers.size(); ++i) {
            ImGui::PushID(static_cast<int>(i));
            std::string label = "Camada " + std::to_string(i);
            ImGui::SetNextItemWidth(220.0f);
            ImGui::InputText(label.c_str(), &s.collisionLayers[i]);
            ImGui::PopID();
        }
        if (ImGui::Button("Adicionar camada")) {
            s.collisionLayers.push_back("Camada " + std::to_string(s.collisionLayers.size()));
            s.ensureCollisionMatrix();
        }
        ImGui::Separator();
        ImGui::TextDisabled("Matriz de colisao (marcado = colide). Simetrica:");
        const int n = static_cast<int>(s.collisionLayers.size());
        if (ImGui::BeginTable("colmatrix", n + 1, ImGuiTableFlags_Borders | ImGuiTableFlags_RowBg)) {
            ImGui::TableSetupColumn("");
            for (int c = 0; c < n; ++c) ImGui::TableSetupColumn(s.collisionLayers[c].c_str());
            ImGui::TableHeadersRow();
            for (int r = 0; r < n; ++r) {
                ImGui::TableNextRow();
                ImGui::TableSetColumnIndex(0);
                ImGui::TextUnformatted(s.collisionLayers[r].c_str());
                for (int c = 0; c < n; ++c) {
                    ImGui::TableSetColumnIndex(c + 1);
                    ImGui::PushID(r * 64 + c);
                    bool on = s.collisionMatrix[r][c];
                    if (ImGui::Checkbox("##m", &on)) {
                        s.collisionMatrix[r][c] = on;
                        s.collisionMatrix[c][r] = on;
                    }
                    ImGui::PopID();
                }
            }
            ImGui::EndTable();
        }
    }

    ImGui::Separator();
    if (ImGui::Button("Salvar e aplicar")) {
        s.applyInput();
        Renderer::setShadowSettings(s.enableShadows, s.shadowMapSize());
        if (!project.file.empty()) project.save();
        Log::info("Editor: Project Settings aplicadas e salvas");
    }
    ImGui::End();
}

void EditorApp::drawBindingEditor() {
    ProjectSettings& s = Engine::project().settings;
    if (settingsProfile_ < 0 || settingsProfile_ >= static_cast<int>(s.inputProfiles.size())) return;
    InputProfile& profile = s.inputProfiles[settingsProfile_];

    auto buf = [&](const std::string& id, const std::string& current) -> std::string& {
        auto it = stringEdits_.find(id);
        if (it == stringEdits_.end()) it = stringEdits_.emplace(id, current).first;
        return it->second;
    };

    ImGui::InputText("Nome do perfil", &profile.name);
    ImGui::SeparatorText("Acoes");
    int removeBinding = -1;
    for (size_t i = 0; i < profile.bindings.size(); ++i) {
        InputBinding& binding = profile.bindings[i];
        ImGui::PushID(static_cast<int>(i));
        ImGui::SetNextItemWidth(140);
        ImGui::InputText("##acao", &binding.actionName);
        ImGui::SameLine();
        const std::string primaryKeyId = "p" + std::to_string(settingsProfile_) + "_" + std::to_string(i);
        const std::string secondaryKeyId = "s" + std::to_string(settingsProfile_) + "_" + std::to_string(i);
        std::string& primaryBuf = buf(primaryKeyId, InputManager::keyToString(binding.primaryKey));
        std::string& secondaryBuf = buf(secondaryKeyId, InputManager::keyToString(binding.secondaryKey));
        ImGui::SetNextItemWidth(110);
        if (ImGui::InputText("##prim", &primaryBuf)) {
            binding.primaryKey = InputManager::keyFromString(primaryBuf);
        }
        ImGui::SameLine();
        ImGui::SetNextItemWidth(110);
        if (ImGui::InputText("##sec", &secondaryBuf)) {
            binding.secondaryKey = InputManager::keyFromString(secondaryBuf);
        }
        ImGui::SameLine();
        if (ImGui::SmallButton("x")) removeBinding = static_cast<int>(i);
        ImGui::PopID();
    }
    if (removeBinding >= 0) {
        profile.bindings.erase(profile.bindings.begin() + removeBinding);
    }
    if (ImGui::SmallButton("+ acao")) {
        profile.bindings.push_back({"NovaAcao", KeyCode::Space, KeyCode::Unknown});
    }

    ImGui::SeparatorText("Eixos");
    int removeAxis = -1;
    for (size_t i = 0; i < profile.axes.size(); ++i) {
        AxisBinding& axis = profile.axes[i];
        ImGui::PushID(1000 + static_cast<int>(i));
        ImGui::SetNextItemWidth(140);
        ImGui::InputText("##eixo", &axis.name);
        ImGui::SameLine();
        const std::string posId = "ap" + std::to_string(settingsProfile_) + "_" + std::to_string(i);
        const std::string negId = "an" + std::to_string(settingsProfile_) + "_" + std::to_string(i);
        std::string& posBuf = buf(posId, InputManager::keyToString(axis.positive));
        std::string& negBuf = buf(negId, InputManager::keyToString(axis.negative));
        ImGui::SetNextItemWidth(110);
        if (ImGui::InputText("##pos", &posBuf)) axis.positive = InputManager::keyFromString(posBuf);
        ImGui::SameLine();
        ImGui::SetNextItemWidth(110);
        if (ImGui::InputText("##neg", &negBuf)) axis.negative = InputManager::keyFromString(negBuf);
        ImGui::SameLine();
        if (ImGui::SmallButton("x")) removeAxis = static_cast<int>(i);
        ImGui::PopID();
    }
    if (removeAxis >= 0) profile.axes.erase(profile.axes.begin() + removeAxis);
    if (ImGui::SmallButton("+ eixo")) {
        profile.axes.push_back({"NovoEixo", KeyCode::D, KeyCode::A});
    }
}

// ---------------------------------------------------------------------------
// Exportação
// ---------------------------------------------------------------------------

void EditorApp::drawExportDialog() {
    ImGui::OpenPopup("Exportar Jogo");
    showExport_ = false;
    if (!ImGui::BeginPopupModal("Exportar Jogo", nullptr, ImGuiWindowFlags_AlwaysAutoResize)) return;

    ImGui::InputText("Destino", &exportDir_);
    ImGui::Checkbox("Compactar assets (pack ZIP)", &exportPack_);
    ImGui::Checkbox("Incluir simbolos de debug (.pdb)", &exportPdb_);
    ImGui::Separator();
    if (ImGui::Button("Exportar")) doExport();
    ImGui::SameLine();
    if (ImGui::Button("Fechar")) ImGui::CloseCurrentPopup();
    if (!exportStatus_.empty()) ImGui::TextWrapped("%s", exportStatus_.c_str());
    ImGui::EndPopup();
}

void EditorApp::doExport() {
    namespace fs = std::filesystem;
    Project& project = Engine::project();
    if (project.file.empty()) {
        exportStatus_ = "Abra um projeto antes de exportar.";
        return;
    }
    saveProjectAndScene();

    const fs::path dest = exportDir_;
    const fs::path exeDir = Engine::getExecutableDir();
    std::error_code ec;
    fs::create_directories(dest, ec);
    if (ec) {
        exportStatus_ = "Falha ao criar destino: " + ec.message();
        return;
    }

    const auto copyFile = [&](const fs::path& from, const fs::path& to) {
        fs::copy_file(from, to, fs::copy_options::overwrite_existing, ec);
        if (ec) Log::warn("Export: " + from.string() + ": " + ec.message());
    };
    copyFile(exeDir / "VelvXPlayer.exe", dest / "VelvXPlayer.exe");
    copyFile(exeDir / "VelvXEngineCore.dll", dest / "VelvXEngineCore.dll");
    for (const char* runtime : {"glfw3.dll", "glad.dll"}) {
        if (fs::exists(exeDir / runtime, ec)) copyFile(exeDir / runtime, dest / runtime);
    }
    fs::copy(exeDir / "shaders", dest / "shaders",
             fs::copy_options::recursive | fs::copy_options::overwrite_existing, ec);
    if (fs::exists(exeDir / "plugins", ec)) {
        fs::copy(exeDir / "plugins", dest / "plugins",
                 fs::copy_options::recursive | fs::copy_options::overwrite_existing, ec);
    }

    if (exportPack_) {
        std::vector<std::pair<std::string, std::vector<uint8_t>>> files;
        for (const auto& entry : fs::recursive_directory_iterator(project.directory, ec)) {
            if (!entry.is_regular_file()) continue;
            const fs::path rel = fs::relative(entry.path(), project.directory, ec);
            if (rel.empty()) continue;
            const std::string first = rel.begin()->string();
            if (first == "Export" || first == "velvx.log") continue;
            files.emplace_back(rel.generic_string(), FS::readBinary(entry.path()));
        }
        if (PackFile::write(dest / "assets.zip", files)) {
            Log::info("Export: pack assets.zip com " + std::to_string(files.size()) + " arquivos");
        } else {
            Log::error("Export: falha ao escrever assets.zip");
        }
        copyFile(project.file, dest / project.file.filename());
        FS::writeText(dest / "Jogar.bat",
                      "@echo off\r\nVelvXPlayer.exe --project \"" +
                          project.file.filename().string() + "\" --pack assets.zip\r\n");
    } else {
        for (const auto& entry : fs::directory_iterator(project.directory, ec)) {
            if (entry.path() == dest) continue;
            fs::copy(entry.path(), dest / entry.path().filename(),
                     fs::copy_options::recursive | fs::copy_options::overwrite_existing, ec);
        }
        FS::writeText(dest / "Jogar.bat",
                      "@echo off\r\nVelvXPlayer.exe --project \"" +
                          project.file.filename().string() + "\"\r\n");
    }

    if (exportPdb_) {
        copyFile(exeDir / "VelvXPlayer.pdb", dest / "VelvXPlayer.pdb");
        copyFile(exeDir / "VelvXEngineCore.pdb", dest / "VelvXEngineCore.pdb");
    }

    exportStatus_ = "Jogo exportado para " + dest.string();
    Log::info("Editor: " + exportStatus_);
}