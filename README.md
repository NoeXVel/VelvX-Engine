# VelvX Engine

**Sua engine, suas regras, seus universos.** Uma engine 2D e 3D para Windows, feita para criar jogos com a **VelvX Language** — do launcher ao modo Play.

![Launcher da VelvX Engine](docs/images/launcher.png)

> **Versão 0.1.0 — Fundação.** O launcher, o editor de cenas, os viewports 2D/3D, o modo Play, o interpretador da VelvX Language e a build portátil já funcionam. Exportação de jogo independente, física, áudio e animação estão no roteiro.

## O que já funciona

- **Launcher** com tela inicial, projetos recentes, catálogo de templates e abertura de projetos existentes.
- **Novo projeto** a partir de um template 2D ou 3D, com nome e pasta de destino.
- **Editor de cenas** com hierarquia, inspector, viewport 3D orbital, viewport 2D com pan/zoom, editor de script e console de saída.
- **Modo Play** com `F5`, input WASD/setas, `Esc` para parar, FPS e restauração automática da cena ao sair.
- **VelvX Language**: variáveis tipadas e inferidas, funções, `if`, `while`, interpolação de strings, vetores, input, acesso a objetos da cena e ciclo `Main()`/`InGame()`.
- **Templates iniciais** de plataforma 2D e de cena 3D, prontos para mover o Player.
- **Build portátil** autossuficiente para `win-x64` (não exige .NET instalado).
- **Executor headless** para testar scripts sem abrir o editor.

| Editor 3D | Editor 2D |
| --- | --- |
| ![Editor 3D](docs/images/editor-3d.png) | ![Editor 2D](docs/images/editor-2d.png) |

![Modo Play](docs/images/play-3d.png)

## Requisitos

- Windows 10 ou 11, 64 bits.
- Para compilar: [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (o SDK 10 também compila o alvo `net8.0-windows`).
- Para usar a build portátil: nada além do Windows.

## Executar pelo código

```powershell
cd "C:\Users\Gean\Documents\MyEngine"
dotnet run
```

Ou abra `VelvX.Engine.csproj` no Visual Studio 2022 com a carga de trabalho **.NET desktop development**.

Na primeira execução, o launcher mostra os templates do repositório. Crie um projeto em **Novo projeto**, escolha 2D ou 3D e o editor abre em seguida.

## Gerar a build portátil

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

O script publica em `dist\VelvX-Engine-win-x64`, confere o executável e gera `dist\VelvX-Engine-win-x64.zip`. Extraia o zip em qualquer pasta e execute `VelvX.Engine.exe`.

A pasta `dist/` fica fora do Git; publique o zip em **Releases** no GitHub — veja [docs/GITHUB.md](docs/GITHUB.md).

## Primeiros passos

1. **Novo projeto** → escolha **Projeto 3D** ou **Projeto 2D** → dê um nome → **Criar projeto**.
2. No editor, pressione **F5** e mova o Player com **WASD** ou as **setas**.
3. Edite `Scripts/Main.velv` e clique em **Salvar**; o próximo Play usa o novo código.
4. Pressione **Esc** para parar. A cena volta ao estado salvo.

Atalhos e controles:

| Ação | Controle |
| --- | --- |
| Iniciar / parar o jogo | `F5` / `Esc` |
| Salvar projeto | `Ctrl+S` |
| Mover o jogador no Play | `WASD` ou setas |
| Orbitar a câmera 3D | botão direito + arrastar |
| Mover a câmera 3D | botão do meio + arrastar |
| Zoom | scroll do mouse |
| Resetar câmera | botão de reset ou duplo clique no viewport |
| Selecionar objeto | clique no viewport ou na hierarquia |
| Remover objeto | `Delete` (fora do Play) |

## VelvX Language em 30 segundos

```velv
# Script de entrada: roda uma vez ao iniciar.
func Main()
{
    console.show("Bem-vindo ao meu mundo!");
}

# Roda a cada quadro.
func InGame()
{
    var direcao = game.get_input_vectors("direita", "esquerda", "baixo", "cima");
    this.CharacterBody3D.velocity = new.Vector3(direcao.x * 6, 0, direcao.y * 6);

    var orbe = Scene("Main").object.Esfera;
    orbe.rotation.y += game.delta_time * 40;
}
```

Comentários `#`, `//`, `/* */` e `#* *#`; variáveis `var vida : Int = 100;` ou `var vida := 100;`; funções; `if`/`else`; `while`; vetores `Vector2`/`Vector3`; `console.show/warn/error`; `this`; `Scene("...")`; `Assets.pathObject("...")`.

A referência completa está em [docs/VelvX-Language.md](docs/VelvX-Language.md).

## Estrutura do projeto

```
VelvX.Engine.csproj
App.xaml / MainWindow.xaml      launcher
Windows/EditorWindow.xaml      editor de cenas
Core/                          projetos, cenas, templates, recentes, caminhos
Language/                      lexer, parser, AST, interpretador, valores
Runtime/                       sessão de jogo, input, executor headless
Rendering/                     viewports 3D (Viewport3D) e 2D (OnRender)
Theme/                         tema escuro e barra de título
Templates/                     modelos 2D/3D e README de templates
docs/                          linguagem, arquitetura, GitHub e capturas
tests/                         scripts de teste e automação de UI
```

Um projeto criado pela Engine tem esta cara:

```
MeuJogo/
├─ MeuJogo.velvxproj
├─ Scenes/Main.scene.json
└─ Scripts/Main.velv
```

## Templates personalizados

Monte um projeto do zero, copie a pasta dele para `Templates/<nome>` e adicione um `template.json`:

```json
{
  "id": "meu-template",
  "name": "Meu template",
  "description": "Minha base de jogo personalizada.",
  "mode": "3D",
  "version": "0.1.0"
}
```

O template aparece em **Novo projeto** depois de **Recarregar**. Detalhes em [Templates/README.md](Templates/README.md).

## Executor headless

```powershell
.\bin\Release\net8.0-windows\VelvX.Engine.exe --run-script tests\language-smoke.velv --frames 10
```

Opções: `--frames N`, `--scene arquivo.scene.json`, `--mode 2D|3D`, `--out resultado.txt` e `--open "pasta do projeto"`. O código de retorno é `0` em sucesso e `1` em erro.

## Documentação

- [VelvX Language](docs/VelvX-Language.md) — sintaxe, tipos, operadores, APIs e limites.
- [Arquitetura](docs/ARQUITETURA.md) — como o launcher, o editor, o runtime e o build se conectam.
- [Publicando no GitHub](docs/GITHUB.md) — primeiro envio, atualizações e releases.

## Roteiro

Exportação de jogo independente, física e colisões, áudio, animação/tilemaps, editor de assets, câmera de jogo, compilador VelvX e builds incrementais. O objetivo é uma ferramenta própria, profissional, para jogos comerciais 2D e 3D.

---

Feito por **Gean** · VelvX Engine 0.1.0 “Fundação” · [github.com/NoeXVel/VelvX-Engine](https://github.com/NoeXVel/VelvX-Engine)
