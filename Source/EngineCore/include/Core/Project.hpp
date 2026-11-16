#pragma once
/**
 * @file Project.hpp
 * @brief Projeto VelvX (.velvproj): metadados, cenas, script principal e settings.
 */

#include "Core/ProjectSettings.hpp"
#include <filesystem>
#include <string>
#include <vector>

namespace VelvX {

/**
 * @brief Um projeto da engine, persistido em @c <nome>.velvproj (JSON).
 */
class Project {
public:
    std::string name = "NovoProjeto"; ///< Nome do projeto.
    std::string engineVersion = "1.0"; ///< Versão da engine usada.
    std::string templateType = "Empty"; ///< Template de origem (Empty/2DPlatformer/3DFPS).
    std::string mainScene = "Main"; ///< Cena carregada no Play.
    std::string mainScript = "scripts/GameScript.velv"; ///< Script Velv principal.
    std::vector<std::string> scenes; ///< Nomes das cenas do projeto.
    ProjectSettings settings; ///< Configurações avançadas.

    std::filesystem::path directory; ///< Diretório raiz do projeto (não serializado).
    std::filesystem::path file; ///< Caminho do .velvproj (não serializado).

    /** @brief Carrega um .velvproj; false se ausente/inválido. */
    bool load(const std::filesystem::path& velvprojFile);

    /** @brief Salva o .velvproj no diretório do projeto. */
    bool save() const;

    /**
     * @brief Cria um projeto novo copiando um template.
     * @param targetDir Diretório onde a pasta do projeto será criada.
     * @param projectName Nome do projeto (vira o nome da pasta).
     * @param templateName "Empty", "2DPlatformer" ou "3DFPS".
     * @param templatesRoot Diretório templates/ da engine.
     * @return Caminho do .velvproj criado (vazio em falha).
     */
    static std::filesystem::path createFromTemplate(const std::filesystem::path& targetDir,
                                                    const std::string& projectName,
                                                    const std::string& templateName,
                                                    const std::filesystem::path& templatesRoot);

    /** @brief Busca recursiva por arquivos .velvproj sob @p searchRoot. */
    static std::vector<std::filesystem::path> findProjects(const std::filesystem::path& searchRoot);
};

} // namespace VelvX