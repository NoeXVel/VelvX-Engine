#pragma once
/**
 * @file InputManager.hpp
 * @brief Entrada unificada: teclado, mouse e gamepad (Xbox/PlayStation) com
 *        perfis de bindings configuráveis nos Project Settings.
 */

#include <GLFW/glfw3.h>
#include <glm/glm.hpp>
#include <array>
#include <filesystem>
#include <string>
#include <vector>

namespace VelvX {

/**
 * @brief Códigos de entrada nomeados (teclado, mouse e gamepad).
 *
 * Os valores de teclado coincidem com os códigos GLFW, permitindo interoperar
 * com a API legada @c Input/Key. Mouse e gamepad usam faixas próprias.
 */
enum class KeyCode : int {
    Unknown = -1,
    Space = GLFW_KEY_SPACE,
    Enter = GLFW_KEY_ENTER,
    Escape = GLFW_KEY_ESCAPE,
    Backspace = GLFW_KEY_BACKSPACE,
    Tab = GLFW_KEY_TAB,
    Delete = GLFW_KEY_DELETE,
    Insert = GLFW_KEY_INSERT,
    Home = GLFW_KEY_HOME,
    End = GLFW_KEY_END,
    PageUp = GLFW_KEY_PAGE_UP,
    PageDown = GLFW_KEY_PAGE_DOWN,
    Left = GLFW_KEY_LEFT,
    Right = GLFW_KEY_RIGHT,
    Up = GLFW_KEY_UP,
    Down = GLFW_KEY_DOWN,
    A = GLFW_KEY_A, B = GLFW_KEY_B, C = GLFW_KEY_C, D = GLFW_KEY_D,
    E = GLFW_KEY_E, F = GLFW_KEY_F, G = GLFW_KEY_G, H = GLFW_KEY_H,
    I = GLFW_KEY_I, J = GLFW_KEY_J, K = GLFW_KEY_K, L = GLFW_KEY_L,
    M = GLFW_KEY_M, N = GLFW_KEY_N, O = GLFW_KEY_O, P = GLFW_KEY_P,
    Q = GLFW_KEY_Q, R = GLFW_KEY_R, S = GLFW_KEY_S, T = GLFW_KEY_T,
    U = GLFW_KEY_U, V = GLFW_KEY_V, W = GLFW_KEY_W, X = GLFW_KEY_X,
    Y = GLFW_KEY_Y, Z = GLFW_KEY_Z,
    Num0 = GLFW_KEY_0, Num1 = GLFW_KEY_1, Num2 = GLFW_KEY_2, Num3 = GLFW_KEY_3,
    Num4 = GLFW_KEY_4, Num5 = GLFW_KEY_5, Num6 = GLFW_KEY_6, Num7 = GLFW_KEY_7,
    Num8 = GLFW_KEY_8, Num9 = GLFW_KEY_9,
    F1 = GLFW_KEY_F1, F2 = GLFW_KEY_F2, F3 = GLFW_KEY_F3, F4 = GLFW_KEY_F4,
    F5 = GLFW_KEY_F5, F6 = GLFW_KEY_F6, F7 = GLFW_KEY_F7, F8 = GLFW_KEY_F8,
    F9 = GLFW_KEY_F9, F10 = GLFW_KEY_F10, F11 = GLFW_KEY_F11, F12 = GLFW_KEY_F12,
    LeftShift = GLFW_KEY_LEFT_SHIFT,
    RightShift = GLFW_KEY_RIGHT_SHIFT,
    LeftControl = GLFW_KEY_LEFT_CONTROL,
    RightControl = GLFW_KEY_RIGHT_CONTROL,
    LeftAlt = GLFW_KEY_LEFT_ALT,
    RightAlt = GLFW_KEY_RIGHT_ALT,

    MouseLeft = 1000, ///< Botão esquerdo do mouse.
    MouseRight = 1001, ///< Botão direito do mouse.
    MouseMiddle = 1002, ///< Botão do meio do mouse.
    Mouse4 = 1003, ///< Quarto botão do mouse.
    Mouse5 = 1004, ///< Quinto botão do mouse.

    GamepadA = 2000, ///< Botão A (Xbox) / Cross (PlayStation).
    GamepadB = 2001, ///< Botão B (Xbox) / Circle (PlayStation).
    GamepadX = 2002, ///< Botão X (Xbox) / Square (PlayStation).
    GamepadY = 2003, ///< Botão Y (Xbox) / Triangle (PlayStation).
    GamepadLeftBumper = 2004, ///< LB / L1.
    GamepadRightBumper = 2005, ///< RB / R1.
    GamepadBack = 2006, ///< Back / Share.
    GamepadStart = 2007, ///< Start / Options.
    GamepadGuide = 2008, ///< Guide / PS.
    GamepadLeftThumb = 2009, ///< Clique do analógico esquerdo.
    GamepadRightThumb = 2010, ///< Clique do analógico direito.
    GamepadDPadUp = 2011, ///< D-pad cima.
    GamepadDPadRight = 2012, ///< D-pad direita.
    GamepadDPadDown = 2013, ///< D-pad baixo.
    GamepadDPadLeft = 2014, ///< D-pad esquerda.

