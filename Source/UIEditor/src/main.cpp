/**
 * @file main.cpp
 * @brief VelvX UI Editor: edição visual de prefabs .ui com drag&drop,
 *        posicionamento relativo/absoluto, script binding e preview.
 */

#include <glad/gl.h>
#include <GLFW/glfw3.h>
#include <imgui.h>
#include <backends/imgui_impl_glfw.h>
#include <backends/imgui_impl_opengl3.h>
#include <misc/cpp/imgui_stdlib.h>

#include "Core/AssetBrowser.hpp"
#include "Core/Log.hpp"
#include "UI/UIElement.hpp"
#include "UI/UIManager.hpp"

#include <algorithm>
#include <cstring>
#include <filesystem>
#include <memory>
#include <string>
#include <vector>

using namespace VelvX;

namespace {

std::unique_ptr<UIPanel> g_root; ///< Painel raiz (tamanho da tela).
UIElement* g_selected = nullptr; ///< Elemento em edição.
std::string g_filePath; ///< Prefab .ui aberto.
std::string g_status = "Arraste elementos da paleta para o canvas."; ///< Status.
glm::vec2 g_screen{1280.0f, 720.0f}; ///< Tela de preview.

UIElement* g_drag = nullptr; ///< Elemento sendo arrastado.
glm::vec2 g_dragOffset{0.0f}; ///< Mouse - topo-esquerdo do elemento.
glm::vec2 g_dragParentOrigin{0.0f}; ///< Origem do pai no arrasto.
glm::vec2 g_dragParentSize{0.0f}; ///< Tamanho do pai no arrasto.

std::string g_textBuf; ///< Buffer do texto selecionado.
UIElement* g_textBufOwner = nullptr; ///< Dono do buffer de texto.

/** @brief Retângulo de um elemento no canvas (coordenadas de tela ImGui). */
struct Hit {
    UIElement* element; ///< Elemento desenhado.
    ImVec2 min; ///< Canto superior esquerdo.
    ImVec2 max; ///< Canto inferior direito.
    glm::vec2 parentOrigin; ///< Origem do pai em px de jogo.
    glm::vec2 parentSize; ///< Tamanho do pai em px de jogo.
};
std::vector<Hit> g_hits; ///< Hits do frame (ordem de desenho).

/** @brief Offset de âncora (mesma semântica de UIElement::resolvePosition). */
glm::vec2 anchorOffset(UIElement::Anchor anchor, const glm::vec2& parentSize, const glm::vec2& size) {
    glm::vec2 offset(0.0f);
    switch (anchor) {
        case UIElement::Anchor::TopCenter: case UIElement::Anchor::Center:
        case UIElement::Anchor::BottomCenter:
            offset.x = (parentSize.x - size.x) * 0.5f;
            break;
        case UIElement::Anchor::TopRight: case UIElement::Anchor::CenterRight:
        case UIElement::Anchor::BottomRight:
            offset.x = parentSize.x - size.x;
            break;
        default: break;
    }
    switch (anchor) {
        case UIElement::Anchor::CenterLeft: case UIElement::Anchor::Center:
        case UIElement::Anchor::CenterRight:
            offset.y = (parentSize.y - size.y) * 0.5f;
            break;
        case UIElement::Anchor::BottomLeft: case UIElement::Anchor::BottomCenter:
        case UIElement::Anchor::BottomRight:
            offset.y = parentSize.y - size.y;
            break;
        default: break;
    }
    return offset;
}

/** @brief Cria um elemento pelo nome de tipo da paleta. */
std::unique_ptr<UIElement> makeElement(const std::string& type) {
    static int counter = 0;
    const std::string suffix = std::to_string(++counter);
    if (type == "Button") return std::make_unique<UIButton>("Button" + suffix);
    if (type == "Text") return std::make_unique<UIText>("Text" + suffix);
    if (type == "Image") return std::make_unique<UIImage>("Image" + suffix);
    return std::make_unique<UIPanel>("Panel" + suffix);
}

/** @brief Pai de um elemento (nullptr se raiz/ausente). */
UIElement* parentOf(UIElement* target, UIElement* current) {
    for (const auto& child : current->children) {
        if (child.get() == target) return current;
        if (UIElement* found = parentOf(target, child.get())) return found;
    }
    return nullptr;
}

/** @brief Adiciona elemento com topo-esquerdo em px de jogo. */
void addElementAt(const std::string& type, const glm::vec2& topLeft) {
    UIElement* parent = g_root.get();
    if (g_selected && std::string(g_selected->typeName()) == "Panel") parent = g_selected;
    const glm::vec2 parentOrigin = parent == g_root.get() ? glm::vec2(0.0f) : parent->resolvedPosition;
    const glm::vec2 parentSize = parent->size();
    auto element = makeElement(type);
    const glm::vec2 size = element->size();
    const glm::vec2 offset = topLeft - parentOrigin - anchorOffset(element->anchor, parentSize, size);
    element->transform.position = glm::vec3(offset.x, offset.y, 0.0f);
    g_selected = parent->addChild(std::move(element));
    g_status = "Elemento adicionado: " + g_selected->name;
}

/** @brief Remove o elemento selecionado da hierarquia. */
void removeSelected() {
    if (!g_selected || g_selected == g_root.get()) return;
    UIElement* parent = parentOf(g_selected, g_root.get());
    if (!parent) return;
    auto& children = parent->children;
    children.erase(std::remove_if(children.begin(), children.end(),
                                  [&](const std::unique_ptr<UIElement>& c) {
                                      return c.get() == g_selected;
                                  }),
                   children.end());
    g_status = "Removido: " + g_selected->name;
    g_selected = nullptr;
    g_drag = nullptr;
}

/** @brief Desenha um elemento (e filhos) no canvas e registra o hit. */
void drawElement(ImDrawList* dl, UIElement& element, const glm::vec2& parentOrigin,
                 const glm::vec2& parentSize, const ImVec2& origin, float scale) {
    const glm::vec2 pos = element.resolvePosition(parentOrigin, parentSize);
    const glm::vec2 size = element.size();
    element.resolvedPosition = pos;
    const ImVec2 min(origin.x + pos.x * scale, origin.y + pos.y * scale);
    const ImVec2 max(min.x + size.x * scale, min.y + size.y * scale);
    g_hits.push_back({&element, min, max, parentOrigin, parentSize});
    if (!element.isVisible) return;

    const ImU32 color = ImGui::ColorConvertFloat4ToU32(
        ImVec4(element.color.x, element.color.y, element.color.z, element.color.w));
    const std::string type = element.typeName();
    if (type == "Button") {
        auto& button = static_cast<UIButton&>(element);
        dl->AddRectFilled(min, max, color, 4.0f * scale);
        const ImVec2 textSize = ImGui::CalcTextSize(button.label.c_str());
        const float fontSize = button.fontSize * scale;
        const ImVec2 textPos(min.x + (max.x - min.x - textSize.x * scale) * 0.5f,
                             min.y + (max.y - min.y - textSize.y * scale) * 0.5f);
        dl->AddText(ImGui::GetFont(), fontSize, textPos,
                    ImGui::ColorConvertFloat4ToU32(ImVec4(button.textColor.x, button.textColor.y,
                                                          button.textColor.z, button.textColor.w)),
                    button.label.c_str());
    } else if (type == "Text") {
        auto& text = static_cast<UIText&>(element);
        dl->AddText(ImGui::GetFont(), text.fontSize * scale, min, color, text.text().c_str());
    } else if (type == "Image") {
        auto& image = static_cast<UIImage&>(element);
        const unsigned int tex = image.texturePath.empty() ? 0 : AssetBrowser::loadTexture(image.texturePath);
        if (tex != 0) {
            dl->AddImage(static_cast<ImTextureID>(tex), min, max);
        } else {
            dl->AddRectFilled(min, max, IM_COL32(90, 90, 110, 255));
            dl->AddText(ImGui::GetFont(), 14.0f * scale, ImVec2(min.x + 4, min.y + 4),
                        IM_COL32(220, 220, 220, 255), "imagem");
        }
    } else {
        dl->AddRectFilled(min, max, color, 2.0f * scale);
    }
    if (&element == g_selected) {
        dl->AddRect(min, max, IM_COL32(255, 210, 60, 255), 0.0f, 0, 2.0f);
    }
    for (const auto& child : element.children) {
        drawElement(dl, *child, pos, size, origin, scale);
    }
}

/** @brief Canvas de preview com seleção, arrasto e drop da paleta. */
void drawCanvas(float width) {
    ImGui::BeginChild("canvas", ImVec2(width, 0), ImGuiChildFlags_Borders);
    const ImVec2 avail = ImGui::GetContentRegionAvail();
    const ImVec2 cursor = ImGui::GetCursorScreenPos();
    ImGui::Dummy(avail);
    const float scale = std::min(avail.x / g_screen.x, avail.y / g_screen.y);
    const ImVec2 origin(cursor.x + (avail.x - g_screen.x * scale) * 0.5f,
                        cursor.y + (avail.y - g_screen.y * scale) * 0.5f);
    ImDrawList* dl = ImGui::GetWindowDrawList();
    dl->AddRectFilled(origin, ImVec2(origin.x + g_screen.x * scale, origin.y + g_screen.y * scale),
                      IM_COL32(24, 24, 30, 255));
    dl->AddRect(origin, ImVec2(origin.x + g_screen.x * scale, origin.y + g_screen.y * scale),
                IM_COL32(90, 90, 110, 255));

    g_hits.clear();
    drawElement(dl, *g_root, glm::vec2(0.0f), g_screen, origin, scale);

    const ImVec2 mouse = ImGui::GetMousePos();
    const bool overCanvas = mouse.x >= origin.x && mouse.y >= origin.y &&
                            mouse.x <= origin.x + g_screen.x * scale &&
                            mouse.y <= origin.y + g_screen.y * scale;
    if (overCanvas && ImGui::IsMouseClicked(ImGuiMouseButton_Left)) {
        for (auto it = g_hits.rbegin(); it != g_hits.rend(); ++it) {
            if (mouse.x >= it->min.x && mouse.x <= it->max.x &&
                mouse.y >= it->min.y && mouse.y <= it->max.y) {
                g_selected = it->element;
                g_drag = it->element;
                g_dragOffset = glm::vec2(mouse.x - it->min.x, mouse.y - it->min.y);
                g_dragParentOrigin = it->parentOrigin;
                g_dragParentSize = it->parentSize;
                if (std::string(it->element->typeName()) == "Button" &&
                    !it->element->scriptBinding.empty()) {
                    g_status = "Clique (preview): " + it->element->name + " -> book '" +
                               it->element->scriptBinding + "'";
                }
                break;
            }
        }
    }
    if (g_drag && ImGui::IsMouseDown(ImGuiMouseButton_Left)) {
        const glm::vec2 desired(mouse.x - g_dragOffset.x, mouse.y - g_dragOffset.y);
        const glm::vec2 topLeft((desired.x - origin.x) / scale, (desired.y - origin.y) / scale);
        const glm::vec2 offset = topLeft - g_dragParentOrigin -
                                 anchorOffset(g_drag->anchor, g_dragParentSize, g_drag->size());
        const glm::vec2 pos = g_drag->relativePosition
                                  ? glm::vec2(offset.x / g_dragParentSize.x, offset.y / g_dragParentSize.y)
                                  : offset;
        g_drag->transform.position = glm::vec3(pos.x, pos.y, 0.0f);
    }
    if (!ImGui::IsMouseDown(ImGuiMouseButton_Left)) g_drag = nullptr;

    if (ImGui::BeginDragDropTarget()) {
        if (const ImGuiPayload* payload = ImGui::AcceptDragDropPayload("VELVX_UI_NEW")) {
            const std::string type(static_cast<const char*>(payload->Data));
            const glm::vec2 drop((mouse.x - origin.x) / scale, (mouse.y - origin.y) / scale);
            addElementAt(type, drop);
        }
        ImGui::EndDragDropTarget();
    }
    ImGui::EndChild();
}

/** @brief Coluna esquerda: paleta com drag&drop + hierarquia. */
void drawTree(UIElement& element);
void drawLeft() {
    ImGui::BeginChild("esquerda", ImVec2(190, 0), ImGuiChildFlags_Borders);
    ImGui::SeparatorText("Paleta");
    const char* types[] = {"Button", "Text", "Image", "Panel"};
    const char* labels[] = {"Botao", "Texto", "Imagem", "Painel"};
    for (int i = 0; i < 4; ++i) {
        if (ImGui::Button(labels[i], ImVec2(-1, 0))) {
            addElementAt(types[i], g_screen * 0.5f - glm::vec2(80.0f, 24.0f));
        }
        if (ImGui::BeginDragDropSource()) {
            ImGui::SetDragDropPayload("VELVX_UI_NEW", types[i], std::strlen(types[i]) + 1);
            ImGui::Text("Novo: %s", labels[i]);
            ImGui::EndDragDropSource();
        }
    }
    if (ImGui::Button("Excluir selecionado", ImVec2(-1, 0))) removeSelected();
    ImGui::SeparatorText("Hierarquia");
    drawTree(*g_root);
    ImGui::EndChild();
}

/** @brief Árvore de hierarquia recursiva. */
void drawTree(UIElement& element) {
    ImGui::PushID(&element);
    const std::string label = element.name + " (" + element.typeName() + ")";
    const bool open = ImGui::TreeNodeEx(label.c_str(), ImGuiTreeNodeFlags_OpenOnArrow |
                                                           ImGuiTreeNodeFlags_SpanAvailWidth |
                                                           (element.children.empty() ? ImGuiTreeNodeFlags_Leaf : 0));
    if (ImGui::IsItemClicked() && !ImGui::IsItemToggledOpen()) g_selected = &element;
    if (&element == g_selected) {
        ImGui::SameLine();
        ImGui::TextDisabled("<-");
    }
    if (open) {
        for (const auto& child : element.children) drawTree(*child);
        ImGui::TreePop();
    }
    ImGui::PopID();
}

/** @brief Inspector do elemento selecionado. */
void drawInspector() {
    ImGui::BeginChild("inspector", ImVec2(300, 0), ImGuiChildFlags_Borders);
    ImGui::SeparatorText("Inspector");
    UIElement* element = g_selected;
    if (!element) {
        ImGui::TextDisabled("Selecione um elemento no canvas ou na hierarquia.");
        ImGui::EndChild();
        return;
    }
    ImGui::InputText("Nome", &element->name);
    ImGui::TextDisabled("Tipo: %s", element->typeName());
    ImGui::DragFloat2("Posicao", &element->transform.position.x, 1.0f);
    ImGui::DragFloat2("Tamanho", &element->transform.scale.x, 1.0f, 1.0f, 4096.0f);

    const char* anchors[] = {"TopLeft", "TopCenter", "TopRight", "CenterLeft", "Center",
                             "CenterRight", "BottomLeft", "BottomCenter", "BottomRight"};
    int anchorIndex = 0;
    for (int i = 0; i < 9; ++i) {
        if (UIElement::anchorToString(element->anchor) == anchors[i]) anchorIndex = i;
    }
    if (ImGui::Combo("Ancora", &anchorIndex, anchors, 9)) {
        element->anchor = UIElement::anchorFromString(anchors[anchorIndex]);
    }
    ImGui::Checkbox("Posicao relativa (fracao do pai)", &element->relativePosition);
    ImGui::Checkbox("Visivel", &element->isVisible);
    ImGui::Checkbox("Interagivel", &element->isInteractable);
    ImGui::ColorEdit4("Cor", &element->color.x);
    ImGui::InputText("Script binding (book)", &element->scriptBinding);

    const std::string type = element->typeName();
    if (type == "Button") {
        auto& button = static_cast<UIButton&>(*element);
        ImGui::SeparatorText("Botao");
        ImGui::InputText("Rotulo", &button.label);
        ImGui::DragFloat("Fonte", &button.fontSize, 0.5f, 6.0f, 96.0f);
        ImGui::ColorEdit4("Cor do texto", &button.textColor.x);
    } else if (type == "Text") {
        auto& text = static_cast<UIText&>(*element);
        ImGui::SeparatorText("Texto");
        if (g_textBufOwner != element) {
            g_textBufOwner = element;
            g_textBuf = text.text();
        }
        if (ImGui::InputText("Conteudo", &g_textBuf)) text.setText(g_textBuf);
        ImGui::DragFloat("Fonte", &text.fontSize, 0.5f, 6.0f, 96.0f);
    } else if (type == "Image") {
        auto& image = static_cast<UIImage&>(*element);
        ImGui::SeparatorText("Imagem");
        ImGui::InputText("Textura", &image.texturePath);
        ImGui::TextDisabled("Assets(\"Texturas/nome\") ou caminho de arquivo.");
    }
    ImGui::EndChild();
}

/** @brief Carrega um prefab .ui como raiz do editor. */
void loadPrefab(const std::filesystem::path& path) {
    auto loaded = UIManager::loadUIPrefab(path.string());
    if (!loaded) {
        g_status = "Falha ao carregar: " + path.string();
        return;
    }
    if (UIPanel* panel = dynamic_cast<UIPanel*>(loaded.get())) {
        loaded.release();
        g_root.reset(panel);
    } else {
        g_root = std::make_unique<UIPanel>("Root");
        g_root->addChild(std::move(loaded));
    }
    g_root->transform.scale = glm::vec3(g_screen.x, g_screen.y, 1.0f);
    g_selected = nullptr;
    g_filePath = path.string();
    g_status = "Prefab aberto: " + g_filePath;
}

/** @brief Toolbar de arquivo e tela. */
void drawToolbar() {
    if (ImGui::Button("Novo")) {
        g_root = std::make_unique<UIPanel>("Root");
        g_root->transform.scale = glm::vec3(g_screen.x, g_screen.y, 1.0f);
        g_root->color = glm::vec4(0.10f, 0.10f, 0.14f, 1.0f);
        g_selected = nullptr;
        g_filePath.clear();
        g_status = "Novo prefab.";
    }
    ImGui::SameLine();
    ImGui::SetNextItemWidth(360);
    ImGui::InputText("##arquivo", &g_filePath);
    ImGui::SameLine();
    if (ImGui::Button("Abrir")) loadPrefab(g_filePath);
    ImGui::SameLine();
    if (ImGui::Button("Salvar")) {
        if (g_filePath.empty()) {
            g_status = "Defina o caminho do arquivo .ui antes de salvar.";
        } else {
            UIManager::saveUIPrefab(*g_root, g_filePath);
            g_status = "Salvo: " + g_filePath;
        }
    }
    ImGui::SameLine();
    const char* screens[] = {"1280x720", "1920x1080", "1024x768"};
    static int screenIndex = 0;
    ImGui::SetNextItemWidth(110);
    if (ImGui::Combo("Tela", &screenIndex, screens, 3)) {
        const glm::vec2 sizes[] = {{1280, 720}, {1920, 1080}, {1024, 768}};
        g_screen = sizes[screenIndex];
        g_root->transform.scale = glm::vec3(g_screen.x, g_screen.y, 1.0f);
    }
    ImGui::SameLine();
    ImGui::TextDisabled("%s", g_status.c_str());
}

} // namespace

