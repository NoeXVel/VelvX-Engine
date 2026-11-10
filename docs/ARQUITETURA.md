# Arquitetura da VelvX Engine

Este documento descreve como a versão **0.4.0 (Bigorna — Fix)** está organizada e quais são os pontos de extensão para as próximas etapas.

## Visão geral

```
Launcher (MainWindow)
  ├─ Início          projetos recentes, atalhos
  ├─ Novo projeto    catálogo de Templates + criação da pasta
  └─ Abrir projeto   pasta ou arquivo .velvxproj
           │
           ▼
Editor (EditorWindow)
  ├─ Hierarquia      árvore de atores (pai/filho), cenas carregadas, busca e criação
  ├─ Viewport 3D     WPF Viewport3D, câmera orbital + WASD, gizmos mover/girar/escalar
  ├─ Viewport 2D     desenho vetorial, sprites, GIFs e spritesheets
  ├─ Inspector       transformação, hierarquia, camada, aparência, materiais,
  │                  luz, volume, câmera, ator (LOD), componentes e script
  ├─ Script          AvalonEdit + realce da VelvX + Documentação da linguagem
  ├─ Script grande   ScriptEditorWindow com autocomplete e auto-indent
  ├─ Recursos        cenas, scripts, assets e prefabs
  └─ Saída           console do jogo + FPS
           │
           ▼
Configurações do Projeto (ProjectSettingsWindow)
  ├─ Geral           nome, modo, pasta, motor
  ├─ Entradas        ações e teclas personalizadas
  ├─ Renderização    limite de FPS, contador, sombreamento
  ├─ Iluminação      ambiente, sol, fundo, neblina
  ├─ Cenas           lista de cenas e streaming
  └─ Camadas e scripts  camadas, matriz de colisão e scripts globais
           │
           ▼
Runtime (GameSession)
  ├─ Parser / Interpreter da VelvX
  ├─ InputState + InputMap do projeto
  ├─ SceneManager    cenas carregadas, ativa, load/unload/set_active
  ├─ Componentes     scripts por ator, Rotator/PointLight nativos,
  │                  colliders, MeshCollider, StaticBody, câmera e áudio
  ├─ Física          integração simples, colisão AABB corpo ↔ estático e triggers
  └─ ponte cena ↔ objetos de script
```

A Engine é um aplicativo WPF .NET 8 para Windows. A build portátil é autossuficiente (`win-x64`) e não exige .NET instalado. Modelos 3D usam **AssimpNet** (Assimp nativo) e o editor de script usa **AvalonEdit**.

## Projetos

Um projeto é uma pasta com:

```
MeuJogo/
├─ MeuJogo.velvxproj        metadados, inputs, render e cenas
├─ Scenes/
│  ├─ Main.scene.json
│  └─ Arena.scene.json
├─ Scripts/
│  ├─ Main.velv
│  └─ ComponenteExemplo.velv
├─ Prefabs/
│  └─ Estrela.prefab.json
└─ Assets/
   ├─ Audio/
   ├─ Materials/
   ├─ Models/
   ├─ Sprites/
   └─ Textures/
```

Os materiais ficam em `Assets/Materials/*.json` e podem ser reutilizados por
qualquer ator com modelo.

O arquivo do projeto segue o nome da pasta (`<Nome>.velvxproj`). Caminhos de cena, script e assets são relativos à pasta do projeto.

O `.velvxproj` guarda:

- `name`, `mode` (`2D`/`3D`), `language`, `createdAt`, `template`;
- `entryScript` e `startScene`;
- `scenes`: entradas `{ name, path, start }`;
- `inputs.actions`: `{ name, keys[] }`;
- `render`: `fpsLimit`, `showFps`, `smoothShading`;
- `layers` e `collisionDisabled` (pares `A|B` que não colidem);
- `globalScripts`: scripts executados para todos os objetos.

Lista de recentes: `%LOCALAPPDATA%\VelvX\recent-projects.json`.
Projetos novos por padrão: `%USERPROFILE%\Documents\VelvX Projects`.

### Formato da cena

