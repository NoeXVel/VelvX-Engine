#include "Core/SceneManager.hpp"
#include "Core/Log.hpp"
#include <chrono>
#include <stdexcept>
#include <utility>

namespace VelvX {

void SceneManager::registerScene(const std::string& name, std::unique_ptr<Scene> scene) {
    scenes_[name] = std::move(scene);
}

void SceneManager::loadScene(const std::string& name) {
    auto it = scenes_.find(name);
    if (it == scenes_.end()) {
        throw std::runtime_error("Scene not found: " + name);
    }
    Scene* next = it->second.get();
    if (activeScene_ && activeScene_ != next) {
        activeScene_->unload();
    }
    activeScene_ = next;
    next->load(name);
    Log::info("SceneManager: cena ativa = '" + name + "' (síncrono)");
}

std::future<void> SceneManager::loadSceneAsync(const std::string& name) {
    auto it = scenes_.find(name);
    if (it == scenes_.end()) {
        throw std::runtime_error("Scene not found: " + name);
    }
    Scene* scene = it->second.get();

    PendingLoad pending;
    pending.name = name;
    pending.done = pool_.submit([scene]() { scene->load(scene->getName()); }).share();
    std::future<void> result = pending.notify.get_future();
    pending_.push_back(std::move(pending));

    Log::info("SceneManager: carregamento assíncrono de '" + name + "' iniciado");
    return result;
}

void SceneManager::positionScene(Transform* target) {
    if (!activeScene_ || !target) {
        return;
    }
    activeScene_->parentTransform.setPosition(target->getPosition());
    activeScene_->parentTransform.setRotation(target->getRotation());
    activeScene_->parentTransform.setScale(target->getScale());
}

void SceneManager::positionScene(const glm::vec3& position) {
    if (activeScene_) {
        activeScene_->parentTransform.setPosition(position);
    }
}

void SceneManager::swapTo(const std::string& name) {
    if (activeScene_) {
        activeScene_->unload();
    }
    loadScene(name);
}

void SceneManager::clear() {
    for (auto& pending : pending_) {
        pending.done.wait();
    }
    pending_.clear();
    activeScene_ = nullptr;
    scenes_.clear();
    prototypes_.clear();
}

void SceneManager::registerPrototype(const std::string& name, std::unique_ptr<GameObject> prototype) {
    prototypes_[name] = std::move(prototype);
}

GameObject* SceneManager::getPrototype(const std::string& name) const {
    auto it = prototypes_.find(name);
    return it == prototypes_.end() ? nullptr : it->second.get();
}

void SceneManager::update() {
    for (size_t i = 0; i < pending_.size();) {
        if (pending_[i].done.wait_for(std::chrono::seconds(0)) != std::future_status::ready) {
            ++i;
            continue;
        }
        PendingLoad local = std::move(pending_[i]);
        pending_.erase(pending_.begin() + static_cast<std::ptrdiff_t>(i));
        try {
            local.done.get();
            Scene* next = scenes_[local.name].get();
            if (activeScene_ && activeScene_ != next) {
                activeScene_->unload();
            }
            activeScene_ = next;

            // Ao terminar, move a nova cena ao seu parentTransform.
            next->parentTransform.setPosition(next->scenePosition);
            positionScene(&next->parentTransform);

            Log::info("SceneManager: cena ativa = '" + local.name + "' (assíncrono concluído)");
            local.notify.set_value();
        } catch (...) {
            Log::error("SceneManager: falha no carregamento assíncrono de '" + local.name + "'");
            local.notify.set_exception(std::current_exception());
        }
    }
}

} // namespace VelvX