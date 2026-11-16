/**
 * @file interpreter_tests.cpp
 * @brief Bateria headless da Velv Language e do núcleo (sem janela/GL).
 *
 * Verifica, de verdade:
 *   - "let" foi removido da linguagem (script com let não carrega);
 *   - "lock" declara variável privada (fora do Inspector);
 *   - "unlock" expõe a variável ao Inspector com tipo/endereço corretos;
 *   - ligação bidirecional unlock <-> Inspector (edição em runtime);
 *   - books, if/else, while, aritmética, vec3, str, clamp, booleanos;
 *   - estados por instância isolados (múltiplos scripts) e seus campos unlock.
 *
 * Retorna 0 se todos os testes passam, 1 caso contrário.
 */

#include "Core/Inspector.hpp"
#include "Core/Log.hpp"
#include "Core/Scene.hpp"
#include "Core/SceneManager.hpp"
#include "Engine/Engine.hpp"
#include "Physics/CollisionSystem.hpp"
#include "Physics/PhysicsComponent.hpp"
#include "Renderer/Camera.hpp"
#include "Renderer/RenderComponent.hpp"
#include "Scripting/ScriptComponent.hpp"
#include "Scripting/ScriptEngine.hpp"

#include <cmath>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <string>
#include <vector>

using namespace VelvX;

