#include "Core/Scene.hpp"
#include "Core/Log.hpp"
#include "Physics/PhysicsComponent.hpp"
#include "PluginAPI/ComponentRegistry.hpp"
#include "Renderer/Camera.hpp"
#include "Renderer/RenderComponent.hpp"
#include "Scripting/ScriptComponent.hpp"
#include "Utils/FileSystem.hpp"
#include "Utils/Json.hpp"
#include <glm/gtc/quaternion.hpp>
#include <utility>

namespace VelvX {

Scene::Scene(std::string name) : name_(std::move(name)) {
    Inspector::expose(name_, "nextScene", "std::string", &nextScene, this);
    Inspector::expose(name_, "scenePosition", "glm::vec3", &scenePosition, this);
}

Scene::~Scene() {
    Inspector::removeFields(this);
}

static MeshType meshFromString(const std::string& s) {
    if (s == "plane") return MeshType::Plane;
    if (s == "quad") return MeshType::Quad;
    if (s == "sprite") return MeshType::Sprite;
    return MeshType::Cube;
}

static const char* meshToString(MeshType m) {
    switch (m) {
        case MeshType::Plane: return "plane";
        case MeshType::Quad: return "quad";
        case MeshType::Sprite: return "sprite";
        default: return "cube";
    }
}

static glm::vec3 jsonVec3(const Json::Value* v, const glm::vec3& def) {
    if (!v || v->type != Json::Value::Type::Array || v->arr.size() < 3) return def;
    return glm::vec3(static_cast<float>(v->arr[0].asNumber()),
                     static_cast<float>(v->arr[1].asNumber()),
                     static_cast<float>(v->arr[2].asNumber()));
}

static Json::Value vec3Json(const glm::vec3& v) {
    Json::Value a = Json::Value::array();
    a.push(Json::Value::ofNumber(v.x));
    a.push(Json::Value::ofNumber(v.y));
    a.push(Json::Value::ofNumber(v.z));
    return a;
}

void Scene::load(const std::string& path) {
    unload();

    std::filesystem::path file;
    if (path.size() >= 7 && path.substr(path.size() - 7) == ".vscene") {
        file = FS::resolveProject(path);
    } else {
        file = FS::resolveProject("scenes/" + path + ".vscene");
        if (!FS::exists(file)) {
            const std::filesystem::path alternate = FS::resolveProject("assets/scenes/" + path + ".vscene");
            if (FS::exists(alternate)) {
                file = alternate;
            }
        }
    }

    const std::string text = FS::readText(file);
    if (deserialize(text)) {
        Log::info("Scene.load: '" + name_ + "' carregada de " + file.string() +
                  " (" + std::to_string(gameObjects.size()) + " objetos)");
        return;
    }
    if (!text.empty() && text.front() != '{') {
        Log::warn("Scene.load: formato não suportado em " + file.string());
    } else if (text.empty()) {
        Log::warn("Scene.load: arquivo não encontrado (" + file.string() + "); usando conteúdo procedural");
    }

    loadBuiltIn();
    loaded_ = true;
}

bool Scene::deserialize(const std::string& text) {
    if (text.empty() || text.front() != '{') return false;
    try {
        const Json::Value root = Json::Value::parse(text);
        unload();
        if (const Json::Value* cam = root.find("camera")) {
                const std::string mode = cam->find("mode") ? cam->find("mode")->asString() : "perspective";
                cameraMode = mode == "ortho2d" ? CameraMode::Ortho2D : CameraMode::Perspective;
                cameraPosition = jsonVec3(cam->find("position"), cameraPosition);
                cameraTarget = jsonVec3(cam->find("target"), cameraTarget);
                if (const Json::Value* oh = cam->find("orthoHeight")) {
                    orthoHeight = static_cast<float>(oh->asNumber(orthoHeight));
                }
            }
            if (const Json::Value* objects = root.find("objects")) {
                for (const Json::Value& o : objects->arr) {
                    auto obj = std::make_unique<GameObject>(o.find("name") ? o.find("name")->asString("Object") : "Object");
                    if (const Json::Value* t = o.find("tag")) {
                        obj->tag = t->asString();
                    }
                    if (const Json::Value* l = o.find("layer")) {
                        obj->layer = static_cast<int>(l->asNumber(0));
                    }
                    if (const Json::Value* tf = o.find("transform")) {
                        obj->transform.setPosition(jsonVec3(tf->find("position"), glm::vec3(0.0f)));
                        obj->transform.setScale(jsonVec3(tf->find("scale"), glm::vec3(1.0f)));
                        const glm::vec3 euler = jsonVec3(tf->find("rotation"), glm::vec3(0.0f));
                        obj->transform.setRotation(glm::quat(glm::radians(euler)));
                    }
                    if (const Json::Value* m = o.find("mesh")) {
                        auto* rc = obj->addComponent<MeshRenderer>();
                        rc->mesh = meshFromString(m->find("type") ? m->find("type")->asString("cube") : "cube");
                        rc->color = jsonVec3(m->find("color"), glm::vec3(1.0f));
                        rc->texturePath = m->find("texture") ? m->find("texture")->asString() : "";
                        rc->importedModel = m->find("importedModel") ? static_cast<int>(m->find("importedModel")->asNumber(-1)) : -1;
                        rc->importedMesh = m->find("imported") ? static_cast<int>(m->find("imported")->asNumber(-1)) : -1;
                    }
                    const std::string phys = o.find("physics") ? o.find("physics")->asString() : "";
                    if (phys == "dynamic") {
                        auto* rb = obj->addComponent<RigidBody>();
                        if (const Json::Value* r = o.find("rigid")) {
                            rb->mass = static_cast<float>(r->find("mass") ? r->find("mass")->asNumber(1) : 1);
                            rb->restitution = static_cast<float>(r->find("restitution") ? r->find("restitution")->asNumber(0.35) : 0.35);
                            rb->friction = static_cast<float>(r->find("friction") ? r->find("friction")->asNumber(0.15) : 0.15);
                            rb->useGravity = r->find("useGravity") ? r->find("useGravity")->asBool(true) : true;
                            rb->kinematic = r->find("kinematic") ? r->find("kinematic")->asBool(false) : false;
                        }
                    } else if (phys == "static") {
                        obj->addComponent<StaticBody>();
                    }
                    if (const Json::Value* c = o.find("collider")) {
                        auto* col = obj->addComponent<Collider>();
                        col->size = jsonVec3(c->find("size"), glm::vec3(1.0f));
                        col->isTrigger = c->find("trigger") ? c->find("trigger")->asBool(false) : false;
                    }
                    if (const Json::Value* cam = o.find("camera")) {
                        auto* camera = obj->addComponent<Camera>();
                        const std::string proj = cam->find("projection") ? cam->find("projection")->asString("perspective") : "perspective";
                        camera->projection = (proj == "ortho" || proj == "orthographic" || proj == "ortho2d")
                                                 ? Camera::Projection::Orthographic
                                                 : Camera::Projection::Perspective;
                        camera->fov = static_cast<float>(cam->find("fov") ? cam->find("fov")->asNumber(45.0) : 45.0);
                        camera->nearPlane = static_cast<float>(cam->find("near") ? cam->find("near")->asNumber(0.1) : 0.1);
                        camera->farPlane = static_cast<float>(cam->find("far") ? cam->find("far")->asNumber(500.0) : 500.0);
                        camera->orthoHeight = static_cast<float>(cam->find("orthoHeight") ? cam->find("orthoHeight")->asNumber(12.0) : 12.0);
                        camera->active = cam->find("active") ? cam->find("active")->asBool(true) : true;
                    }
                    if (const Json::Value* s = o.find("script")) {
                        obj->addScript(s->asString());
                    }
                    if (const Json::Value* scripts = o.find("scripts")) {
                        for (const Json::Value& sc : scripts->arr) {
                            obj->addScript(sc.asString());
                        }
                    }
                    if (const Json::Value* comps = o.find("components")) {
                        for (const Json::Value& compJson : comps->arr) {
                            const std::string type = compJson.find("type") ? compJson.find("type")->asString() : "";
                            auto component = ComponentRegistry::create(type, obj.get());
                            if (component) {
                                component->fromJson(compJson);
                                obj->components.push_back(std::move(component));
                            } else if (!type.empty()) {
                                Log::warn("Scene.load: componente desconhecido '" + type + "' (plugin ausente?)");
                            }
                        }
                    }
                    addObject(std::move(obj));
                }
                // Segunda passada: resolve hierarquias (parent pelo nome).
                for (const Json::Value& o : objects->arr) {
                    const Json::Value* p = o.find("parent");
                    if (!p) continue;
                    GameObject* child = findGameObject(o.find("name") ? o.find("name")->asString() : "");
                    GameObject* parentObj = findGameObject(p->asString());
                    if (child && parentObj && child != parentObj) {
                        child->setParent(parentObj);
                    }
                }
            }
            loaded_ = true;
            return true;
        } catch (const std::exception& e) {
            Log::error(std::string("Scene.deserialize: JSON inválido: ") + e.what());
            return false;
        }
}

void Scene::loadBuiltIn() {
    const bool alternate = name_.find('2') != std::string::npos;
    const glm::vec3 floorColor = alternate ? glm::vec3(0.22f, 0.30f, 0.38f)
                                           : glm::vec3(0.30f, 0.32f, 0.38f);
    const glm::vec3 accent = alternate ? glm::vec3(0.85f, 0.45f, 0.25f)
                                       : glm::vec3(0.55f, 0.35f, 0.85f);

    const auto makeStatic = [&](const std::string& name, MeshType mesh,
                                const glm::vec3& pos, const glm::vec3& scale,
                                const glm::vec3& color) {
        auto obj = std::make_unique<GameObject>(name);
        obj->transform.setPosition(pos);
        obj->transform.setScale(scale);
        auto* rc = obj->addComponent<MeshRenderer>();
        rc->mesh = mesh;
        rc->color = color;
        obj->addComponent<StaticBody>();
        addObject(std::move(obj));
    };

    makeStatic("Floor", MeshType::Plane, {0.0f, -0.5f, 0.0f}, {24.0f, 1.0f, 24.0f}, floorColor);
    makeStatic("PillarNW", MeshType::Cube, {-6.0f, 1.5f, -6.0f}, {1.0f, 4.0f, 1.0f}, accent);
    makeStatic("PillarNE", MeshType::Cube, {6.0f, 1.5f, -6.0f}, {1.0f, 4.0f, 1.0f}, accent);
    makeStatic("PillarSW", MeshType::Cube, {-6.0f, 1.5f, 6.0f}, {1.0f, 4.0f, 1.0f}, accent);
    makeStatic("PillarSE", MeshType::Cube, {6.0f, 1.5f, 6.0f}, {1.0f, 4.0f, 1.0f}, accent);
    makeStatic("Platform", MeshType::Cube, {0.0f, 0.25f, 0.0f}, {5.0f, 0.5f, 5.0f}, floorColor * 1.3f);
}

std::future<void> Scene::loadAsync(const std::string& path) {
    return std::async(std::launch::async, [this, path]() { load(path); });
}

void Scene::unload() {
    gameObjects.clear();
    loaded_ = false;
}

GameObject* Scene::addObject(std::unique_ptr<GameObject> object) {
    object->transform.parent = &parentTransform;
    GameObject* raw = object.get();
    for (const auto& comp : raw->components) {
        if (auto* rb = dynamic_cast<RigidBody*>(comp.get())) {
            PhysicsWorld::add(rb);
        }
    }
    gameObjects.push_back(std::move(object));
    return raw;
}

bool Scene::removeObject(GameObject* object) {
    for (auto it = gameObjects.begin(); it != gameObjects.end(); ++it) {
        if (it->get() == object) {
            for (const auto& comp : object->components) {
                if (auto* rb = dynamic_cast<RigidBody*>(comp.get())) {
                    PhysicsWorld::remove(rb);
                }
            }
            gameObjects.erase(it);
            return true;
        }
    }
    return false;
}

GameObject* Scene::findGameObject(const std::string& name) {
    for (const auto& obj : gameObjects) {
        if (obj->name == name) {
            return obj.get();
        }
    }
    return nullptr;
}

Camera* Scene::getActiveCamera() const {
    Camera* first = nullptr;
    for (const auto& obj : gameObjects) {
        if (auto* cam = obj->getComponent<Camera>()) {
            if (cam->active) {
                return cam;
            }
            if (!first) {
                first = cam;
            }
        }
    }
    return first;
}

void Scene::save(const std::filesystem::path& file) const {
    FS::writeText(file, serialize());
}

std::string Scene::serialize() const {
    Json::Value root = Json::Value::object();
    root["name"] = Json::Value::ofString(name_);

    Json::Value cam = Json::Value::object();
    cam["mode"] = Json::Value::ofString(cameraMode == CameraMode::Ortho2D ? "ortho2d" : "perspective");
    cam["position"] = vec3Json(cameraPosition);
    cam["target"] = vec3Json(cameraTarget);
    cam["orthoHeight"] = Json::Value::ofNumber(orthoHeight);
    root["camera"] = cam;

    Json::Value objects = Json::Value::array();
    for (const auto& obj : gameObjects) {
        Json::Value o = Json::Value::object();
        o["name"] = Json::Value::ofString(obj->name);
        if (!obj->tag.empty()) {
            o["tag"] = Json::Value::ofString(obj->tag);
        }
        if (obj->layer != 0) {
            o["layer"] = Json::Value::ofNumber(obj->layer);
        }
        if (obj->parent) {
            o["parent"] = Json::Value::ofString(obj->parent->name);
        }

        Json::Value tf = Json::Value::object();
        tf["position"] = vec3Json(obj->transform.position);
        tf["scale"] = vec3Json(obj->transform.scale);
        tf["rotation"] = vec3Json(glm::degrees(glm::eulerAngles(obj->transform.rotation)));
        o["transform"] = tf;

        if (const auto* rc = obj->getComponent<MeshRenderer>()) {
            Json::Value m = Json::Value::object();
            m["type"] = Json::Value::ofString(meshToString(rc->mesh));
            m["color"] = vec3Json(rc->color);
            m["texture"] = Json::Value::ofString(rc->texturePath);
            m["importedModel"] = Json::Value::ofNumber(rc->importedModel);
            m["imported"] = Json::Value::ofNumber(rc->importedMesh);
            o["mesh"] = m;
        }
        if (obj->getComponent<RigidBody>()) {
            o["physics"] = Json::Value::ofString("dynamic");
            const auto* rb = obj->getComponent<RigidBody>();
            Json::Value r = Json::Value::object();
            r["mass"] = Json::Value::ofNumber(rb->mass);
            r["restitution"] = Json::Value::ofNumber(rb->restitution);
            r["friction"] = Json::Value::ofNumber(rb->friction);
            r["useGravity"] = Json::Value::ofBool(rb->useGravity);
            r["kinematic"] = Json::Value::ofBool(rb->kinematic);
            o["rigid"] = r;
        } else if (obj->getComponent<StaticBody>()) {
            o["physics"] = Json::Value::ofString("static");
        }
        if (const auto* col = obj->getComponent<Collider>()) {
            Json::Value c = Json::Value::object();
            c["size"] = vec3Json(col->size);
            c["trigger"] = Json::Value::ofBool(col->isTrigger);
            o["collider"] = c;
        }
        if (const auto* cam = obj->getComponent<Camera>()) {
            Json::Value c = Json::Value::object();
            c["projection"] = Json::Value::ofString(
                cam->projection == Camera::Projection::Orthographic ? "ortho" : "perspective");
            c["fov"] = Json::Value::ofNumber(cam->fov);
            c["near"] = Json::Value::ofNumber(cam->nearPlane);
            c["far"] = Json::Value::ofNumber(cam->farPlane);
            c["orthoHeight"] = Json::Value::ofNumber(cam->orthoHeight);
            c["active"] = Json::Value::ofBool(cam->active);
            o["camera"] = c;
        }
        const auto scripts = obj->getScripts();
        if (!scripts.empty()) {
            Json::Value arr = Json::Value::array();
            for (const auto* sc : scripts) {
                arr.push(Json::Value::ofString(sc->scriptPath));
            }
            o["scripts"] = arr;
        }
        Json::Value extraComponents = Json::Value::array();
        for (const auto& component : obj->components) {
            const std::string type = component->typeName();
            if (type == "MeshRenderer" || type == "RigidBody" || type == "StaticBody" ||
                type == "Collider" || type == "Camera" || type == "ScriptComponent") {
                continue;
            }
            Json::Value compJson = component->toJson();
            compJson["type"] = Json::Value::ofString(type);
            extraComponents.push(compJson);
        }
        if (extraComponents.size() > 0) {
            o["components"] = extraComponents;
        }
        objects.push(o);
    }
    root["objects"] = objects;
    return root.dump();
}

} // namespace VelvX