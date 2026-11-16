#pragma once
/**
 * @file ScriptEngine.hpp
 * @brief ScriptEngine da Velv Language (arquivos .velv) e hooks Main/InGame.
 */

#include <filesystem>
#include <functional>
#include <string>
#include <vector>

namespace VelvX {

class GameObject;
struct ScriptState;

/** @brief Assinatura do entry-point executado uma vez no início. */
using MainFn = void();
/** @brief Assinatura do entry-point executado a cada frame. */
using InGameFn = void();
/** @brief Função nativa registrável, chamável de scripts VelvX. */
using VelvFunction = std::function<void()>;

/**
 * @brief Registra callbacks nativos de Main/InGame (fallback sem script).
 * @param mainFn Executado uma vez antes do loop.
 * @param inGameFn Executado a cada frame dentro do loop.
 */
void registerScript(MainFn mainFn, InGameFn inGameFn);

/** @brief Executa o Main registrado (script .velv ou callback nativo). */
void executeMain();

/** @brief Executa o InGame registrado (script .velv ou callback nativo). */
void executeInGame();

/**
 * @brief Interpretador da Velv Language.
 *
 * Um script .velv contém books (funções) — obrigatoriamente @c book Main()
 * (executado uma vez no início) e @c book InGame() (executado a cada frame) —
 * além de variáveis globais, if/else, while, e chamadas nativas como
 * @c SceneManager.loadSceneAsync(...), @c Instantiate(...), @c print(...).
 *
 * Declarações de variáveis usam @c lock (privada, fora do Inspector) ou
 * @c unlock (editável no Inspector quando o script está anexado a um objeto).
 * A palavra-chave @c let NÃO faz parte da linguagem.
 */
class ScriptEngine {
public:
    /**
     * @brief Carrega e interpreta um arquivo .velv, registrando seus books.
     * @param path Caminho do script (absoluto ou relativo ao projeto).
     * @return true se o script foi carregado e compilado sem erros.
     */
    static bool loadFile(const std::filesystem::path& path);

    /** @brief Alias de loadFile (API do spec). */
    static bool loadScript(const std::string& filePath);

    /**
     * @brief Registra uma função C++ chamável pelo nome a partir de scripts.
     * @param Nome global da função (ex.: "MinhaFuncao").
     */
    static void registerNativeFunction(const std::string& name, VelvFunction func);

    /** @brief Executa o book Main() do script carregado. */
    static void executeMain();

    /** @brief Executa o book InGame() do script carregado. */
    static void executeInGame();

    /**
     * @brief Interpreta código VelvX diretamente (sem arquivo).
     *
     * Books definidos no código ficam disponíveis; statements de topo são
     * executados imediatamente. Usado pelo Script IDE e por testes.
     */
    static void interpretVelvCode(const std::string& code);

    /** @brief Executa um book nomeado (usado pelos triggers e pela UI). */
    static void callBook(const std::string& name);

    /** @brief true se há um script .velv ativo. */
    static bool hasScript();

    /** @brief Nomes dos books carregados (autocomplete do Script IDE). */
    static std::vector<std::string> bookNames();

    /** @brief Descarta o script e todo o estado do interpretador. */
    static void shutdown();

    // --- Scripts por instância (ScriptComponent / múltiplos scripts) ---

    /** @brief Cria um estado de script independente (ownership do chamador). */
    static ScriptState* createState();

    /** @brief Destrói um estado criado por createState (remove campos unlock). */
    static void destroyState(ScriptState* state);

    /**
     * @brief Carrega um arquivo .velv num estado independente.
     * @param state Estado alvo (createState).
     * @param path Caminho do script.
     * @param owner GameObject dono (resolve @c self); pode ser nulo.
     * @param inspectorBase Base dos campos unlock no Inspector (ex.: componente).
     * @param ownerName Rótulo do dono no Inspector.
     * @return true se carregado sem erros.
     */
    static bool loadStateFile(ScriptState* state, const std::filesystem::path& path,
                              GameObject* owner, const void* inspectorBase,
                              const std::string& ownerName);

    /** @brief Executa um book de um estado independente. */
    static void callStateBook(ScriptState* state, const std::string& name);

    /** @brief true se o estado independente define o book. */
    static bool stateHasBook(ScriptState* state, const std::string& name);

    /** @brief Base dos campos unlock do script principal (para o Inspector). */
    static const void* variableBase();
};

} // namespace VelvX