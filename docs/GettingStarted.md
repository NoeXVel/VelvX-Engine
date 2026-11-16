# VelvX Engine — Guia de Uso

Este guia leva você do zero ao primeiro jogo publicado: instalação, tour do
editor, criação de projeto, script, UI, física e build do jogo.

---

## 1. Instalação

### Portable (recomendado para começar)

1. Baixe `VelvXEngine-1.0.0-portable.zip` na página de Releases.
2. Extraia em qualquer pasta (ex.: `C:\VelvX`).
3. Execute `VelvXLauncher.exe`.

Não há dependências externas: OpenGL 3.3 (qualquer GPU Intel/AMD/NVIDIA dos
últimos 10 anos) e Windows 10+.

### Instalador

`VelvXEngine-1.0.0-setup.exe` abre um assistente gráfico: licença, pasta de
instalação (padrão `%LOCALAPPDATA%\VelvXEngine`), atalhos e associações
`.velv`/`.ui`. Instala por usuário, sem administrador. Desinstale pelo Menu
Iniciar ("Desinstalar VelvX Engine") ou por Adicionar/Remover Programas do
Windows. Instalação silenciosa: `setup.exe /S /D=<pasta>`.

### Ferramentas incluídas

| Executável | Função |
|------------|--------|
| `VelvXLauncher.exe` | Hub de projetos: abrir/criar projetos e demos |
| `VelvXEditor.exe` | Editor completo (cena, hierarquia, inspector, assets, script, game view) |
| `VelvXScriptIDE.exe` | IDE de script standalone (uma janela, abas, autocomplete) |
| `VelvXUIEditor.exe` | Editor visual de prefabs de UI |
| `VelvXPlayer.exe` | Runtime do jogo: `VelvXPlayer --project MeuJogo.velvproj` |
| `VelvXTests.exe` | Bateria headless de testes do interpretador/núcleo |

---

## 2. Tour do editor

O layout padrão é dockado e pode ser rearrastado; tudo persiste entre sessões.

- **Toolbar** — Play/Stop, salvar cena/projeto, configurações.
- **Hierarchy** — árvore de objetos da cena. **Botão direito → Criar**:
  objeto vazio (só Transform), submenu **3D** (cubo, esfera, plano, luz...) e
  **2D** (sprite/quad), além de Colar/Duplicar/Excluir. `Ctrl+N` cria vazio.
- **Inspector** — componentes do objeto selecionado + campos `unlock` dos
  scripts, editáveis em runtime.
- **Assets** — navegador de arquivos do projeto em **thumbnails**, com ícones
  de pasta, badges por tipo (`.velv`, `.vscene`, `.ui`, texturas) e breadcrumb.
  Duplo clique abre o asset na ferramenta certa (script → painel Script).
- **Scene** — viewport 3D/2D com **gizmos** de mover/rotacionar/escalar
  (teclas W/E/R), seleção por clique e grade.
- **Game** — visão do jogo (câmera ativa), igual ao que o Player mostra.
- **Script** — editor de código unificado: realce de sintaxe, gutter,
  autocomplete contextual (`Ctrl+Space`), busca (`Ctrl+F`), abas e painel de
  problemas. Sem visualizador separado: você escreve e vê o resultado na
  mesma janela.
- **Console** — logs da engine e `print()` dos scripts.

### Atalhos essenciais

| Tecla | Ação |
|-------|------|
| W / E / R | Gizmo mover / rotacionar / escalar |
| Ctrl+N | Objeto vazio |
| Ctrl+D | Duplicar |
| Ctrl+Z / Ctrl+Y | Undo / Redo |
| Delete | Excluir selecionado |
| Ctrl+S | Salvar cena |
| F | Focar selecionado na Scene |

---

## 3. Primeiro projeto

1. No Launcher: **Novo projeto** → escolha o template **2D Platformer** ou
   **3D FPS** → pasta de destino.
2. O template cria `MeuJogo.velvproj`, cenas, scripts e UI prontos para jogar.
3. Abra no editor e aperte **Play**.

Estrutura de um projeto:

```
MeuJogo/
├── MeuJogo.velvproj      # manifesto: mainScene, mainScript, scenes[]
├── scenes/               # *.vscene (JSON: objetos, transforms, física, câmera)
├── scripts/              # *.velv (GameManager + comportamentos)
├── ui/                   # *.ui (prefabs de UI: painéis, textos, botões)
└── assets/               # texturas, modelos (FBX/glTF/OBJ), áudio
```

### O manifesto `.velvproj`

```json
{
    "name": "MeuJogo",
    "engineVersion": "1.0",
    "template": "2DPlatformer",
    "mainScene": "Menu",
    "mainScript": "scripts/GameScript.velv",
    "scenes": ["Menu", "Level1", "End"]
}
```

`mainScript` é o GameManager global; `mainScene` é a cena carregada no boot.

---

## 4. Cenas e objetos

- Toda cena tem uma **câmera como GameObject** (componente Camera, `active`).
  Troque de câmera com `Camera.setActive("Nome")`.
- Objetos combinam componentes: Transform (sempre), MeshRenderer, RigidBody /
  StaticBody, Collider, Camera, scripts.
- **Física**: `physics: "dynamic"` usa gravidade por objeto (`useGravity`),
  massa, restituição e atrito; `layer` + matriz de colisão em
  **Project Settings** controlam quem colide com quem; `collider.trigger`
  cria zonas de evento (moedas, goals, dano).
- O tamanho do Collider é **local** (multiplicado pela escala do Transform):
  use `size [1,1,1]` e dimensione pelo Transform.

## 5. UI

Prefabs `.ui` são JSON com painéis, textos, botões e imagens, com âncoras
(TopLeft…BottomRight), posição relativa ou em pixels e hierarquia de filhos.
Edite visualmente no **UIEditor** ou à mão; no jogo:

```velv
UI.loadPrefab("ui/HUD.ui");
UI.setText("Score", "Score: " + str(score));
UI.setVisible("Pause", true);
```

Botões disparam books do script global pelo campo `"script"`.

## 6. Programando em Velv

Leia [VelvLanguage.md](VelvLanguage.md) para a referência completa. O ciclo
recomendado:

1. `mainScript` = máquina de estados (menu/jogo/pause/fim).
2. Scripts anexados = comportamentos (player, inimigo, câmera).
3. `unlock` = parâmetros de design expostos no Inspector.
4. `Time.setScale(0)` = pause real; `SceneManager.loadScene` = reset/replay.

## 7. Testando e publicando

- **Play** no editor roda o projeto na Game view.
- `VelvXPlayer.exe --project caminho/MeuJogo.velvproj` roda standalone
  (é assim que as demos são executadas).
- `VelvXTests.exe` valida o núcleo (66 testes headless).
- Para distribuir seu jogo: copie a pasta do runtime (exe + dlls + shaders +
  assets) junto do projeto, ou gere um pack de assets. O Player aceita
  `--pack assets.zip` e `--scene Nome` para boot direto.

## 8. Solução de problemas

| Sintoma | Causa provável |
|---------|----------------|
| Script não carrega | Erro de parse — veja arquivo:linha no Console |
| Objeto não colide | Matriz de layers ou collider ausente |
| UI não aparece | Prefab não carregado ou `visible:false` |
| Jogo congela | `Time.setScale(0)` ativo (pause) |
| Texto com `?` | Glifo fora do atlas Latin-1 (use ASCII/Latin-1) |