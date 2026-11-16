/**
 * @file main.cpp
 * @brief VelvX Launcher: lista, cria e abre projetos; inicia Editor ou Player.
 */

#ifdef _WIN32
#ifndef NOMINMAX
#define NOMINMAX
#endif
#include <windows.h>
#else
#include <unistd.h>
#endif

#include <glad/gl.h>
#include <GLFW/glfw3.h>
#include <imgui.h>
#include <backends/imgui_impl_glfw.h>
#include <backends/imgui_impl_opengl3.h>
#include <misc/cpp/imgui_stdlib.h>

#include "Core/Log.hpp"
#include "Core/Project.hpp"
#include "Engine/Engine.hpp"

#include <algorithm>
#include <cstdlib>
#include <filesystem>
#include <string>
#include <vector>

using namespace VelvX;

namespace {

/** @brief Projeto listado no Launcher. */
struct ProjectEntry {
    std::filesystem::path file; ///< Caminho do .velvproj.
    std::string name; ///< Nome do projeto.
    std::string templateType; ///< Template de origem.
};

std::vector<ProjectEntry> g_projects; ///< Projetos encontrados.
int g_selected = -1; ///< Índice selecionado.
std::string g_status; ///< Mensagem de status.
std::string g_searchDir; ///< Diretório extra de busca.
std::string g_newName = "MeuJogo"; ///< Nome do novo projeto.
std::string g_newDir; ///< Diretório do novo projeto.
int g_newTemplate = 0; ///< Template escolhido.
std::vector<std::string> g_templates; ///< Templates disponíveis.

/** @brief Diretório padrão de projetos do usuário. */
std::filesystem::path defaultProjectsDir() {
#ifdef _WIN32
    const char* profile = std::getenv("USERPROFILE");
    const std::filesystem::path home = profile ? profile : "C:";
#else
    const char* profile = std::getenv("HOME");
    const std::filesystem::path home = profile ? profile : ".";
#endif
    return home / "Documents" / "VelvXProjects";
}

/** @brief Inicia um executável da engine com argumentos (sem esperar). */
bool launch(const std::filesystem::path& exe, const std::string& args,
            const std::filesystem::path& workdir) {
#ifdef _WIN32
    std::string cmd = "\"" + exe.string() + "\" " + args;
    std::vector<char> buf(cmd.begin(), cmd.end());
    buf.push_back('\0');
    STARTUPINFOA si{};
    si.cb = sizeof(si);
    PROCESS_INFORMATION pi{};
    if (!CreateProcessA(nullptr, buf.data(), nullptr, nullptr, FALSE, 0, nullptr,
                        workdir.string().c_str(), &si, &pi)) {
        return false;
    }
    CloseHandle(pi.hThread);
    CloseHandle(pi.hProcess);
    return true;
#else
    std::vector<std::string> parts;
    std::string current;
    bool quoted = false;
    for (char c : args) {
        if (c == '"') { quoted = !quoted; continue; }
        if (c == ' ' && !quoted) {
            if (!current.empty()) parts.push_back(current);
            current.clear();
            continue;
        }
        current += c;
    }
    if (!current.empty()) parts.push_back(current);
    const pid_t pid = fork();
    if (pid != 0) return pid > 0;
    std::filesystem::current_path(workdir);
    std::vector<char*> argv;
    argv.push_back(const_cast<char*>(exe.c_str()));
    for (auto& p : parts) argv.push_back(p.data());
    argv.push_back(nullptr);
    execv(exe.c_str(), argv.data());
    _exit(127);
#endif
}

/** @brief Recarrega a lista de projetos (padrão + demos + diretório extra). */
void refreshProjects() {
    g_projects.clear();
    std::vector<std::filesystem::path> roots = {defaultProjectsDir(),
                                                Engine::getExecutableDir() / "Demos"};
    if (!g_searchDir.empty()) roots.push_back(g_searchDir);
    for (const auto& root : roots) {
        std::error_code ec;
        if (!std::filesystem::exists(root, ec)) continue;
        for (const auto& file : Project::findProjects(root)) {
            const bool duplicate =
                std::any_of(g_projects.begin(), g_projects.end(), [&](const ProjectEntry& e) {
                    return std::filesystem::equivalent(e.file, file, ec);
                });
            if (duplicate) continue;
            Project project;
            ProjectEntry entry;
            entry.file = file;
            if (project.load(file)) {
                entry.name = project.name;
                entry.templateType = project.templateType;
            } else {
                entry.name = file.stem().string();
                entry.templateType = "?";
            }
            g_projects.push_back(std::move(entry));
        }
    }
    std::sort(g_projects.begin(), g_projects.end(),
              [](const ProjectEntry& a, const ProjectEntry& b) { return a.name < b.name; });
    if (g_selected >= static_cast<int>(g_projects.size())) g_selected = -1;
}

/** @brief Carrega os templates disponíveis em Build Release/templates. */
void refreshTemplates() {
    g_templates.clear();
    std::error_code ec;
    const std::filesystem::path root = Engine::getExecutableDir() / "templates";
    for (const auto& entry : std::filesystem::directory_iterator(root, ec)) {
        if (entry.is_directory()) g_templates.push_back(entry.path().filename().string());
    }
    std::sort(g_templates.begin(), g_templates.end());
}

/** @brief Painel lateral: lista de projetos + ações. */
void drawProjectList() {
    ImGui::BeginChild("projetos", ImVec2(0, -140), ImGuiChildFlags_Borders);
    if (g_projects.empty()) {
        ImGui::TextDisabled("Nenhum projeto encontrado.");
        ImGui::TextWrapped("Crie um projeto ao lado ou adicione um diretorio de busca.");
    }
    for (int i = 0; i < static_cast<int>(g_projects.size()); ++i) {
        const ProjectEntry& entry = g_projects[i];
        ImGui::PushID(i);
        const std::string label = entry.name + "  [" + entry.templateType + "]";
        if (ImGui::Selectable(label.c_str(), g_selected == i)) g_selected = i;
        if (ImGui::IsItemHovered()) {
            ImGui::SetTooltip("%s", entry.file.string().c_str());
        }
        ImGui::PopID();
    }
    ImGui::EndChild();

    const bool hasSelection = g_selected >= 0 && g_selected < static_cast<int>(g_projects.size());
    if (!hasSelection) ImGui::BeginDisabled();
    if (ImGui::Button("Abrir no Editor", ImVec2(170, 0)) && hasSelection) {
        const ProjectEntry& entry = g_projects[g_selected];
        const std::filesystem::path exe = Engine::getExecutableDir() / "VelvXEditor.exe";
        if (launch(exe, "\"" + entry.file.string() + "\"", entry.file.parent_path())) {
            g_status = "Editor iniciado: " + entry.name;
        } else {
            g_status = "Falha ao iniciar VelvXEditor.exe";
        }
    }
    ImGui::SameLine();
    if (ImGui::Button("Jogar (Play Window)", ImVec2(170, 0)) && hasSelection) {
        const ProjectEntry& entry = g_projects[g_selected];
        const std::filesystem::path exe = Engine::getExecutableDir() / "VelvXPlayer.exe";
        if (launch(exe, "--project \"" + entry.file.string() + "\"", entry.file.parent_path())) {
            g_status = "Player iniciado: " + entry.name;
        } else {
            g_status = "Falha ao iniciar VelvXPlayer.exe";
        }
    }
    if (!hasSelection) ImGui::EndDisabled();
    ImGui::SameLine();
    if (ImGui::Button("Atualizar lista")) refreshProjects();
}

/** @brief Painel inferior: criação de projeto + busca. */
void drawCreatePanel() {
    ImGui::SeparatorText("Novo projeto");
    ImGui::SetNextItemWidth(220);
    ImGui::InputText("Nome", &g_newName);
    ImGui::SameLine();
    ImGui::SetNextItemWidth(320);
    ImGui::InputText("Diretorio", &g_newDir);
    if (!g_templates.empty()) {
        ImGui::SameLine();
        ImGui::SetNextItemWidth(200);
        ImGui::Combo(
            "Template", &g_newTemplate,
            [](void* data, int idx) -> const char* {
                auto& list = *static_cast<std::vector<std::string>*>(data);
                if (idx < 0 || idx >= static_cast<int>(list.size())) return "";
                return list[idx].c_str();
            },
            &g_templates, static_cast<int>(g_templates.size()));
    }
    ImGui::SameLine();
    if (ImGui::Button("Criar projeto") && !g_templates.empty() && !g_newName.empty()) {
        const std::filesystem::path created = Project::createFromTemplate(
            g_newDir, g_newName, g_templates[g_newTemplate],
            Engine::getExecutableDir() / "templates");
        if (!created.empty()) {
            refreshProjects();
            for (int i = 0; i < static_cast<int>(g_projects.size()); ++i) {
                std::error_code ec;
                if (std::filesystem::equivalent(g_projects[i].file, created, ec)) g_selected = i;
            }
            g_status = "Projeto criado: " + created.string();
        } else {
            g_status = "Falha ao criar projeto (verifique nome/diretorio)";
        }
    }

    ImGui::SetNextItemWidth(420);
    ImGui::InputText("Buscar projetos em", &g_searchDir);
    ImGui::SameLine();
    if (ImGui::Button("Buscar")) refreshProjects();
    if (!g_status.empty()) {
        ImGui::SameLine();
        ImGui::TextWrapped("%s", g_status.c_str());
    }
}

} // namespace

