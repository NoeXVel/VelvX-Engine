/**
 * @file ScriptEngine.cpp
 * @brief Interpretador da Velv Language: lexer, parser e avaliação em árvore.
 *
 * Gramática suportada:
 * @code
 * programa   := (book | stmt)*
 * book       := 'book' IDENT '(' ')' bloco
 * bloco      := '{' stmt* '}'
 * stmt       := decl | assign | if | while | bloco | exprStmt
 * decl       := ('lock'|'unlock') IDENT ('=' expr)? ';'
 * assign     := IDENT '=' expr ';'
 * if         := 'if' '(' expr ')' bloco ('else' (if|bloco))?
 * while      := 'while' '(' expr ')' bloco
 * exprStmt   := expr ';'
 * @endcode
 *
 * @c lock declara uma variável privada (fora do Inspector). @c unlock declara
 * uma variável editável no Inspector quando o script está anexado a um objeto.
 */

#include "Scripting/ScriptEngine.hpp"
#include "Core/AssetBrowser.hpp"
#include "Core/GameObject.hpp"
#include "Core/Input.hpp"
#include "Core/Inspector.hpp"
#include "Core/Log.hpp"
#include "Core/Trigger.hpp"
#include "Engine/Engine.hpp"
#include "Input/InputManager.hpp"
#include "Physics/CollisionSystem.hpp"
#include "Physics/PhysicsComponent.hpp"
#include "Renderer/Camera.hpp"
#include "Renderer/RenderComponent.hpp"
#include "Scripting/ScriptComponent.hpp"
#include "UI/UIManager.hpp"
#include "Utils/FileSystem.hpp"
#include <GLFW/glfw3.h>
#include <algorithm>
#include <cctype>
#include <cmath>
#include <format>
#include <fstream>
#include <glm/gtc/quaternion.hpp>
#include <random>
#include <sstream>
#include <stdexcept>
#include <unordered_map>
#include <vector>

namespace VelvX {

// ---------------------------------------------------------------------------
// Valores
// ---------------------------------------------------------------------------

/** @brief Valor dinâmico da Velv Language. */
struct Value {
    enum class Type { Null, Bool, Number, String, Vec3, Object, Native };

    Type type = Type::Null; ///< Tipo atual.
    bool boolean = false; ///< Payload bool.
    double number = 0.0; ///< Payload numérico.
    std::string text; ///< Payload string (ou nome do namespace nativo).
    glm::vec3 vec{0.0f}; ///< Payload vec3.
    GameObject* object = nullptr; ///< Payload GameObject.

    static Value null() { return {}; }
    static Value ofBool(bool b) { Value v; v.type = Type::Bool; v.boolean = b; return v; }
    static Value ofNumber(double n) { Value v; v.type = Type::Number; v.number = n; return v; }
    static Value ofString(std::string s) { Value v; v.type = Type::String; v.text = std::move(s); return v; }
    static Value ofVec3(glm::vec3 vec) { Value v; v.type = Type::Vec3; v.vec = vec; return v; }
    static Value ofObject(GameObject* o) { Value v; v.type = Type::Object; v.object = o; return v; }
    static Value ofNative(std::string ns) { Value v; v.type = Type::Native; v.text = std::move(ns); return v; }

    bool truthy() const {
        switch (type) {
            case Type::Null: return false;
            case Type::Bool: return boolean;
            case Type::Number: return number != 0.0;
            case Type::String: return !text.empty();
            case Type::Object: return object != nullptr;
            default: return true;
        }
    }

    std::string toString() const {
        switch (type) {
            case Type::Null: return "null";
            case Type::Bool: return boolean ? "true" : "false";
            case Type::Number: return std::format("{}", number);
            case Type::String: return text;
            case Type::Vec3: return std::format("vec3({}, {}, {})", vec.x, vec.y, vec.z);
            case Type::Object: return object ? std::format("GameObject('{}')", object->name) : "GameObject(null)";
            case Type::Native: return "<native " + text + ">";
        }
        return "?";
    }
};

/** @brief Erro de script com linha de origem. */
struct ScriptError : std::runtime_error {
    int line;
    ScriptError(const std::string& msg, int ln) : std::runtime_error(msg), line(ln) {}
};

// ---------------------------------------------------------------------------
// Lexer
// ---------------------------------------------------------------------------

/** @brief Token da Velv Language. */
struct Token {
    enum class Kind {
        Ident, Number, String,
        LParen, RParen, LBrace, RBrace, Comma, Semicolon, Dot,
        Assign, Eq, Neq, Lt, Gt, Le, Ge,
        Plus, Minus, Star, Slash, Bang, And, Or,
        KwBook, KwLock, KwUnlock, KwIf, KwElse, KwWhile, KwTrue, KwFalse,
        KwReturn, KwBreak,
        End
    };

