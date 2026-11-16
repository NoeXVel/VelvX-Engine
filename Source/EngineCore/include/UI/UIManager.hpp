#pragma once
/**
 * @file UIManager.hpp
 * @brief Gerenciamento de UI em runtime + prefabs .ui (JSON).
 */

#include "UI/UIElement.hpp"
#include <filesystem>
#include <functional>
#include <memory>
#include <string>
#include <vector>

namespace VelvX {

/**
 * @brief Gerencia elementos de UI, cliques e prefabs .ui.
 */
class UIManager {
public:
    /** @brief Inicializa o gerenciador (tela padrão 1280x720). */
    static void init();

    /** @brief Remove todos os elementos. */
    static void shutdown();

    /** @brief Atualiza elementos e processa cliques do mouse. */
    static void update(float deltaTime);

    /** @brief Desenha todos os elementos visíveis (pass 2D). */
    static void render();

    /** @brief Adiciona um elemento raiz. */
    static void addUI(std::unique_ptr<UIElement> ui);

    /** @brief Remove um elemento raiz (seguro para nulos). */
    static void removeUI(UIElement* ui);

    /** @brief Elementos raiz atuais. */
    static const std::vector<std::unique_ptr<UIElement>>& elements();

    /** @brief Busca elemento (qualquer nível) pelo nome. */
    static UIElement* findByName(const std::string& name);

    /** @brief Define o tamanho da tela para âncoras/posicionamento relativo. */
    static void setScreenSize(float width, float height);

    /** @brief Tamanho da tela configurado. */
    static glm::vec2 screenSize();

    /**
     * @brief Processa um clique em coordenadas de tela.
     * @return true se algum elemento interagível consumiu o clique.
     */
    static bool handleClick(const glm::vec2& point);

    /** @brief Carrega um prefab .ui (JSON); nullptr em falha. */
    static std::unique_ptr<UIElement> loadUIPrefab(const std::string& filePath);

    /** @brief Salva um elemento (e filhos) como prefab .ui. */
    static void saveUIPrefab(const UIElement& ui, const std::string& filePath);

    /** @brief Ponte para a Velv Language: dispara books por scriptBinding. */
    static std::function<void(const std::string&)> onScriptEvent;

private:
    static std::vector<std::unique_ptr<UIElement>> uiElements_; ///< Raízes de UI.
    static glm::vec2 screenSize_; ///< Dimensões da tela.

    /** @brief Atualiza/desenha recursivamente resolvendo posições. */
    static void updateElement(UIElement& element, float deltaTime);
    static void renderElement(UIElement& element, const glm::vec2& origin, const glm::vec2& area);
    static bool clickElement(UIElement& element, const glm::vec2& point);
};

} // namespace VelvX