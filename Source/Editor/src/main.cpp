/**
 * @file main.cpp
 * @brief Bootstrap do VelvX Editor: janela GLFW + ImGui docking + EngineCore.
 */

#include <glad/gl.h>
#include <GLFW/glfw3.h>
#include <imgui.h>
#include <backends/imgui_impl_glfw.h>
#include <backends/imgui_impl_opengl3.h>

#include "EditorApp.hpp"
#include "Core/Log.hpp"
#include "Core/ProjectSettings.hpp"
#include "Engine/Engine.hpp"

#define STB_IMAGE_IMPLEMENTATION
#include <stb_image.h>

#include <algorithm>
#include <filesystem>
#include <vector>

namespace {

/** @brief Paleta grafite moderna do VelvX Editor. */
void applyGraphiteTheme() {
    ImGui::StyleColorsDark();
    ImGuiStyle& style = ImGui::GetStyle();
    style.WindowRounding = 6.0f;
    style.ChildRounding = 4.0f;
    style.FrameRounding = 4.0f;
    style.GrabRounding = 4.0f;
    style.TabRounding = 4.0f;
    style.ScrollbarRounding = 6.0f;
    style.PopupRounding = 4.0f;
    style.WindowBorderSize = 1.0f;
    style.FrameBorderSize = 0.0f;
    style.WindowPadding = ImVec2(10, 10);
    style.FramePadding = ImVec2(8, 5);
    style.ItemSpacing = ImVec2(8, 6);
    style.ItemInnerSpacing = ImVec2(6, 5);
    style.ScrollbarSize = 13.0f;
    style.GrabMinSize = 10.0f;

    ImVec4* c = style.Colors;
    const ImVec4 text(0.90f, 0.91f, 0.94f, 1.00f);
    const ImVec4 accent(0.42f, 0.60f, 0.95f, 1.00f);
    c[ImGuiCol_Text] = text;
    c[ImGuiCol_TextDisabled] = ImVec4(0.50f, 0.51f, 0.55f, 1.00f);
    c[ImGuiCol_WindowBg] = ImVec4(0.10f, 0.10f, 0.12f, 1.00f);
    c[ImGuiCol_ChildBg] = ImVec4(0.12f, 0.12f, 0.14f, 1.00f);
    c[ImGuiCol_PopupBg] = ImVec4(0.14f, 0.14f, 0.16f, 1.00f);
    c[ImGuiCol_Border] = ImVec4(0.22f, 0.22f, 0.26f, 1.00f);
    c[ImGuiCol_BorderShadow] = ImVec4(0.00f, 0.00f, 0.00f, 0.00f);
    c[ImGuiCol_FrameBg] = ImVec4(0.16f, 0.16f, 0.19f, 1.00f);
    c[ImGuiCol_FrameBgHovered] = ImVec4(0.22f, 0.22f, 0.26f, 1.00f);
    c[ImGuiCol_FrameBgActive] = ImVec4(0.26f, 0.26f, 0.31f, 1.00f);
    c[ImGuiCol_TitleBg] = ImVec4(0.08f, 0.08f, 0.10f, 1.00f);
    c[ImGuiCol_TitleBgActive] = ImVec4(0.12f, 0.12f, 0.15f, 1.00f);
    c[ImGuiCol_TitleBgCollapsed] = ImVec4(0.08f, 0.08f, 0.10f, 1.00f);
    c[ImGuiCol_MenuBarBg] = ImVec4(0.10f, 0.10f, 0.12f, 1.00f);
    c[ImGuiCol_ScrollbarBg] = ImVec4(0.10f, 0.10f, 0.12f, 1.00f);
    c[ImGuiCol_ScrollbarGrab] = ImVec4(0.26f, 0.26f, 0.31f, 1.00f);
    c[ImGuiCol_ScrollbarGrabHovered] = ImVec4(0.32f, 0.32f, 0.38f, 1.00f);
    c[ImGuiCol_ScrollbarGrabActive] = ImVec4(0.38f, 0.38f, 0.44f, 1.00f);
    c[ImGuiCol_CheckMark] = accent;
    c[ImGuiCol_SliderGrab] = ImVec4(0.40f, 0.55f, 0.85f, 1.00f);
    c[ImGuiCol_SliderGrabActive] = accent;
    c[ImGuiCol_Button] = ImVec4(0.20f, 0.20f, 0.24f, 1.00f);
    c[ImGuiCol_ButtonHovered] = ImVec4(0.30f, 0.30f, 0.36f, 1.00f);
    c[ImGuiCol_ButtonActive] = ImVec4(0.36f, 0.36f, 0.42f, 1.00f);
    c[ImGuiCol_Header] = ImVec4(0.20f, 0.20f, 0.24f, 1.00f);
    c[ImGuiCol_HeaderHovered] = ImVec4(0.28f, 0.28f, 0.33f, 1.00f);
    c[ImGuiCol_HeaderActive] = ImVec4(0.32f, 0.32f, 0.38f, 1.00f);
    c[ImGuiCol_Separator] = ImVec4(0.22f, 0.22f, 0.26f, 1.00f);
    c[ImGuiCol_SeparatorHovered] = accent;
    c[ImGuiCol_SeparatorActive] = accent;
    c[ImGuiCol_ResizeGrip] = ImVec4(0.26f, 0.26f, 0.31f, 1.00f);
    c[ImGuiCol_ResizeGripHovered] = accent;
    c[ImGuiCol_ResizeGripActive] = accent;
    c[ImGuiCol_Tab] = ImVec4(0.14f, 0.14f, 0.17f, 1.00f);
    c[ImGuiCol_TabHovered] = ImVec4(0.30f, 0.45f, 0.75f, 1.00f);
    c[ImGuiCol_TabSelected] = ImVec4(0.20f, 0.30f, 0.50f, 1.00f);
    c[ImGuiCol_TabDimmed] = ImVec4(0.12f, 0.12f, 0.14f, 1.00f);
    c[ImGuiCol_TabDimmedSelected] = ImVec4(0.16f, 0.20f, 0.28f, 1.00f);
    c[ImGuiCol_DockingPreview] = ImVec4(0.42f, 0.60f, 0.95f, 0.60f);
    c[ImGuiCol_DockingEmptyBg] = ImVec4(0.08f, 0.08f, 0.10f, 1.00f);
    c[ImGuiCol_TextSelectedBg] = ImVec4(0.42f, 0.60f, 0.95f, 0.35f);
    c[ImGuiCol_NavHighlight] = accent;
}

/** @brief Define o ícone da janela a partir de Icon.png (reduzido a 48x48). */
void applyWindowIcon(GLFWwindow* window) {
    const std::filesystem::path iconPath = VelvX::Engine::getExecutableDir() / "Icon.png";
    int w = 0, h = 0, channels = 0;
    stbi_uc* pixels = stbi_load(iconPath.string().c_str(), &w, &h, &channels, 4);
    if (!pixels || w <= 0 || h <= 0) {
        if (pixels) stbi_image_free(pixels);
        return;
    }
    const int target = 48;
    std::vector<unsigned char> small(static_cast<size_t>(target) * target * 4);
    for (int y = 0; y < target; ++y) {
        const int sy = std::min(h - 1, y * h / target);
        for (int x = 0; x < target; ++x) {
            const int sx = std::min(w - 1, x * w / target);
            const stbi_uc* src = pixels + (static_cast<size_t>(sy) * w + sx) * 4;
            unsigned char* dst = small.data() + (static_cast<size_t>(y) * target + x) * 4;
            dst[0] = src[0]; dst[1] = src[1]; dst[2] = src[2]; dst[3] = src[3];
        }
    }
    GLFWimage image;
    image.width = target;
    image.height = target;
    image.pixels = small.data();
    glfwSetWindowIcon(window, 1, &image);
    stbi_image_free(pixels);
}

} // namespace