    Kind kind = Kind::End; ///< Tipo do token.
    std::string text; ///< Texto (identificador, string, operador).
    double number = 0.0; ///< Valor numérico.
    int line = 1; ///< Linha de origem.
};

static std::vector<Token> tokenize(const std::string& source) {
    std::vector<Token> tokens;
    size_t i = 0;
    int line = 1;
    const size_t n = source.size();

    const auto isIdentStart = [](char c) { return std::isalpha(static_cast<unsigned char>(c)) || c == '_'; };
    const auto isIdentChar = [](char c) { return std::isalnum(static_cast<unsigned char>(c)) || c == '_'; };

    while (i < n) {
        const char c = source[i];
        if (c == '\n') { ++line; ++i; continue; }
        if (std::isspace(static_cast<unsigned char>(c))) { ++i; continue; }
        if (c == '/' && i + 1 < n && source[i + 1] == '/') {
            while (i < n && source[i] != '\n') ++i;
            continue;
        }
        if (c == '/' && i + 1 < n && source[i + 1] == '*') {
            i += 2;
            while (i + 1 < n && !(source[i] == '*' && source[i + 1] == '/')) {
                if (source[i] == '\n') ++line;
                ++i;
            }
            i = std::min(i + 2, n);
            continue;
        }

        Token t;
        t.line = line;

        if (isIdentStart(c)) {
            size_t start = i;
            while (i < n && isIdentChar(source[i])) ++i;
            t.text = source.substr(start, i - start);
            static const std::unordered_map<std::string, Token::Kind> keywords = {
                {"book", Token::Kind::KwBook}, {"lock", Token::Kind::KwLock},
                {"unlock", Token::Kind::KwUnlock}, {"if", Token::Kind::KwIf},
                {"else", Token::Kind::KwElse}, {"while", Token::Kind::KwWhile},
                {"true", Token::Kind::KwTrue}, {"false", Token::Kind::KwFalse},
                {"return", Token::Kind::KwReturn}, {"break", Token::Kind::KwBreak}
            };
            auto it = keywords.find(t.text);
            t.kind = it != keywords.end() ? it->second : Token::Kind::Ident;
            tokens.push_back(t);
            continue;
        }

        if (std::isdigit(static_cast<unsigned char>(c))) {
            size_t start = i;
            while (i < n && (std::isdigit(static_cast<unsigned char>(source[i])) || source[i] == '.')) ++i;
            t.kind = Token::Kind::Number;
            t.text = source.substr(start, i - start);
            t.number = std::stod(t.text);
            tokens.push_back(t);
            continue;
        }

        if (c == '"') {
            ++i;
            std::string value;
            while (i < n && source[i] != '"') {
                if (source[i] == '\\' && i + 1 < n) {
                    ++i;
                    switch (source[i]) {
                        case 'n': value += '\n'; break;
                        case 't': value += '\t'; break;
                        case '"': value += '"'; break;
                        case '\\': value += '\\'; break;
                        default: value += source[i]; break;
                    }
                } else {
                    value += source[i];
                }
                ++i;
            }
            if (i >= n) throw ScriptError("string não terminada", line);
            ++i;
            t.kind = Token::Kind::String;
            t.text = value;
            tokens.push_back(t);
            continue;
        }

        const auto two = [&](const char* op, Token::Kind kind) {
            if (i + 1 < n && source[i] == op[0] && source[i + 1] == op[1]) {
                t.kind = kind;
                t.text = op;
                tokens.push_back(t);
                i += 2;
                return true;
            }
            return false;
        };
        if (two("==", Token::Kind::Eq) || two("!=", Token::Kind::Neq) ||
            two("<=", Token::Kind::Le) || two(">=", Token::Kind::Ge) ||
            two("&&", Token::Kind::And) || two("||", Token::Kind::Or)) {
            continue;
        }

        static const std::unordered_map<char, Token::Kind> singles = {
            {'(', Token::Kind::LParen}, {')', Token::Kind::RParen},
            {'{', Token::Kind::LBrace}, {'}', Token::Kind::RBrace},
            {',', Token::Kind::Comma}, {';', Token::Kind::Semicolon},
            {'.', Token::Kind::Dot}, {'=', Token::Kind::Assign},
            {'<', Token::Kind::Lt}, {'>', Token::Kind::Gt},
            {'+', Token::Kind::Plus}, {'-', Token::Kind::Minus},
            {'*', Token::Kind::Star}, {'/', Token::Kind::Slash},
            {'!', Token::Kind::Bang}
        };
        auto it = singles.find(c);
        if (it == singles.end()) {
            throw ScriptError(std::string("caractere inesperado: '") + c + "'", line);
        }
        t.kind = it->second;
        t.text = std::string(1, c);
        tokens.push_back(t);
        ++i;
    }

    Token end;
    end.kind = Token::Kind::End;
    end.line = line;
    tokens.push_back(end);
    return tokens;
}

// ---------------------------------------------------------------------------
// AST
// ---------------------------------------------------------------------------

struct Expr;
struct Stmt;
using ExprPtr = std::unique_ptr<Expr>;
using StmtPtr = std::unique_ptr<Stmt>;

/** @brief Nó de expressão da AST. */
struct Expr {
    enum class Kind { Number, String, Bool, Ident, Unary, Binary, Call, Member };
    Kind kind = Kind::Number; ///< Tipo do nó.
    double number = 0.0; ///< Literal numérico.
    bool boolean = false; ///< Literal bool.
    std::string text; ///< Identificador, string, membro ou operador.
    ExprPtr a; ///< Operando esquerdo / alvo de chamada/membro.
    ExprPtr b; ///< Operando direito.
    std::vector<ExprPtr> args; ///< Argumentos de chamada.
    int line = 0; ///< Linha de origem.
};

/** @brief Nó de statement da AST. */
struct Stmt {
    enum class Kind { Decl, Assign, If, While, Block, Expr, Return, Break };
    Kind kind = Kind::Expr; ///< Tipo do nó.
    std::string name; ///< Nome (decl/assign).
    bool unlocked = false; ///< Declaração 'unlock' (editável no Inspector).
    ExprPtr expr; ///< Inicializador, valor, condição ou expressão.
    std::vector<StmtPtr> body; ///< Corpo (if/while/bloco).
    std::vector<StmtPtr> elseBody; ///< Corpo do else.
    int line = 0; ///< Linha de origem.
};

/** @brief Sinal de controle: break interrompe o while mais interno. */
struct BreakSignal {};

/** @brief Sinal de controle: return encerra o book com valor opcional. */
struct ReturnSignal { Value value; };

/** @brief Book da Velv: parâmetros nomeados + corpo. */
struct Book {
    std::vector<std::string> params; ///< Nomes dos parâmetros (viram locais).
    std::vector<StmtPtr> body; ///< Corpo do book.
};

/** @brief Ambiente léxico com encadeamento para o escopo global. */
struct Environment {
    std::unordered_map<std::string, Value> values; ///< Valores deste escopo.
    Environment* parent = nullptr; ///< Escopo pai.
    ScriptState* state = nullptr; ///< Estado dono (resolução de books/self).

    Value* find(const std::string& name) {
        auto it = values.find(name);
        if (it != values.end()) return &it->second;
        return parent ? parent->find(name) : nullptr;
    }
};

/** @brief Estado completo de um script carregado. */
struct ScriptState {
    std::unordered_map<std::string, Book> books; ///< Books por nome.
    Environment globals; ///< Escopo global.
    std::string path; ///< Caminho do arquivo de origem.
    bool broken = false; ///< true após erro de execução (script desativado).
    GameObject* ownerObject = nullptr; ///< Dono (resolve 'self'); nulo no script principal.
    const void* inspectorBase = nullptr; ///< Base dos campos unlock no Inspector.
    std::string inspectorOwner; ///< Rótulo do dono no Inspector.
};

// ---------------------------------------------------------------------------
// Parser
// ---------------------------------------------------------------------------

class Parser {
public:
    explicit Parser(std::vector<Token> tokens) : tokens_(std::move(tokens)) {}

    void parseProgram(ScriptState& state, std::vector<StmtPtr>& topLevel) {
        while (peek().kind != Token::Kind::End) {
            if (peek().kind == Token::Kind::KwBook) {
                advance();
                const Token name = expect(Token::Kind::Ident, "nome do book");
                expect(Token::Kind::LParen, "'('");
                Book book;
                if (peek().kind != Token::Kind::RParen) {
                    do {
                        book.params.push_back(expect(Token::Kind::Ident, "nome do parâmetro").text);
                    } while (match(Token::Kind::Comma));
                }
                expect(Token::Kind::RParen, "')'");
                book.body = parseBlock();
                state.books[name.text] = std::move(book);
            } else {
                topLevel.push_back(parseStmt());
            }
        }
    }

private:
    std::vector<Token> tokens_;
    size_t pos_ = 0;

    const Token& peek() const { return tokens_[pos_]; }
    const Token& advance() { return tokens_[pos_++]; }
    bool match(Token::Kind kind) {
        if (peek().kind == kind) { advance(); return true; }
        return false;
    }
    const Token& expect(Token::Kind kind, const char* what) {
        if (peek().kind != kind) {
            throw ScriptError(std::string("esperado ") + what + ", encontrado '" + peek().text + "'", peek().line);
        }
        return advance();
    }

    std::vector<StmtPtr> parseBlock() {
        expect(Token::Kind::LBrace, "'{'");
        std::vector<StmtPtr> body;
        while (peek().kind != Token::Kind::RBrace && peek().kind != Token::Kind::End) {
            body.push_back(parseStmt());
        }
        expect(Token::Kind::RBrace, "'}'");
        return body;
    }

    StmtPtr parseStmt() {
        const int line = peek().line;
        auto stmt = std::make_unique<Stmt>();
        stmt->line = line;

        if (peek().kind == Token::Kind::KwLock || peek().kind == Token::Kind::KwUnlock) {
            stmt->unlocked = peek().kind == Token::Kind::KwUnlock;
            advance();
            stmt->kind = Stmt::Kind::Decl;
            stmt->name = expect(Token::Kind::Ident, "nome da variável").text;
            if (match(Token::Kind::Assign)) {
                stmt->expr = parseExpr();
            }
            expect(Token::Kind::Semicolon, "';'");
            return stmt;
        }
        if (match(Token::Kind::KwIf)) {
            stmt->kind = Stmt::Kind::If;
            expect(Token::Kind::LParen, "'('");
            stmt->expr = parseExpr();
            expect(Token::Kind::RParen, "')'");
            stmt->body = parseBlock();
            if (match(Token::Kind::KwElse)) {
                if (peek().kind == Token::Kind::KwIf) {
                    stmt->elseBody.push_back(parseStmt());
                } else {
                    stmt->elseBody = parseBlock();
                }
            }
            return stmt;
        }
        if (match(Token::Kind::KwWhile)) {
            stmt->kind = Stmt::Kind::While;
            expect(Token::Kind::LParen, "'('");
            stmt->expr = parseExpr();
            expect(Token::Kind::RParen, "')'");
            stmt->body = parseBlock();
            return stmt;
        }
        if (match(Token::Kind::KwReturn)) {
            stmt->kind = Stmt::Kind::Return;
            if (peek().kind != Token::Kind::Semicolon) {
                stmt->expr = parseExpr();
            }
            expect(Token::Kind::Semicolon, "';'");
            return stmt;
        }
        if (match(Token::Kind::KwBreak)) {
            stmt->kind = Stmt::Kind::Break;
            expect(Token::Kind::Semicolon, "';'");
            return stmt;
        }
        if (peek().kind == Token::Kind::LBrace) {
            stmt->kind = Stmt::Kind::Block;
            stmt->body = parseBlock();
            return stmt;
        }
        if (peek().kind == Token::Kind::Ident && tokens_[pos_ + 1].kind == Token::Kind::Assign) {
            stmt->kind = Stmt::Kind::Assign;
            stmt->name = advance().text;
            advance(); // '='
            stmt->expr = parseExpr();
            expect(Token::Kind::Semicolon, "';'");
            return stmt;
        }

        stmt->kind = Stmt::Kind::Expr;
        stmt->expr = parseExpr();
        expect(Token::Kind::Semicolon, "';'");
        return stmt;
    }

