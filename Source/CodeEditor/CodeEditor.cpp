/**
 * @file CodeEditor.cpp
 * @brief Implementacao do editor de codigo Velv de painel unico.
 */

#include "CodeEditor.hpp"

#include <misc/cpp/imgui_stdlib.h>

#include <algorithm>
#include <cctype>
#include <cstdio>
#include <fstream>
#include <sstream>

namespace {

struct ApiMethod { const char* ns; const char* name; const char* sig; };

const std::vector<ApiMethod>& apiMethods() {
    static const std::vector<ApiMethod> m = {
        {"SceneManager", "loadScene", "loadScene(name: string)"},
        {"SceneManager", "loadSceneAsync", "loadSceneAsync(name: string)"},
        {"SceneManager", "positionScene", "positionScene(pos: vec3)"},
        {"SceneManager", "swapTo", "swapTo(name: string)"},
        {"SceneManager", "getActiveSceneName", "getActiveSceneName() -> string"},
        {"SceneManager", "findGameObject", "findGameObject(name: string) -> GameObject"},
        {"Input", "isKeyDown", "isKeyDown(key: int) -> bool"},
        {"Input", "isKeyPressed", "isKeyPressed(key: int) -> bool"},
        {"Input", "isKeyUp", "isKeyUp(key: int) -> bool"},
        {"Input", "isActionDown", "isActionDown(action: string) -> bool"},
        {"Input", "isActionPressed", "isActionPressed(action: string) -> bool"},
        {"Input", "getAxis", "getAxis(name: string) -> number"},
        {"Input", "getMousePosition", "getMousePosition() -> vec3"},
        {"Input", "getMouseDelta", "getMouseDelta() -> vec3"},
        {"Input", "setCursorCaptured", "setCursorCaptured(captured: bool)"},
        {"Input", "isCursorCaptured", "isCursorCaptured() -> bool"},
        {"Trigger", "bindKey", "bindKey(key: int, action: string)"},
        {"Trigger", "fire", "fire(action: string)"},
        {"Inspector", "dump", "dump()"},
        {"Assets", "resolve", "resolve(path: string) -> string"},
        {"Assets", "loadModel", "loadModel(path: string) -> int"},
        {"Assets", "loadTexture", "loadTexture(path: string) -> int"},
        {"GameObject", "find", "find(name: string) -> GameObject"},
        {"GameObject", "create", "create(name: string) -> GameObject"},
        {"GameObject", "setPosition", "setPosition(obj, pos: vec3)"},
        {"GameObject", "getPosition", "getPosition(obj) -> vec3"},
        {"GameObject", "setScale", "setScale(obj, scale: vec3)"},
        {"GameObject", "setVelocity", "setVelocity(obj, vel: vec3)"},
        {"GameObject", "getVelocity", "getVelocity(obj) -> vec3"},
        {"GameObject", "isGrounded", "isGrounded(obj) -> bool"},
        {"GameObject", "getLayer", "getLayer(obj) -> int"},
        {"GameObject", "setLayer", "setLayer(obj, layer: int)"},
        {"GameObject", "setUseGravity", "setUseGravity(obj, enabled: bool)"},
        {"GameObject", "setMass", "setMass(obj, mass: number)"},
        {"GameObject", "setKinematic", "setKinematic(obj, enabled: bool)"},
        {"GameObject", "setColor", "setColor(obj, color: vec3)"},
        {"GameObject", "getRotation", "getRotation(obj) -> vec3 (graus)"},
        {"GameObject", "setRotation", "setRotation(obj, euler: vec3 graus)"},
        {"GameObject", "getTag", "getTag(obj) -> string"},
        {"GameObject", "setTag", "setTag(obj, tag: string)"},
        {"GameObject", "setName", "setName(obj, name: string)"},
        {"GameObject", "setParent", "setParent(obj, parent)"},
        {"GameObject", "addComponent", "addComponent(obj, name: string)"},
        {"GameObject", "removeComponent", "removeComponent(obj, name: string)"},
        {"GameObject", "addScript", "addScript(obj, path: string)"},
        {"UI", "loadPrefab", "loadPrefab(path: string)"},
        {"UI", "setText", "setText(element: string, text: string)"},
        {"UI", "setVisible", "setVisible(element: string, visible: bool)"},
        {"Time", "setScale", "setScale(factor: number) (0 = pausa)"},
        {"Time", "getScale", "getScale() -> number"},
        {"Application", "quit", "quit() (encerra o jogo)"},
        {"Physics", "overlaps", "overlaps(a: string, b: string) -> bool"},
        {"Physics", "raycast", "raycast(origin: vec3, dir: vec3, maxDist?) -> GameObject"},
        {"Physics", "setGravity", "setGravity(g: vec3)"},
        {"Physics", "getGravity", "getGravity() -> vec3"},
        {"Physics", "setLayerCollision", "setLayerCollision(a: int, b: int, enabled: bool)"},
        {"Physics", "getLayerCollision", "getLayerCollision(a: int, b: int) -> bool"},
        {"Camera", "setPosition", "setPosition(pos: vec3)"},
        {"Camera", "getPosition", "getPosition() -> vec3"},
        {"Camera", "setTarget", "setTarget(target: vec3)"},
        {"Camera", "getTarget", "getTarget() -> vec3"},
        {"Camera", "getForward", "getForward() -> vec3"},
        {"Camera", "move", "move(delta: vec3)"},
        {"Camera", "setRotation", "setRotation(euler: vec3 graus)"},
        {"Camera", "setFOV", "setFOV(degrees: number)"},
        {"Camera", "setOrthoHeight", "setOrthoHeight(height: number)"},
        {"Camera", "setProjection", "setProjection(\"perspective\" | \"ortho\")"},
        {"Camera", "attachTo", "attachTo(obj: GameObject)"},
        {"Camera", "setActive", "setActive(name: string)"},
        {"", "print", "print(value)"},
        {"", "vec3", "vec3(x, y, z) -> vec3"},
        {"", "str", "str(value) -> string"},
        {"", "rand", "rand(lo, hi) -> number"},
        {"", "dt", "dt() -> number (delta time)"},
        {"", "time", "time() -> number (tempo total)"},
        {"", "sin", "sin(x) -> number"},
        {"", "cos", "cos(x) -> number"},
        {"", "sqrt", "sqrt(x) -> number"},
        {"", "abs", "abs(x) -> number"},
        {"", "floor", "floor(x) -> number"},
        {"", "min", "min(a, b) -> number"},
        {"", "max", "max(a, b) -> number"},
        {"", "clamp", "clamp(v, lo, hi) -> number"},
        {"", "Instantiate", "Instantiate(proto: string|GameObject, pos: vec3, scale?) -> GameObject"},
        {"", "Destroy", "Destroy(obj)"},
    };
    return m;
}

const std::vector<std::string>& keyConstants() {
    static const std::vector<std::string> k = {
        "Space", "Escape", "Enter", "Tab", "E", "R", "W", "A", "S", "D",
        "Left", "Right", "Up", "Down", "MouseLeft", "MouseRight", "LeftShift"};
    return k;
}

const std::vector<std::string>& keywords() {
    static const std::vector<std::string> k = {
        "lock", "unlock", "book", "if", "else", "while", "return", "true",
        "false", "break", "self"};
    return k;
}

struct Snippet { const char* insert; const char* label; };
const std::vector<Snippet>& snippets() {
    static const std::vector<Snippet> s = {
        {"book Main()\n{\n    \n}\n", "book Main() { ... }"},
        {"book InGame()\n{\n    \n}\n", "book InGame() { ... }"},
        {"if () {\n    \n}\n", "if () { ... }"},
        {"while () {\n    \n}\n", "while () { ... }"},
        {"lock ", "lock (privada)"},
        {"unlock ", "unlock (editavel no Inspector)"},
    };
    return s;
}

std::string findSignature(const std::string& ns, const std::string& name) {
    for (const auto& m : apiMethods()) if (ns == m.ns && name == m.name) return m.sig;
    return "";
}

struct Completion { std::string insert, label, hint; };

std::vector<Completion> buildCompletions(const std::string& ns, const std::string& prefix) {
    std::vector<Completion> out;
    const auto starts = [&](const std::string& s) {
        if (prefix.empty()) return true;
        if (s.size() < prefix.size()) return false;
        for (size_t i = 0; i < prefix.size(); ++i)
            if (std::tolower((unsigned char)s[i]) != std::tolower((unsigned char)prefix[i])) return false;
        return true;
    };
    if (ns == "Key") {
        for (const auto& k : keyConstants()) if (starts(k)) out.push_back({k, k, "constante de tecla"});
        return out;
    }
    if (!ns.empty()) {
        for (const auto& m : apiMethods())
            if (ns == m.ns && starts(m.name))
                out.push_back({std::string(m.name) + "(", std::string(m.name) + "(", m.sig});
        return out;
    }
    for (const auto& m : apiMethods())
        if (m.ns[0] == '\0' && starts(m.name))
            out.push_back({std::string(m.name) + "(", std::string(m.name) + "(", m.sig});
    static const std::vector<std::string> namespaces = {
        "SceneManager", "Input", "Key", "Trigger", "Inspector",
        "Assets", "GameObject", "UI", "Physics", "Camera", "Time", "Application"};
    for (const auto& n : namespaces) if (starts(n)) out.push_back({n + ".", n + ".", "namespace"});
    for (const auto& k : keywords()) if (starts(k)) out.push_back({k, k, "palavra-chave"});
    for (const auto& s : snippets()) if (starts(s.label)) out.push_back({s.insert, s.label, "snippet"});
    return out;
}

ImVec4 tokenColor(const std::string& text, bool isIdentifier) {
    static const std::vector<std::string> kw = keywords();
    static const std::vector<std::string> builtins = {
        "SceneManager", "GameObject", "Input", "Key", "Trigger", "Inspector",
        "Assets", "Instantiate", "Destroy", "UI", "Physics", "Camera",
        "Time", "Application", "print", "vec3", "rand", "self"};
    if (!isIdentifier) return ImVec4(0.85f, 0.85f, 0.85f, 1.0f);
    if (std::find(kw.begin(), kw.end(), text) != kw.end()) return ImVec4(0.78f, 0.45f, 0.95f, 1.0f);
    if (std::find(builtins.begin(), builtins.end(), text) != builtins.end()) return ImVec4(0.95f, 0.80f, 0.35f, 1.0f);
    return ImVec4(0.85f, 0.85f, 0.85f, 1.0f);
}

ImU32 toU32(const ImVec4& c) { return ImGui::ColorConvertFloat4ToU32(c); }

/** @brief Desenha uma linha com highlighting diretamente no draw list. */
void drawHighlightedLineTo(ImDrawList* dl, const std::string& line, const ImVec2& pos) {
    float x = pos.x;
    const auto emit = [&](const std::string& text, const ImVec4& color) {
        dl->AddText(ImVec2(x, pos.y), toU32(color), text.c_str());
        x += ImGui::CalcTextSize(text.c_str()).x;
    };
    size_t i = 0;
    while (i < line.size()) {
        const char c = line[i];
        if (c == '/' && i + 1 < line.size() && line[i + 1] == '/') { emit(line.substr(i), ImVec4(0.45f, 0.55f, 0.45f, 1)); break; }
        if (c == '"') {
            size_t j = i + 1;
            while (j < line.size() && line[j] != '"') { if (line[j] == '\\') ++j; ++j; }
            if (j < line.size()) ++j;
            emit(line.substr(i, j - i), ImVec4(0.55f, 0.85f, 0.55f, 1)); i = j; continue;
        }
        if (std::isdigit((unsigned char)c)) {
            size_t j = i;
            while (j < line.size() && (std::isdigit((unsigned char)line[j]) || line[j] == '.')) ++j;
            emit(line.substr(i, j - i), ImVec4(0.95f, 0.65f, 0.35f, 1)); i = j; continue;
        }
        if (std::isalpha((unsigned char)c) || c == '_') {
            size_t j = i;
            while (j < line.size() && (std::isalnum((unsigned char)line[j]) || line[j] == '_')) ++j;
            const std::string w = line.substr(i, j - i);
            emit(w, tokenColor(w, true)); i = j; continue;
        }
        emit(std::string(1, c), tokenColor("", false)); ++i;
    }
}

std::vector<std::string> splitLines(const std::string& code) {
    std::vector<std::string> lines;
    std::istringstream s(code);
    std::string line;
    while (std::getline(s, line)) lines.push_back(line);
    if (lines.empty()) lines.push_back("");
    return lines;
}

} // namespace