int main() {
    if (!glfwInit()) {
        Log::error("Launcher: glfwInit() falhou");
        return 1;
    }
    glfwWindowHint(GLFW_CONTEXT_VERSION_MAJOR, 3);
    glfwWindowHint(GLFW_CONTEXT_VERSION_MINOR, 3);
    glfwWindowHint(GLFW_OPENGL_PROFILE, GLFW_OPENGL_CORE_PROFILE);
#ifdef __APPLE__
    glfwWindowHint(GLFW_OPENGL_FORWARD_COMPAT, GL_TRUE);
#endif
    GLFWwindow* window = glfwCreateWindow(1024, 640, "VelvX Launcher", nullptr, nullptr);
    if (!window) {
        glfwTerminate();
        return 1;
    }
    glfwMakeContextCurrent(window);
    glfwSwapInterval(1);
    gladLoadGL(reinterpret_cast<GLADloadfunc>(glfwGetProcAddress));

    IMGUI_CHECKVERSION();
    ImGui::CreateContext();
    ImGuiIO& io = ImGui::GetIO();
    io.IniFilename = "velvx_launcher.ini";
    ImGui::StyleColorsDark();
    ImGui_ImplGlfw_InitForOpenGL(window, true);
    ImGui_ImplOpenGL3_Init("#version 330");

    g_newDir = defaultProjectsDir().string();
    refreshTemplates();
    refreshProjects();

    while (!glfwWindowShouldClose(window)) {
        glfwPollEvents();
        ImGui_ImplOpenGL3_NewFrame();
        ImGui_ImplGlfw_NewFrame();
        ImGui::NewFrame();

        const ImGuiViewport* viewport = ImGui::GetMainViewport();
        ImGui::SetNextWindowPos(viewport->WorkPos);
        ImGui::SetNextWindowSize(viewport->WorkSize);
        ImGui::Begin("VelvX Launcher", nullptr,
                     ImGuiWindowFlags_NoDecoration | ImGuiWindowFlags_NoMove |
                         ImGuiWindowFlags_NoSavedSettings);
        ImGui::Text("VelvX Engine — Launcher de Projetos");
        ImGui::Separator();
        drawProjectList();
        drawCreatePanel();
        ImGui::End();

        ImGui::Render();
        int width = 0;
        int height = 0;
        glfwGetFramebufferSize(window, &width, &height);
        glViewport(0, 0, width, height);
        glClearColor(0.10f, 0.10f, 0.13f, 1.0f);
        glClear(GL_COLOR_BUFFER_BIT);
        ImGui_ImplOpenGL3_RenderDrawData(ImGui::GetDrawData());
        glfwSwapBuffers(window);
    }

    ImGui_ImplOpenGL3_Shutdown();
    ImGui_ImplGlfw_Shutdown();
    ImGui::DestroyContext();
    glfwDestroyWindow(window);
    glfwTerminate();
    return 0;
}