    ExprPtr makeExpr(Expr::Kind kind, int line) {
        auto e = std::make_unique<Expr>();
        e->kind = kind;
        e->line = line;
        return e;
    }

    ExprPtr parseExpr() { return parseOr(); }

    ExprPtr parseOr() {
        ExprPtr left = parseAnd();
        while (peek().kind == Token::Kind::Or) {
            const int line = advance().line;
            auto e = makeExpr(Expr::Kind::Binary, line);
            e->text = "||";
            e->a = std::move(left);
            e->b = parseAnd();
            left = std::move(e);
        }
        return left;
    }

    ExprPtr parseAnd() {
        ExprPtr left = parseEquality();
        while (peek().kind == Token::Kind::And) {
            const int line = advance().line;
            auto e = makeExpr(Expr::Kind::Binary, line);
            e->text = "&&";
            e->a = std::move(left);
            e->b = parseEquality();
            left = std::move(e);
        }
        return left;
    }

    ExprPtr parseEquality() {
        ExprPtr left = parseComparison();
        while (peek().kind == Token::Kind::Eq || peek().kind == Token::Kind::Neq) {
            const Token op = advance();
            auto e = makeExpr(Expr::Kind::Binary, op.line);
            e->text = op.text;
            e->a = std::move(left);
            e->b = parseComparison();
            left = std::move(e);
        }
        return left;
    }

    ExprPtr parseComparison() {
        ExprPtr left = parseTerm();
        while (peek().kind == Token::Kind::Lt || peek().kind == Token::Kind::Gt ||
               peek().kind == Token::Kind::Le || peek().kind == Token::Kind::Ge) {
            const Token op = advance();
            auto e = makeExpr(Expr::Kind::Binary, op.line);
            e->text = op.text;
            e->a = std::move(left);
            e->b = parseTerm();
            left = std::move(e);
        }
        return left;
    }

    ExprPtr parseTerm() {
        ExprPtr left = parseFactor();
        while (peek().kind == Token::Kind::Plus || peek().kind == Token::Kind::Minus) {
            const Token op = advance();
            auto e = makeExpr(Expr::Kind::Binary, op.line);
            e->text = op.text;
            e->a = std::move(left);
            e->b = parseFactor();
            left = std::move(e);
        }
        return left;
    }

    ExprPtr parseFactor() {
        ExprPtr left = parseUnary();
        while (peek().kind == Token::Kind::Star || peek().kind == Token::Kind::Slash) {
            const Token op = advance();
            auto e = makeExpr(Expr::Kind::Binary, op.line);
            e->text = op.text;
            e->a = std::move(left);
            e->b = parseUnary();
            left = std::move(e);
        }
        return left;
    }

    ExprPtr parseUnary() {
        if (peek().kind == Token::Kind::Bang || peek().kind == Token::Kind::Minus) {
            const Token op = advance();
            auto e = makeExpr(Expr::Kind::Unary, op.line);
            e->text = op.text;
            e->a = parseUnary();
            return e;
        }
        return parsePostfix();
    }

    ExprPtr parsePostfix() {
        ExprPtr expr = parsePrimary();
        for (;;) {
            if (match(Token::Kind::Dot)) {
                const Token name = expect(Token::Kind::Ident, "nome de membro");
                auto e = makeExpr(Expr::Kind::Member, name.line);
                e->text = name.text;
                e->a = std::move(expr);
                expr = std::move(e);
            } else if (match(Token::Kind::LParen)) {
                auto e = makeExpr(Expr::Kind::Call, peek().line);
                e->a = std::move(expr);
                if (peek().kind != Token::Kind::RParen) {
                    e->args.push_back(parseExpr());
                    while (match(Token::Kind::Comma)) {
                        e->args.push_back(parseExpr());
                    }
                }
                expect(Token::Kind::RParen, "')'");
                expr = std::move(e);
            } else {
                break;
            }
        }
        return expr;
    }