// ---------------------------------------------------------------------------
// Arquivo
// ---------------------------------------------------------------------------

bool CodeEditor::load(const std::filesystem::path& p) {
    std::ifstream in(p, std::ios::binary);
    if (!in) { status = "Falha ao abrir: " + p.string(); return false; }
    std::ostringstream b; b << in.rdbuf();
    code_ = b.str(); path_ = p; dirty_ = false; cursorPos_ = 0;
    status = "Aberto: " + p.string();
    return true;
}

bool CodeEditor::save() {
    if (path_.empty()) { status = "Sem caminho: use Salvar como."; return false; }
    return saveAs(path_);
}

bool CodeEditor::saveAs(const std::filesystem::path& p) {
    std::ofstream out(p, std::ios::binary | std::ios::trunc);
    if (!out) { status = "Falha ao salvar: " + p.string(); return false; }
    out << code_;
    path_ = p; dirty_ = false;
    status = "Salvo: " + p.string();
    return true;
}

void CodeEditor::setText(const std::string& t) { code_ = t; dirty_ = true; }

void CodeEditor::newScript() {
    code_ =
        "// Script VelvX — book Main() roda uma vez, book InGame() a cada frame.\n"
        "// Variaveis: lock (privada) / unlock (editavel no Inspector).\n"
        "\n"
        "book Main()\n{\n    print(\"Ola, VelvX!\");\n}\n"
        "\n"
        "book InGame()\n{\n}\n";
    path_.clear(); dirty_ = true; cursorPos_ = 0;
    status = "Novo script (modelo).";
}