Cada cena tem `name`, `mode`, `environment` e `objects`. Cada objeto tem `id`, `name`, `type`, `position`, `rotation`, `scale`, `color`, `layer` e, opcionalmente:

- `parentId` (hierarquia);
- `size` (2D);
- `script` (script do ator);
- `model`, `modelTexture`, `material`, `modelScale`, `animation`, `animationSpeed`, `animationLoop` (Model3D);
- `colliderBounds` (caixa local do modelo para MeshCollider e física);
- `lodEnabled`, `lodDistance`, `renderDistance` (ator e LOD);
- `sprite` (path, columns, rows, fps, loop);
- `intensity`, `range` (luzes e volumes);
- `fov`, `nearPlane`, `farPlane`, `cameraActive` (câmeras);
- `components`: lista de `{ type, ... }` com `Enabled`, `Color`, `Amount`, `Range`, `Axis`, `Speed`, `Script`, `Shape`, `Trigger`, `Mass`, `Gravity`, `UseGravity`, `LayerMask`, `Volume`, `Loop`, `Fov`, `NearPlane`, `FarPlane`, etc.

Tipos principais:

- Genérico: `GameObject`/`Empty` — só Transform, usado como pai, marcador ou contêiner de componentes.
- 3D: `Box` (Cubo/Caixa), `Sphere`, `Cylinder`, `Capsule`, `Cone`, `Plane`, `Model3D`, `CharacterBody3D`, `StaticBody3D`, `PointLight`, `DirectionalLight`, `GlobalVolume`, `BoxVolume`, `Camera3D`.
- 2D: `Rectangle`, `Ellipse`, `Sprite`, `CharacterBody2D`, `GlobalVolume`, `BoxVolume`, `Camera2D`.

Todo ator tem transformação e pode receber componentes; o Transform é a base da hierarquia. A hierarquia do editor é uma árvore: mover, girar ou escalar um pai move os filhos junto.

## Templates

`Templates/<pasta>/template.json` descreve um modelo. O serviço (`Core/TemplateInfo.cs`) varre a pasta, ordena por modo/nome e mostra os cartões em **Novo projeto**.

Na criação, a Engine copia a árvore do template para a pasta escolhida, ignora `template.json`, `.gitkeep` e pastas ocultas (começando com `.`), grava o `.velvxproj` e registra o projeto nos recentes.

Durante o desenvolvimento, `EnginePaths.TemplatesFolder` procura o `VelvX.Engine.csproj` alguns níveis acima do executável e usa a pasta `Templates` do repositório. Em uma build publicada, usa a pasta `Templates` ao lado do executável — por isso o `build.ps1` garante essa cópia no pacote.

## Linguagem

Fluxo de execução:

1. `Lexer` produz tokens (comentários `#`, `//`, `/* */`, `#* *#`, strings, números, operadores).
2. `Parser` monta a AST (`Language/Ast.cs`) com declarações, funções, `if`, `while`, `return`, atribuições e expressões.
3. `Interpreter` executa a AST com escopos encadeados, funções globais e objetos nativos (`ScriptObject`, `NativeFunction`).
4. `IScriptHost` desacopla o interpretador do editor: `GameSession` fornece `this`, cenas, assets, input e saída.

Valores em tempo de execução: `null`, `bool`, `double`, `string`, `VelvVector2`, `VelvVector3`, `ScriptObject`, `NativeFunction`, `UserFunction`.

Limites de proteção: profundidade de chamadas 64; laços `while` até 500.000 iterações por execução.

O realce de sintaxe fica em `Language/VelvXHighlighting.cs` (definição XSHD embutida, carregada no AvalonEdit). A janela grande de script (`Windows/ScriptEditorWindow.xaml`) usa o mesmo realce, com `CompletionWindow` para autocomplete, estratégia de indentação de 4 espaços e lista de scripts do projeto. A referência da linguagem fica em `docs/VelvX-Language.md` e é exibida dentro do editor por `Windows/LanguageDocsWindow.xaml` (seções navegáveis, exemplos e formatação própria).

## Ponte cena ↔ script

`GameSession` cria um `ScriptObject` para cada objeto da cena, espelhando `position`, `rotation`, `scale`, `visible`, `layer`, o componente `CharacterBody3D`/`CharacterBody2D` e os componentes declarados. A cada quadro:

