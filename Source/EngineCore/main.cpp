/**
 * @file main.cpp
 * @brief Entry-point do VelvX Player (PlayWindow): executa um projeto VelvX
 * sem a interface do Editor, usando as Project Settings do projeto.
 *
 * Uso:
 * @code
 * VelvXPlayer --project Caminho/Projeto.velvproj [--pack assets.zip] [--scene Nome]
 * VelvXPlayer                       (modo demo embutido, sem projeto)
 * @endcode
 */

#include "Engine/Engine.hpp"
#include "Core/GameObject.hpp"
#include "Core/Input.hpp"
#include "Core/Inspector.hpp"
#include "Core/Log.hpp"
#include "Core/Project.hpp"
#include "Physics/PhysicsComponent.hpp"
#include "Renderer/RenderComponent.hpp"
#include "Scripting/ScriptEngine.hpp"
#include <algorithm>
#include <cstdlib>
#include <filesystem>
#include <memory>
#include <string>
#include <vector>

using namespace VelvX;

/** @brief Opções de linha de comando do Player. */
struct PlayerOptions {
    std::filesystem::path projectFile; ///< Projeto .velvproj (vazio = demo).
    std::filesystem::path packFile; ///< Pack de assets exportado (opcional).
    std::string sceneOverride; ///< Cena inicial forçada (opcional).
};

/** @brief Interpreta os argumentos de linha de comando. */
static PlayerOptions parseArgs(int argc, char** argv) {
    PlayerOptions options;
    for (int i = 1; i < argc; ++i) {
        const std::string arg = argv[i];
        const auto next = [&](const char* what) -> std::string {
            if (i + 1 >= argc) {
                Log::error(std::string("Player: faltou o valor de ") + what);
                std::exit(1);
            }
            return argv[++i];
        };
        if (arg == "--project" || arg == "-p") {
            options.projectFile = next("--project");
        } else if (arg == "--pack") {
            options.packFile = next("--pack");
        } else if (arg == "--scene" || arg == "-s") {
            options.sceneOverride = next("--scene");
        } else if (arg == "--help" || arg == "-h") {
            Log::info("Uso: VelvXPlayer [--project arquivo.velvproj] [--pack assets.zip] [--scene Nome]");
            std::exit(0);
        } else {
            Log::warn("Player: argumento desconhecido '" + arg + "'");
        }
    }
    return options;
}

/** @brief Registra os protótipos built-in clonáveis por Instantiate(). */
static void registerBuiltInPrototypes() {
    SceneManager& sm = Engine::getSceneManager();

    auto cube = std::make_unique<GameObject>("Cube");
    auto* cubeRender = cube->addComponent<MeshRenderer>();
    cubeRender->mesh = MeshType::Cube;
    cubeRender->color = glm::vec3(0.95f, 0.55f, 0.25f);
    cube->addComponent<RigidBody>();
    sm.registerPrototype("Cube", std::move(cube));

    auto block = std::make_unique<GameObject>("Block");
    auto* blockRender = block->addComponent<MeshRenderer>();
    blockRender->mesh = MeshType::Cube;
    blockRender->color = glm::vec3(0.4f, 0.65f, 0.85f);
    block->addComponent<StaticBody>();
    sm.registerPrototype("Block", std::move(block));
}

/** @brief Registra as cenas do modo demo (sem projeto). */
static void registerDemoScenes() {
    SceneManager& sm = Engine::getSceneManager();

    auto floor1 = std::make_unique<Scene>("Floor1F");
    floor1->nextScene = "Floor2F";
    sm.registerScene("Floor1F", std::move(floor1));

    auto floor2 = std::make_unique<Scene>("Floor2F");
    floor2->nextScene = "Floor1F";
    sm.registerScene("Floor2F", std::move(floor2));
}

static std::string g_initialScene = "Floor1F";

/** @brief Main() nativo de fallback (quando não há script .velv). */
static void nativeMain() {
    Log::info("Player: Main() nativo — carregando '" + g_initialScene + "'");
    Engine::getSceneManager().loadScene(g_initialScene);
    Log::info("Controles: ESPACO = trocar cena (async) | E = instanciar cubo");
}

/** @brief InGame() nativo de fallback (quando não há script .velv). */
static void nativeInGame() {
    SceneManager& sm = Engine::getSceneManager();
    if (Input::isKeyPressed(Key::Space)) {
        std::vector<std::string> names;
        for (const auto& [name, scene] : sm.scenes()) {
            names.push_back(name);
        }
        std::sort(names.begin(), names.end());
        const Scene* active = sm.getActiveScene();
        const std::string current = active ? active->getName() : "";
        std::string next = current;
        for (size_t i = 0; i < names.size(); ++i) {
            if (names[i] == current) {
                next = names[(i + 1) % names.size()];
                break;
            }
        }
        if (!next.empty()) {
            sm.loadSceneAsync(next);
            sm.positionScene(glm::vec3(0.0f));
        }
    }
    if (Input::isKeyPressed(Key::E)) {
        const float x = static_cast<float>(std::rand() % 9) - 4.0f;
        const float z = static_cast<float>(std::rand() % 9) - 4.0f;
        Instantiate("Cube", glm::vec3(x, 9.0f, z));
    }
}

/**
 * @brief Entry-point do VelvX Player.
 *
 * Sequência: opções → loadProject/openPack (opcional) → Engine::init com as
 * Project Settings → protótipos → script Velv (ou fallback nativo) →
 * Engine::run (Main() uma vez, InGame() por frame) → shutdown.
 */
int main(int argc, char** argv) {
    const PlayerOptions options = parseArgs(argc, argv);

    ProjectSettings settings = ProjectSettings::defaults();
    const bool hasProject = !options.projectFile.empty();

    if (hasProject) {
        if (!Engine::loadProject(options.projectFile)) {
            Log::error("Player: não foi possível carregar o projeto " + options.projectFile.string());
            return 1;
        }
        if (!options.packFile.empty()) {
            Engine::openPack(options.packFile);
        }
        settings = Engine::project().settings;
        g_initialScene = Engine::project().mainScene;
    } else {
        settings.windowTitle = "VelvX Engine — Demo";
        registerDemoScenes();
    }

    Engine::init(settings);
    registerBuiltInPrototypes();
    Inspector::dump();

    const std::string scriptPath = hasProject ? Engine::project().mainScript
                                              : "scripts/GameScript.velv";
    bool scriptLoaded = ScriptEngine::loadFile(scriptPath);
    if (!scriptLoaded) {
        Log::warn("Player: script '" + scriptPath + "' indisponível — fallback nativo em C++");
        registerScript(nativeMain, nativeInGame);
    }

    if (!options.sceneOverride.empty()) {
        g_initialScene = options.sceneOverride;
        if (scriptLoaded && options.sceneOverride.find('"') == std::string::npos) {
            ScriptEngine::interpretVelvCode(
                "book Main() { SceneManager.loadScene(\"" + options.sceneOverride + "\"); }");
        }
    }

    Engine::run();
    Engine::shutdown();
    return 0;
}