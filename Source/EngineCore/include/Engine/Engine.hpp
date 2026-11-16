#pragma once
/**
 * @file Engine.hpp
 * @brief Ponto central da VelvX Engine: janela, loop e subsistemas.
 */

#include "Core/AssetBrowser.hpp"
#include "Core/Project.hpp"
#include "Core/SceneManager.hpp"
#include "Core/Transform.hpp"
#include "Renderer/Renderer.hpp"
#include <GLFW/glfw3.h>
#include <filesystem>

namespace VelvX {

/**
 * @brief Fachada estática da engine (PlayWindow / Player).
 *
 * Fluxo: @c loadProject() (opcional) define o projeto; @c init() cria a
 * janela OpenGL 3.3 core com as configurações; @c run() executa Main() uma
 * vez e InGame() a cada frame; @c shutdown() libera tudo.
 */
class Engine {
public:
    /**
     * @brief Inicializa GLFW, contexto OpenGL 3.3 core, GLAD e subsistemas.
     * @param settings Configurações de janela/gráficos/input.
     */
    static void init(const ProjectSettings& settings);

    /** @brief Conveniência legada: init com título e tamanho. */
    static void init(const char* title, int width, int height);

    /**
     * @brief Inicializa os subsistemas sobre uma janela GLFW já existente.
     *
     * Usado pelo Editor: a janela/contexto GL são criados pela ferramenta e
     * a engine apenas inicializa Renderer/Input/UI/plugins sobre eles.
     * O chamador deve ter chamado glfwMakeContextCurrent antes.
     */
    static void initWithWindow(GLFWwindow* window, const ProjectSettings& settings);

    /** @brief Executa Main() uma vez e InGame() continuamente até fechar. */
    static void run();

    /** @brief Encerra subsistemas, destrói a janela e finaliza o GLFW. */
    static void shutdown();

    /**
     * @brief Carrega um projeto (.velvproj) e aplica raiz/settings.
     *
     * Registra as cenas do projeto no SceneManager. Deve ser chamado antes
     * de init() para que a janela use as configurações do projeto.
     */
    static bool loadProject(const std::filesystem::path& velvprojFile);

    /** @brief Abre um pack de assets exportado (.zip) para leitura. */
    static void openPack(const std::filesystem::path& packFile);

    /** @brief Projeto atual (vazio se nenhum foi carregado). */
    static Project& project();

    /**
     * @brief Configurações ativas (janela/gráficos/input/física).
     *
     * Fonte única consultada em runtime (ex.: matriz de colisão). Inicializada
     * em init/initWithWindow; o Editor pode editá-la ao vivo.
     */
    static ProjectSettings& settings();

    /** @brief Gerenciador de cenas global. */
    static SceneManager& getSceneManager();

    /** @brief Navegador de assets global. */
    static AssetBrowser& getAssetBrowser();

    /** @brief Janela GLFW principal (framebuffer único 2D/3D). */
    static GLFWwindow* getWindow() { return window_; }

    /** @brief Tempo total de execução em segundos. */
    static float getTime() { return time_; }

    /** @brief Delta time do último frame em segundos (limitado a 0.1). */
    static float getDeltaTime() { return deltaTime_; }

    /** @brief Escala do tempo de jogo (0 = pausa, 1 = normal; afeta dt()/time()/física). */
    static void setTimeScale(float scale);
    static float getTimeScale() { return timeScale_; }

    /** @brief Diretório do executável (base para shaders/plugins). */
    static std::filesystem::path getExecutableDir();

private:
    /** @brief Loop principal: input → física → script → UI → render. */
    static void mainLoop();

    /** @brief Destrói a janela e finaliza o GLFW. */
    static void shutdownGLFW();

    static SceneManager sceneManager_; ///< Gerenciador de cenas global.
    static AssetBrowser assetBrowser_; ///< Navegador de assets global.
    static Project project_; ///< Projeto carregado.
    static ProjectSettings settings_; ///< Configurações ativas (física/janela/etc.).
    static bool projectLoaded_; ///< true após loadProject() com sucesso.
    static GLFWwindow* window_; ///< Janela principal.
    static float time_; ///< Tempo acumulado.
    static float deltaTime_; ///< Delta do frame.
    static float timeScale_; ///< Escala do tempo de jogo (1 = normal).
};

} // namespace VelvX