    GamepadLStickX = 2100, ///< Eixo X do analógico esquerdo (-1..1).
    GamepadLStickY = 2101, ///< Eixo Y do analógico esquerdo (-1..1).
    GamepadRStickX = 2102, ///< Eixo X do analógico direito (-1..1).
    GamepadRStickY = 2103, ///< Eixo Y do analógico direito (-1..1).
    GamepadLTrigger = 2104, ///< Gatilho esquerdo (0..1).
    GamepadRTrigger = 2105 ///< Gatilho direito (0..1).
};

/**
 * @brief Mapeamento de uma ação para teclas/botões (primário + alternativo).
 */
struct InputBinding {
    std::string actionName; ///< Nome da ação (ex.: "Jump").
    KeyCode primaryKey = KeyCode::Unknown; ///< Entrada principal.
    KeyCode secondaryKey = KeyCode::Unknown; ///< Entrada alternativa (acessibilidade).
};

/**
 * @brief Eixo nomeado composto por uma entrada positiva e uma negativa.
 */
struct AxisBinding {
    std::string name; ///< Nome do eixo (ex.: "Horizontal").
    KeyCode positive = KeyCode::Unknown; ///< Entrada que contribui +1.
    KeyCode negative = KeyCode::Unknown; ///< Entrada que contribui -1.
};

/**
 * @brief Perfil completo de input (jogador/acessibilidade).
 */
struct InputProfile {
    std::string name = "Padrao"; ///< Nome do perfil.
    std::vector<InputBinding> bindings; ///< Ações do perfil.
    std::vector<AxisBinding> axes; ///< Eixos do perfil.
};

/**
 * @brief Gerenciador global de entrada com perfis e bindings persistíveis.
 */
class InputManager {
public:
    /** @brief Inicializa o estado e registra a janela GLFW alvo. */
    static void init(GLFWwindow* window);

    /** @brief Libera recursos (nenhum no momento). */
    static void shutdown();

    /** @brief Amostra teclado/mouse/gamepad (após glfwPollEvents). */
    static void update();

    /** @brief true no frame em que @p key desceu. */
    static bool isKeyPressed(KeyCode key);

    /** @brief true enquanto @p key estiver pressionada. */
    static bool isKeyDown(KeyCode key);

    /** @brief true no frame em que @p key foi solta. */
    static bool isKeyUp(KeyCode key);

    /** @brief true enquanto a ação estiver ativa (qualquer binding). */
    static bool isActionDown(const std::string& actionName);

    /** @brief true no frame em que a ação foi ativada. */
    static bool isActionPressed(const std::string& actionName);

    /** @brief Posição do mouse em pixels (origem superior-esquerda). */
    static glm::vec2 getMousePosition();

    /** @brief Deslocamento do mouse desde o frame anterior. */
    static glm::vec2 getMouseDelta();

    /** @brief Captura e oculta o cursor (modo FPS); false restaura o cursor. */
    static void setCursorCaptured(bool captured);

    /** @brief true se o cursor esta capturado/oculto. */
    static bool isCursorCaptured();

    /**
     * @brief Valor de um eixo nomeado (-1..1).
     *
     * Eixos built-in: "Horizontal" (A/D, setas, analógico esquerdo) e
     * "Vertical" (W/S, setas, analógico esquerdo). Eixos customizados vêm
     * dos AxisBinding do perfil ativo.
     */
    static float getAxis(const std::string& axisName);

    /** @brief Valor analógico de um eixo/gatilho de gamepad. */
    static float getAxisValue(KeyCode axis);

    /** @brief Registra uma ação no perfil ativo. */
    static void addBinding(const std::string& actionName, KeyCode primary,
                           KeyCode secondary = KeyCode::Unknown);

    /** @brief Registra um eixo customizado no perfil ativo. */
    static void addAxis(const std::string& axisName, KeyCode positive, KeyCode negative);

    /** @brief Bindings do perfil ativo. */
    static const std::vector<InputBinding>& bindings();

    /** @brief Perfis configurados. */
    static const std::vector<InputProfile>& profiles();

    /** @brief Substitui todos os perfis. */
    static void setProfiles(std::vector<InputProfile> profiles);

    /** @brief Índice do perfil ativo. */
    static int activeProfile();

    /** @brief Ativa um perfil pelo índice (ignora índices inválidos). */
    static void setActiveProfile(int index);

    /** @brief Carrega perfis/bindings de um arquivo JSON. */
    static void loadBindingsFromFile(const std::string& filePath);

    /** @brief Salva perfis/bindings em um arquivo JSON. */
    static void saveBindingsToFile(const std::string& filePath);

    /** @brief Zona morta dos analógicos (0..0.9). */
    static void setGamepadDeadzone(float deadzone);

    /** @brief true se um gamepad está conectado. */
    static bool isGamepadConnected();

    /** @brief Rótulo do gamepad: "Xbox", "PlayStation" ou "Generico". */
    static std::string gamepadLabel();

    /** @brief Nome serializável de um KeyCode. */
    static std::string keyToString(KeyCode key);

    /** @brief KeyCode a partir do nome (Unknown se desconhecido). */
    static KeyCode keyFromString(const std::string& name);

private:
    static constexpr int kMaxCode = 2200; ///< Tamanho das tabelas de estado.

    static GLFWwindow* window_; ///< Janela amostrada.
    static std::array<bool, kMaxCode> current_; ///< Estado deste frame.
    static std::array<bool, kMaxCode> previous_; ///< Estado do frame anterior.
    static std::array<float, 6> gamepadAxes_; ///< Eixos analógicos GLFW.
    static glm::vec2 mousePosition_; ///< Posição atual do mouse.
    static glm::vec2 mouseDelta_; ///< Delta do mouse.
    static bool firstMouseSample_; ///< Primeiro frame sem delta.
    static float deadzone_; ///< Zona morta dos analógicos.
    static std::vector<InputProfile> profiles_; ///< Perfis de input.
    static int activeProfile_; ///< Perfil em uso.

    /** @brief Estado bruto de um código (teclado/mouse/gamepad). */
    static bool sampleKey(KeyCode key);
};

} // namespace VelvX