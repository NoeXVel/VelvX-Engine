# Arquitetura da VelvX Engine

Este documento descreve como a versão **0.2.0 (Forja)** está organizada e quais são os pontos de extensão para as próximas etapas.

## Visão geral

```
Launcher (MainWindow)
  ├─ Início          projetos recentes, atalhos
  ├─ Novo projeto    catálogo de Templates + criação da pasta
  └─ Abrir projeto   pasta ou arquivo .velvxproj
           │
           ▼
Editor (EditorWindow)
  ├─ Hierarquia      objetos, cenas carregadas, busca e criação
  ├─ Viewport 3D     WPF Viewport3D, câmera orbital + WASD, gizmos
  ├─ Viewport 2D     desenho vetorial, sprites, GIFs e spritesheets
  ├─ Inspector       transformação, aparência, assets, luz, volume,
  │                  componentes e script do objeto
  ├─ Script          AvalonEdit + realce da VelvX + ajuda
  ├─ Recursos        cenas, scripts, assets e prefabs
  └─ Saída           console do jogo + FPS
           │
           ▼
Configurações do Projeto (ProjectSettingsWindow)
  ├─ Geral           nome, modo, pasta, motor
  ├─ Entradas        ações e teclas personalizadas
  ├─ Renderização    limite de FPS, contador, sombreamento
  ├─ Iluminação      ambiente, sol, fundo, neblina
  └─ Cenas           lista de cenas e streaming
           │
           ▼
Runtime (GameSession)
  ├─ Parser / Interpreter da VelvX
  ├─ InputState + InputMap do projeto
  ├─ SceneManager    cenas carregadas, ativa, load/unload/set_active
  ├─ Componentes     scripts por objeto e Rotator/PointLight nativos
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
   ├─ Models/
   ├─ Sprites/
   └─ Textures/
```

O arquivo do projeto segue o nome da pasta (`<Nome>.velvxproj`). Caminhos de cena, script e assets são relativos à pasta do projeto.

O `.velvxproj` guarda:

- `name`, `mode` (`2D`/`3D`), `language`, `createdAt`, `template`;
- `entryScript` e `startScene`;
- `scenes`: entradas `{ name, path, start }`;
- `inputs.actions`: `{ name, keys[] }`;
- `render`: `fpsLimit`, `showFps`, `smoothShading`.

Lista de recentes: `%LOCALAPPDATA%\VelvX\recent-projects.json`.
Projetos novos por padrão: `%USERPROFILE%\Documents\VelvX Projects`.

### Formato da cena

Cada cena tem `name`, `mode`, `environment` e `objects`. Cada objeto tem `id`, `name`, `type`, `position`, `rotation`, `scale`, `color` e, opcionalmente:

- `size` (2D);
- `script` (script do objeto);
- `model`, `animation`, `animationSpeed`, `animationLoop` (Model3D);
- `sprite` (path, columns, rows, fps, loop);
- `intensity`, `range` (luzes e volumes);
- `components`: lista de `{ type, ... }` com `Enabled`, `Color`, `Amount`, `Range`, `Axis`, `Speed`, `Script`, etc.

Tipos principais:

- 3D: `Box`, `Sphere`, `Model3D`, `CharacterBody3D`, `PointLight`, `DirectionalLight`, `GlobalVolume`, `BoxVolume`, `Camera3D`.
- 2D: `Rectangle`, `Ellipse`, `Sprite`, `CharacterBody2D`, `GlobalVolume`, `BoxVolume`, `Camera2D`.

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

O realce de sintaxe fica em `Language/VelvXHighlighting.cs` (definição XSHD embutida, carregada no AvalonEdit).

## Ponte cena ↔ script

`GameSession` cria um `ScriptObject` para cada objeto da cena, espelhando `position`, `rotation`, `scale`, `visible`, o componente `CharacterBody3D`/`CharacterBody2D` e os componentes declarados. A cada quadro:

1. tira um snapshot das posições;
2. executa `InGame()` e os scripts de componente;
3. integra a `velocity` do jogador quando a posição não foi alterada manualmente;
4. aplica posições de volta aos objetos e dispara `SceneObjectChanged` para o editor;
5. executa componentes nativos (como o `Rotator`) e atualiza o gerenciador de cenas.

O jogador é o objeto cujo `script` é o `EntryScript` do projeto; se não houver, o primeiro `CharacterBody`; se não houver, o primeiro objeto.

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
- Câmera 3D: órbita com botão direito, pan com o botão do meio, zoom com scroll, `WASD`/setas para mover, `E`/`Q` para subir/descer, `Shift` para acelerar, `F` ou **Focar** para centralizar no objeto.
- Gizmos: projeção dos eixos X/Y/Z para a tela, teste de acerto por segmento e arraste convertido em unidades por pixel. `Ctrl`/`Shift` aplicam snap.
- 2D: clique esquerdo seleciona, setas X/Y movem o objeto, botão direito/meio move a vista e scroll dá zoom; sprites, GIFs e spritesheets são desenhados com `CroppedBitmap`.
- Recursos: a árvore é montada a partir das pastas do projeto; arrastar um objeto para `Prefabs` grava um `.prefab.json` e arrastar o prefab para a cena cria uma cópia.

## Renderização 3D

`Rendering/ModelLoader.cs` usa Assimp para importar OBJ/FBX/GLTF/GLB, monta materiais com texturas, aplica skinning por CPU e amostra animações de nós/esqueleto por tempo. O `Scene3DView` converte modelos e formas primitivas em `ModelVisual3D`, aplica o ambiente da cena (`EnvironmentSettings`), mistura o tom do `GlobalVolume`, adiciona luzes pontuais de objetos/componentes e anima o componente `Rotator` no editor.

## Execução headless

O mesmo `GameSession` roda sem janela para testes:

```
VelvX.Engine.exe --run-script Scripts\Main.velv --frames 60
VelvX.Engine.exe --run-script Scripts\Main.velv --scene Scenes\Main.scene.json --mode 2D --out resultado.txt
```

O código de saída é `0` em sucesso e `1` em erro. Sem `--scene`, é criada uma cena padrão mínima no modo indicado. O executor procura o `.velvxproj` do projeto para usar Inputs e caminhos de assets.

## Build e distribuição

`build.ps1` executa `dotnet publish` self-contained para `win-x64`, limpa a pasta de destino, garante a cópia de `Templates`, gera `dist\VelvX-Engine-win-x64.zip` e valida a presença do executável. A pasta `dist/` não é versionada; o pacote deve ser publicado em **GitHub Releases**.

## Próximas etapas sugeridas

1. Exportação de jogo independente (executável do jogo sem o editor).
2. Física e colisão para `CharacterBody2D/3D` e corpos rígidos.
3. Áudio, animação com blending e tilemaps.
4. Editor de assets (materiais, atlas, animação) e câmera de jogo.
5. Compilador VelvX para bytecode, com diagnóstico mais rico.
6. Sistema de pacotes/projetos e builds incrementais.
7. CI no GitHub Actions para compilar e anexar o zip automaticamente a cada release.
