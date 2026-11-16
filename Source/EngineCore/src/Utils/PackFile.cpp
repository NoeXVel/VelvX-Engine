#include "Utils/PackFile.hpp"
#include "Core/Log.hpp"
#include <miniz.h>
#include <mutex>
#include <unordered_map>

namespace VelvX {

static mz_zip_archive g_zip{};
static bool g_open = false;
static std::mutex g_mutex;
static std::unordered_map<std::string, mz_uint> g_index; ///< Nome normalizado -> índice.

static std::string normalize(const std::string& rel) {
    std::string s = rel;
    for (char& c : s) {
        if (c == '\\') c = '/';
    }
    while (!s.empty() && s.front() == '/') s.erase(s.begin());
    return s;
}

bool PackFile::open(const std::filesystem::path& zipPath) {
    std::lock_guard<std::mutex> lock(g_mutex);
    close();
    if (!mz_zip_reader_init_file(&g_zip, zipPath.string().c_str(), 0)) {
        return false;
    }
    const mz_uint count = mz_zip_reader_get_num_files(&g_zip);
    for (mz_uint i = 0; i < count; ++i) {
        char name[MZ_ZIP_MAX_ARCHIVE_FILENAME_SIZE];
        mz_zip_reader_get_filename(&g_zip, i, name, sizeof(name));
        g_index[normalize(name)] = i;
    }
    g_open = true;
    Log::info("PackFile: aberto " + zipPath.string());
    return true;
}

void PackFile::close() {
    if (g_open) {
        mz_zip_reader_end(&g_zip);
        g_open = false;
    }
    g_index.clear();
}

bool PackFile::isOpen() {
    return g_open;
}

bool PackFile::contains(const std::string& rel) {
    std::lock_guard<std::mutex> lock(g_mutex);
    if (!g_open) return false;
    return g_index.count(normalize(rel)) > 0;
}

std::vector<uint8_t> PackFile::read(const std::string& rel) {
    std::lock_guard<std::mutex> lock(g_mutex);
    if (!g_open) return {};
    const auto it = g_index.find(normalize(rel));
    if (it == g_index.end()) return {};
    size_t size = 0;
    void* data = mz_zip_reader_extract_to_heap(&g_zip, it->second, &size, 0);
    if (!data) return {};
    std::vector<uint8_t> out(static_cast<uint8_t*>(data), static_cast<uint8_t*>(data) + size);
    mz_free(data);
    return out;
}

std::vector<std::string> PackFile::entries() {
    std::lock_guard<std::mutex> lock(g_mutex);
    std::vector<std::string> out;
    if (!g_open) return out;
    for (const auto& [name, index] : g_index) {
        out.push_back(name);
    }
    return out;
}

bool PackFile::write(const std::filesystem::path& zipPath,
                     const std::vector<std::pair<std::string, std::vector<uint8_t>>>& files) {
    mz_zip_archive zip{};
    if (!mz_zip_writer_init_file(&zip, zipPath.string().c_str(), 0)) {
        return false;
    }
    for (const auto& [rel, bytes] : files) {
        if (!mz_zip_writer_add_mem(&zip, normalize(rel).c_str(), bytes.data(), bytes.size(),
                                   MZ_DEFAULT_COMPRESSION)) {
            mz_zip_writer_end(&zip);
            return false;
        }
    }
    return mz_zip_writer_finalize_archive(&zip) && mz_zip_writer_end(&zip);
}

} // namespace VelvX