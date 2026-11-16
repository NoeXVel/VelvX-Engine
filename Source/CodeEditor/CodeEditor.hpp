#pragma once
/**
 * @file CodeEditor.hpp
 * @brief Editor de codigo Velv de painel UNICO: editing + highlighting inline
 *        (overlay de texto transparente), numeros de linha, marcadores de erro,
 *        autocomplete contextual, dica de parametros e busca/substituicao.
 *        Sem visualizador read-only separado.
 */

#include <imgui.h>
#include <filesystem>
#include <string>
#include <vector>

/** @brief Editor de um buffer .velv, renderizado numa unica area editavel. */
class CodeEditor {
public:
    void setFont(ImFont* f) { font_ = f; }

    bool load(const std::filesystem::path& p);
    bool save();
    bool saveAs(const std::filesystem::path& p);
    void setText(const std::string& t);
    void newScript();

    const std::string& text() const { return code_; }
    const std::filesystem::path& path() const { return path_; }
    void setPath(const std::filesystem::path& p) { path_ = p; }
    bool dirty() const { return dirty_; }
    void clearDirty() { dirty_ = false; }

    /** @brief Desenha a area de codigo (gutter + highlight + input sobreposto). */
    void draw(const ImVec2& size);
    /** @brief Barra de busca/substituicao (opcional, acima do codigo). */
    void drawSearchBar();
    void openSearch() { showSearch_ = true; focusSearch_ = true; }

    /** @brief Assinatura da chamada ativa sob o cursor (dica de parametros). */
    std::string activeCallHint() const;
    /** @brief Problemas de sintaxe do buffer atual. */
    struct Issue { int line; std::string message; bool warning; };
    std::vector<Issue> issues() const;

    std::string status; ///< Mensagem de status exibida pelo hospedeiro.

private:
    int callback(ImGuiInputTextCallbackData* data);
    static int callbackTrampoline(ImGuiInputTextCallbackData* data);
    void computeCompletionContext();
    void applyCompletion(const std::string& candidate);
    void drawAutocomplete();
    void findNext(bool backward);
    void replaceCurrent();
    void replaceAll();

    std::string code_;
    std::filesystem::path path_;
    bool dirty_ = false;
    int cursorPos_ = 0;
    ImFont* font_ = nullptr;

    std::string completePrefix_;
    std::string completeNs_;
    int completePos_ = 0;
    bool openPopup_ = false;
    bool jumpToCursor_ = false;

    std::string searchText_;
    std::string replaceText_;
    bool showSearch_ = false;
    bool focusSearch_ = false;
};