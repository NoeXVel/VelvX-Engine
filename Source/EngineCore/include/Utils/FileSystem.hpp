#pragma once
/**
 * @file FileSystem.hpp
 * @brief Helpers de filesystem com suporte a pack de exportação.
 */

#include <cstdint>
#include <filesystem>
#include <string>
#include <vector>

namespace VelvX::FS {

/** @brief Raiz do projeto atual (diretório do .velvproj ou do executável). */
std::filesystem::path projectRoot();

/** @brief Define a raiz do projeto. */
void setProjectRoot(const std::filesystem::path& root);

/** @brief Raiz de assets (projectRoot/assets). */
std::filesystem::path assetsRoot();

/** @brief Caminho absoluto dentro do projeto. */
std::filesystem::path resolveProject(const std::string& rel);

/** @brief true se existe no pack aberto ou em disco. */
bool exists(const std::filesystem::path& path);

/** @brief Lê texto (pack primeiro, depois disco); vazio se ausente. */
std::string readText(const std::filesystem::path& path);

/** @brief Lê bytes (pack primeiro, depois disco); vazio se ausente. */
std::vector<uint8_t> readBinary(const std::filesystem::path& path);

/** @brief Escreve texto criando diretórios pais. */
bool writeText(const std::filesystem::path& path, const std::string& text);

/** @brief Lista arquivos de um diretório (recursivo opcional), ordenados. */
std::vector<std::filesystem::path> listDir(const std::filesystem::path& dir, bool recursive = false);

} // namespace VelvX::FS