    ExprPtr parsePrimary() {
        const Token t = peek();
        switch (t.kind) {
            case Token::Kind::Number: {
                advance();
                auto e = makeExpr(Expr::Kind::Number, t.line);
                e->number = t.number;
                return e;
            }
            case Token::Kind::String: {
                advance();
                auto e = makeExpr(Expr::Kind::String, t.line);
                e->text = t.text;
                return e;
            }
            case Token::Kind::KwTrue:
            case Token::Kind::KwFalse: {
                advance();
                auto e = makeExpr(Expr::Kind::Bool, t.line);
                e->boolean = t.kind == Token::Kind::KwTrue;
                return e;
            }
            case Token::Kind::Ident: {
                advance();
                auto e = makeExpr(Expr::Kind::Ident, t.line);
                e->text = t.text;
                return e;
            }
            case Token::Kind::LParen: {
                advance();
                ExprPtr inner = parseExpr();
                expect(Token::Kind::RParen, "')'");
                return inner;
            }
            default:
                throw ScriptError("expressão inesperada: '" + t.text + "'", t.line);
        }
    }
};

// ---------------------------------------------------------------------------
// Interpretador
// ---------------------------------------------------------------------------

static std::unique_ptr<ScriptState> g_state;
static MainFn* g_nativeMain = nullptr;
static InGameFn* g_nativeInGame = nullptr;
static int g_bookDepth = 0;
static std::unordered_map<std::string, VelvFunction> g_nativeFunctions;

static const std::unordered_map<std::string, int>& keyConstants() {
    static const std::unordered_map<std::string, int> keys = {
        {"Space", Key::Space}, {"Escape", Key::Escape}, {"Enter", Key::Enter},
        {"Tab", Key::Tab},
        {"E", Key::E}, {"R", Key::R}, {"W", Key::W}, {"A", Key::A},
        {"S", Key::S}, {"D", Key::D},
        {"Left", Key::Left}, {"Right", Key::Right}, {"Up", Key::Up}, {"Down", Key::Down},
        {"MouseLeft", Key::MouseLeft}, {"MouseRight", Key::MouseRight},
        {"LeftShift", Key::LeftShift}
    };
    return keys;
}

static bool isNativeNamespace(const std::string& name) {
    return name == "SceneManager" || name == "Input" || name == "Key" ||
           name == "Trigger" || name == "Inspector" || name == "Assets" ||
           name == "GameObject" || name == "UI" || name == "Physics" ||
           name == "Camera" || name == "Time" || name == "Application";
}

static Value eval(const Expr& e, Environment& env);
static void execStmt(const Stmt& s, Environment& env);
static void execBody(const std::vector<StmtPtr>& body, Environment& env);

static void requireArgs(const std::vector<Value>& args, size_t count, const std::string& what, int line) {
    if (args.size() < count) {
        throw ScriptError(what + ": argumentos insuficientes", line);
    }
}

static const std::string& argString(const std::vector<Value>& args, size_t i, const std::string& what, int line) {
    requireArgs(args, i + 1, what, line);
    if (args[i].type != Value::Type::String) {
        throw ScriptError(what + ": esperado string", line);
    }
    return args[i].text;
}

static double argNumber(const std::vector<Value>& args, size_t i, const std::string& what, int line) {
    requireArgs(args, i + 1, what, line);
    if (args[i].type != Value::Type::Number) {
        throw ScriptError(what + ": esperado número", line);
    }
    return args[i].number;
}

static bool argBool(const std::vector<Value>& args, size_t i, const std::string& what, int line) {
    requireArgs(args, i + 1, what, line);
    if (args[i].type == Value::Type::Bool) {
        return args[i].boolean;
    }
    if (args[i].type == Value::Type::Number) {
        return args[i].number != 0.0;
    }
    throw ScriptError(what + ": esperado bool", line);
}

static glm::vec3 argVec3(const std::vector<Value>& args, size_t i, const std::string& what, int line) {
    requireArgs(args, i + 1, what, line);
    if (args[i].type != Value::Type::Vec3) {
        throw ScriptError(what + ": esperado vec3", line);
    }
    return args[i].vec;
}

static GameObject* argObject(const std::vector<Value>& args, size_t i, const std::string& what, int line) {
    requireArgs(args, i + 1, what, line);
    if (args[i].type == Value::Type::Object) {
        return args[i].object;
    }
    if (args[i].type == Value::Type::String) {
        Scene* scene = Engine::getSceneManager().getActiveScene();
        return scene ? scene->findGameObject(args[i].text) : nullptr;
    }
    throw ScriptError(what + ": esperado GameObject ou nome", line);
}

/**
 * @brief Expõe uma variável unlock ao Inspector pelo endereço do payload.
 *
 * O Value vive no unordered_map do Environment (referências estáveis), então
 * o endereço do membro permanece válido e reflete escritas do interpretador;
 * o Inspector escreve diretamente no mesmo endereço (edição em tempo real).
 */
static void exposeUnlock(const std::string& ownerName, const std::string& varName,
                         Value& v, const void* base) {
    switch (v.type) {
        case Value::Type::Number:
            Inspector::expose(ownerName, varName, "double", &v.number, base);
            break;
        case Value::Type::Bool:
            Inspector::expose(ownerName, varName, "bool", &v.boolean, base);
            break;
        case Value::Type::Vec3:
            Inspector::expose(ownerName, varName, "glm::vec3", &v.vec, base);
            break;
        case Value::Type::String:
            Inspector::expose(ownerName, varName, "std::string", &v.text, base);
            break;
        default:
            break;
    }
}

static void callBookState(ScriptState* state, const std::string& name,
                          const std::vector<Value>* args = nullptr, Value* outResult = nullptr);

/** @brief Despacha chamadas aos namespaces nativos da engine. */
static Value callNative(const std::string& ns, const std::string& method,
                        std::vector<Value>& args, int line) {
    if (ns == "SceneManager") {
        SceneManager& sm = Engine::getSceneManager();
        if (method == "loadScene") {
            sm.loadScene(argString(args, 0, "SceneManager.loadScene", line));
            return Value::null();
        }
        if (method == "loadSceneAsync") {
            sm.loadSceneAsync(argString(args, 0, "SceneManager.loadSceneAsync", line));
            return Value::null();
        }
        if (method == "positionScene") {
            sm.positionScene(argVec3(args, 0, "SceneManager.positionScene", line));
            return Value::null();
        }
        if (method == "swapTo") {
            sm.swapTo(argString(args, 0, "SceneManager.swapTo", line));
            return Value::null();
        }
        if (method == "getActiveSceneName") {
            Scene* scene = sm.getActiveScene();
            return Value::ofString(scene ? scene->getName() : "");
        }
        if (method == "findGameObject") {
            Scene* scene = sm.getActiveScene();
            return Value::ofObject(scene ? scene->findGameObject(argString(args, 0, "SceneManager.findGameObject", line)) : nullptr);
        }
    } else if (ns == "Input") {
        if (method == "isKeyPressed") {
            return Value::ofBool(Input::isKeyPressed(static_cast<int>(argNumber(args, 0, "Input.isKeyPressed", line))));
        }
        if (method == "isKeyDown") {
            return Value::ofBool(Input::isKeyDown(static_cast<int>(argNumber(args, 0, "Input.isKeyDown", line))));
        }
        if (method == "getAxis") {
            return Value::ofNumber(InputManager::getAxis(argString(args, 0, "Input.getAxis", line)));
        }
        if (method == "isActionDown") {
            return Value::ofBool(InputManager::isActionDown(argString(args, 0, "Input.isActionDown", line)));
        }
        if (method == "isActionPressed") {
            return Value::ofBool(InputManager::isActionPressed(argString(args, 0, "Input.isActionPressed", line)));
        }
        if (method == "getMousePosition") {
            const glm::vec2 mouse = InputManager::getMousePosition();
            return Value::ofVec3(glm::vec3(mouse.x, mouse.y, 0.0f));
        }
        if (method == "getMouseDelta") {
            const glm::vec2 delta = InputManager::getMouseDelta();
            return Value::ofVec3(glm::vec3(delta.x, delta.y, 0.0f));
        }
        if (method == "setCursorCaptured") {
            InputManager::setCursorCaptured(argBool(args, 0, "Input.setCursorCaptured", line));
            return Value::null();
        }
        if (method == "isCursorCaptured") {
            return Value::ofBool(InputManager::isCursorCaptured());
        }
        if (method == "isKeyUp") {
            return Value::ofBool(Input::isKeyUp(static_cast<int>(argNumber(args, 0, "Input.isKeyUp", line))));
        }
    } else if (ns == "Trigger") {
        if (method == "bindKey") {
            const int key = static_cast<int>(argNumber(args, 0, "Trigger.bindKey", line));
            Trigger::bindKey(key, argString(args, 1, "Trigger.bindKey", line));
            return Value::null();
        }
        if (method == "fire") {
            Trigger::fire(argString(args, 0, "Trigger.fire", line));
            return Value::null();
        }
    } else if (ns == "Inspector") {
        if (method == "dump") {
            Inspector::dump();
            return Value::null();
        }
    } else if (ns == "Assets") {
        if (method == "resolve") {
            return Value::ofString(AssetBrowser::resolve(argString(args, 0, "Assets.resolve", line)).string());
        }
        if (method == "loadModel") {
            return Value::ofNumber(AssetBrowser::loadModel(argString(args, 0, "Assets.loadModel", line)));
        }
        if (method == "loadTexture") {
            return Value::ofNumber(AssetBrowser::loadTexture(argString(args, 0, "Assets.loadTexture", line)));
        }
    } else if (ns == "GameObject") {
        if (method == "find") {
            Scene* scene = Engine::getSceneManager().getActiveScene();
            return Value::ofObject(scene ? scene->findGameObject(argString(args, 0, "GameObject.find", line)) : nullptr);
        }
        if (method == "setPosition") {
            GameObject* object = argObject(args, 0, "GameObject.setPosition", line);
            if (object) object->transform.setPosition(argVec3(args, 1, "GameObject.setPosition", line));
            return Value::null();
        }
        if (method == "getPosition") {
            GameObject* object = argObject(args, 0, "GameObject.getPosition", line);
            return Value::ofVec3(object ? object->transform.position : glm::vec3(0.0f));
        }
        if (method == "setScale") {
            GameObject* object = argObject(args, 0, "GameObject.setScale", line);
            if (object) object->transform.setScale(argVec3(args, 1, "GameObject.setScale", line));
            return Value::null();
        }
        if (method == "setVelocity") {
            GameObject* object = argObject(args, 0, "GameObject.setVelocity", line);
            if (object) {
                if (auto* body = object->getComponent<RigidBody>()) {
                    body->velocity = argVec3(args, 1, "GameObject.setVelocity", line);
                }
            }
            return Value::null();
        }
        if (method == "getVelocity") {
            GameObject* object = argObject(args, 0, "GameObject.getVelocity", line);
            const auto* body = object ? object->getComponent<RigidBody>() : nullptr;
            return Value::ofVec3(body ? body->velocity : glm::vec3(0.0f));
        }
        if (method == "isGrounded") {
            GameObject* object = argObject(args, 0, "GameObject.isGrounded", line);
            const auto* body = object ? object->getComponent<RigidBody>() : nullptr;
            return Value::ofBool(body && body->grounded);
        }
        if (method == "getLayer") {
            GameObject* object = argObject(args, 0, "GameObject.getLayer", line);
            return Value::ofNumber(object ? object->layer : 0);
        }
        if (method == "setLayer") {
            GameObject* object = argObject(args, 0, "GameObject.setLayer", line);
            if (object) object->layer = static_cast<int>(argNumber(args, 1, "GameObject.setLayer", line));
            return Value::null();
        }
        if (method == "setUseGravity") {
            GameObject* object = argObject(args, 0, "GameObject.setUseGravity", line);
            if (object) {
                if (auto* body = object->getComponent<RigidBody>()) {
                    body->useGravity = args.size() > 1 && args[1].type == Value::Type::Bool ? args[1].boolean : true;
                }
            }
            return Value::null();
        }
        if (method == "setMass") {
            GameObject* object = argObject(args, 0, "GameObject.setMass", line);
            if (object) {
                if (auto* body = object->getComponent<RigidBody>()) {
                    body->mass = static_cast<float>(argNumber(args, 1, "GameObject.setMass", line));
                }
            }
            return Value::null();
        }
        if (method == "setKinematic") {
            GameObject* object = argObject(args, 0, "GameObject.setKinematic", line);
            if (object) {
                if (auto* body = object->getComponent<RigidBody>()) {
                    body->kinematic = args.size() > 1 && args[1].type == Value::Type::Bool ? args[1].boolean : false;
                }
            }
            return Value::null();
        }
        if (method == "setColor") {
            GameObject* object = argObject(args, 0, "GameObject.setColor", line);
            if (object) {
                if (auto* renderer = object->getComponent<MeshRenderer>()) {
                    renderer->color = argVec3(args, 1, "GameObject.setColor", line);
                }
            }
            return Value::null();
        }
        if (method == "getRotation") {
            GameObject* object = argObject(args, 0, "GameObject.getRotation", line);
            if (!object) return Value::ofVec3(glm::vec3(0.0f));
            return Value::ofVec3(glm::degrees(glm::eulerAngles(object->transform.rotation)));
        }
        if (method == "setRotation") {
            GameObject* object = argObject(args, 0, "GameObject.setRotation", line);
            if (object) object->transform.setRotation(glm::quat(glm::radians(argVec3(args, 1, "GameObject.setRotation", line))));
            return Value::null();
        }
        if (method == "getTag") {
            GameObject* object = argObject(args, 0, "GameObject.getTag", line);
            return Value::ofString(object ? object->tag : "");
        }
        if (method == "setTag") {
            GameObject* object = argObject(args, 0, "GameObject.setTag", line);
            if (object) object->tag = argString(args, 1, "GameObject.setTag", line);
            return Value::null();
        }
        if (method == "setName") {
            GameObject* object = argObject(args, 0, "GameObject.setName", line);
            if (object) object->name = argString(args, 1, "GameObject.setName", line);
            return Value::null();
        }
        if (method == "setParent") {
            GameObject* object = argObject(args, 0, "GameObject.setParent", line);
            GameObject* parentObj = argObject(args, 1, "GameObject.setParent", line);
            if (object) object->setParent(parentObj);
            return Value::null();
        }
        if (method == "addComponent") {
            GameObject* object = argObject(args, 0, "GameObject.addComponent", line);
            if (object) object->addComponentByName(argString(args, 1, "GameObject.addComponent", line));
            return Value::null();
        }
        if (method == "removeComponent") {
            GameObject* object = argObject(args, 0, "GameObject.removeComponent", line);
            if (object) {
                if (auto* comp = object->getComponentByName(argString(args, 1, "GameObject.removeComponent", line))) {
                    object->removeComponent(comp);
                }
            }
            return Value::null();
        }
        if (method == "addScript") {
            GameObject* object = argObject(args, 0, "GameObject.addScript", line);
            if (object) object->addScript(argString(args, 1, "GameObject.addScript", line));
            return Value::null();
        }
        if (method == "create") {
            const std::string name = argString(args, 0, "GameObject.create", line);
            Scene* scene = Engine::getSceneManager().getActiveScene();
            if (!scene) return Value::ofObject(nullptr);
            auto obj = std::make_unique<GameObject>(name);
            return Value::ofObject(scene->addObject(std::move(obj)));
        }
    } else if (ns == "UI") {
        if (method == "loadPrefab") {
            if (auto prefab = UIManager::loadUIPrefab(argString(args, 0, "UI.loadPrefab", line))) {
                UIManager::addUI(std::move(prefab));
            }
            return Value::null();
        }
        if (method == "setText") {
            UIElement* element = UIManager::findByName(argString(args, 0, "UI.setText", line));
            if (auto* text = dynamic_cast<UIText*>(element)) {
                text->setText(argString(args, 1, "UI.setText", line));
            }
            return Value::null();
        }
        if (method == "setVisible") {
            UIElement* element = UIManager::findByName(argString(args, 0, "UI.setVisible", line));
            if (element) {
                element->isVisible = args.size() > 1 && args[1].type == Value::Type::Bool ? args[1].boolean : true;
            }
            return Value::null();
        }
    } else if (ns == "Time") {
        if (method == "setScale") {
            Engine::setTimeScale(static_cast<float>(argNumber(args, 0, "Time.setScale", line)));
            return Value::null();
        }
        if (method == "getScale") {
            return Value::ofNumber(Engine::getTimeScale());
        }
    } else if (ns == "Application") {
        if (method == "quit") {
            if (GLFWwindow* w = Engine::getWindow()) glfwSetWindowShouldClose(w, GLFW_TRUE);
            return Value::null();
        }
    } else if (ns == "Physics") {
        if (method == "overlaps") {
            return Value::ofBool(CollisionSystem::overlaps(argString(args, 0, "Physics.overlaps", line),
                                                           argString(args, 1, "Physics.overlaps", line)));
        }
        if (method == "raycast") {
            const glm::vec3 origin = argVec3(args, 0, "Physics.raycast", line);
            const glm::vec3 dir = argVec3(args, 1, "Physics.raycast", line);
            const float maxDist = args.size() > 2 ? static_cast<float>(argNumber(args, 2, "Physics.raycast", line)) : 1000.0f;
            return Value::ofObject(CollisionSystem::raycast(origin, dir, maxDist));
        }
        if (method == "setGravity") {
            PhysicsWorld::setGravity(argVec3(args, 0, "Physics.setGravity", line));
            return Value::null();
        }
        if (method == "getGravity") {
            return Value::ofVec3(PhysicsWorld::getGravity());
        }
        if (method == "setLayerCollision") {
            const int a = static_cast<int>(argNumber(args, 0, "Physics.setLayerCollision", line));
            const int b = static_cast<int>(argNumber(args, 1, "Physics.setLayerCollision", line));
            const bool enabled = args.size() > 2 && args[2].type == Value::Type::Bool ? args[2].boolean : true;
            ProjectSettings& s = Engine::settings();
            s.ensureCollisionMatrix();
            const int n = static_cast<int>(s.collisionLayers.size());
            if (a >= 0 && b >= 0 && a < n && b < n) {
                s.collisionMatrix[a][b] = enabled;
                s.collisionMatrix[b][a] = enabled;
            }
            return Value::null();
        }
        if (method == "getLayerCollision") {
            const int a = static_cast<int>(argNumber(args, 0, "Physics.getLayerCollision", line));
            const int b = static_cast<int>(argNumber(args, 1, "Physics.getLayerCollision", line));
            return Value::ofBool(Engine::settings().layerCollides(a, b));
        }
    } else if (ns == "Camera") {
        Scene* scene = Engine::getSceneManager().getActiveScene();
        if (!scene) throw ScriptError("Camera: nenhuma cena ativa", line);
        Camera* cam = scene->getActiveCamera();
        GameObject* camObj = cam ? cam->owner() : nullptr;
        if (method == "setPosition") {
            const glm::vec3 p = argVec3(args, 0, "Camera.setPosition", line);
            if (camObj) camObj->transform.setPosition(p);
            else scene->cameraPosition = p;
            return Value::null();
        }
        if (method == "getPosition") {
            return Value::ofVec3(cam ? cam->worldPosition() : scene->cameraPosition);
        }
        if (method == "setTarget") {
            const glm::vec3 t = argVec3(args, 0, "Camera.setTarget", line);
            if (cam) cam->lookAt(t);
            else scene->cameraTarget = t;
            return Value::null();
        }
        if (method == "getTarget") {
            if (cam) return Value::ofVec3(cam->worldPosition() + cam->forward());
            return Value::ofVec3(scene->cameraTarget);
        }
        if (method == "getForward") {
            if (cam) return Value::ofVec3(cam->forward());
            return Value::ofVec3(glm::normalize(scene->cameraTarget - scene->cameraPosition));
        }
        if (method == "move") {
            const glm::vec3 delta = argVec3(args, 0, "Camera.move", line);
            if (camObj) camObj->transform.position += delta;
            else { scene->cameraPosition += delta; scene->cameraTarget += delta; }
            return Value::null();
        }
        if (method == "setRotation") {
            const glm::vec3 euler = argVec3(args, 0, "Camera.setRotation", line);
            if (camObj) camObj->transform.setRotation(glm::quat(glm::radians(euler)));
            return Value::null();
        }
        if (method == "setFOV") {
            if (cam) cam->fov = static_cast<float>(argNumber(args, 0, "Camera.setFOV", line));
            return Value::null();
        }
        if (method == "setOrthoHeight") {
            if (cam) cam->orthoHeight = static_cast<float>(argNumber(args, 0, "Camera.setOrthoHeight", line));
            else scene->orthoHeight = static_cast<float>(argNumber(args, 0, "Camera.setOrthoHeight", line));
            return Value::null();
        }
        if (method == "setProjection") {
            const std::string p = argString(args, 0, "Camera.setProjection", line);
            const bool ortho = p == "ortho" || p == "orthographic" || p == "ortho2d";
            if (cam) cam->projection = ortho ? Camera::Projection::Orthographic : Camera::Projection::Perspective;
            else scene->cameraMode = ortho ? Scene::CameraMode::Ortho2D : Scene::CameraMode::Perspective;
            return Value::null();
        }
        if (method == "attachTo") {
            GameObject* target = argObject(args, 0, "Camera.attachTo", line);
            if (camObj && target) camObj->setParent(target);
            return Value::null();
        }
        if (method == "setActive") {
            const std::string name = argString(args, 0, "Camera.setActive", line);
            for (const auto& obj : scene->gameObjects) {
                if (auto* c = obj->getComponent<Camera>()) {
                    c->active = obj->name == name;
                }
            }
            return Value::null();
        }
    }
    throw ScriptError(ns + "." + method + ": método nativo desconhecido", line);
}

/** @brief Funções globais da Velv Language. */
static Value callGlobal(const std::string& name, std::vector<Value>& args, int line,
                        Environment& env) {
    if (name == "print") {
        std::string out;
        for (size_t i = 0; i < args.size(); ++i) {
            if (i) out += ' ';
            out += args[i].toString();
        }
        Log::info("[velv] " + out);
        return Value::null();
    }
    if (name == "vec3") {
        const float x = args.size() > 0 ? static_cast<float>(argNumber(args, 0, "vec3", line)) : 0.0f;
        const float y = args.size() > 1 ? static_cast<float>(argNumber(args, 1, "vec3", line)) : 0.0f;
        const float z = args.size() > 2 ? static_cast<float>(argNumber(args, 2, "vec3", line)) : 0.0f;
        return Value::ofVec3({x, y, z});
    }
    if (name == "str") {
        requireArgs(args, 1, "str", line);
        return Value::ofString(args[0].toString());
    }
    if (name == "rand") {
        static std::mt19937 rng{std::random_device{}()};
        const double lo = argNumber(args, 0, "rand", line);
        const double hi = argNumber(args, 1, "rand", line);
        std::uniform_real_distribution<double> dist(lo, hi);
        return Value::ofNumber(dist(rng));
    }
    if (name == "dt") return Value::ofNumber(Engine::getDeltaTime());
    if (name == "time") return Value::ofNumber(Engine::getTime());
    if (name == "sin") return Value::ofNumber(std::sin(argNumber(args, 0, "sin", line)));
    if (name == "cos") return Value::ofNumber(std::cos(argNumber(args, 0, "cos", line)));
    if (name == "sqrt") return Value::ofNumber(std::sqrt(argNumber(args, 0, "sqrt", line)));
    if (name == "abs") return Value::ofNumber(std::abs(argNumber(args, 0, "abs", line)));
    if (name == "floor") return Value::ofNumber(std::floor(argNumber(args, 0, "floor", line)));
    if (name == "min") {
        return Value::ofNumber(std::min(argNumber(args, 0, "min", line),
                                        argNumber(args, 1, "min", line)));
    }
    if (name == "max") {
        return Value::ofNumber(std::max(argNumber(args, 0, "max", line),
                                        argNumber(args, 1, "max", line)));
    }
    if (name == "clamp") {
        const double v = argNumber(args, 0, "clamp", line);
        const double lo = argNumber(args, 1, "clamp", line);
        const double hi = argNumber(args, 2, "clamp", line);
        return Value::ofNumber(std::clamp(v, lo, hi));
    }
    if (name == "Instantiate") {
        const glm::vec3 pos = argVec3(args, 1, "Instantiate", line);
        const glm::vec3 scale = args.size() > 2 ? argVec3(args, 2, "Instantiate", line) : glm::vec3(1.0f);
        if (!args.empty() && args[0].type == Value::Type::Object) {
            return Value::ofObject(Instantiate(args[0].object, pos, scale));
        }
        const std::string& proto = argString(args, 0, "Instantiate", line);
        return Value::ofObject(Instantiate(proto, pos, scale));
    }
    if (name == "Destroy") {
        requireArgs(args, 1, "Destroy", line);
        if (args[0].type != Value::Type::Object) {
            throw ScriptError("Destroy: esperado GameObject", line);
        }
        Destroy(args[0].object);
        return Value::null();
    }
    ScriptState* state = env.state ? env.state : g_state.get();
    if (state && state->books.count(name)) {
        Value result = Value::null();
        callBookState(state, name, &args, &result);
        return result;
    }
    const auto native = g_nativeFunctions.find(name);
    if (native != g_nativeFunctions.end()) {
        native->second();
        return Value::null();
    }
    throw ScriptError("função desconhecida: " + name, line);
}

static bool valuesEqual(const Value& a, const Value& b) {
    if (a.type != b.type) return false;
    switch (a.type) {
        case Value::Type::Null: return true;
        case Value::Type::Bool: return a.boolean == b.boolean;
        case Value::Type::Number: return a.number == b.number;
        case Value::Type::String: return a.text == b.text;
        case Value::Type::Vec3: return a.vec == b.vec;
        case Value::Type::Object: return a.object == b.object;
        case Value::Type::Native: return a.text == b.text;
    }
    return false;
}

static Value evalBinary(const std::string& op, const Value& a, const Value& b, int line) {
    if (op == "==") return Value::ofBool(valuesEqual(a, b));
    if (op == "!=") return Value::ofBool(!valuesEqual(a, b));

    if (op == "+" && a.type == Value::Type::String && b.type == Value::Type::String) {
        return Value::ofString(a.text + b.text);
    }
    if (op == "+" && a.type == Value::Type::Vec3 && b.type == Value::Type::Vec3) {
        return Value::ofVec3(a.vec + b.vec);
    }
    if (op == "-" && a.type == Value::Type::Vec3 && b.type == Value::Type::Vec3) {
        return Value::ofVec3(a.vec - b.vec);
    }
    if (op == "*" && a.type == Value::Type::Vec3 && b.type == Value::Type::Number) {
        return Value::ofVec3(a.vec * static_cast<float>(b.number));
    }
    if (op == "*" && a.type == Value::Type::Number && b.type == Value::Type::Vec3) {
        return Value::ofVec3(b.vec * static_cast<float>(a.number));
    }

    if (a.type != Value::Type::Number || b.type != Value::Type::Number) {
        throw ScriptError("operador '" + op + "' requer números", line);
    }
    const double x = a.number;
    const double y = b.number;
    if (op == "+") return Value::ofNumber(x + y);
    if (op == "-") return Value::ofNumber(x - y);
    if (op == "*") return Value::ofNumber(x * y);
    if (op == "/") {
        if (y == 0.0) throw ScriptError("divisão por zero", line);
        return Value::ofNumber(x / y);
    }
    if (op == "<") return Value::ofBool(x < y);
    if (op == ">") return Value::ofBool(x > y);
    if (op == "<=") return Value::ofBool(x <= y);
    if (op == ">=") return Value::ofBool(x >= y);
    throw ScriptError("operador desconhecido: " + op, line);
}

static Value evalMember(const Value& base, const std::string& name, int line) {
    if (base.type == Value::Type::Vec3) {
        if (name == "x" || name == "r") return Value::ofNumber(base.vec.x);
        if (name == "y" || name == "g") return Value::ofNumber(base.vec.y);
        if (name == "z" || name == "b") return Value::ofNumber(base.vec.z);
    } else if (base.type == Value::Type::Native && base.text == "Key") {
        const auto& keys = keyConstants();
        auto it = keys.find(name);
        if (it != keys.end()) return Value::ofNumber(it->second);
        const KeyCode code = InputManager::keyFromString(name);
        if (code != KeyCode::Unknown) return Value::ofNumber(static_cast<int>(code));
    } else if (base.type == Value::Type::Object && base.object) {
        if (name == "name") return Value::ofString(base.object->name);
        if (name == "position") return Value::ofVec3(base.object->transform.position);
        if (name == "scale") return Value::ofVec3(base.object->transform.scale);
        if (name == "tag") return Value::ofString(base.object->tag);
        if (name == "rotation") return Value::ofVec3(glm::degrees(glm::eulerAngles(base.object->transform.rotation)));
    }
    throw ScriptError("membro desconhecido: ." + name, line);
}

static Value eval(const Expr& e, Environment& env) {
    switch (e.kind) {
        case Expr::Kind::Number: return Value::ofNumber(e.number);
        case Expr::Kind::String: return Value::ofString(e.text);
        case Expr::Kind::Bool: return Value::ofBool(e.boolean);
        case Expr::Kind::Ident: {
            if (Value* v = env.find(e.text)) return *v;
            if ((e.text == "self" || e.text == "this") && env.state && env.state->ownerObject) {
                return Value::ofObject(env.state->ownerObject);
            }
            if (isNativeNamespace(e.text)) return Value::ofNative(e.text);
            if (e.text == "time") return Value::ofNumber(Engine::getTime());
            if (e.text == "deltaTime") return Value::ofNumber(Engine::getDeltaTime());
            throw ScriptError("variável indefinida: " + e.text, e.line);
        }
        case Expr::Kind::Unary: {
            const Value v = eval(*e.a, env);
            if (e.text == "!") return Value::ofBool(!v.truthy());
            if (v.type != Value::Type::Number) throw ScriptError("'-' requer número", e.line);
            return Value::ofNumber(-v.number);
        }
        case Expr::Kind::Binary: {
            if (e.text == "&&") {
                const Value a = eval(*e.a, env);
                return a.truthy() ? eval(*e.b, env) : a;
            }
            if (e.text == "||") {
                const Value a = eval(*e.a, env);
                return a.truthy() ? a : eval(*e.b, env);
            }
            return evalBinary(e.text, eval(*e.a, env), eval(*e.b, env), e.line);
        }
        case Expr::Kind::Member: {
            return evalMember(eval(*e.a, env), e.text, e.line);
        }
        case Expr::Kind::Call: {
            std::vector<Value> args;
            args.reserve(e.args.size());
            for (const auto& a : e.args) args.push_back(eval(*a, env));

            if (e.a->kind == Expr::Kind::Member) {
                const Value base = eval(*e.a->a, env);
                if (base.type == Value::Type::Native) {
                    return callNative(base.text, e.a->text, args, e.line);
                }
                throw ScriptError("chamada inválida (membro não é função)", e.line);
            }
            if (e.a->kind == Expr::Kind::Ident) {
                return callGlobal(e.a->text, args, e.line, env);
            }
            throw ScriptError("expressão não é chamável", e.line);
        }
    }
    throw ScriptError("expressão inválida", e.line);
}

static void execBody(const std::vector<StmtPtr>& body, Environment& env) {
    for (const auto& s : body) {
        execStmt(*s, env);
    }
}

static void execStmt(const Stmt& s, Environment& env) {
    switch (s.kind) {
        case Stmt::Kind::Decl: {
            env.values[s.name] = s.expr ? eval(*s.expr, env) : Value::null();
            if (s.unlocked && env.parent == nullptr && env.state && env.state->inspectorBase) {
                exposeUnlock(env.state->inspectorOwner, s.name, env.values[s.name],
                             env.state->inspectorBase);
            }
            return;
        }
        case Stmt::Kind::Assign: {
            Value* target = env.find(s.name);
            if (!target) throw ScriptError("variável não declarada: " + s.name, s.line);
            *target = eval(*s.expr, env);
            return;
        }
        case Stmt::Kind::If: {
            if (eval(*s.expr, env).truthy()) {
                Environment child;
                child.parent = &env;
                child.state = env.state;
                execBody(s.body, child);
            } else if (!s.elseBody.empty()) {
                Environment child;
                child.parent = &env;
                child.state = env.state;
                execBody(s.elseBody, child);
            }
            return;
        }
        case Stmt::Kind::While: {
            int guard = 0;
            while (eval(*s.expr, env).truthy()) {
                if (++guard > 100000) throw ScriptError("while excedeu 100000 iterações", s.line);
                Environment child;
                child.parent = &env;
                child.state = env.state;
                try {
                    execBody(s.body, child);
                } catch (const BreakSignal&) {
                    break;
                }
            }
            return;
        }
        case Stmt::Kind::Return:
            throw ReturnSignal{ s.expr ? eval(*s.expr, env) : Value::null() };
        case Stmt::Kind::Break:
            throw BreakSignal{};
        case Stmt::Kind::Block: {
            Environment child;
            child.parent = &env;
            child.state = env.state;
            execBody(s.body, child);
            return;
        }
        case Stmt::Kind::Expr: {
            eval(*s.expr, env);
            return;
        }
    }
}

// ---------------------------------------------------------------------------
// API pública
// ---------------------------------------------------------------------------

void registerScript(MainFn mainFn, InGameFn inGameFn) {
    g_nativeMain = mainFn;
    g_nativeInGame = inGameFn;
}

void executeMain() {
    if (g_state && !g_state->broken) {
        ScriptEngine::callBook("Main");
    } else if (g_nativeMain) {
        g_nativeMain();
    }
}

void executeInGame() {
    if (g_state && !g_state->broken) {
        ScriptEngine::callBook("InGame");
    } else if (g_nativeInGame) {
        g_nativeInGame();
    }
}

bool ScriptEngine::loadFile(const std::filesystem::path& path) {
    std::filesystem::path resolved = path;
    if (resolved.is_relative()) {
        const std::filesystem::path projectRel = FS::resolveProject(path.string());
        const std::filesystem::path exeRel = Engine::getExecutableDir() / path;
        const std::filesystem::path cwdRel = std::filesystem::current_path() / path;
        if (FS::exists(projectRel)) {
            resolved = projectRel;
        } else if (std::filesystem::exists(exeRel)) {
            resolved = exeRel;
        } else if (std::filesystem::exists(cwdRel)) {
            resolved = cwdRel;
        } else {
            resolved = projectRel;
        }
    }

    const std::string source = FS::readText(resolved);
    if (source.empty()) {
        Log::warn("ScriptEngine: não foi possível abrir " + resolved.string());
        return false;
    }

    try {
        auto state = std::make_unique<ScriptState>();
        state->path = resolved.string();
        state->globals.state = state.get();
        state->inspectorBase = state.get();
        state->inspectorOwner = "Script";

        std::vector<Token> tokens = tokenize(source);
        Parser parser(std::move(tokens));
        std::vector<StmtPtr> topLevel;
        parser.parseProgram(*state, topLevel);

        // Statements de topo (declarações globais) executam no carregamento.
        try {
            for (const auto& stmt : topLevel) {
                execStmt(*stmt, state->globals);
            }
        } catch (const ReturnSignal&) {
        } catch (const BreakSignal&) {
            Log::warn("ScriptEngine: 'break' fora de loop em " + resolved.string());
        }

        if (!state->books.count("Main")) {
            Log::warn("ScriptEngine: script sem book Main()");
        }
        if (!state->books.count("InGame")) {
            Log::warn("ScriptEngine: script sem book InGame()");
        }

        Log::info("ScriptEngine: '" + resolved.string() + "' carregado (" +
                  std::to_string(state->books.size()) + " books)");
        if (g_state) {
            Inspector::removeFields(g_state.get());
        }
        g_state = std::move(state);
        return true;
    } catch (const ScriptError& e) {
        Log::error(std::format("ScriptEngine: erro de parse em {}:{} — {}",
                               resolved.string(), e.line, e.what()));
        return false;
    } catch (const std::exception& e) {
        Log::error(std::string("ScriptEngine: falha ao carregar script: ") + e.what());
        return false;
    }
}

static void callBookState(ScriptState* state, const std::string& name,
                          const std::vector<Value>* args, Value* outResult) {
    if (!state || state->broken) {
        return;
    }
    auto it = state->books.find(name);
    if (it == state->books.end()) {
        return;
    }
    if (g_bookDepth >= 64) {
        Log::error("ScriptEngine: profundidade máxima de books excedida em '" + name + "'");
        state->broken = true;
        return;
    }

    ++g_bookDepth;
    try {
        Environment local;
        local.parent = &state->globals;
        local.state = state;
        const Book& book = it->second;
        const size_t argc = args ? args->size() : 0;
        if (argc != book.params.size()) {
            throw ScriptError("book '" + name + "' espera " + std::to_string(book.params.size()) +
                              " argumento(s), recebeu " + std::to_string(argc), 0);
        }
        for (size_t i = 0; i < book.params.size(); ++i) {
            local.values[book.params[i]] = (*args)[i];
        }
        execBody(book.body, local);
    } catch (const ReturnSignal& r) {
        if (outResult) *outResult = r.value;
    } catch (const ScriptError& e) {
        Log::error(std::format("ScriptEngine: erro em {}:{} (book {}) — {}",
                               state->path, e.line, name, e.what()));
        state->broken = true;
    } catch (const std::exception& e) {
        Log::error("ScriptEngine: erro no book '" + name + "': " + e.what());
        state->broken = true;
    }
    --g_bookDepth;
}

void ScriptEngine::callBook(const std::string& name) {
    callBookState(g_state.get(), name);
}

bool ScriptEngine::hasScript() {
    return g_state != nullptr;
}

std::vector<std::string> ScriptEngine::bookNames() {
    std::vector<std::string> names;
    if (g_state) {
        for (const auto& entry : g_state->books) {
            names.push_back(entry.first);
        }
        std::sort(names.begin(), names.end());
    }
    return names;
}

bool ScriptEngine::loadScript(const std::string& filePath) {
    return loadFile(filePath);
}

void ScriptEngine::registerNativeFunction(const std::string& name, VelvFunction func) {
    g_nativeFunctions[name] = std::move(func);
}

void ScriptEngine::executeMain() {
    VelvX::executeMain();
}

void ScriptEngine::executeInGame() {
    VelvX::executeInGame();
}

void ScriptEngine::interpretVelvCode(const std::string& code) {
    try {
        if (!g_state) {
            g_state = std::make_unique<ScriptState>();
            g_state->path = "<inline>";
            g_state->globals.state = g_state.get();
            g_state->inspectorBase = g_state.get();
            g_state->inspectorOwner = "Script";
        }
        std::vector<Token> tokens = tokenize(code);
        Parser parser(std::move(tokens));
        std::vector<StmtPtr> topLevel;
        parser.parseProgram(*g_state, topLevel);
        for (const auto& stmt : topLevel) {
            execStmt(*stmt, g_state->globals);
        }
        g_state->broken = false;
    } catch (const ScriptError& e) {
        Log::error(std::format("ScriptEngine: erro de parse em {}:{} — {}",
                               g_state ? g_state->path : "<inline>", e.line, e.what()));
        if (g_state) {
            g_state->broken = true;
        }
    } catch (const std::exception& e) {
        Log::error(std::string("ScriptEngine: falha ao interpretar código: ") + e.what());
    }
}

void ScriptEngine::shutdown() {
    if (g_state) {
        Inspector::removeFields(g_state.get());
    }
    g_state.reset();
    g_nativeFunctions.clear();
}

// ---------------------------------------------------------------------------
// Scripts por instância (ScriptComponent)
// ---------------------------------------------------------------------------

ScriptState* ScriptEngine::createState() {
    auto state = std::make_unique<ScriptState>();
    state->globals.state = state.get();
    return state.release();
}

void ScriptEngine::destroyState(ScriptState* state) {
    if (!state) {
        return;
    }
    if (state->inspectorBase) {
        Inspector::removeFields(state->inspectorBase);
    }
    delete state;
}

bool ScriptEngine::loadStateFile(ScriptState* state, const std::filesystem::path& path,
                                 GameObject* owner, const void* inspectorBase,
                                 const std::string& ownerName) {
    if (!state) {
        return false;
    }
    std::filesystem::path resolved = path;
    if (resolved.is_relative()) {
        const std::filesystem::path projectRel = FS::resolveProject(path.string());
        const std::filesystem::path exeRel = Engine::getExecutableDir() / path;
        const std::filesystem::path cwdRel = std::filesystem::current_path() / path;
        if (FS::exists(projectRel)) {
            resolved = projectRel;
        } else if (std::filesystem::exists(exeRel)) {
            resolved = exeRel;
        } else if (std::filesystem::exists(cwdRel)) {
            resolved = cwdRel;
        } else {
            resolved = projectRel;
        }
    }

    const std::string source = FS::readText(resolved);
    if (source.empty()) {
        Log::warn("ScriptEngine: não foi possível abrir " + resolved.string());
        return false;
    }

    if (state->inspectorBase) {
        Inspector::removeFields(state->inspectorBase);
    }
    state->books.clear();
    state->globals.values.clear();
    state->broken = false;
    state->path = resolved.string();
    state->ownerObject = owner;
    state->inspectorBase = inspectorBase;
    state->inspectorOwner = ownerName;
    state->globals.state = state;

    try {
        std::vector<Token> tokens = tokenize(source);
        Parser parser(std::move(tokens));
        std::vector<StmtPtr> topLevel;
        parser.parseProgram(*state, topLevel);
        try {
            for (const auto& stmt : topLevel) {
                execStmt(*stmt, state->globals);
            }
        } catch (const ReturnSignal&) {
        } catch (const BreakSignal&) {
            Log::warn("ScriptEngine: 'break' fora de loop em " + resolved.string());
        }
        Log::info("ScriptEngine: '" + resolved.string() + "' anexado a '" + ownerName +
                  "' (" + std::to_string(state->books.size()) + " books)");
        return true;
    } catch (const ScriptError& e) {
        Log::error(std::format("ScriptEngine: erro de parse em {}:{} — {}",
                               resolved.string(), e.line, e.what()));
        state->broken = true;
        return false;
    } catch (const std::exception& e) {
        Log::error(std::string("ScriptEngine: falha ao carregar script: ") + e.what());
        state->broken = true;
        return false;
    }
}

void ScriptEngine::callStateBook(ScriptState* state, const std::string& name) {
    callBookState(state, name);
}

bool ScriptEngine::stateHasBook(ScriptState* state, const std::string& name) {
    return state && state->books.count(name) > 0;
}

const void* ScriptEngine::variableBase() {
    return g_state.get();
}

} // namespace VelvX