int main(int argc, char** argv) {
    using namespace VelvX;

    if (!glfwInit()) {
        Log::error("Editor: glfwInit() falhou");
        return 1;
    }
    glfwWindowHint(GLFW_CONTEXT_VERSION_MAJOR, 3);
    glfwWindowHint(GLFW_CONTEXT_VERSION_MINOR, 3);
    glfwWindowHint(GLFW_OPENGL_PROFILE, GLFW_OPENGL_CORE_PROFILE);
    glfwWindowHint(GLFW_SAMPLES, 4);
#ifdef __APPLE__
    glfwWindowHint(GLFW_OPENGL_FORWARD_COMPAT, GL_TRUE);
#endif

    GLFWwindow* window = glfwCreateWindow(1600, 900, "VelvX Editor", nullptr, nullptr);
    if (!window) {
        Log::error("Editor: glfwCreateWindow() falhou (OpenGL 3.3 core indisponivel?)");
        glfwTerminate();
        return 1;
    }
    glfwMakeContextCurrent(window);
    glfwSwapInterval(1);

    Engine::initWithWindow(window, ProjectSettings::defaults());

    EditorApp app;
    std::filesystem::path projectFile;
    if (argc > 1) projectFile = argv[1];
    app.init(window, projectFile);

    IMGUI_CHECKVERSION();
    ImGui::CreateContext();
    ImGuiIO& io = ImGui::GetIO();
    io.ConfigFlags |= ImGuiConfigFlags_DockingEnable;
    io.IniFilename = "velvx_editor.ini";
    applyGraphiteTheme();
    applyWindowIcon(window);
    const char* monoPath = "C:\\Windows\\Fonts\\consola.ttf";
    if (std::filesystem::exists(monoPath)) {
        app.setMonoFont(io.Fonts->AddFontFromFileTTF(monoPath, 15.0f));
    }

    ImGui_ImplGlfw_InitForOpenGL(window, true);
    ImGui_ImplOpenGL3_Init("#version 330");

    double lastTime = glfwGetTime();
    while (!glfwWindowShouldClose(window)) {
        glfwPollEvents();
        const double now = glfwGetTime();
        const float deltaTime = std::clamp(static_cast<float>(now - lastTime), 0.0f, 0.1f);
        lastTime = now;

        ImGui_ImplOpenGL3_NewFrame();
        ImGui_ImplGlfw_NewFrame();
        ImGui::NewFrame();

        app.frame(deltaTime);

        ImGui::Render();
        int width = 0;
        int height = 0;
        glfwGetFramebufferSize(window, &width, &height);
        glViewport(0, 0, width, height);
        glClearColor(0.09f, 0.09f, 0.11f, 1.0f);
        glClear(GL_COLOR_BUFFER_BIT | GL_DEPTH_BUFFER_BIT);
        ImGui_ImplOpenGL3_RenderDrawData(ImGui::GetDrawData());
        glfwSwapBuffers(window);
    }

    app.shutdown();
    ImGui_ImplOpenGL3_Shutdown();
    ImGui_ImplGlfw_Shutdown();
    ImGui::DestroyContext();
    Engine::shutdown();
    return 0;
}