#pragma once
/**
 * @file UIElement.hpp
 * @brief Elementos de UI: base, botão, texto, imagem e painel com hierarquia.
 */

#include "Core/Transform.hpp"
#include <functional>
#include <glm/glm.hpp>
#include <memory>
#include <string>
#include <vector>

namespace VelvX {

namespace Json { class Value; }

/**
 * @brief Base de todos os elementos de UI.
 *
 * A posição vem de @c transform.position (x,y) e o tamanho de
 * @c transform.scale (x,y). Com @c relativePosition o offset é uma fração
 * (0..1) da área pai; caso contrário são pixels a partir da âncora.
 */
class UIElement {
public:
    /** @brief Âncora de posicionamento dentro da área pai. */
    enum class Anchor {
        TopLeft, TopCenter, TopRight,
        CenterLeft, Center, CenterRight,
        BottomLeft, BottomCenter, BottomRight
    };

    std::string name; ///< Nome identificador.
    Transform transform; ///< Posição (x,y) e tamanho (scale x,y) 2D.
    bool isVisible = true; ///< Visibilidade.
    bool isInteractable = true; ///< Recebe cliques.
    Anchor anchor = Anchor::TopLeft; ///< Âncora na área pai.
    bool relativePosition = false; ///< Offset relativo (fração) vs pixels.
    std::string scriptBinding; ///< Book Velv disparado no clique.
    glm::vec4 color{0.16f, 0.16f, 0.20f, 0.95f}; ///< Cor de fundo/tint.

    glm::vec2 resolvedPosition{0.0f}; ///< Posição absoluta calculada pelo UIManager.
    std::vector<std::unique_ptr<UIElement>> children; ///< Filhos (painéis).

    /** @brief Elemento nomeado. */
    explicit UIElement(std::string name = "UIElement") : name(std::move(name)) {}
    virtual ~UIElement() = default;

    /** @brief Atualização por frame. */
    virtual void update(float deltaTime) { (void)deltaTime; }

    /** @brief Desenho (usa resolvedPosition). */
    virtual void render() {}

    /** @brief Callback de clique. */
    virtual void onClick() {}

    /** @brief Nome do tipo para serialização. */
    virtual const char* typeName() const { return "Element"; }

    /** @brief Serializa o elemento (sem filhos) em JSON. */
    virtual Json::Value toJson() const;

    /** @brief Lê propriedades específicas do tipo. */
    virtual void fromJson(const Json::Value& json);

    /** @brief Adiciona um filho e retorna o ponteiro. */
    UIElement* addChild(std::unique_ptr<UIElement> child);

    /** @brief Busca descendente pelo nome (nullptr se ausente). */
    UIElement* findByName(const std::string& childName);

    /** @brief Calcula a posição absoluta dentro da área pai. */
    glm::vec2 resolvePosition(const glm::vec2& parentOrigin, const glm::vec2& parentSize) const;

    /** @brief Tamanho em pixels. */
    glm::vec2 size() const { return glm::vec2(transform.scale.x, transform.scale.y); }

    /** @brief true se @p point está dentro do retângulo do elemento. */
    bool contains(const glm::vec2& point) const;

    /** @brief Nome da âncora. */
    static std::string anchorToString(Anchor anchor);

    /** @brief Âncora a partir do nome. */
    static Anchor anchorFromString(const std::string& name);
};

/**
 * @brief Botão clicável com rótulo e callback.
 */
class UIButton : public UIElement {
public:
    std::string label = "Button"; ///< Texto do botão.
    float fontSize = 18.0f; ///< Tamanho da fonte do rótulo.
    glm::vec4 textColor{1.0f, 1.0f, 1.0f, 1.0f}; ///< Cor do rótulo.
    std::function<void()> onButtonClicked; ///< Callback nativo opcional.

    /** @brief Botão nomeado. */
    explicit UIButton(std::string name = "Button") : UIElement(std::move(name)) {
        transform.scale = glm::vec3(160.0f, 44.0f, 1.0f);
        color = glm::vec4(0.25f, 0.35f, 0.65f, 1.0f);
    }

    /** @brief Desenha fundo + rótulo centralizado. */
    void render() override;

    /** @brief Dispara callback e script binding. */
    void onClick() override;

    /** @brief Nome do tipo. */
    const char* typeName() const override { return "Button"; }

    /** @brief Serialização. */
    Json::Value toJson() const override;

    /** @brief Desserialização. */
    void fromJson(const Json::Value& json) override;
};

/**
 * @brief Texto estático ou dinâmico.
 */
class UIText : public UIElement {
public:
    float fontSize = 18.0f; ///< Tamanho da fonte.

    /** @brief Texto nomeado. */
    explicit UIText(std::string name = "Text") : UIElement(std::move(name)) {
        transform.scale = glm::vec3(240.0f, 28.0f, 1.0f);
        color = glm::vec4(1.0f, 1.0f, 1.0f, 1.0f);
    }

    /** @brief Define o conteúdo textual. */
    void setText(const std::string& text) { text_ = text; }

    /** @brief Conteúdo textual. */
    const std::string& text() const { return text_; }

    /** @brief Desenha o texto. */
    void render() override;

    /** @brief Nome do tipo. */
    const char* typeName() const override { return "Text"; }

    /** @brief Serialização. */
    Json::Value toJson() const override;

    /** @brief Desserialização. */
    void fromJson(const Json::Value& json) override;

private:
    std::string text_ = "Text"; ///< Conteúdo.
};

/**
 * @brief Imagem texturizada.
 */
class UIImage : public UIElement {
public:
    std::string texturePath; ///< Textura (Assets("...") ou caminho).

    /** @brief Imagem nomeada. */
    explicit UIImage(std::string name = "Image") : UIElement(std::move(name)) {
        transform.scale = glm::vec3(128.0f, 128.0f, 1.0f);
        color = glm::vec4(1.0f, 1.0f, 1.0f, 1.0f);
    }

    /** @brief Desenha a textura (fallback: retângulo sólido). */
    void render() override;

    /** @brief Nome do tipo. */
    const char* typeName() const override { return "Image"; }

    /** @brief Serialização. */
    Json::Value toJson() const override;

    /** @brief Desserialização. */
    void fromJson(const Json::Value& json) override;
};

/**
 * @brief Painel/container com fundo e filhos.
 */
class UIPanel : public UIElement {
public:
    /** @brief Painel nomeado. */
    explicit UIPanel(std::string name = "Panel") : UIElement(std::move(name)) {
        transform.scale = glm::vec3(320.0f, 240.0f, 1.0f);
    }

    /** @brief Desenha o fundo. */
    void render() override;

    /** @brief Nome do tipo. */
    const char* typeName() const override { return "Panel"; }
};

} // namespace VelvX