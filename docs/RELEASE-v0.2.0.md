# VelvX Engine 0.2.0 — Forja

A primeira build pensada para começar jogos de verdade. O editor ganhou setas de transformação, câmera livre, script colorido, Inputs personalizados, prefabs, modelos 3D animados, sprites/GIFs/spritesheets, componentes, iluminação com volumes e streaming de cenas.

## Download

- **Build portátil:** `VelvX-Engine-win-x64.zip` (anexo da release)
- **SHA-256:** `3D12139BC740CD8EC29564BA4CB303ABAE54B58F78988D58EFDCC5B1737E5DA4`
- Tamanho: aproximadamente 69 MB

Extraia o zip em qualquer pasta e execute `VelvX.Engine.exe`. Não é necessário instalar o .NET.

## Editor

- Setas de transformação X/Y/Z com arraste no viewport, snap com `Ctrl`/`Shift` e foco com `F`.
- Câmera 3D de edição livre: `WASD`/setas, `E` sobe, `Q` desce, `Shift` acelera e `Ctrl` aproxima.
- Painel de script ao lado da Saída, com realce de sintaxe, números de linha e ajuda da VelvX Language.
- Configurações do Projeto (`Ctrl+,`) com abas de Geral, Entradas, Renderização/FPS, Iluminação/volumes e Cenas/streaming.
- Painel de Recursos ao lado da Saída: cenas, scripts, assets e prefabs.
- Prefabs por arraste da hierarquia para `Prefabs` e de volta para a cena.
- Inspetor com componentes (Script, Rotador, Animador de sprite, Luz pontual, Box Volume).
- Hierarquia melhorada, criação de cenas e rodapé com as cenas carregadas.

## Jogos

- Modelos 3D OBJ/FBX/GLTF/GLB, com texturas e animações por skin/nó.
- 2D com sprites PNG/JPG, GIFs animados e spritesheets.
- Streaming de cenas com `Scene("Nome").load()`, `unload()`, `set_active()` e `is_loaded()`, além do gerenciador `Scenes`.
- Iluminação com ambiente, sol direcional, luzes pontuais, Global Volume e Box Volume.
- Limite de FPS configurável (60 por padrão) e sombreamento suave.
- VelvX Language com `Main()`/`InGame()`, vetores, funções, laços, inputs personalizados, acesso à cena e componentes.

## Requisitos

- Windows 10 ou 11, 64 bits.
- 4 GB de RAM (8 GB recomendado).
- GPU compatível com DirectX 11 (GTX 1650 roda com folga).

## Controles principais

| Ação | Controle |
| --- | --- |
| Iniciar / parar o jogo | `F5` / `Esc` |
| Salvar projeto | `Ctrl+S` |
| Configurações do Projeto | `Ctrl+,` |
| Focar o objeto selecionado | `F` ou **Focar** |
| Mover a câmera 3D | `WASD` / setas |
| Subir / descer a câmera | `E` / `Q` |
| Mover o jogador no Play | `WASD` ou setas |

## Primeiros passos

1. Abra `VelvX.Engine.exe` e clique em **Novo projeto**.
2. Escolha **Projeto 3D** ou **Projeto 2D**, dê um nome e clique em **Criar projeto**.
3. Selecione objetos na hierarquia, mova com as setas coloridas e edite os campos no inspetor.
4. Edite `Scripts/Main.velv`, pressione **F5** e teste o jogo.
5. No template 3D, pressione `E` durante o Play para carregar a Arena por streaming; no 2D, carregue a Floresta.

## Limitações desta versão

- Ainda não há exportação de um executável de jogo independente do editor.
- Física, colisões, áudio, tilemaps e controle de animação por script entram nas próximas etapas.
- O interpretador VelvX executa a árvore sintática (sem compilação para bytecode nesta versão).

## Publicação (mantenedor)

```powershell
# 1. Gerar o pacote
powershell -ExecutionPolicy Bypass -File .\build.ps1

# 2. Enviar o código
git add .
git commit -m "VelvX Engine 0.2.0 - Forja"
git push

# 3. Criar a release v0.2.0 no GitHub e anexar:
#    dist\VelvX-Engine-win-x64.zip
```

O repositório fica em <https://github.com/NoeXVel/VelvX-Engine>. Instruções completas em [GITHUB.md](GITHUB.md).