// ---------------------------------------------------------------------------
// Sintaxe
// ---------------------------------------------------------------------------

std::vector<CodeEditor::Issue> CodeEditor::issues() const {
    std::vector<Issue> out;
    struct Open { char symbol; int line; };
    std::vector<Open> stack;
    int line = 1; bool inString = false; int stringLine = 0;
    for (size_t i = 0; i < code_.size(); ++i) {
        const char c = code_[i];
        if (c == '\n') {
            if (inString) { out.push_back({stringLine, "String nao fechada antes do fim da linha", false}); inString = false; }
            ++line; continue;
        }
        if (inString) { if (c == '\\') { ++i; continue; } if (c == '"') inString = false; continue; }
        if (c == '"') { inString = true; stringLine = line; continue; }
        if (c == '/' && i + 1 < code_.size() && code_[i + 1] == '/') {
            while (i < code_.size() && code_[i] != '\n') ++i;
            if (i < code_.size()) --i;
            continue;
        }
        if (c == '(' || c == '{' || c == '[') stack.push_back({c, line});
        if (c == ')' || c == '}' || c == ']') {
            const char exp = c == ')' ? '(' : (c == '}' ? '{' : '[');
            if (stack.empty() || stack.back().symbol != exp)
                out.push_back({line, std::string("Fechamento '") + c + "' sem abertura", false});
            else stack.pop_back();
        }
    }
    if (inString) out.push_back({stringLine, "String nao fechada", false});
    for (const auto& o : stack) out.push_back({o.line, std::string("Abertura '") + o.symbol + "' nao fechada", false});
    if (code_.find("book Main(") == std::string::npos) out.push_back({1, "book Main() ausente", true});
    if (code_.find("book InGame(") == std::string::npos) out.push_back({1, "book InGame() ausente", true});
    std::sort(out.begin(), out.end(), [](const Issue& a, const Issue& b) { return a.line < b.line; });
    return out;
}

