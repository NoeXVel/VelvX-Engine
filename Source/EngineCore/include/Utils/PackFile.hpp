#pragma once
/**
 * @file PackFile.hpp
 * @brief Pack de assets comprimido (ZIP via miniz) para exportação de jogos.
 */

#include <cstdint>
#include <filesystem>
#include <string>
#include <utility>
#include <vector>

namespace VelvX {

/**
 * @brief Leitor/escritor de packs .zip usados na exportação.
 *
 * Quando um pack está aberto, FileSystem/AssetBrowser leem arquivos dele
 * antes de tentar o disco (caminhos relativos à raiz do projeto).
 */
class PackFile {
public:
    /** @brief Abre um pack para leitura (fecha qualquer pack anterior). */
    static bool open(const std::filesystem::path& zipPath);

    /** @brief Fecha o pack aberto. */
    static void close();

    /** @brief true se há pack aberto. */
    static bool isOpen();

    /** @brief true se o pack contém o caminho relativo @p rel. */
    static bool contains(const std::string& rel);

    /** @brief Lê um arquivo do pack (vazio se ausente). */
    static std::vector<uint8_t> read(const std::string& rel);

    /** @brief Lista os caminhos relativos contidos no pack. */
    static std::vector<std::string> entries();

    /**
     * @brief Escreve um pack ZIP com os pares (caminho relativo, bytes).
     * @return true em sucesso.
     */
    static bool write(const std::filesystem::path& zipPath,
                      const std::vector<std::pair<std::string, std::vector<uint8_t>>>& files);
};

} // namespace VelvX