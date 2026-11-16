/**
 * @file main.cpp
 * @brief VelvX Script IDE: criacao e edicao de scripts .velv em UMA UNICA
 *        janela/area (sem visualizador separado): highlighting inline,
 *        numeros de linha, marcadores de erro, autocomplete contextual,
 *        dicas de parametros e busca/substituicao, com abas multi-arquivo.
 */

#include <glad/gl.h>
#include <GLFW/glfw3.h>
#include <imgui.h>
#include <backends/imgui_impl_glfw.h>
#include <backends/imgui_impl_opengl3.h>
#include <misc/cpp/imgui_stdlib.h>

#include "CodeEditor.hpp"
#include "Core/Log.hpp"

#include <filesystem>
#include <string>
#include <vector>

namespace {

std::vector<CodeEditor> g_tabs; ///< Abas abertas (um editor por arquivo).
int g_activeTab = 0; ///< Indice da aba ativa.
ImFont* g_monoFont = nullptr; ///< Fonte monoespacada do editor.
std::string g_pathBuf; ///< Campo de caminho da barra de ferramentas.
int g_pathBufTab = -1; ///< Aba a que g_pathBuf pertence (sync ao trocar).

CodeEditor& cur() { return g_tabs[static_cast<size_t>(g_activeTab)]; }

/** @brief Cria uma nova aba com o modelo de script. */
void newTab() {
    CodeEditor ed;
    ed.setFont(g_monoFont);
    ed.newScript();
    g_tabs.push_back(ed);
    g_activeTab = static_cast<int>(g_tabs.size()) - 1;
    g_pathBuf = cur().path().string();
    g_pathBufTab = g_activeTab;
}

/** @brief Abre um arquivo .velv em uma aba (reaproveita se ja aberto). */
void openFile(const std::filesystem::path& path) {
    for (int i = 0; i < static_cast<int>(g_tabs.size()); ++i) {
        if (g_tabs[static_cast<size_t>(i)].path() == path) {
            g_activeTab = i;
            g_pathBuf = path.string();
            g_pathBufTab = i;
            return;
        }
    }
    CodeEditor ed;
    ed.setFont(g_monoFont);
    if (!ed.load(path)) {
        cur().status = ed.status;
        return;
    }
    g_tabs.push_back(ed);
    g_activeTab = static_cast<int>(g_tabs.size()) - 1;
    g_pathBuf = path.string();
    g_pathBufTab = g_activeTab;
}

/** @brief Fecha a aba ativa (garante ao menos uma aba). */
void closeActiveTab() {
    if (g_tabs.empty()) return;
    g_tabs.erase(g_tabs.begin() + g_activeTab);
    if (g_tabs.empty()) { newTab(); return; }
    if (g_activeTab >= static_cast<int>(g_tabs.size())) g_activeTab = static_cast<int>(g_tabs.size()) - 1;
    g_pathBuf = cur().path().string();
    g_pathBufTab = g_activeTab;
}

/** @brief Salva a aba ativa (pede caminho via barra se ainda nao tem). */
void saveActive() {
    if (cur().path().empty()) {
        if (g_pathBuf.empty()) { cur().status = "Defina um caminho na barra e use Salvar como."; return; }
        cur().setPath(g_pathBuf);
    }
    cur().save();
}

/** @brief Salva como: usa o caminho digitado na barra. */
void saveAsActive() {
    if (g_pathBuf.empty()) { cur().status = "Digite o caminho de destino na barra."; return; }
    cur().saveAs(g_pathBuf);
}

/** @brief Barra de ferramentas: novo/salvar/caminho/abrir/buscar + status. */
void drawToolbar() {
    if (ImGui::Button("Novo")) newTab();
    ImGui::SameLine();
    if (ImGui::Button("Salvar")) saveActive();
    ImGui::SameLine();
    ImGui::SetNextItemWidth(380);
    if (ImGui::InputText("##caminho", &g_pathBuf, ImGuiInputTextFlags_EnterReturnsTrue)) {
        if (!g_pathBuf.empty()) cur().setPath(g_pathBuf);
    }
    if (ImGui::IsItemHovered()) ImGui::SetTooltip("Caminho do arquivo .velv (Enter aplica na aba).");
    ImGui::SameLine();
    if (ImGui::Button("Abrir")) {
        if (!g_pathBuf.empty()) openFile(g_pathBuf);
    }
    ImGui::SameLine();
    if (ImGui::Button("Salvar como")) saveAsActive();
    ImGui::SameLine();
    if (ImGui::Button("Buscar")) cur().openSearch();
    ImGui::SameLine();
    ImGui::TextDisabled("%s", cur().status.c_str());
}

/** @brief Barra de abas multi-arquivo. */
void drawTabBar() {
    if (!ImGui::BeginTabBar("velv_tabs", ImGuiTabBarFlags_Reorderable)) return;
    int closeIdx = -1;
    for (int i = 0; i < static_cast<int>(g_tabs.size()); ++i) {
        const CodeEditor& ed = g_tabs[static_cast<size_t>(i)];
        std::string name = ed.path().empty() ? "Sem titulo" : ed.path().filename().string();
        if (ed.dirty()) name += " *";
        name += "###tab" + std::to_string(i);
        bool open = true;
        ImGuiTabItemFlags flags = (i == g_activeTab) ? ImGuiTabItemFlags_SetSelected : 0;
        if (ImGui::BeginTabItem(name.c_str(), &open, flags)) {
            if (g_activeTab != i) {
                g_activeTab = i;
                g_pathBuf = cur().path().string();
                g_pathBufTab = i;
            }
            ImGui::EndTabItem();
        }
        if (!open && closeIdx < 0) closeIdx = i;
    }
    if (ImGui::TabItemButton("+", ImGuiTabItemFlags_Trailing)) newTab();
    ImGui::EndTabBar();
    if (closeIdx >= 0) {
        g_tabs.erase(g_tabs.begin() + closeIdx);
        if (g_tabs.empty()) newTab();
        if (g_activeTab >= static_cast<int>(g_tabs.size())) g_activeTab = static_cast<int>(g_tabs.size()) - 1;
        g_pathBuf = cur().path().string();
        g_pathBufTab = g_activeTab;
    }
}

} // namespace

