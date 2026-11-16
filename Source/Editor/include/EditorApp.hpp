#pragma once
/**
 * @file EditorApp.hpp
 * @brief VelvX Editor: aplicação ImGui docking sobre a EngineCore.
 */

#include "CodeEditor.hpp"
#include "Core/Project.hpp"
#include <GLFW/glfw3.h>
#include <deque>
#include <filesystem>
#include <map>
#include <mutex>
#include <string>
#include <vector>

namespace VelvX {
class GameObject;
class Scene;
}

struct ImVec2;

/**
 * @brief Editor completo da VelvX Engine.
 *
 * Painéis docking: Hierarchy, Inspector (campos UNLOCKED editáveis),
 * AssetBrowser (drag&drop), Console e SceneViewport (preview 3D/2D).
 * Play inicia o VelvX Player em subprocesso com as Project Settings.
 */
class EditorApp {
public:
    /** @brief Inicializa painéis, sink de console e projeto opcional. */
    bool init(GLFWwindow* window, const std::filesystem::path& projectFile);

    /** @brief Encerra subprocessos e libera estado do editor. */
    void shutdown();

    /** @brief Desenha todos os painéis e diálogos do frame. */
    void frame(float deltaTime);

    /** @brief Fonte monoespacada usada pelo painel Script embutido. */
    void setMonoFont(ImFont* f) { monoFont_ = f; scriptEditor_.setFont(f); }

private:
    /** @brief Linha do painel Console. */
    struct ConsoleLine {
        std::string level; ///< info/warn/error.
        std::string message; ///< Texto sem timestamp.
    };

    // --- painéis ---
    void drawMenuBar();
    void drawToolbar();
    void drawHierarchy();
    void drawInspector();
    void drawAssets();
    void drawConsole();
    void drawScriptPanel();
    void drawViewport();
    void drawGameView();
    void drawViewportOverlay(VelvX::Scene& scene, const ImVec2& rectMin, const ImVec2& rectMax);

    // --- diálogos ---
    void drawNewProjectDialog();
    void drawOpenProjectDialog();
    void drawSettingsWindow();
    void drawExportDialog();

    // --- ações ---
    void openProject(const std::filesystem::path& file);
    void saveProjectAndScene();
    void loadSceneByName(const std::string& name);
    void addPrimitive(const std::string& kind, VelvX::GameObject* parent = nullptr);
    void deleteSelected();
    void duplicateSelected();
    void createEmptyObject();
    void copySelected();
    void pasteClipboard();
    void frameSelected();
    void handleShortcuts();

    // --- undo/redo (snapshots JSON inertes da cena) ---
    struct Snapshot {
        std::string json; ///< Cena serializada.
        std::string selectedName; ///< Nome do objeto selecionado.
    };
    void pushUndo();
    void undo();
    void redo();
    void restoreSnapshot(const Snapshot& snap);
    void drawRenameDialog();
    void drawSaveAsDialog();
    void drawAssetDialogs();
    void openAsset(const std::filesystem::path& path);
    void openScriptInPanel(const std::filesystem::path& path);
    void assignAsset(const std::filesystem::path& path);
    void refreshAssets();
    void play();
    void stopPlay();
    bool isPlaying();
    void doExport();
    void spawnTool(const std::string& exeName, const std::filesystem::path& argument);

    // --- helpers de desenho ---
    void drawUnlockedFields(const void* base);
    void drawTransformEditor(VelvX::GameObject& object);
    void drawComponentEditors(VelvX::GameObject& object);
    void drawSceneCameraEditor(VelvX::Scene& scene);
    void drawAssetTree(const std::filesystem::path& dir);
    unsigned int assetThumbnail(const std::filesystem::path& full);
    void drawAssetCell(const std::filesystem::path& entry, const ImVec2& pos,
                       float cellW, float cellH, float thumbSize);
    void drawBindingEditor();