std::string CodeEditor::activeCallHint() const {
    int pos = std::clamp(cursorPos_, 0, (int)code_.size());
    int depth = 0, open = -1; bool inStr = false;
    for (int i = pos - 1; i >= 0; --i) {
        const char c = code_[(size_t)i];
        if (inStr) { if (c == '"') inStr = false; continue; }
        if (c == '"') { inStr = true; continue; }
        if (c == ')') ++depth;
        else if (c == '(') { if (depth == 0) { open = i; break; } --depth; }
    }
    if (open < 0) return "";
    int start = open;
    while (start > 0) {
        const char c = code_[(size_t)start - 1];
        if (std::isalnum((unsigned char)c) || c == '_' || c == '.') --start; else break;
    }
    const std::string token = code_.substr((size_t)start, (size_t)(open - start));
    const size_t dot = token.rfind('.');
    const std::string ns = dot != std::string::npos ? token.substr(0, dot) : "";
    const std::string name = dot != std::string::npos ? token.substr(dot + 1) : token;
    return findSignature(ns, name);
}

// ---------------------------------------------------------------------------
// Callback / autocomplete / busca
// ---------------------------------------------------------------------------

int CodeEditor::callbackTrampoline(ImGuiInputTextCallbackData* d) {
    return static_cast<CodeEditor*>(d->UserData)->callback(d);
}