int main(int argc, char** argv) {
    if (!glfwInit()) {
        VelvX::Log::error("ScriptIDE: glfwInit() falhou");
        return 1;
    }
    glfwWindowHint(GLFW_CONTEXT_VERSION_MAJOR, 3);
    glfwWindowHint(GLFW_CONTEXT_VERSION_MINOR, 3);
    glfwWindowHint(GLFW_OPENGL_PROFILE, GLFW_OPENGL_CORE_PROFILE);
#ifdef __APPLE__
    glfwWindowHint(GLFW_OPENGL_FORWARD_COMPAT, GL_TRUE);
#endif
    GLFWwindow* window = glfwCreateWindow(1280, 800, "VelvX Script IDE", nullptr, nullptr);
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
    io.IniFilename = "velvx_scriptide.ini";
    ImGui::StyleColorsDark();
    const char* monoPath = "C:\\Windows\\Fonts\\consola.ttf";
    if (std::filesystem::exists(monoPath)) {
        g_monoFont = io.Fonts->AddFontFromFileTTF(monoPath, 16.0f);
    }
    ImGui_ImplGlfw_InitForOpenGL(window, true);
    ImGui_ImplOpenGL3_Init("#version 330");

    if (argc > 1) openFile(argv[1]);
    else newTab();

    while (!glfwWindowShouldClose(window)) {
        glfwPollEvents();
        ImGui_ImplOpenGL3_NewFrame();
        ImGui_ImplGlfw_NewFrame();
        ImGui::NewFrame();

        if (io.KeyCtrl && ImGui::IsKeyPressed(ImGuiKey_N)) newTab();
        if (io.KeyCtrl && ImGui::IsKeyPressed(ImGuiKey_W)) closeActiveTab();

        const ImGuiViewport* viewport = ImGui::GetMainViewport();
        ImGui::SetNextWindowPos(viewport->WorkPos);
        ImGui::SetNextWindowSize(viewport->WorkSize);
        ImGui::Begin("VelvX Script IDE", nullptr,
                     ImGuiWindowFlags_NoDecoration | ImGuiWindowFlags_NoMove |
                         ImGuiWindowFlags_NoSavedSettings | ImGuiWindowFlags_MenuBar);

        if (ImGui::BeginMenuBar()) {
            if (ImGui::BeginMenu("Arquivo")) {
                if (ImGui::MenuItem("Novo", "Ctrl+N")) newTab();
                if (ImGui::MenuItem("Salvar", "Ctrl+S")) saveActive();
                if (ImGui::MenuItem("Salvar como...")) saveAsActive();
                if (ImGui::MenuItem("Abrir (caminho da barra)")) {
                    if (!g_pathBuf.empty()) openFile(g_pathBuf);
                }
                if (ImGui::MenuItem("Fechar aba", "Ctrl+W")) closeActiveTab();
                ImGui::Separator();
                if (ImGui::MenuItem("Sair")) glfwSetWindowShouldClose(window, GLFW_TRUE);
                ImGui::EndMenu();
            }
            if (ImGui::BeginMenu("Editar")) {
                if (ImGui::MenuItem("Buscar", "Ctrl+F")) cur().openSearch();
                if (ImGui::MenuItem("Substituir", "Ctrl+H")) cur().openSearch();
                ImGui::EndMenu();
            }
            if (ImGui::BeginMenu("Ajuda")) {
                ImGui::TextDisabled("Atalhos: Ctrl+N novo, Ctrl+S salvar,");
                ImGui::TextDisabled("Ctrl+W fechar, Ctrl+F buscar,");
                ImGui::TextDisabled("Ctrl+H substituir, Ctrl+Espaco autocomplete,");
                ImGui::TextDisabled("Tab indenta. Variaveis: lock / unlock.");
                ImGui::EndMenu();
            }
            ImGui::EndMenuBar();
        }

        drawToolbar();
        drawTabBar();
        cur().drawSearchBar();
        ImGui::Separator();

        const std::string hint = cur().activeCallHint();
        if (!hint.empty()) {
            ImGui::TextColored(ImVec4(0.55f, 0.8f, 1.0f, 1.0f), "Parametros: %s", hint.c_str());
        } else {
            ImGui::TextDisabled("Dica de parametros aparece ao digitar uma chamada, ex: Camera.setFOV(");
        }

        const float footerHeight = 110.0f;
        const ImVec2 avail = ImGui::GetContentRegionAvail();
        cur().draw(ImVec2(avail.x, avail.y - footerHeight));

        ImGui::BeginChild("problemas", ImVec2(0, footerHeight), ImGuiChildFlags_Borders);
        ImGui::TextDisabled("Problemas:");
        const auto issues = cur().issues();
        if (issues.empty()) {
            ImGui::TextColored(ImVec4(0.4f, 0.9f, 0.4f, 1.0f), "Nenhum problema detectado.");
        }
        for (const auto& issue : issues) {
            const ImVec4 color = issue.warning ? ImVec4(0.95f, 0.8f, 0.3f, 1.0f)
                                               : ImVec4(0.95f, 0.4f, 0.4f, 1.0f);
            ImGui::TextColored(color, "Linha %d: %s%s", issue.line,
                               issue.warning ? "[aviso] " : "[erro] ", issue.message.c_str());
        }
        ImGui::EndChild();

        ImGui::End();

        ImGui::Render();
        int width = 0;
        int height = 0;
        glfwGetFramebufferSize(window, &width, &height);
        glViewport(0, 0, width, height);
        glClearColor(0.08f, 0.08f, 0.10f, 1.0f);
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