namespace {

int g_pass = 0;
int g_fail = 0;
std::vector<std::string> g_logs;

void check(bool cond, const std::string& label) {
    if (cond) {
        ++g_pass;
        std::cout << "[PASS] " << label << '\n';
    } else {
        ++g_fail;
        std::cout << "[FAIL] " << label << '\n';
    }
}

void clearLogs() { g_logs.clear(); }

bool saw(const std::string& substr) {
    for (const auto& line : g_logs) {
        if (line.find(substr) != std::string::npos) {
            return true;
        }
    }
    return false;
}

std::filesystem::path g_tmp;

std::filesystem::path writeScript(const std::string& name, const std::string& code) {
    const std::filesystem::path path = g_tmp / name;
    std::ofstream out(path, std::ios::out | std::ios::trunc);
    out << code;
    out.close();
    return path;
}

const Inspector::Field* findField(const std::string& name, const void* base) {
    for (const auto& f : Inspector::fields()) {
        if (f.name == name && f.base == base) {
            return &f;
        }
    }
    return nullptr;
}

// --- Testes ---------------------------------------------------------------

void testLetRemoved() {
    clearLogs();
    const auto path = writeScript("let_rejected.velv",
        "let x = 5;\nbook Main() {}\nbook InGame() {}\n");
    const bool loaded = ScriptEngine::loadFile(path);
    check(!loaded, "let nao faz parte da linguagem (script com let nao carrega)");
}

void testLockUnlockAccepted() {
    clearLogs();
    const auto path = writeScript("lock_unlock.velv",
        "lock a = 1;\nunlock b = 2;\nbook Main() {}\nbook InGame() {}\n");
    const bool loaded = ScriptEngine::loadFile(path);
    check(loaded, "lock/unlock carregam sem erro");

    const void* base = ScriptEngine::variableBase();
    const Inspector::Field* pub = findField("b", base);
    check(pub != nullptr, "unlock 'b' exposto ao Inspector");
    check(pub && pub->type == "double", "unlock numerico tem tipo 'double'");
    check(findField("a", base) == nullptr, "lock 'a' permanece privado (fora do Inspector)");
}

void testBinding() {
    clearLogs();
    const auto path = writeScript("binding.velv",
        "unlock speed = 6;\n"
        "book Main() {}\nbook InGame() {}\n"
        "book Probe() { print(\"SPEED\", speed); }\n"
        "book Mutate() { speed = 99; }\n");
    check(ScriptEngine::loadFile(path), "script de binding carrega");

    const void* base = ScriptEngine::variableBase();
    const Inspector::Field* f = findField("speed", base);
    check(f != nullptr, "campo 'speed' encontrado");
    if (!f) {
        return;
    }

    // Inspector -> script: escreve no endereco e o script le o novo valor.
    *static_cast<double*>(const_cast<void*>(f->address)) = 12.5;
    clearLogs();
    ScriptEngine::callBook("Probe");
    check(saw("SPEED 12.5"), "Inspector -> script: edicao reflete no interpretador");

    // Script -> Inspector: o script escreve e o endereco reflete.
    ScriptEngine::callBook("Mutate");
    const double readBack = *static_cast<const double*>(f->address);
    check(readBack == 99.0, "script -> Inspector: escrita reflete no endereco");
}

void testLanguageCore() {
    ScriptEngine::shutdown();
    clearLogs();
    ScriptEngine::interpretVelvCode(
        "book Main() {\n"
        "  lock s = 0;\n"
        "  lock i = 0;\n"
        "  while (i < 5) { s = s + i; i = i + 1; }\n"
        "  print(\"SUM\", s);\n"
        "  if (s == 10) { print(\"BRANCH OK\"); } else { print(\"BRANCH BAD\"); }\n"
        "  lock v = vec3(1, 2, 3);\n"
        "  print(\"VEC\", v);\n"
        "  print(\"CONCAT\", \"a\" + \"b\");\n"
        "  print(\"CLAMP\", clamp(15, 0, 10));\n"
        "  print(\"BOOL\", true && false, true || false);\n"
        "}\n"
        "book InGame() {}\n");
    ScriptEngine::executeMain();
    check(saw("SUM 10"), "while + aritmetica (soma 0..4 = 10)");
    check(saw("BRANCH OK") && !saw("BRANCH BAD"), "if/else seleciona o ramo correto");
    check(saw("VEC vec3(1, 2, 3)"), "vec3 e acesso a membros .x/.y/.z");
    check(saw("CONCAT ab"), "concatenacao de strings");
    check(saw("CLAMP 10"), "clamp nativo");
    check(saw("BOOL false true"), "operadores booleanos && / ||");
}

void testBookParamsReturnBreak() {
    ScriptEngine::shutdown();
    clearLogs();
    const auto path = writeScript("params_return_break.velv",
        "book add(a, b) { return a + b; }\n"
        "book fat(n) { if (n <= 1) { return 1; } return n * fat(n - 1); }\n"
        "book early() { print(\"BEFORE\"); return; print(\"AFTER\"); }\n"
        "book Main() {\n"
        "  print(\"ADD\", add(2, 3));\n"
        "  print(\"FAT\", fat(5));\n"
        "  early();\n"
        "  lock s = 0;\n"
        "  lock i = 0;\n"
        "  while (i < 100) { if (i == 5) { break; } s = s + i; i = i + 1; }\n"
        "  print(\"BREAKSUM\", s);\n"
        "}\n"
        "book InGame() {}\n");
    check(ScriptEngine::loadFile(path), "books com parametros carregam");
    ScriptEngine::executeMain();
    check(saw("ADD 5"), "book com parametros recebe argumentos (2+3=5)");
    check(saw("FAT 120"), "recursao com return (fat(5)=120)");
    check(saw("BEFORE") && !saw("AFTER"), "return sem valor encerra o book");
    check(saw("BREAKSUM 10"), "break interrompe o while (soma 0..4=10)");

    clearLogs();
    const auto badPath = writeScript("arity.velv",
        "book add(a, b) { return a + b; }\n"
        "book Main() { print(\"X\", add(1)); }\n"
        "book InGame() {}\n");
    check(ScriptEngine::loadFile(badPath), "script de aridade carrega (erro so em runtime)");
    ScriptEngine::executeMain();
    check(saw("espera 2"), "aridade errada reporta erro claro");
}

void testPerInstance() {
    ScriptEngine::shutdown();
    clearLogs();
    int markerA = 0;
    int markerB = 0;

    const auto pathA = writeScript("inst_a.velv",
        "unlock val = 1;\nbook Main() {}\nbook InGame() {}\nbook Show() { print(\"A\", val); }\n");
    const auto pathB = writeScript("inst_b.velv",
        "unlock val = 2;\nbook Main() {}\nbook InGame() {}\nbook Show() { print(\"B\", val); }\n");

    ScriptState* a = ScriptEngine::createState();
    ScriptState* b = ScriptEngine::createState();
    check(ScriptEngine::loadStateFile(a, pathA, nullptr, &markerA, "ObjA"), "estado A carrega");
    check(ScriptEngine::loadStateFile(b, pathB, nullptr, &markerB, "ObjB"), "estado B carrega");

    clearLogs();
    ScriptEngine::callStateBook(a, "Show");
    ScriptEngine::callStateBook(b, "Show");
    check(saw("A 1"), "estado A isolado (val = 1)");
    check(saw("B 2"), "estado B isolado (val = 2)");

    const Inspector::Field* fa = findField("val", &markerA);
    const Inspector::Field* fb = findField("val", &markerB);
    check(fa && *static_cast<const double*>(fa->address) == 1.0, "unlock de A vinculado a base A");
    check(fb && *static_cast<const double*>(fb->address) == 2.0, "unlock de B vinculado a base B");

    ScriptEngine::destroyState(a);
    ScriptEngine::destroyState(b);
    check(findField("val", &markerA) == nullptr && findField("val", &markerB) == nullptr,
          "destroyState remove campos unlock do Inspector");
}

// --- Física ---------------------------------------------------------------

GameObject* addBox(Scene* scene, const std::string& name, const glm::vec3& pos,
                   const glm::vec3& scale, bool dynamic, int layer) {
    auto obj = std::make_unique<GameObject>(name);
    obj->transform.setPosition(pos);
    obj->transform.setScale(scale);
    obj->layer = layer;
    obj->addComponent<MeshRenderer>()->mesh = MeshType::Cube;
    if (dynamic) {
        obj->addComponent<RigidBody>();
    } else {
        obj->addComponent<StaticBody>();
    }
    return scene->addObject(std::move(obj));
}

void stepFor(int frames) {
    for (int i = 0; i < frames; ++i) {
        PhysicsWorld::step(1.0f / 60.0f);
    }
}

// Regressao: o tamanho do Collider e LOCAL (multiplicado pela escala do
// Transform). Conteudo deve usar size unitario + escala = dimensao mundial.
// Um size ja mundial duplicaria a escala e inflaria a AABB, teleportando o
// corpo para a borda do chao (bug observado no demo 2D).
void testColliderConvention() {
    ProjectSettings& settings = Engine::settings();
    settings.ensureCollisionMatrix();
    settings.useGroundPlane = false;
    PhysicsWorld::setGravity(glm::vec3(0.0f, -9.81f, 0.0f));

    SceneManager& sm = Engine::getSceneManager();
    sm.registerScene("ColTest", std::make_unique<Scene>("ColTest"));
    sm.loadScene("ColTest");
    Scene* scene = sm.getActiveScene();
    scene->gameObjects.clear();

    auto ground = std::make_unique<GameObject>("Ground");
    ground->transform.setPosition(glm::vec3(0, -1, 0));
    ground->transform.setScale(glm::vec3(34, 2, 1));
    ground->addComponent<MeshRenderer>()->mesh = MeshType::Cube;
    ground->addComponent<StaticBody>();
    ground->addComponent<Collider>()->size = glm::vec3(1, 1, 1);
    scene->addObject(std::move(ground));

    auto player = std::make_unique<GameObject>("Player");
    player->transform.setPosition(glm::vec3(-12, 1, 0));
    player->transform.setScale(glm::vec3(1, 1.4f, 1));
    player->addComponent<MeshRenderer>()->mesh = MeshType::Cube;
    auto* rb = player->addComponent<RigidBody>();
    rb->restitution = 0.0f;
    rb->friction = 0.0f;
    player->addComponent<Collider>()->size = glm::vec3(1, 1, 1);
    GameObject* p = scene->addObject(std::move(player));

    stepFor(120);
    check(std::fabs(p->transform.position.x - (-12.0f)) < 0.01f,
          "colisor local: sem teleporte horizontal (size unitario x escala)");
    check(std::fabs(p->transform.position.y - 0.7f) < 0.05f,
          "colisor local: repousa no topo do chao (y~0.7)");
    check(rb->grounded, "colisor local: grounded ao repousar");

    sm.clear();
}

// Regressao (playtest): empilhamento dinâmico estável, apoio em corpo móvel
// e contenção contra parede. O solver antigo refletia velocidade absoluta e
// depenetrava 100% num passe só -> jitter ao subir em cubo e ejeção para fora
// da arena ao empilhar perto da parede.
void testStackAndRide() {
    ProjectSettings& settings = Engine::settings();
    settings.ensureCollisionMatrix();
    settings.useGroundPlane = false;
    PhysicsWorld::setGravity(glm::vec3(0.0f, -9.81f, 0.0f));

    SceneManager& sm = Engine::getSceneManager();
    sm.registerScene("StackTest", std::make_unique<Scene>("StackTest"));
    sm.loadScene("StackTest");
    Scene* scene = sm.getActiveScene();
    scene->gameObjects.clear();

    addBox(scene, "Ground", glm::vec3(0, -1, 0), glm::vec3(34, 2, 34), false, 0);
    GameObject* a = addBox(scene, "CubeA", glm::vec3(0, 0.5f, 0), glm::vec3(1, 1, 1), true, 0);
    GameObject* b = addBox(scene, "CubeB", glm::vec3(0, 1.5f, 0), glm::vec3(1, 1, 1), true, 0);
    auto* rbA = a->getComponent<RigidBody>();
    auto* rbB = b->getComponent<RigidBody>();
    rbA->restitution = 0.0f;
    rbB->restitution = 0.0f;
    stepFor(240);
    check(std::fabs(a->transform.position.y - 0.5f) < 0.05f, "pilha: cubeA repousa no chao (y~0.5)");
    check(std::fabs(b->transform.position.y - 1.5f) < 0.08f, "pilha: cubeB repousa sobre cubeA (y~1.5)");
    check(std::fabs(a->transform.position.x) < 0.05f && std::fabs(b->transform.position.x) < 0.05f,
          "pilha: sem deriva horizontal");
    check(rbB->grounded, "pilha: grounded sobre corpo dinâmico");

    sm.clear();
    sm.registerScene("RideTest", std::make_unique<Scene>("RideTest"));
    sm.loadScene("RideTest");
    scene = sm.getActiveScene();
    scene->gameObjects.clear();

    GameObject* plat = addBox(scene, "Plat", glm::vec3(0, 0, 0), glm::vec3(4, 1, 4), true, 0);
    auto* prb = plat->getComponent<RigidBody>();
    prb->kinematic = true;
    prb->useGravity = false;
    GameObject* rider = addBox(scene, "Rider", glm::vec3(0, 1.0f, 0), glm::vec3(1, 1, 1), true, 0);
    auto* rrb = rider->getComponent<RigidBody>();
    rrb->restitution = 0.0f;
    rrb->friction = 0.5f;
    for (int i = 0; i < 180; ++i) {
        plat->transform.position.x += 2.0f / 60.0f;
        PhysicsWorld::step(1.0f / 60.0f);
    }
    check(std::fabs(rider->transform.position.x - plat->transform.position.x) < 0.15f,
          "apoio movel: jogador carregado pela plataforma kinematic");
    check(std::fabs(rider->transform.position.y - 1.0f) < 0.08f,
          "apoio movel: altura estavel sobre a plataforma");
    check(rrb->grounded, "apoio movel: grounded sobre kinematic");

    addBox(scene, "Wall", glm::vec3(10, 1.5f, 0), glm::vec3(1, 4, 8), false, 0);
    for (int i = 0; i < 120; ++i) {
        rrb->velocity.x = 11.0f;
        PhysicsWorld::step(1.0f / 60.0f);
    }
    check(rider->transform.position.x < 9.05f && rider->transform.position.x > 8.0f,
          "contencao: correr contra a parede para dentro, sem atravessar/ejetar");

    sm.clear();
}

void testPhysics() {
    ProjectSettings& settings = Engine::settings();
    settings.ensureCollisionMatrix();
    settings.useGroundPlane = false; // colisão vem só da geometria
    PhysicsWorld::setGravity(glm::vec3(0.0f, -9.81f, 0.0f));

    SceneManager& sm = Engine::getSceneManager();
    sm.registerScene("PhysTest", std::make_unique<Scene>("PhysTest"));
    sm.loadScene("PhysTest");
    Scene* scene = sm.getActiveScene();
    scene->gameObjects.clear();

    // Chão estático com topo em y = 0.
    addBox(scene, "Ground", glm::vec3(0, -0.5f, 0), glm::vec3(10, 1, 10), false, 0);
    // Caixa dinâmica caindo de y = 5.
    GameObject* box = addBox(scene, "Box", glm::vec3(0, 5, 0), glm::vec3(1, 1, 1), true, 0);
    auto* rb = box->getComponent<RigidBody>();
    rb->restitution = 0.0f;
    rb->friction = 0.0f;

    stepFor(180);
    check(std::fabs(box->transform.position.y - 0.5f) < 0.05f, "colisao AABB: caixa repousa sobre o chao (y~0.5)");
    check(rb->grounded, "ground detection: grounded = true ao repousar");
    check(std::fabs(rb->velocity.y) < 0.05f, "resposta: velocidade vertical zerada no repouso");

    // Gravidade por objeto (desligada => nao cai).
    rb->useGravity = false;
    box->transform.setPosition(glm::vec3(0, 5, 0));
    rb->velocity = glm::vec3(0.0f);
    stepFor(60);
    check(box->transform.position.y > 4.9f, "gravidade por objeto: useGravity=false mantem a caixa no ar");

    // Matriz de colisao: camadas 1 x 2 desligadas => caixa atravessa o chao.
    rb->useGravity = true;
    box->layer = 1;
    scene->findGameObject("Ground")->layer = 2;
    settings.collisionMatrix[1][2] = false;
    settings.collisionMatrix[2][1] = false;
    box->transform.setPosition(glm::vec3(0, 5, 0));
    rb->velocity = glm::vec3(0.0f);
    stepFor(180);
    check(box->transform.position.y < 0.0f, "matriz de colisao: camada desligada deixa a caixa atravessar");

    // Religa a matriz => volta a repousar.
    settings.collisionMatrix[1][2] = true;
    settings.collisionMatrix[2][1] = true;
    box->transform.setPosition(glm::vec3(0, 5, 0));
    rb->velocity = glm::vec3(0.0f);
    stepFor(180);
    check(std::fabs(box->transform.position.y - 0.5f) < 0.05f, "matriz de colisao: camada religada restaura a colisao");

    // CollisionSystem.overlaps respeita a matriz.
    addBox(scene, "Coin", glm::vec3(0, 0.5f, 0), glm::vec3(1, 1, 1), false, 3);
    box->transform.setPosition(glm::vec3(0, 0.5f, 0));
    rb->velocity = glm::vec3(0.0f);
    CollisionSystem::update();
    check(CollisionSystem::overlaps("Box", "Coin"), "overlaps detecta sobreposicao (matriz ligada)");
    settings.collisionMatrix[1][3] = false;
    settings.collisionMatrix[3][1] = false;
    check(!CollisionSystem::overlaps("Box", "Coin"), "overlaps respeita matriz (camada desligada)");

    sm.clear();
}

// --- Serialização (backbone do undo/redo do Editor) -----------------------

void testSceneSerialization() {
    SceneManager& sm = Engine::getSceneManager();
    sm.registerScene("SerTest", std::make_unique<Scene>("SerTest"));
    sm.loadScene("SerTest");
    Scene* scene = sm.getActiveScene();
    scene->gameObjects.clear();
    scene->cameraMode = Scene::CameraMode::Ortho2D;
    scene->orthoHeight = 20.0f;

    addBox(scene, "Ground", glm::vec3(0, -1, 0), glm::vec3(20, 2, 20), false, 0);
    GameObject* box = addBox(scene, "Box", glm::vec3(1, 2, 3), glm::vec3(1, 1, 1), true, 2);
    box->getComponent<RigidBody>()->mass = 3.0f;
    box->addComponent<Collider>()->size = glm::vec3(1, 1, 1);
    box->tag = "player";

    auto camObj = std::make_unique<GameObject>("MainCam");
    camObj->transform.setPosition(glm::vec3(0, 5, 10));
    auto* cam = camObj->addComponent<Camera>();
    cam->fov = 60.0f;
    cam->active = true;
    scene->addObject(std::move(camObj));

    GameObject* parentPtr = scene->addObject(std::make_unique<GameObject>("Parent"));
    GameObject* childPtr = scene->addObject(std::make_unique<GameObject>("Child"));
    childPtr->setParent(parentPtr);

    const auto scriptPath = writeScript("ser_script.velv",
        "unlock hp = 10;\nbook Main() {}\nbook InGame() {}\n");
    box->addScript(scriptPath.string());

    const std::string json = scene->serialize();
    check(!json.empty(), "serialize: JSON nao vazio");
    check(scene->deserialize(json), "deserialize: round-trip aplica sem erro");
    check(scene->gameObjects.size() == 5, "round-trip: 5 objetos preservados");
    check(scene->cameraMode == Scene::CameraMode::Ortho2D, "round-trip: cameraMode preservado");
    check(std::fabs(scene->orthoHeight - 20.0f) < 0.001f, "round-trip: orthoHeight preservado");

    GameObject* box2 = scene->findGameObject("Box");
    check(box2 != nullptr, "round-trip: Box encontrado");
    check(box2 && box2->tag == "player", "round-trip: tag preservada");
    check(box2 && box2->layer == 2, "round-trip: layer preservada");
    check(box2 && std::fabs(box2->transform.position.x - 1.0f) < 0.001f, "round-trip: posicao preservada");
    check(box2 && box2->getComponent<RigidBody>() &&
              std::fabs(box2->getComponent<RigidBody>()->mass - 3.0f) < 0.001f,
          "round-trip: RigidBody.mass preservado");
    check(box2 && box2->getComponent<Collider>() != nullptr, "round-trip: Collider preservado");
    check(box2 && box2->getScripts().size() == 1, "round-trip: script reanexado");

    GameObject* cam2 = scene->findGameObject("MainCam");
    check(cam2 && cam2->getComponent<Camera>() &&
              std::fabs(cam2->getComponent<Camera>()->fov - 60.0f) < 0.001f,
          "round-trip: Camera.fov preservado");
    check(scene->getActiveCamera() != nullptr, "round-trip: camera ativa resolvida");

    GameObject* child2 = scene->findGameObject("Child");
    GameObject* parent2 = scene->findGameObject("Parent");
    check(child2 && parent2 && child2->parent == parent2, "round-trip: hierarquia pai/filho preservada");

    sm.clear();
}

void testCameraActive() {
    SceneManager& sm = Engine::getSceneManager();
    sm.registerScene("CamTest", std::make_unique<Scene>("CamTest"));
    sm.loadScene("CamTest");
    Scene* scene = sm.getActiveScene();
    scene->gameObjects.clear();

    auto c1 = std::make_unique<GameObject>("Cam1");
    c1->addComponent<Camera>()->active = false;
    scene->addObject(std::move(c1));
    auto c2 = std::make_unique<GameObject>("Cam2");
    auto* cam2 = c2->addComponent<Camera>();
    cam2->active = true;
    GameObject* c2ptr = scene->addObject(std::move(c2));

    check(scene->getActiveCamera() == cam2, "camera ativa: escolhe a marcada active");
    auto clone = c2ptr->clone();
    check(clone->getComponent<Camera>() != nullptr, "clone: Camera preservada");
    check(clone->getComponent<Camera>() && clone->getComponent<Camera>()->active,
          "clone: Camera.active preservada");

    sm.clear();
}

} // namespace

int main() {
    g_tmp = std::filesystem::temp_directory_path() / "velvx_tests";
    std::filesystem::create_directories(g_tmp);

    Log::setConsoleSink([](const std::string& level, const std::string& message) {
        g_logs.push_back("[" + level + "] " + message);
    });

    std::cout << "=== VelvX: bateria de testes do interpretador / nucleo ===\n";
    testLetRemoved();
    testLockUnlockAccepted();
    testBinding();
    testLanguageCore();
    testBookParamsReturnBreak();
    testPerInstance();
    ScriptEngine::shutdown();
    testPhysics();
    testColliderConvention();
    testStackAndRide();
    testSceneSerialization();
    testCameraActive();

    std::cout << "=== Resultado: " << g_pass << " passaram, " << g_fail << " falharam ===\n";
    return g_fail == 0 ? 0 : 1;
}