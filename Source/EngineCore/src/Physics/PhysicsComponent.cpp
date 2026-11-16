#include "Physics/PhysicsComponent.hpp"
#include "Core/GameObject.hpp"
#include "Core/Scene.hpp"
#include "Engine/Engine.hpp"
#include "Renderer/RenderComponent.hpp"
#include <algorithm>
#include <cmath>

namespace VelvX {

glm::vec3 PhysicsWorld::gravity{0.0f, -9.81f, 0.0f};

glm::vec3 PhysicsWorld::getGravity() {
    return gravity;
}

void PhysicsWorld::setGravity(const glm::vec3& g) {
    gravity = g;
}

RigidBody::RigidBody(GameObject* owner) : Component(owner) {
    const std::string& n = owner->name;
    Inspector::expose(n, "mass", "float", &mass, this);
    Inspector::expose(n, "velocity", "glm::vec3", &velocity, this);
    Inspector::expose(n, "useGravity", "bool", &useGravity, this);
    Inspector::expose(n, "restitution", "float", &restitution, this);
    Inspector::expose(n, "friction", "float", &friction, this);
    Inspector::expose(n, "kinematic", "bool", &kinematic, this);
}

Collider::Collider(GameObject* owner) : Component(owner) {
    const std::string& n = owner->name;
    Inspector::expose(n, "size", "glm::vec3", &size, this);
    Inspector::expose(n, "isTrigger", "bool", &isTrigger, this);
}

static std::vector<RigidBody*>& bodies() {
    static std::vector<RigidBody*> list;
    return list;
}

void PhysicsWorld::add(RigidBody* body) {
    if (!body) {
        return;
    }
    auto& list = bodies();
    if (std::find(list.begin(), list.end(), body) == list.end()) {
        list.push_back(body);
    }
}

void PhysicsWorld::remove(RigidBody* body) {
    auto& list = bodies();
    list.erase(std::remove(list.begin(), list.end(), body), list.end());
}

namespace {

/** @brief Meia-extensão AABB de um objeto (Collider ou escala do mesh). */
glm::vec3 halfExtent(GameObject* obj) {
    if (const auto* col = obj->getComponent<Collider>()) {
        return obj->transform.scale * col->size * 0.5f;
    }
    return obj->transform.scale * 0.5f;
}

/** @brief Alvo sólido para resolução de colisão. */
struct Solid {
    GameObject* obj = nullptr;
    RigidBody* rb = nullptr;
    int layer = 0;
    bool trigger = false;
    glm::vec3 vel{0.0f}; ///< Velocidade de apoio (kinematic movido por script).
};

} // namespace

void PhysicsWorld::step(float dt) {
    Scene* scene = Engine::getSceneManager().getActiveScene();
    const ProjectSettings& settings = Engine::settings();
    const float clamped = std::min(dt, 1.0f / 30.0f);

    // Coleta alvos de colisão da cena (estáticos, dinâmicos e kinematic).
    std::vector<Solid> solids;
    if (scene) {
        for (const auto& objPtr : scene->gameObjects) {
            GameObject* obj = objPtr.get();
            const auto* col = obj->getComponent<Collider>();
            const auto* mesh = obj->getComponent<MeshRenderer>();
            if (!col && !mesh) {
                continue;
            }
            Solid s;
            s.obj = obj;
            s.rb = obj->getComponent<RigidBody>();
            s.layer = obj->layer;
            s.trigger = col && col->isTrigger;
            if (s.rb && s.rb->kinematic) {
                // Kinematic movido por script: velocidade = deslocamento desde o
                // passo anterior, para o atrito carregar quem está em cima.
                if (s.rb->hasPrevPos && clamped > 0.0f) {
                    s.vel = (obj->transform.position - s.rb->prevPos) / clamped;
                }
                s.rb->prevPos = obj->transform.position;
                s.rb->hasPrevPos = true;
            }
            solids.push_back(s);
        }
    }

    std::vector<RigidBody*> dyn;
    for (RigidBody* rb : bodies()) {
        if (rb->owner() && !rb->kinematic) {
            rb->grounded = false;
            dyn.push_back(rb);
        }
    }
    if (dyn.empty()) {
        return;
    }

    // Substeps mantêm a penetração rasa: a ejeição escolhe sempre o lado certo
    // da parede e o empilhamento dinâmico não explode.
    const int substeps = 3;
    const float h = clamped / static_cast<float>(substeps);
    const float slop = 0.005f;
    const float percent = 0.8f;

    struct Contact {
        RigidBody* a = nullptr;
        RigidBody* b = nullptr; // nulo => estático/kinematic
        glm::vec3 n{0.0f};
        glm::vec3 vb{0.0f}; ///< Velocidade do apoio não-dinâmico.
    };

    for (int sub = 0; sub < substeps; ++sub) {
        for (RigidBody* rb : dyn) {
            if (rb->useGravity) {
                rb->velocity += gravity * h;
            }
            rb->owner()->transform.position += rb->velocity * h;
        }

        std::vector<Contact> contacts;
        for (int iter = 0; iter < 2; ++iter) {
            for (RigidBody* rb : dyn) {
                GameObject* obj = rb->owner();
                Transform& tf = obj->transform;
                const glm::vec3 half = halfExtent(obj);
                const float invMassA = 1.0f / std::max(rb->mass, 0.0001f);

                for (const Solid& s : solids) {
                    if (s.obj == obj || s.trigger) {
                        continue;
                    }
                    if (!settings.layerCollides(obj->layer, s.layer)) {
                        continue;
                    }
                    const glm::vec3 sh = halfExtent(s.obj);
                    const glm::vec3 center = s.obj->transform.position;
                    const glm::vec3 d = tf.position - center;
                    const glm::vec3 overlap = half + sh - glm::vec3(std::fabs(d.x), std::fabs(d.y), std::fabs(d.z));
                    if (overlap.x <= 0.0f || overlap.y <= 0.0f || overlap.z <= 0.0f) {
                        continue;
                    }

                    int axis = 0;
                    if (overlap.y < overlap[axis]) axis = 1;
                    if (overlap.z < overlap[axis]) axis = 2;
                    glm::vec3 n(0.0f);
                    n[axis] = (d[axis] >= 0.0f) ? 1.0f : -1.0f;
                    const float pen = overlap[axis];

                    const bool otherDynamic = s.rb && !s.rb->kinematic;
                    const float invMassB = otherDynamic ? 1.0f / std::max(s.rb->mass, 0.0001f) : 0.0f;
                    const float totalInv = invMassA + invMassB;
                    if (totalInv <= 0.0f) {
                        continue;
                    }

                    // Correção posicional com folga: repouso sem jitter.
                    const float corr = std::max(pen - slop, 0.0f) * percent / totalInv;
                    tf.position += n * (corr * invMassA);
                    if (otherDynamic) {
                        s.obj->transform.position -= n * (corr * invMassB);
                    }

                    // Impulso sobre a velocidade RELATIVA (apoio em corpo móvel).
                    const glm::vec3 vB = otherDynamic ? s.rb->velocity : s.vel;
                    const float rel = glm::dot(rb->velocity - vB, n);
                    if (rel < 0.0f) {
                        const float rest = (rel < -1.0f)
                            ? (otherDynamic ? std::min(rb->restitution, s.rb->restitution) : rb->restitution)
                            : 0.0f;
                        const float j = -(1.0f + rest) * rel / totalInv;
                        rb->velocity += n * (j * invMassA);
                        if (otherDynamic) {
                            s.rb->velocity -= n * (j * invMassB);
                        }
                    }

                    if (n.y > 0.5f) {
                        rb->grounded = true;
                    }
                    if (iter == 1) {
                        contacts.push_back(Contact{rb, otherDynamic ? s.rb : nullptr, n, s.vel});
                    }
                }
            }
        }

        // Atrito tangencial relativo: transfere momento do apoio para o corpo
        // (ficar em cima de um cubo que se move carrega o jogador junto).
        for (const Contact& c : contacts) {
            const float invMassA = 1.0f / std::max(c.a->mass, 0.0001f);
            const float invMassB = c.b ? 1.0f / std::max(c.b->mass, 0.0001f) : 0.0f;
            const float totalInv = invMassA + invMassB;
            if (totalInv <= 0.0f) {
                continue;
            }
            const glm::vec3 vB = c.b ? c.b->velocity : c.vb;
            const glm::vec3 relV = c.a->velocity - vB;
            const glm::vec3 relT = relV - c.n * glm::dot(relV, c.n);
            const float mu = std::clamp(c.a->friction, 0.0f, 1.0f);
            const float factor = std::clamp(mu * 60.0f * h, 0.0f, 1.0f);
            c.a->velocity -= relT * (factor * invMassA / totalInv);
            if (c.b) {
                c.b->velocity += relT * (factor * invMassB / totalInv);
            }
        }

        // Plano de chão global (y = 0) como rede de segurança.
        for (RigidBody* rb : dyn) {
            Transform& tf = rb->owner()->transform;
            const glm::vec3 half = halfExtent(rb->owner());
            if (settings.useGroundPlane && tf.position.y < half.y) {
                tf.position.y = half.y;
                if (rb->velocity.y < 0.0f) {
                    rb->velocity.y = (rb->velocity.y < -1.0f) ? -rb->velocity.y * rb->restitution : 0.0f;
                }
                rb->grounded = true;
            }
        }
    }
}

} // namespace VelvX