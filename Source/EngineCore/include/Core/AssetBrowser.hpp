#pragma once
/**
 * @file AssetBrowser.hpp
 * @brief Resolução de caminhos de assets no formato Assets("Path/Name").
 */

#include <filesystem>
#include <string>
#include <vector>

namespace VelvX {

/**
 * @brief Navegador de assets: converte expressões de caminho em caminhos reais.
 *
 * A raiz é @c assets/ dentro do projeto atual (FileSystem). @c resolve aceita
 * @c Assets("Path/Name.ext"), @c "Path/Name.ext" ou @c Path/Name.ext.
 */
class AssetBrowser {
public:
    /**
     * @brief Converte uma expressão de asset em std::filesystem::path absoluto.
     * @param expr Expressão no formato Assets("Path/Name"), com ou sem aspas.
     * @throws std::runtime_error se a expressão for vazia ou malformada.
     */
    static std::filesystem::path resolve(const std::string& expr);

    /**
     * @brief Importa um modelo 3D (.fbx/.gltf/.obj) via Assimp.
     * @param expr Expressão de caminho do modelo.
     * @return Índice do modelo importado, ou -1 em falha.
     */
    static int loadModel(const std::string& expr);

    /**
     * @brief Carrega uma textura para a GPU (cache do Renderer).
     * @param expr Expressão de caminho da textura.
     * @return Nome GL da textura (branco 1x1 em falha).
     */
    static unsigned int loadTexture(const std::string& expr);

    /** @brief Define o diretório raiz dos assets (vazio = assets/ do projeto). */
    static void setRoot(const std::filesystem::path& root);

    /** @brief Diretório raiz atual dos assets. */
    static const std::filesystem::path& root();

    /** @brief Lista todos os arquivos sob a raiz de assets (para o Editor). */
    static std::vector<std::filesystem::path> listAssets();

private:
    /** @brief Remove wrapper Assets(...) / aspas e normaliza o caminho. */
    static std::filesystem::path resolveInternal(const std::string& expr);

    static std::filesystem::path root_; ///< Raiz explícita (vazio = FS::assetsRoot).
};

} // namespace VelvX