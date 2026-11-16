#pragma once
/**
 * @file Log.hpp
 * @brief Log simples (console + arquivo velvx.log) da VelvX Engine.
 */

#include <filesystem>
#include <functional>
#include <string>

namespace VelvX::Log {

/** @brief Sink de console (Editor): recebe nível e mensagem sem timestamp. */
using ConsoleSink = std::function<void(const std::string& level, const std::string& message)>;

/** @brief Define o arquivo de log (vazio desativa a escrita em arquivo). */
void setLogFile(const std::filesystem::path& path);

/** @brief Define um sink adicional (ex.: painel Console do Editor). */
void setConsoleSink(ConsoleSink sink);

/** @brief Mensagem informativa. */
void info(const std::string& message);

/** @brief Mensagem de aviso. */
void warn(const std::string& message);

/** @brief Mensagem de erro. */
void error(const std::string& message);

} // namespace VelvX::Log