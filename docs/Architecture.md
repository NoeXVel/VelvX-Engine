# VelvX Engine — Arquitetura

Visão técnica dos módulos, do pipeline de frame e das convenções do código.
Para uso do editor e da linguagem, veja [GettingStarted.md](GettingStarted.md)
e [VelvLanguage.md](VelvLanguage.md).

---

## 1. Layout do repositório

```
Source/
├── EngineCore/          # núcleo compartilhado (DLL VelvXEngineCore)
│   ├── include/ + src/
│   │   ├── Core/        # GameObject, Scene, SceneManager, Transform, Inspector, Log, FS
│   │   ├── Engine/      # Engine (loop, tempo, settings), bootstrap
│   │   ├── Renderer/    # OpenGL 3.3 core: malhas, sombras, ortho2d, sprites, texto
│   │   ├── Scripting/   # Velv Language (lexer/parser/interpretador) + ScriptComponent
│   │   ├── Physics/     # AABB, RigidBody, gravidade por objeto, matriz de layers
│   │   ├── UI/          # UIElement/UIButton/UIText/UIImage/UIPanel + UIManager
│   │   ├── Input/       # InputManager (teclas, ações, eixos, captura de cursor)
│   │   ├── PluginAPI/   # registro de plugins nativos
│   │   └── Utils/       # JSON próprio, filesystem, strings
│   └── main.cpp         # VelvXPlayer (runtime standalone)
├── Editor/              # VelvXEditor (ImGui docking)
├── CodeEditor/          # widget de código compartilhado (Editor + ScriptIDE)
├── ScriptIDE/           # IDE standalone de painel único
├── UIEditor/            # editor visual de prefabs .ui
├── Launcher/            # hub de projetos
├── Tests/               # bateria headless (VelvXTests)
├── Plugins/             # exemplo CustomPhysics
├── templates/           # EmptyProject, 2DPlatformerTemplate, 3DFPSTemplate
├── Demos/               # Game1_2D (CRYSTAL RUSH), Game2_3D (ARENA NEON)
└── third_party/         # GLFW, GLM, GLAD, ImGui (docking), Assimp, miniz, stb_image
```

## 2. Pipeline de frame

```
Engine::mainLoop
├── rawDt = min(clock delta, 0.1)
├── deltaTime = rawDt * timeScale          # Time.setScale(0) = pause real
├── time += deltaTime
├── InputManager::update                   # teclas/axes/mouse (tempo real)
├── executeInGame()                        # book InGame de todos os scripts
├── PhysicsWorld::step(deltaTime)          # congelado quando timeScale = 0
├── CollisionSystem::sync                  # AABBs + triggers + overlaps
├── UIManager::update(rawDt)               # UI vive no tempo REAL (menus no pause)
├── Renderer::render                       # 3D com sombras + ortho2d + sprites
│   └── UIManager::render                  # UI por último, sobre a cena
└── present
```

Consequências de design:

- **Pause verdadeiro**: `dt()`, `time()` e física escalam juntos; a UI não.
- **Câmera é objeto**: componente Camera em GameObject, `active` resolve a
  câmera da cena; scripts de follow anexados à própria câmera.
- **Scripts por instância**: cada ScriptComponent tem seu `ScriptState`
  (globals + books + campos unlock próprios no Inspector).

## 3. Velv Language (Scripting)

- Lexer → parser recursivo → AST (`Expr`/`Stmt`) → interpretador com
  `Environment` encadeado (escopo léxico real).
- Books armazenados como `Book { params, body }`; chamadas verificam aridade;
  `return`/`break` via sinais de controle (`ReturnSignal`/`BreakSignal`).
- Natives resolvidos por namespace (`GameObject.*`, `UI.*`, `Physics.*`...);
  books do próprio estado são chamáveis como funções (recursão suportada).
- Erros carregam arquivo:linha; script quebrado é desativado com log claro.
- `unlock` registra campos no **Inspector** com endereço real por instância
  (edição em runtime reflete no jogo imediatamente).

## 4. Física

- AABB por collider; tamanho **local** × escala do Transform (convenção
  documentada e coberta por teste de regressão).
- Corpos dinâmicos: massa, restituição, atrito, `useGravity` por objeto;
  gravidade global ajustável (`Physics.setGravity`).
- `StaticBody` para cenário; triggers para eventos.
- Matriz de layers em ProjectSettings (`Physics.setLayerCollision`).
- `isGrounded` por contato inferior; raycast AABB slab-test.

## 5. Renderer

- OpenGL 3.3 core via GLAD; GLFW para janela/input.
- 3D: shadow map, MSAA, materiais por cor/textura; Assimp para FBX/glTF/OBJ.
- 2D: projeção ortho + sprites/quad; atlas de texto Latin-1 (512×512).
- UI desenhada com o mesmo pipeline 2D (drawSprite + TextRenderer).

## 6. Editor

- ImGui docking; layout persistido em `velvx_editor.ini` (com migração
  automática quando painéis novos aparecem).
- Gizmos com hit-test antes da seleção (clique no gizmo não seleciona outro
  objeto); undo/redo por comandos.
- Asset Browser em thumbnails com ícones por tipo e breadcrumb.
- Painel Script usa o mesmo `CodeEditor` do ScriptIDE: realce, autocomplete
  contextual (API + books + variáveis), busca, problemas, abas.

## 7. Build e testes

- CMake + Ninja (MSVC). `Source\build_release.bat` configura e compila tudo,
  copiando Demos/templates/Icon para `Build Release/`.
- `VelvXTests`: bateria headless sem janela/GL — linguagem, Inspector,
  física, serialização de cena, câmeras. Retorna 0 apenas com tudo passando.
- Convenção: edições de demos/templates sempre em `Source/`, nunca em
  `Build Release/` (sobrescrito a cada build).

## 8. Plugins

`PluginAPI` registra módulos nativos carregados no boot (exemplo:
`CustomPhysics`). Plugins aparecem no log e podem estender componentes.