int CodeEditor::callback(ImGuiInputTextCallbackData* data) {
    cursorPos_ = data->CursorPos;
    if (data->EventFlag != ImGuiInputTextFlags_CallbackEdit) return 0;
    dirty_ = true;
    if (data->CursorPos > 0 && data->Buf[data->CursorPos - 1] == '\t') {
        data->DeleteChars(data->CursorPos - 1, 1);
        data->InsertChars(data->CursorPos - 1, "    ");
        return 0;
    }
    if (data->CursorPos > 0 && data->Buf[data->CursorPos - 1] == '\n') {
        int prevEnd = data->CursorPos - 2, lineStart = prevEnd;
        while (lineStart >= 0 && data->Buf[lineStart] != '\n') --lineStart;
        ++lineStart;
        int indent = 0;
        while (lineStart + indent <= prevEnd && data->Buf[lineStart + indent] == ' ') ++indent;
        int last = prevEnd;
        while (last >= lineStart && (data->Buf[last] == ' ' || data->Buf[last] == '\r')) --last;
        if (last >= lineStart && data->Buf[last] == '{') indent += 4;
        if (indent > 0) data->InsertChars(data->CursorPos, std::string(indent, ' ').c_str());
        return 0;
    }
    if (data->BufTextLen == data->CursorPos && data->CursorPos > 0) {
        const char c = data->Buf[data->CursorPos - 1];
        char close = 0;
        if (c == '(') close = ')'; else if (c == '[') close = ']';
        else if (c == '{') close = '}'; else if (c == '"') close = '"';
        if (close) { char tmp[2] = {close, 0}; data->InsertChars(data->CursorPos, tmp); data->CursorPos -= 1; }
    }
    return 0;
}

void CodeEditor::computeCompletionContext() {
    int end = std::clamp(cursorPos_, 0, (int)code_.size());
    int start = end;
    while (start > 0) {
        const char c = code_[(size_t)start - 1];
        if (std::isalnum((unsigned char)c) || c == '_' || c == '.') --start; else break;
    }
    const std::string token = code_.substr((size_t)start, (size_t)(end - start));
    const size_t dot = token.rfind('.');
    if (dot != std::string::npos) {
        completeNs_ = token.substr(0, dot); completePrefix_ = token.substr(dot + 1);
        completePos_ = start + (int)dot + 1;
    } else { completeNs_.clear(); completePrefix_ = token; completePos_ = start; }
}

void CodeEditor::applyCompletion(const std::string& candidate) {
    const int pos = std::clamp(completePos_, 0, (int)code_.size());
    const int len = std::min((int)completePrefix_.size(), (int)code_.size() - pos);
    code_.replace((size_t)pos, (size_t)len, candidate);
    cursorPos_ = pos + (int)candidate.size();
    dirty_ = true; jumpToCursor_ = true;
    status = "Autocomplete: " + candidate;
}