    GLFWwindow* window_ = nullptr; ///< Janela do editor.
    VelvX::GameObject* selected_ = nullptr; ///< Objeto selecionado.
    int gizmoMode_ = 0; ///< 0=move 1=rotate 2=scale.
    int dragAxis_ = -1; ///< Eixo do gizmo em arraste (-1 = nenhum).
    bool gizmoDragging_ = false; ///< Arrastando gizmo neste frame.
    unsigned int gameTex_ = 0; ///< Textura própria do Game view (cópia do FBO).
    std::string sceneName_; ///< Cena em edição.
    float time_ = 0.0f; ///< Tempo acumulado do preview.

    std::deque<ConsoleLine> console_; ///< Linhas do Console.
    std::mutex consoleMutex_; ///< Protege console_ (threads de load).
    bool consoleAutoScroll_ = true; ///< Rolar automaticamente.
    std::string consoleFilter_; ///< Filtro de texto do Console.

    std::vector<std::filesystem::path> assetEntries_; ///< Cache da árvore de assets.
    float assetRefreshTimer_ = 0.0f; ///< Recarregar árvore periodicamente.
    std::filesystem::path assetCurrentDir_; ///< Pasta atual do browser (relativa; vazio = raiz).
    std::filesystem::path assetSelected_; ///< Asset selecionado (destaque).
    std::map<std::string, unsigned int> assetThumbs_; ///< Cache path -> textura GL de thumbnail.
    bool assetGridView_ = true; ///< true = thumbnails, false = árvore/lista.
    float assetThumbSize_ = 92.0f; ///< Lado do thumbnail em pixels.

    CodeEditor scriptEditor_; ///< Editor de script embutido (painel Script).
    ImFont* monoFont_ = nullptr; ///< Fonte monoespacada do painel Script.
    bool focusScriptPanel_ = false; ///< Trazer painel Script para frente.

    bool showNewProject_ = false; ///< Diálogo Novo Projeto aberto.
    bool showOpenProject_ = false; ///< Diálogo Abrir Projeto aberto.
    bool showSettings_ = false; ///< Janela Project Settings aberta.
    bool showExport_ = false; ///< Diálogo Exportar aberto.
    bool resetLayout_ = false; ///< Força reconstrução do layout docking.

    std::string newProjectName_ = "MeuJogo"; ///< Nome do novo projeto.
    std::string newProjectDir_; ///< Diretório destino do novo projeto.
    int newProjectTemplate_ = 0; ///< Índice do template escolhido.
    std::vector<std::string> templates_; ///< Templates disponíveis.

    std::string openProjectPath_; ///< Caminho digitado no diálogo Abrir.
    std::vector<std::filesystem::path> foundProjects_; ///< Projetos encontrados.

    std::string exportDir_; ///< Destino da exportação.
    bool exportPack_ = true; ///< Compactar assets em pack ZIP.
    bool exportPdb_ = false; ///< Incluir símbolos de debug.
    std::string exportStatus_; ///< Resultado da última exportação.

    std::map<std::string, std::string> stringEdits_; ///< Buffers de campos string.
    int settingsProfile_ = 0; ///< Perfil em edição nos Settings.

    std::deque<Snapshot> undoStack_; ///< Histórico de desfazer.
    std::deque<Snapshot> redoStack_; ///< Histórico de refazer.
    std::string clipboardJson_; ///< Objeto copiado (JSON de cena com 1 objeto).
    bool showRename_ = false; ///< Diálogo Renomear aberto.
    std::string renameBuffer_; ///< Texto do diálogo Renomear.
    VelvX::GameObject* renameTarget_ = nullptr; ///< Objeto sendo renomeado.
    bool showSaveAs_ = false; ///< Diálogo Salvar cena como aberto.
    std::string saveAsName_; ///< Nome destino do Salvar como.
    bool showAssetRename_ = false; ///< Diálogo Renomear asset aberto.
    std::string assetRenameBuffer_; ///< Novo nome do asset.
    std::filesystem::path assetRenamePath_; ///< Asset sendo renomeado.
    bool showAssetDelete_ = false; ///< Confirmação de exclusão de asset.
    std::filesystem::path assetDeletePath_; ///< Asset a excluir.

#ifdef _WIN32
    void* playProcess_ = nullptr; ///< HANDLE do Player em execução.
#else
    int playPid_ = -1; ///< PID do Player em execução.
#endif
};