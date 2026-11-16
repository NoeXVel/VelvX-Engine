/**
 * @file Project.cpp
 * @brief Persistência e criação de projetos a partir de templates.
 */

#include "Core/Project.hpp"
#include "Core/Log.hpp"
#include "Utils/FileSystem.hpp"
#include "Utils/Json.hpp"
#include <algorithm>
#include <system_error>

namespace VelvX {

bool Project::load(const std::filesystem::path& velvprojFile) {
    const std::string text = FS::readText(velvprojFile);
    if (text.empty()) {
        Log::warn("Project: arquivo não encontrado: " + velvprojFile.string());
        return false;
    }
    try {
        const Json::Value root = Json::Value::parse(text);
        name = root["name"].asString("NovoProjeto");
        engineVersion = root["engineVersion"].asString("1.0");
        templateType = root["template"].asString("Empty");
        mainScene = root["mainScene"].asString("Main");
        mainScript = root["mainScript"].asString("scripts/GameScript.velv");
        scenes.clear();
        if (const Json::Value* sceneList = root.find("scenes")) {
            for (const auto& scene : sceneList->arr) {
                scenes.push_back(scene.asString());
            }
        }
        if (const Json::Value* settingsJson = root.find("settings")) {
            settings = ProjectSettings::fromJson(*settingsJson);
        } else {
            settings = ProjectSettings::defaults();
        }
        file = std::filesystem::absolute(velvprojFile);
        directory = file.parent_path();
        return true;
    } catch (const std::exception& e) {
        Log::error(std::string("Project: .velvproj inválido: ") + e.what());
        return false;
    }
}

bool Project::save() const {
    Json::Value root = Json::Value::object();
    root["name"] = Json::Value::ofString(name);
    root["engineVersion"] = Json::Value::ofString(engineVersion);
    root["template"] = Json::Value::ofString(templateType);
    root["mainScene"] = Json::Value::ofString(mainScene);
    root["mainScript"] = Json::Value::ofString(mainScript);
    Json::Value sceneList = Json::Value::array();
    for (const auto& scene : scenes) {
        sceneList.push(Json::Value::ofString(scene));
    }
    root["scenes"] = sceneList;
    root["settings"] = settings.toJson();

    std::filesystem::path target = file;
    if (target.empty()) {
        target = directory / (name + ".velvproj");
    }
    return FS::writeText(target, root.dump());
}

std::filesystem::path Project::createFromTemplate(const std::filesystem::path& targetDir,
                                                  const std::string& projectName,
                                                  const std::string& templateName,
                                                  const std::filesystem::path& templatesRoot) {
    const std::filesystem::path templateDir = templatesRoot / templateName;
    std::error_code ec;
    if (!std::filesystem::is_directory(templateDir, ec)) {
        Log::error("Project: template não encontrado: " + templateDir.string());
        return {};
    }
    const std::filesystem::path projectDir = targetDir / projectName;
    if (std::filesystem::exists(projectDir, ec)) {
        Log::error("Project: destino já existe: " + projectDir.string());
        return {};
    }
    std::filesystem::create_directories(projectDir, ec);
    std::filesystem::copy(templateDir, projectDir,
                          std::filesystem::copy_options::recursive |
                              std::filesystem::copy_options::copy_symlinks,
                          ec);
    if (ec) {
        Log::error("Project: falha ao copiar template: " + ec.message());
        return {};
    }

    std::filesystem::path templateProjFile;
    for (const auto& entry : std::filesystem::directory_iterator(projectDir, ec)) {
        if (entry.path().extension() == ".velvproj") {
            templateProjFile = entry.path();
            break;
        }
    }

    Project project;
    if (!templateProjFile.empty()) {
        project.load(templateProjFile);
    }
    project.name = projectName;
    project.templateType = templateName;
    project.directory = projectDir;
    project.file = projectDir / (projectName + ".velvproj");
    if (!templateProjFile.empty() && templateProjFile != project.file) {
        std::filesystem::remove(templateProjFile, ec);
    }
    if (!project.save()) {
        return {};
    }
    Log::info("Project: criado '" + projectName + "' a partir do template " + templateName);
    return project.file;
}

std::vector<std::filesystem::path> Project::findProjects(const std::filesystem::path& searchRoot) {
    std::vector<std::filesystem::path> found;
    std::error_code ec;
    if (!std::filesystem::is_directory(searchRoot, ec)) {
        return found;
    }
    std::filesystem::recursive_directory_iterator it(searchRoot, ec);
    const std::filesystem::recursive_directory_iterator end;
    for (; it != end; it.increment(ec)) {
        if (ec) {
            ec.clear();
            continue;
        }
        if (it->is_regular_file(ec) && it->path().extension() == ".velvproj") {
            found.push_back(it->path());
        }
    }
    std::sort(found.begin(), found.end());
    return found;
}

} // namespace VelvX