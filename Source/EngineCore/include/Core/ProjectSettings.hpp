#pragma once
/**
 * @file ProjectSettings.hpp
 * @brief Configurações avançadas do projeto: janela, input, gráficos, áudio
 *        e interface/acessibilidade (persistidas em JSON).
 */

#include "Input/InputManager.hpp"
#include "Utils/Json.hpp"
#include <filesystem>
#include <string>
#include <vector>

namespace VelvX {

/**
 * @brief Configurações completas de um projeto VelvX.
 */
struct ProjectSettings {
    // Janela
    int windowWidth = 1280; ///< Largura da janela em pixels.
    int windowHeight = 720; ///< Altura da janela em pixels.
    bool fullscreen = false; ///< Modo tela cheia.
    std::string windowTitle = "VelvX Game"; ///< Título da janela.

    // Input
    std::vector<InputBinding> inputBindings; ///< Bindings diretos (perfil único).
    std::vector<InputProfile> inputProfiles; ///< Perfis de input (jogadores/acessibilidade).
    int activeInputProfile = 0; ///< Índice do perfil ativo.
    float gamepadDeadzone = 0.15f; ///< Zona morta dos analógicos.

    // Gráficos
    int antiAliasingSamples = 4; ///< Amostras de MSAA (0, 2, 4, 8).
    bool enableShadows = true; ///< Sombras direcionais ativadas.
    int shadowQuality = 2; ///< 0=baixa(512) 1=média(1024) 2=alta(2048) 3=ultra(4096).
    bool vsync = true; ///< Sincronização vertical.

    // Áudio
    float masterVolume = 1.0f; ///< Volume mestre (0..1).
    float musicVolume = 0.8f; ///< Volume da música (0..1).
    float sfxVolume = 1.0f; ///< Volume dos efeitos (0..1).

    // Interface / acessibilidade
    std::string language = "pt-BR"; ///< Idioma da interface.
    float fontScale = 1.0f; ///< Escala global de fonte.
    bool highContrast = false; ///< Tema de alto contraste.

    // Física / colisão
    /// Nomes das camadas de colisão (índice = GameObject::layer).
    std::vector<std::string> collisionLayers = {
        "Default", "Player", "Enemies", "Pickups", "Ground", "Projectiles"
    };
    /// Matriz camada×camada: true = colide. Controla colisão real e triggers.
    std::vector<std::vector<bool>> collisionMatrix;
    bool useGroundPlane = true; ///< Plano de chão global y=0 (rede de segurança).

    /** @brief Configurações padrão com bindings de teclado/gamepad. */
    static ProjectSettings defaults();

    /** @brief Carrega de JSON (retorna defaults se ausente/inválido). */
    static ProjectSettings load(const std::filesystem::path& file);

    /** @brief Serializa em JSON no caminho dado. */
    void save(const std::filesystem::path& file) const;

    /** @brief Converte para DOM JSON (usado dentro do .velvproj). */
    Json::Value toJson() const;

    /** @brief Lê de DOM JSON (campos ausentes mantêm defaults). */
    static ProjectSettings fromJson(const Json::Value& root);

    /** @brief Aplica os perfis de input ao InputManager. */
    void applyInput() const;

    /** @brief Tamanho do shadow map conforme shadowQuality. */
    int shadowMapSize() const;

    /** @brief Garante que a matriz tem o tamanho das camadas (preenche com true). */
    void ensureCollisionMatrix();

    /** @brief true se as camadas @p a e @p b colidem (fora do rango = true). */
    bool layerCollides(int a, int b) const;

    /** @brief Índice de uma camada pelo nome (-1 se ausente). */
    int layerIndex(const std::string& name) const;
};

} // namespace VelvX