1. tira um snapshot das posições;
2. executa os scripts globais, `InGame()` e os scripts de componente;
3. integra a `velocity` do jogador quando a posição não foi alterada manualmente;
4. aplica posições de volta aos objetos e dispara `SceneObjectChanged` para o editor;
5. executa componentes nativos (como o `Rotator`), a física e os triggers, e atualiza o gerenciador de cenas.

O jogador é o objeto cujo `script` é o `EntryScript` do projeto; se não houver, o primeiro `CharacterBody`; se não houver, o primeiro objeto.

### Hierarquia em scripts

- `get_parent()`, `get_children()` e `find_child("Nome")` navegam pela árvore de `parentId`;
- `is_child_of(outro)` verifica ancestralidade;
- o inspetor permite escolher o pai sem criar ciclos;
- a hierarquia visual do editor mostra a árvore aninhada e reordena os atores ao trocar o pai.

`Core/SceneGraph.cs` é a fonte única das transformações: matriz local (`escala → rotação → translação`), matriz de mundo (`local × mundo do pai`), posição/eixos de mundo, conversão de delta para o espaço do pai e caixa de colisão mundial. Posição, rotação e escala exibidas no inspetor são locais em relação ao pai.

### Física, colliders e triggers

`BoxCollider` e `SphereCollider` contribuem com AABBs. `MeshCollider` usa a caixa alinhada do modelo importado preenchida automaticamente em `SceneObject.ColliderBounds`, sem desenho manual. `Rigidbody` integra gravidade e velocidade; `CharacterBody` move por `velocity` e o runtime resolve colisão contra `StaticBody`, `StaticBody3D` e colliders em várias passadas, detectando chão (`on_ground`) e zerando a velocidade vertical no impacto. Quando dois colliders se cruzam e as camadas colidem, o runtime dispara `OnTriggerEnter`, `OnTriggerStay` e `OnTriggerExit` nos scripts dos atores envolvidos. A matriz é configurada em **Configurações do Projeto → Camadas e scripts** (lista `A|B` para pares que não colidem).

## Cenas e streaming

`GameSession` mantém um dicionário de cenas carregadas:

- `Scene("Nome")` devolve um wrapper com `load()`, `unload()`, `set_active()` e `is_loaded()`;
- a cena ativa é a que recebe `Scene(nomeSemArgumento).object` e os scripts;
- `LoadScene` resolve o caminho pelo nome da cena no projeto, pelo arquivo em `Scenes/` ou por um caminho relativo;
- cenas carregadas continuam sendo atualizadas e aparecem em `Cenas carregadas` na hierarquia;
- descarregar remove os objetos e os scripts daquela cena.

## Editor

- O modo Play clona a cena antes de executar e restaura o clone ao parar, então o jogo nunca altera a cena salva.
- O laço roda em um `DispatcherTimer` no intervalo do limite de FPS; `game.delta_time` é limitado a 0,1 s para evitar saltos.
- Input: `Window.PreviewKeyDown/Up` alimenta `InputState`; durante o Play qualquer tecla é encaminhada ao jogo e `Esc` encerra.
- Câmera 3D: movimento livre e contínuo (sem grade), órbita com botão direito, pan com o botão do meio, zoom com scroll, `WASD`/setas para mover, `E`/`Q` para subir/descer, `Shift` para acelerar, `F` ou **Focar** para centralizar no ator.
- Câmeras de jogo: `Camera3D`/`Camera2D` (ou o componente `Camera`) com FOV, near, far e **câmera principal**; uma única câmera é marcada como principal e o Play usa sua transformação.
- Gizmos: modos **Mover**, **Girar** e **Escalar**, alternados por `1`, `2` e `3` (ou teclado numérico) e pela barra no viewport. As alças são projetadas para a tela e o arraste é convertido em unidades/eixos de mundo; o centro do gizmo arrasta o ator livremente no plano da câmera. `Ctrl`/`Shift` aplicam snap.
- Seleção: o viewport faz hit-test no modelo visual e nos componentes, selecionando o objeto clicado.
- 2D: clique esquerdo seleciona, setas X/Y movem o objeto, botão direito/meio move a vista e scroll dá zoom; sprites, GIFs e spritesheets são desenhados com `CroppedBitmap`.
- Recursos: a árvore é montada a partir das pastas do projeto, com ícones por tipo; arrastar um objeto para `Prefabs` grava um `.prefab.json` e arrastar o prefab para a cena cria uma cópia.
- Textos de botões usam `MinHeight` + `Padding` em vez de altura fixa, evitando corte vertical em fontes maiores.

