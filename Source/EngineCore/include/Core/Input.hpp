#pragma once
/**
 * @file Input.hpp
 * @brief API legada de teclado: Input.isKeyPressed(Key::Space).
 *
 * A implementação real vive em @c InputManager (teclado + mouse + gamepad);
 * esta fachada mantém compatibilidade com scripts e código da fase 1.
 */

#include "Input/InputManager.hpp"

namespace VelvX {

/**
 * @brief Códigos de tecla nomeados (mapeados para os códigos GLFW).
 */
struct Key {
    static constexpr int Space = GLFW_KEY_SPACE; ///< Barra de espaço.
    static constexpr int Escape = GLFW_KEY_ESCAPE; ///< Escape.
    static constexpr int Enter = GLFW_KEY_ENTER; ///< Enter.
    static constexpr int Tab = GLFW_KEY_TAB; ///< Tab.
    static constexpr int E = GLFW_KEY_E; ///< Tecla E.
    static constexpr int R = GLFW_KEY_R; ///< Tecla R.
    static constexpr int W = GLFW_KEY_W; ///< Tecla W.
    static constexpr int A = GLFW_KEY_A; ///< Tecla A.
    static constexpr int S = GLFW_KEY_S; ///< Tecla S.
    static constexpr int D = GLFW_KEY_D; ///< Tecla D.
    static constexpr int Q = GLFW_KEY_Q; ///< Tecla Q.
    static constexpr int F = GLFW_KEY_F; ///< Tecla F.
    static constexpr int Left = GLFW_KEY_LEFT; ///< Seta esquerda.
    static constexpr int Right = GLFW_KEY_RIGHT; ///< Seta direita.
    static constexpr int Up = GLFW_KEY_UP; ///< Seta cima.
    static constexpr int Down = GLFW_KEY_DOWN; ///< Seta baixo.
    static constexpr int LeftShift = GLFW_KEY_LEFT_SHIFT; ///< Shift esquerdo.
    static constexpr int MouseLeft = 1000; ///< Botão esquerdo do mouse.
    static constexpr int MouseRight = 1001; ///< Botão direito do mouse.
};

/**
 * @brief Estado de teclado por frame (delegado ao InputManager).
 *
 * @c isKeyPressed é "edge-triggered": retorna true apenas no frame em que a
 * tecla desce — ideal para disparar ações (triggers) sem repetição.
 * @c isKeyDown retorna true enquanto a tecla estiver pressionada.
 */
struct Input {
    /** @brief true no frame em que a tecla @p key foi pressionada. */
    static bool isKeyPressed(int key);

    /** @brief true enquanto a tecla @p key estiver segurada. */
    static bool isKeyDown(int key);

    /** @brief true no frame em que a tecla @p key foi solta. */
    static bool isKeyUp(int key);

    /** @brief Compatibilidade: o InputManager já atualiza o estado global. */
    static void update();
};

} // namespace VelvX