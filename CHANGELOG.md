# Changelog

Todas as mudanças notáveis da VelvX Engine são documentadas neste arquivo.
O formato segue [Keep a Changelog](https://keepachangelog.com/pt-BR/1.0.0/)
e o projeto adere a [Versionamento Semântico](https://semver.org/lang/pt-BR/).

## [1.0.0] — 2026-11-16

Primeira versão pública da VelvX Engine: motor de jogos 2D/3D com editor
integrado, linguagem de script própria e distribuição profissional.

### Adicionado

- **Núcleo (VelvXEngineCore.dll)**: GameObjects com hierarquia parent/child,
  tags, layers e componentes — Transform, MeshRenderer (Cube/Plane/Quad/
  Sprite + modelos importados), Camera, RigidBody, StaticBody, Collider,
  ScriptComponent — e sistema de UI com prefabs `.ui`.
- **Renderização**: forward renderer OpenGL 3.3 com viewport duplo 2D/3D no
  mesmo framebuffer, luz direcional com sombras PCF, MSAA, projeção
  ortográfica/perspectiva, texturas albedo e modo unlit.
- **Importação de modelos**: Assimp (FBX/glTF/GLB/OBJ).
- **Física**: colisão AABB, gravidade por objeto, corpos kinematic,
  restitution/friction, triggers, raycast, matriz de colisão por layers e
  detecção de chão (`grounded`).
- **Velv Language**: interpretador com `lock`/`unlock`, `book` (parâmetros,
  `return`, recursão), `if/else`, `while`, `break`, `self`, strings formatadas
  e natives de gameplay (`SceneManager`, `GameObject`, `Input`,
  `Physics`, `Camera`, `Time`, `UI`, `Application`, `Assets`, `Trigger`,
  `Inspector`); books de estado `Main`/`InGame` para fluxo de jogo.
- **Editor ImGui docking**: Scene View com gizmos (move/rotate/scale) e
  hit-test próprio, Hierarchy com menu de contexto, Inspector com componentes
  e variáveis `unlock`, Asset Browser em thumbnails, Game View, undo/redo,
  atalhos e tema escuro próprio.
- **Ferramentas**: Launcher (gerenciador de projetos), Script IDE (autocomplete
  contextual, abas, busca/substituição), UI Editor visual e CodeEditor
  compartilhado.
- **Plugins nativos**: DLLs com API `IPlugin` + `ComponentRegistry` (exemplo
  CustomPhysics incluído).
- **Exportação standalone**: pack ZIP opcional de assets + executável runtime
  (VelvXPlayer) sem o Editor.
- **Demos completas**: CRYSTAL RUSH (plataforma 2D com menu, 3 fases, HUD,
  pause e tela final) e ARENA NEON (FPS 3D de ondas com menu, HUD e fim).
- **Testes**: bateria headless VelvXTests com 66 testes de interpretador e
  núcleo.
- **Distribuição**: instalador nativo VelvXSetup.exe (wizard gráfico, atalhos,
  associações .velv/.ui, entrada em Adicionar/Remover Programas, desinstalador
  e modo silencioso `/S`) e pacote portable zip com conteúdo idêntico.
- **Documentação**: GettingStarted, VelvLanguage e Architecture em `docs/`.

### Corrigido

- Gizmos: hit-test do manipulador antes da seleção de objetos.
- Centralização de texto UI em âncoras TopCenter/Center/BottomCenter.

[1.0.0]: https://github.com/NoeXVel/VelvX-Engine/releases/tag/v1.0.0