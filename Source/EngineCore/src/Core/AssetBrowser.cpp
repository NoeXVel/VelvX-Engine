#include "Core/AssetBrowser.hpp"
#include "Core/Log.hpp"
#include "Renderer/ModelImporter.hpp"
#include "Renderer/Renderer.hpp"
#include "Utils/FileSystem.hpp"
#include <algorithm>
#include <stdexcept>

namespace VelvX {

std::filesystem::path AssetBrowser::root_;

std::filesystem::path AssetBrowser::resolveInternal(const std::string& expr) {
    std::string s = expr;

    const auto ltrim = [&s]() {
        s.erase(s.begin(), std::find_if(s.begin(), s.end(),
                [](unsigned char c) { return !std::isspace(c); }));
    };
    const auto rtrim = [&s]() {
        s.erase(std::find_if(s.rbegin(), s.rend(),
                [](unsigned char c) { return !std::isspace(c); }).base(), s.end());
    };
    ltrim();
    rtrim();

    // Remove o wrapper Assets("...") quando presente.
    constexpr std::string_view prefix = "Assets(";
    if (s.starts_with(prefix) && !s.empty() && s.back() == ')') {
        s = s.substr(prefix.size(), s.size() - prefix.size() - 1);
        ltrim();
        rtrim();
    }

    // Remove aspas externas quando presentes.
    if (s.size() >= 2 && s.front() == '"' && s.back() == '"') {
        s = s.substr(1, s.size() - 2);
    }

    if (s.empty()) {
        throw std::runtime_error("AssetBrowser: expressão de asset vazia: " + expr);
    }

    std::replace(s.begin(), s.end(), '\\', '/');
    return std::filesystem::path(s);
}

std::filesystem::path AssetBrowser::resolve(const std::string& expr) {
    std::filesystem::path rel = resolveInternal(expr);
    if (rel.is_absolute()) {
        return rel;
    }
    const std::filesystem::path base = root_.empty() ? FS::assetsRoot() : root_;
    return base / rel;
}

int AssetBrowser::loadModel(const std::string& expr) {
    const std::filesystem::path path = resolve(expr);
    if (!FS::exists(path)) {
        Log::warn("AssetBrowser.loadModel: modelo não encontrado: " + path.string());
        return -1;
    }
    return ModelImporter::import(path.string());
}

unsigned int AssetBrowser::loadTexture(const std::string& expr) {
    const std::filesystem::path path = resolve(expr);
    if (!FS::exists(path)) {
        Log::warn("AssetBrowser.loadTexture: textura não encontrada: " + path.string());
    }
    return Renderer::textureFor(path.string());
}

void AssetBrowser::setRoot(const std::filesystem::path& root) {
    root_ = root;
    Log::info("AssetBrowser: raiz de assets = " + (root_.empty() ? FS::assetsRoot().string() : root_.string()));
}

const std::filesystem::path& AssetBrowser::root() {
    return root_;
}

std::vector<std::filesystem::path> AssetBrowser::listAssets() {
    const std::filesystem::path base = root_.empty() ? FS::assetsRoot() : root_;
    return FS::listDir(base, true);
}

} // namespace VelvX