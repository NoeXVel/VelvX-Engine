#include "Physics/CollisionSystem.hpp"
#include "Core/GameObject.hpp"
#include "Core/Scene.hpp"
#include "Engine/Engine.hpp"
#include "Physics/PhysicsComponent.hpp"
#include "Renderer/RenderComponent.hpp"
#include <algorithm>
#include <cmath>
#include <set>
#include <utility>

namespace VelvX {

static std::vector<CollisionSystem::AABB> g_boxes;
static std::set<std::pair<std::string, std::string>> g_lastOverlaps;

static bool intersect(const CollisionSystem::AABB& a, const CollisionSystem::AABB& b) {
    return a.min.x <= b.max.x && a.max.x >= b.min.x &&
           a.min.y <= b.max.y && a.max.y >= b.min.y &&
           a.min.z <= b.max.z && a.max.z >= b.min.z;
}

const std::vector<CollisionSystem::AABB>& CollisionSystem::boxes() {
    return g_boxes;
}

void CollisionSystem::update() {
    g_boxes.clear();
    Scene* scene = Engine::getSceneManager().getActiveScene();
    if (!scene) {
        g_lastOverlaps.clear();
        return;
    }

    for (const auto& obj : scene->gameObjects) {
        const auto* collider = obj->getComponent<Collider>();
        const auto* mesh = obj->getComponent<MeshRenderer>();
        if (!collider && !mesh) {
            continue;
        }
        const glm::vec3 half = collider
            ? obj->transform.scale * collider->size * 0.5f
            : obj->transform.scale * 0.5f;
        const glm::vec3 center = obj->transform.position;
        AABB box;
        box.min = center - half;
        box.max = center + half;
        box.object = obj.get();
        box.trigger = collider && collider->isTrigger;
        box.layer = obj->layer;
        g_boxes.push_back(box);
    }

    const ProjectSettings& settings = Engine::settings();
    std::set<std::pair<std::string, std::string>> current;
    for (size_t i = 0; i < g_boxes.size(); ++i) {
        for (size_t j = i + 1; j < g_boxes.size(); ++j) {
            if (!intersect(g_boxes[i], g_boxes[j])) {
                continue;
            }
            if (!settings.layerCollides(g_boxes[i].layer, g_boxes[j].layer)) {
                continue;
            }
            auto key = std::make_pair(g_boxes[i].object->name, g_boxes[j].object->name);
            current.insert(key);
            if (!g_lastOverlaps.count(key)) {
                for (const auto& comp : g_boxes[i].object->components) {
                    for (const auto& other : g_boxes[j].object->components) {
                        comp->onTrigger3D(other.get());
                    }
                }
                for (const auto& comp : g_boxes[j].object->components) {
                    for (const auto& other : g_boxes[i].object->components) {
                        comp->onTrigger3D(other.get());
                    }
                }
            }
        }
    }
    g_lastOverlaps = std::move(current);
}

bool CollisionSystem::overlaps(const std::string& a, const std::string& b) {
    const AABB* ba = nullptr;
    const AABB* bb = nullptr;
    for (const auto& box : g_boxes) {
        if (box.object->name == a) ba = &box;
        if (box.object->name == b) bb = &box;
    }
    if (!ba || !bb || !intersect(*ba, *bb)) {
        return false;
    }
    return Engine::settings().layerCollides(ba->layer, bb->layer);
}

GameObject* CollisionSystem::raycast(const glm::vec3& origin, const glm::vec3& dir,
                                     float maxDist, glm::vec3* hitPoint) {
    const glm::vec3 d = glm::normalize(dir);
    GameObject* best = nullptr;
    float bestT = maxDist;

    for (const auto& box : g_boxes) {
        float tmin = 0.0f;
        float tmax = maxDist;
        bool hit = true;
        for (int axis = 0; axis < 3; ++axis) {
            const float o = origin[axis];
            const float dv = d[axis];
            const float lo = box.min[axis];
            const float hi = box.max[axis];
            if (std::fabs(dv) < 1e-8f) {
                if (o < lo || o > hi) { hit = false; break; }
            } else {
                float t1 = (lo - o) / dv;
                float t2 = (hi - o) / dv;
                if (t1 > t2) std::swap(t1, t2);
                tmin = std::max(tmin, t1);
                tmax = std::min(tmax, t2);
                if (tmin > tmax) { hit = false; break; }
            }
        }
        if (hit && tmin < bestT) {
            bestT = tmin;
            best = box.object;
            if (hitPoint) *hitPoint = origin + d * tmin;
        }
    }
    return best;
}

} // namespace VelvX