int main(int argc, char** argv) {
    if (!glfwInit()) {
        Log::error("UIEditor: glfwInit() falhou");
        return 1;
    }
    glfwWindowHint(GLFW_CONTEXT_VERSION_MAJOR, 3);
    glfwWindowHint(GLFW_CONTEXT_VERSION_MINOR, 3);
    glfwWindowHint(GLFW_OPENGL_PROFILE, GLFW_OPENGL_CORE_PROFILE);
#ifdef __APPLE__
    glfwWindowHint(GLFW_OPENGL_FORWARD_COMPAT, GL_TRUE);
#endif
    GLFWwindow* window = glfwCreateWindow(1400, 860, "VelvX UI Editor", nullptr, nullptr);
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
    io.IniFilename = "velvx_uieditor.ini";
    ImGui::StyleColorsDark();
    ImGui_ImplGlfw_InitForOpenGL(window, true);
    ImGui_ImplOpenGL3_Init("#version 330");

    g_root = std::make_unique<UIPanel>("Root");
    g_root->transform.scale = glm::vec3(g_screen.x, g_screen.y, 1.0f);
    g_root->color = glm::vec4(0.10f, 0.10f, 0.14f, 1.0f);
    if (argc > 1) {
        loadPrefab(argv[1]);
    } else {
        auto button = std::make_unique<UIButton>("PlayButton");
        button->label = "Jogar";
        button->scriptBinding = "OnPlay";
        button->transform.position = glm::vec3(560.0f, 320.0f, 0.0f);
        g_root->addChild(std::move(button));
        auto text = std::make_unique<UIText>("Title");
        text->setText("VelvX UI Editor");
        text->fontSize = 32.0f;
        text->transform.position = glm::vec3(520.0f, 120.0f, 0.0f);
        g_root->addChild(std::move(text));
    }

    while (!glfwWindowShouldClose(window)) {
        glfwPollEvents();
        ImGui_ImplOpenGL3_NewFrame();
        ImGui_ImplGlfw_NewFrame();
        ImGui::NewFrame();

        const ImGuiViewport* viewport = ImGui::GetMainViewport();
        ImGui::SetNextWindowPos(viewport->WorkPos);
        ImGui::SetNextWindowSize(viewport->WorkSize);
        ImGui::Begin("VelvX UI Editor", nullptr,
                     ImGuiWindowFlags_NoDecoration | ImGuiWindowFlags_NoMove |
                         ImGuiWindowFlags_NoSavedSettings);
        drawToolbar();
        ImGui::Separator();
        drawLeft();
        ImGui::SameLine();
        const float canvasWidth =
            ImGui::GetContentRegionAvail().x - 300.0f - ImGui::GetStyle().ItemSpacing.x;
        drawCanvas(canvasWidth);
        ImGui::SameLine();
        drawInspector();
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