void CodeEditor::drawAutocomplete() {
    if (!ImGui::BeginPopup("velv_ac")) return;
    if (completeNs_.empty()) ImGui::TextDisabled("Global — prefixo '%s'", completePrefix_.c_str());
    else ImGui::TextDisabled("%s.* — prefixo '%s'", completeNs_.c_str(), completePrefix_.c_str());
    ImGui::Separator();
    const auto items = buildCompletions(completeNs_, completePrefix_);
    int shown = 0;
    for (const auto& it : items) {
        std::string label = it.label;
        std::replace(label.begin(), label.end(), '\n', ' ');
        if (!it.hint.empty()) label += "    (" + it.hint + ")";
        if (ImGui::Selectable(label.c_str())) { applyCompletion(it.insert); ImGui::CloseCurrentPopup(); }
        if (++shown >= 16) break;
    }
    if (shown == 0) ImGui::TextDisabled("Nenhum candidato para este contexto.");
    ImGui::EndPopup();
}

void CodeEditor::findNext(bool backward) {
    if (searchText_.empty()) return;
    const size_t n = code_.size(), q = searchText_.size();
    if (n < q) { status = "Busca: nao encontrado."; return; }
    int from = std::clamp(cursorPos_, 0, (int)n);
    size_t pos = std::string::npos;
    if (!backward) { pos = code_.find(searchText_, (size_t)from); if (pos == std::string::npos) pos = code_.find(searchText_, 0); }
    else { if (from > 0) pos = code_.rfind(searchText_, (size_t)from - 1); if (pos == std::string::npos) pos = code_.rfind(searchText_); }
    if (pos == std::string::npos) { status = "Busca: nao encontrado."; return; }
    cursorPos_ = (int)(pos + q); jumpToCursor_ = true;
    int line = 1; for (size_t i = 0; i < pos; ++i) if (code_[i] == '\n') ++line;
    status = "Encontrado na linha " + std::to_string(line) + ".";
}

void CodeEditor::replaceCurrent() {
    if (searchText_.empty()) return;
    const int end = cursorPos_, start = end - (int)searchText_.size();
    if (start >= 0 && (size_t)end <= code_.size() && code_.compare((size_t)start, searchText_.size(), searchText_) == 0) {
        code_.replace((size_t)start, searchText_.size(), replaceText_);
        cursorPos_ = start + (int)replaceText_.size();
        dirty_ = true; jumpToCursor_ = true; status = "Substituido.";
    }
    findNext(false);
}

void CodeEditor::replaceAll() {
    if (searchText_.empty()) return;
    size_t pos = 0; int count = 0;
    while ((pos = code_.find(searchText_, pos)) != std::string::npos) {
        code_.replace(pos, searchText_.size(), replaceText_);
        pos += replaceText_.size(); ++count;
    }
    if (count) dirty_ = true;
    status = std::to_string(count) + " substituicao(oes).";
}

void CodeEditor::drawSearchBar() {
    if (!showSearch_) return;
    if (focusSearch_) { ImGui::SetKeyboardFocusHere(); focusSearch_ = false; }
    ImGui::SetNextItemWidth(220);
    if (ImGui::InputText("Buscar", &searchText_, ImGuiInputTextFlags_EnterReturnsTrue)) findNext(false);
    ImGui::SameLine(); if (ImGui::Button("Proximo")) findNext(false);
    ImGui::SameLine(); if (ImGui::Button("Anterior")) findNext(true);
    ImGui::SameLine(); ImGui::SetNextItemWidth(220);
    ImGui::InputText("Substituir", &replaceText_);
    ImGui::SameLine(); if (ImGui::Button("Substituir")) replaceCurrent();
    ImGui::SameLine(); if (ImGui::Button("Tudo")) replaceAll();
    ImGui::SameLine(); if (ImGui::Button("X")) showSearch_ = false;
}

// ---------------------------------------------------------------------------
// Desenho (painel unico)
// ---------------------------------------------------------------------------

