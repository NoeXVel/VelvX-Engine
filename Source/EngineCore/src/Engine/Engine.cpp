#ifdef _WIN32
#ifndef NOMINMAX
#define NOMINMAX
#endif
#include <windows.h>
#endif

#include "Engine/Engine.hpp"
#include "Core/Input.hpp"
#include "Core/Log.hpp"
#include "Core/Trigger.hpp"
#include "Input/InputManager.hpp"
#include "Physics/CollisionSystem.hpp"
#include "Physics/PhysicsComponent.hpp"
#include "PluginAPI/PluginManager.hpp"
#include "Renderer/ModelImporter.hpp"
#include "Scripting/ScriptEngine.hpp"
#include "UI/UIManager.hpp"
#include "Utils/FileSystem.hpp"
#include "Utils/PackFile.hpp"
#include <glad/gl.h>
#include <algorithm>
#include <cstdlib>
#include <format>

namespace VelvX {

SceneManager Engine::sceneManager_;
AssetBrowser Engine::assetBrowser_;
Project Engine::project_;
ProjectSettings Engine::settings_;
bool Engine::projectLoaded_ = false;
GLFWwindow* Engine::window_ = nullptr;
float Engine::time_ = 0.0f;
float Engine::deltaTime_ = 0.0f;
float Engine::timeScale_ = 1.0f;

void Engine::setTimeScale(float scale) {
    timeScale_ = std::clamp(scale, 0.0f, 8.0f);
}

std::filesystem::path Engine::getExecutableDir() {
#ifdef _WIN32
    wchar_t buffer[MAX_PATH];
    const DWORD len = GetModuleFileNameW(nullptr, buffer, MAX_PATH);
    return std::filesystem::path(std::wstring(buffer, len)).parent_path();
#else
    return std::filesystem::read_symlink("/proc/self/exe").parent_path();
#endif
}

bool Engine::loadProject(const std::filesystem::path& velvprojFile) {
    if (!project_.load(velvprojFile)) {
        return false;
    }
    FS::setProjectRoot(project_.directory);
    projectLoaded_ = true;
    if (project_.scenes.empty()) {
        project_.scenes.push_back(project_.mainScene);
    }
    for (const auto& sceneName : project_.scenes) {
        sceneManager_.registerScene(sceneName, std::make_unique<Scene>(sceneName));
    }
    Log::info("Engine: projeto '" + project_.name + "' carregado (" +
              std::to_string(project_.scenes.size()) + " cenas)");
    return true;
}

void Engine::openPack(const std::filesystem::path& packFile) {
    if (PackFile::open(packFile)) {
        Log::info("Engine: pack de assets aberto: " + packFile.string());
    } else {
        Log::warn("Engine: falha ao abrir pack: " + packFile.string());
    }
}

Project& Engine::project() {
    return project_;
}

ProjectSettings& Engine::settings() {
    return settings_;
}

void Engine::init(const ProjectSettings& settings) {
    if (!glfwInit()) {
        Log::error("glfwInit() falhou");
        std::exit(1);
    }
    glfwWindowHint(GLFW_CONTEXT_VERSION_MAJOR, 3);
    glfwWindowHint(GLFW_CONTEXT_VERSION_MINOR, 3);
    glfwWindowHint(GLFW_OPENGL_PROFILE, GLFW_OPENGL_CORE_PROFILE);
    glfwWindowHint(GLFW_SAMPLES, std::clamp(settings.antiAliasingSamples, 0, 8));
#ifdef __APPLE__
    glfwWindowHint(GLFW_OPENGL_FORWARD_COMPAT, GL_TRUE);
#endif

    GLFWmonitor* monitor = nullptr;
    int width = settings.windowWidth;
    int height = settings.windowHeight;
    if (settings.fullscreen) {
        monitor = glfwGetPrimaryMonitor();
        if (const GLFWvidmode* mode = glfwGetVideoMode(monitor)) {
            width = mode->width;
            height = mode->height;
        }
    }
    window_ = glfwCreateWindow(width, height, settings.windowTitle.c_str(), monitor, nullptr);
    if (!window_) {
        Log::error("glfwCreateWindow() falhou (OpenGL 3.3 core indisponível?)");
        glfwTerminate();
        std::exit(1);
    }
    glfwMakeContextCurrent(window_);
    glfwSwapInterval(settings.vsync ? 1 : 0);
    initWithWindow(window_, settings);
}

void Engine::initWithWindow(GLFWwindow* window, const ProjectSettings& settings) {
    window_ = window;
    settings_ = settings;
    settings_.ensureCollisionMatrix();
    if (!projectLoaded_) {
        FS::setProjectRoot(getExecutableDir());
    }
    Log::setLogFile(FS::projectRoot() / "velvx.log");
    Log::info("VelvX Engine 1.0 — inicializando");

    if (!gladLoadGL(reinterpret_cast<GLADloadfunc>(glfwGetProcAddress))) {
        Log::error("gladLoadGL() falhou");
        std::exit(1);
    }
    Log::info(std::format("OpenGL: {} | Renderer: {}",
                          reinterpret_cast<const char*>(glGetString(GL_VERSION)),
                          reinterpret_cast<const char*>(glGetString(GL_RENDERER))));

    const std::filesystem::path exeDir = getExecutableDir();
    std::filesystem::path shaderRoot = exeDir / "shaders";
    if (!std::filesystem::exists(shaderRoot)) {
        shaderRoot = FS::resolveProject("shaders");
    }
    Renderer::setShaderRoot(shaderRoot);
    Renderer::init(window_);
    Renderer::setShadowSettings(settings.enableShadows, settings.shadowMapSize());

    InputManager::init(window_);
    settings.applyInput();
    UIManager::init();
    UIManager::onScriptEvent = [](const std::string& bookName) {
        ScriptEngine::callBook(bookName);
    };

    PluginManager::loadPluginsFromDirectory((FS::projectRoot() / "plugins").string());
    PluginManager::loadPluginsFromDirectory((exeDir / "plugins").string());

    Log::info("Engine.init concluído");
}

void Engine::init(const char* title, int width, int height) {
    ProjectSettings settings = ProjectSettings::defaults();
    settings.windowTitle = title;
    settings.windowWidth = width;
    settings.windowHeight = height;
    init(settings);
}

void Engine::run() {
    executeMain();
    mainLoop();
}

void Engine::mainLoop() {
    double last = glfwGetTime();
    while (!glfwWindowShouldClose(window_)) {
        const double now = glfwGetTime();
        const float rawDt = std::min(static_cast<float>(now - last), 0.1f);
        last = now;
        deltaTime_ = rawDt * timeScale_;
        time_ += deltaTime_;

        glfwPollEvents();
        InputManager::update();
        Input::update();
        sceneManager_.update();
        Trigger::update();
        if (Scene* scene = sceneManager_.getActiveScene()) {
            for (size_t i = 0; i < scene->gameObjects.size(); ++i) {
                scene->gameObjects[i]->update(deltaTime_);
            }
        }
        PhysicsWorld::step(deltaTime_);
        CollisionSystem::update();
        ModelImporter::update(deltaTime_);

        executeInGame();
        UIManager::update(rawDt);

        Renderer::beginFrame();
        Renderer::renderScene(sceneManager_.getActiveScene(), time_);
        glfwSwapBuffers(window_);
    }
}

void Engine::shutdown() {
    ScriptEngine::shutdown();
    Trigger::clear();
    UIManager::shutdown();
    PluginManager::shutdownAllPlugins();
    ModelImporter::shutdown();
    PackFile::close();
    Renderer::shutdown();
    InputManager::shutdown();
    shutdownGLFW();
    Log::info("VelvX Engine encerrada");
}

void Engine::shutdownGLFW() {
    if (window_) {
        glfwDestroyWindow(window_);
        window_ = nullptr;
    }
    glfwTerminate();
}

SceneManager& Engine::getSceneManager() {
    return sceneManager_;
}

AssetBrowser& Engine::getAssetBrowser() {
    return assetBrowser_;
}

} // namespace VelvX