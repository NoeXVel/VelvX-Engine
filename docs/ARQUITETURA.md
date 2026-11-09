# Arquitetura da VelvX Engine

Este documento descreve como a versão **0.1.0 (Fundação)** está organizada e quais são os pontos de extensão para as próximas etapas.

## Visão geral

```
Launcher (MainWindow)
  ├─ Início          projetos recentes, atalhos
  ├─ Novo projeto    catálogo de Templates + criação da pasta
  └─ Abrir projeto   pasta ou arquivo .velvxproj
           │
           ▼
Editor (EditorWindow)
  ├─ Hierarquia      lista de objetos da cena
  ├─ Viewport 3D     WPF Viewport3D + câmera orbital
  ├─ Viewport 2D     desenho vetorial em FrameworkElement.OnRender
  ├─ Inspector       nome, posição, escala, cor, script
  ├─ Script          editor .velv com números de linha
  └─ Saída           console do jogo + FPS
           │
           ▼
Runtime (GameSession)
  ├─ Parser / Interpreter da VelvX
  ├─ InputState (WASD + setas)
  └─ ponte cena ↔ objetos de script
```

A Engine é um aplicativo WPF .NET 8 para Windows, sem dependências externas de runtime. A build portátil é autossuficiente (`win-x64`).

## Projetos

Um projeto é uma pasta com:

```
MeuJogo/
├─ MeuJogo.velvxproj        metadados (nome, modo, cena inicial, script de entrada)
├─ Scenes/
│  └─ Main.scene.json       cena editável
└─ Scripts/
   └─ Main.velv             ponto de entrada da VelvX
```

O arquivo do projeto segue o nome da pasta (`<Nome>.velvxproj`). Os caminhos de cena e script são relativos à pasta do projeto e podem ser trocados no inspector/metadados.

Lista de recentes: `%LOCALAPPDATA%\VelvX\recent-projects.json`.
Projetos novos por padrão: `%USERPROFILE%\Documents\VelvX Projects`.

### Formato da cena

Cada objeto tem `id`, `name`, `type`, `position`, `rotation`, `scale`, `color` e, opcionalmente, `script` (2D também usa `size`). Tipos renderizados hoje:

- 3D: `Box` (qualquer tipo diferente de `Sphere`), `Sphere`, `CharacterBody3D`, além de `DirectionalLight`/câmeras (ignorados na renderização atual).
- 2D: `Ellipse` desenha uma elipse; os demais tipos desenham um retângulo arredondado com `size`.

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

### Ponte cena ↔ script

`GameSession` cria um `ScriptObject` para cada objeto da cena, espelhando `position`, `rotation`, `scale`, `visible` e o componente `CharacterBody3D`/`CharacterBody2D`. A cada quadro:

1. tira um snapshot das posições;
2. executa `InGame()`;
3. integra a `velocity` do componente do jogador quando a posição não foi alterada manualmente;
4. aplica as posições de volta aos objetos da cena e dispara `SceneObjectChanged` para o editor atualizar viewport e inspector.

O jogador é o objeto cujo `script` é o `EntryScript` do projeto; se não houver, o primeiro `CharacterBody`; se não houver, o primeiro objeto.

## Editor

- O modo Play clona a cena antes de executar e restaura o clone ao parar, então o jogo nunca altera a cena salva.
- O laço roda em um `DispatcherTimer` de ~15 ms; `game.delta_time` é limitado a 0,1 s para evitar saltos.
- Input: `Window.PreviewKeyDown/Up` alimenta `InputState`; ao entrar em Play o foco vai para o viewport ativo para que WASD/setas funcionem sem clique.
- Câmera 3D: órbita com botão direito, pan com botão do meio, zoom com scroll, duplo clique ou botão de reset. FOV 55°, câmera inicial a 18 unidades.
- A grade do editor é desenhada em geometria 3D e fica sob os objetos; o piso do template pode cobri-la, como em qualquer editor de cena.
- 2D: clique esquerdo seleciona, botão direito/meio move (pan) e scroll dá zoom; o viewport mostra grade e destaque do objeto selecionado.

## Execução headless

O mesmo `GameSession` roda sem janela para testes:

```
VelvX.Engine.exe --run-script Scripts\Main.velv --frames 60
VelvX.Engine.exe --run-script Scripts\Main.velv --scene Scenes\Main.scene.json --mode 2D --out resultado.txt
```

O código de saída é `0` em sucesso e `1` em erro. Sem `--scene`, é criada uma cena padrão mínima no modo indicado (`3D` por padrão).

## Build e distribuição

`build.ps1` executa `dotnet publish` self-contained para `win-x64`, limpa a pasta de destino, garante a cópia de `Templates`, gera `dist\VelvX-Engine-win-x64.zip` e valida a presença do executável. A pasta `dist/` não é versionada; o pacote deve ser publicado em **GitHub Releases**.

## Próximas etapas sugeridas

1. Exportação de jogo independente (executável do jogo sem o editor).
2. Física e colisão para `CharacterBody2D/3D` e corpos rígidos.
3. Áudio, animação e tilemaps.
4. Editor de assets (texturas, sprites, materiais) e câmera de jogo.
5. Compilador VelvX para bytecode, com diagnóstico mais rico.
6. Sistema de pacotes/projetos e builds incrementais.
7. CI no GitHub Actions para compilar e anexar o zip automaticamente a cada release.
