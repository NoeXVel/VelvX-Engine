#include "Utils/FileSystem.hpp"
#include "Utils/PackFile.hpp"
#include <algorithm>
#include <fstream>
#include <sstream>

namespace VelvX::FS {

static std::filesystem::path g_root = std::filesystem::current_path();

std::filesystem::path projectRoot() {
    return g_root;
}

void setProjectRoot(const std::filesystem::path& root) {
    g_root = root;
}

std::filesystem::path assetsRoot() {
    return g_root / "assets";
}

std::filesystem::path resolveProject(const std::string& rel) {
    return g_root / rel;
}

static std::string relativeKey(const std::filesystem::path& path) {
    std::error_code ec;
    const std::filesystem::path rel = std::filesystem::relative(path, g_root, ec);
    std::string s = ec ? path.string() : rel.string();
    for (char& c : s) {
        if (c == '\\') c = '/';
    }
    return s;
}

bool exists(const std::filesystem::path& path) {
    if (PackFile::isOpen() && PackFile::contains(relativeKey(path))) {
        return true;
    }
    return std::filesystem::exists(path);
}

std::vector<uint8_t> readBinary(const std::filesystem::path& path) {
    if (PackFile::isOpen()) {
        const std::string key = relativeKey(path);
        if (PackFile::contains(key)) {
            return PackFile::read(key);
        }
    }
    std::ifstream in(path, std::ios::binary);
    if (!in.is_open()) return {};
    return std::vector<uint8_t>(std::istreambuf_iterator<char>(in), std::istreambuf_iterator<char>());
}

std::string readText(const std::filesystem::path& path) {
    const std::vector<uint8_t> bytes = readBinary(path);
    return std::string(bytes.begin(), bytes.end());
}

bool writeText(const std::filesystem::path& path, const std::string& text) {
    std::error_code ec;
    std::filesystem::create_directories(path.parent_path(), ec);
    std::ofstream out(path, std::ios::binary | std::ios::trunc);
    if (!out.is_open()) return false;
    out << text;
    return out.good();
}

std::vector<std::filesystem::path> listDir(const std::filesystem::path& dir, bool recursive) {
    std::vector<std::filesystem::path> out;
    std::error_code ec;
    if (!std::filesystem::exists(dir, ec)) return out;
    if (recursive) {
        for (const auto& entry : std::filesystem::recursive_directory_iterator(dir, ec)) {
            if (entry.is_regular_file()) out.push_back(entry.path());
        }
    } else {
        for (const auto& entry : std::filesystem::directory_iterator(dir, ec)) {
            out.push_back(entry.path());
        }
    }
    std::sort(out.begin(), out.end());
    return out;
}

} // namespace VelvX::FS