## Renderização 3D

`Rendering/ModelLoader.cs` usa Assimp para importar OBJ/FBX/GLTF/GLB, monta materiais com texturas, aplica skinning por CPU e amostra animações de nós/esqueleto por tempo. A importação mede os limites reais da malha e normaliza apenas exportações fora da faixa usual (maior eixo > 100 ou < 0,05), aplicando a correção na raiz para preservar rigs e animações. O `Scene3DView` converte modelos e formas primitivas em `ModelVisual3D`, aplica o ambiente da cena (`EnvironmentSettings`), mistura o tom do `GlobalVolume`, adiciona luzes pontuais e anima o componente `Rotator` no editor. A iluminação combina ambiente, sol direcional, preenchimento frio, recorte lilás e toque quente, com especular nos materiais. `BoxVolume` desenha preenchimento translúcido e arestas pulsantes, ficando mais forte quando um `CharacterBody` está dentro.

Cada ator é renderizado com LOD por distância: malha completa perto, proxy de caixa no meio e corte pela distância de renderização no fim. O inspetor liga/desliga o LOD e ajusta as distâncias; ao desligar, o proxy sai da cena imediatamente. Materiais (`Core/MaterialDocument.cs`, `Windows/MaterialWindow.xaml`) guardam cor, textura, emissão, brilho, opacidade e tiling em `Assets/Materials`, e são aplicados ao modelo do ator.

`MeshRenderer` permite caixa/esfera/modelo com cor, escala e textura sem criar um objeto `Model3D` separado.

## Scripts globais

Scripts listados em **Configurações do Projeto → Camadas e scripts** rodam uma vez por quadro no escopo global do runtime, antes dos scripts dos objetos. Eles são úteis para regras de jogo, HUD, spawns e temporizadores. Um objeto também pode receber o componente `GlobalScript` para indicar que aquele script deve ser tratado como global.

## Execução headless

O mesmo `GameSession` roda sem janela para testes:

```
VelvX.Engine.exe --run-script Scripts\Main.velv --frames 60
VelvX.Engine.exe --run-script Scripts\Main.velv --scene Scenes\Main.scene.json --mode 2D --out resultado.txt
```

O código de saída é `0` em sucesso e `1` em erro. Sem `--scene`, é criada uma cena padrão mínima no modo indicado. O executor procura o `.velvxproj` do projeto para usar Inputs e caminhos de assets.

## Build e distribuição

`build.ps1` executa `dotnet publish` self-contained para `win-x64`, limpa a pasta de destino, garante a cópia de `Templates`, gera o `LEIA-ME.txt`, compacta `dist\VelvX-Engine-win-x64.zip`, gera `dist\SHA256.txt` e valida a presença do executável. A pasta `dist/` não é versionada; o pacote deve ser publicado em **GitHub Releases** com o checksum no anexo.

## Próximas etapas sugeridas

1. Sistema de UI (HUD, botões, textos e imagens) dentro do runtime.
2. Matriz de colisão visual, em vez de lista de pares, e colisão triângulo a triângulo no MeshCollider.
3. Jogo de exemplo completo para demonstrar o fluxo de criação.
4. Exportação de jogo independente (executável do jogo sem o editor).
5. Física mais completa para `CharacterBody2D/3D`, raycasts e corpos rígidos.
6. Áudio, animação com blending e tilemaps.
7. Editor de assets (materiais, atlas, animação) e câmera de jogo avançada.
8. Compilador VelvX para bytecode, com diagnóstico mais rico.
9. Sistema de pacotes/projetos e builds incrementais.
10. CI no GitHub Actions para compilar e anexar o zip automaticamente a cada release.