void CodeEditor::draw(const ImVec2& size) {
    ImGui::BeginChild("velv_code", size, ImGuiChildFlags_Borders);
    if (font_) ImGui::PushFont(font_);
    ImGui::PushStyleVar(ImGuiStyleVar_ItemSpacing, ImVec2(0, 0));
    ImGui::PushStyleVar(ImGuiStyleVar_FramePadding, ImVec2(0, 0));
    ImGui::PushStyleVar(ImGuiStyleVar_FrameBorderSize, 0.0f);
    ImGui::PushStyleColor(ImGuiCol_Text, ImVec4(0, 0, 0, 0));
    ImGui::PushStyleColor(ImGuiCol_FrameBg, ImVec4(0, 0, 0, 0));
    ImGui::PushStyleColor(ImGuiCol_FrameBgHovered, ImVec4(0, 0, 0, 0));
    ImGui::PushStyleColor(ImGuiCol_FrameBgActive, ImVec4(0, 0, 0, 0));
    ImGui::PushStyleColor(ImGuiCol_TextSelectedBg, ImVec4(0.26f, 0.45f, 0.85f, 0.45f));
    ImGui::PushStyleColor(ImGuiCol_InputTextCursor, ImVec4(0.95f, 0.95f, 1.0f, 1.0f));

    const auto lines = splitLines(code_);
    const auto iss = issues();
    std::vector<int> lineState(lines.size(), 0);
    for (const auto& is : iss) {
        const int idx = is.line - 1;
        if (idx >= 0 && idx < (int)lineState.size())
            lineState[(size_t)idx] = std::max(lineState[(size_t)idx], is.warning ? 1 : 2);
    }
    const float lineH = ImGui::GetTextLineHeight();
    const float gutterW = 46.0f;
    float maxW = 0;
    for (const auto& l : lines) maxW = std::max(maxW, ImGui::CalcTextSize(l.c_str()).x);
    const float contentW = gutterW + maxW + 32.0f;
    const float contentH = (float)lines.size() * lineH + 8.0f;
    const ImVec2 origin = ImGui::GetCursorScreenPos();
    ImGui::Dummy(ImVec2(contentW, contentH));
    ImDrawList* dl = ImGui::GetWindowDrawList();

    for (size_t i = 0; i < lines.size(); ++i) {
        const float y = origin.y + (float)i * lineH;
        const int st = lineState[i];
        const ImVec4 nc = st == 2 ? ImVec4(0.95f, 0.4f, 0.4f, 1) : st == 1 ? ImVec4(0.95f, 0.8f, 0.3f, 1) : ImVec4(0.5f, 0.5f, 0.55f, 1);
        char num[16];
        std::snprintf(num, sizeof(num), "%s%4zu", st == 2 ? "!" : (st == 1 ? "?" : " "), i + 1);
        dl->AddText(ImVec2(origin.x + 2, y), toU32(nc), num);
        drawHighlightedLineTo(dl, lines[i], ImVec2(origin.x + gutterW, y));
    }

    ImGui::SetCursorScreenPos(ImVec2(origin.x + gutterW, origin.y));
    if (openPopup_) { ImGui::OpenPopup("velv_ac"); openPopup_ = false; }
    if (jumpToCursor_) { ImGui::SetKeyboardFocusHere(); jumpToCursor_ = false; }
    ImGuiIO& io = ImGui::GetIO();
    ImGui::InputTextMultiline("##velvcode", &code_, ImVec2(contentW - gutterW, contentH),
                              ImGuiInputTextFlags_AllowTabInput | ImGuiInputTextFlags_CallbackEdit |
                                  ImGuiInputTextFlags_CallbackAlways,
                              &CodeEditor::callbackTrampoline, this);
    const bool active = ImGui::IsItemActive();
    if (active && io.KeyCtrl && ImGui::IsKeyPressed(ImGuiKey_Space)) { computeCompletionContext(); openPopup_ = true; }
    if (active && io.KeyCtrl && ImGui::IsKeyPressed(ImGuiKey_S)) save();
    if (active && io.KeyCtrl && ImGui::IsKeyPressed(ImGuiKey_F)) openSearch();
    if (active && io.KeyCtrl && ImGui::IsKeyPressed(ImGuiKey_H)) openSearch();
    drawAutocomplete();

    ImGui::PopStyleColor(6);
    ImGui::PopStyleVar(3);
    if (font_) ImGui::PopFont();
    ImGui::EndChild();
}