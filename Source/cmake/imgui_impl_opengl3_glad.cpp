/**
 * @file imgui_impl_opengl3_glad.cpp
 * @brief Compila o backend OpenGL3 do ImGui usando o GLAD como loader.
 *
 * O wrapper inclui glad/gl.h antes do backend e define
 * IMGUI_IMPL_OPENGL_LOADER_CUSTOM (via CMake) para desativar o loader
 * embutido do ImGui, evitando símbolos GL duplicados.
 */

#include <glad/gl.h>
#include "backends/imgui_impl_opengl3.cpp"