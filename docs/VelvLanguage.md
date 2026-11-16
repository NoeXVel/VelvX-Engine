# Velv Language — Referência da Linguagem

Velv é a linguagem de script da VelvX Engine. Ela foi desenhada para ser
pequena, previsível e rápida de escrever: todo jogo é controlado por **books**
(blocos de código nomeados) que a engine chama nos momentos certos.

```velv
unlock speed = 6;          // editável no Inspector em runtime
lock score = 0;            // privado do script

book Main()                // chamado uma vez, ao carregar
{
    print("Olá, VelvX!");
}

book InGame()              // chamado todo frame, durante o jogo
{
    lock x = Input.getAxis("Horizontal");
    GameObject.setVelocity(self, vec3(x * speed, 0, 0));
}
```

---

## 1. Conceitos

### 1.1 Books

Books são funções nomeadas. Dois nomes são especiais:

| Book      | Quando a engine chama                          |
|-----------|------------------------------------------------|
| `Main()`  | Uma vez, quando o script é carregado/anexado   |
| `InGame()`| Todo frame, enquanto o jogo roda               |

Qualquer outro book pode ser chamado **como função**, de outro book ou de um
botão de UI (`"script": "NomeDoBook"` no prefab `.ui`):

```velv
book add(a, b) { return a + b; }      // parâmetros nomeados
book fat(n)                            // recursão funciona
{
    if (n <= 1) { return 1; }
    return n * fat(n - 1);
}

book Main()
{
    print("2+3 =", add(2, 3));         // 5
    print("5! =", fat(5));             // 120
}
```

- Parâmetros viram variáveis locais do book.
- `return;` encerra o book; `return expr;` devolve um valor ao chamador.
- A aridade é verificada em runtime com erro claro (`espera 2 argumento(s)...`).
- Profundidade máxima de chamada: 64 (proteção contra recursão infinita).

### 1.2 Variáveis: `lock` e `unlock`

Não existe `let`. Toda declaração usa uma das duas palavras:

- `lock x = 1;` — variável privada do script.
- `unlock speed = 6;` — além de privada no código, o campo aparece no
  **Inspector** do editor e pode ser editado em runtime, por instância.

Escopo: declarações no topo do arquivo são globais do script; declarações
dentro de books/blocos são locais. Atribuição (`x = ...`) exige que a variável
já exista (`lock`/`unlock` antes).

### 1.3 Controle de fluxo

```velv
if (hp <= 0) { finish(false); } else { print("vivo"); }

lock i = 0;
while (i < 10) {
    if (i == 5) { break; }   // break sai do while mais interno
    i = i + 1;
}
```

`while` tem guarda de 100000 iterações por execução (proteção de loop infinito).

### 1.4 Tipos e operadores

| Tipo     | Exemplo                          | Observações                          |
|----------|----------------------------------|--------------------------------------|
| number   | `3`, `2.5`                       | ponto flutuante                      |
| string   | `"olá"`                          | concatenação com `+`                 |
| bool     | `true`, `false`                  | `&&`, `\|\|`, `!`                    |
| vec3     | `vec3(1, 2, 3)`                  | membros `.x`, `.y`, `.z`             |
| GameObject | `GameObject.find("Player")`    | membro `.name`; `null` se ausente    |

Operadores: `+ - * /`, comparações `== != < > <= >=`, lógicos `&& || !`.
`if (obj)` é verdadeiro quando o objeto existe (não nulo).

### 1.5 `self`

Em scripts **anexados a um GameObject** (componente de script), `self` é o
próprio objeto:

```velv
book InGame()
{
    lock p = GameObject.getPosition(self);
    GameObject.setPosition(self, vec3(p.x, p.y + 1, p.z));
}
```

No script principal do projeto (`mainScript`), `self` não existe; use
`GameObject.find("...")`.

---

## 2. Funções globais

| Função | Descrição |
|--------|-----------|
| `print(...)` | Log no console (vários argumentos separados por espaço) |
| `vec3(x, y, z)` | Constrói vetor |
| `str(v)` | Converte para string |
| `rand(lo, hi)` | Aleatório uniforme |
| `dt()` | Delta time do frame (afetado por `Time.setScale`) |
| `time()` | Tempo total de jogo (afetado por `Time.setScale`) |
| `sin/cos/sqrt/abs/floor` | Matemática |
| `min(a, b)`, `max(a, b)`, `clamp(v, lo, hi)` | Utilidades |
| `Instantiate(proto, pos, scale?)` | Clona um protótipo registrado **ou um GameObject existente**; devolve o clone |
| `Destroy(obj)` | Remove o objeto da cena ativa (seguro para nulo) |

`Instantiate(GameObject.find("EnemyProto"), vec3(0,1,0))` é o padrão de
spawning: o clone herda malha, física, tag e **scripts** do original.

---

## 3. API por namespace

### SceneManager
| Método | Descrição |
|--------|-----------|
| `loadScene(name)` | Carrega a cena `.vscene` do disco (sempre relê — replay/reset de graça) |
| `loadSceneAsync(name)` | Versão assíncrona |
| `swapTo(name)` | Troca de cena preservando posição |
| `positionScene(pos)` | Posiciona a cena |
| `getActiveSceneName()` | Nome da cena ativa |
| `findGameObject(name)` | Busca na cena ativa |

### GameObject
`find`, `create`, `setPosition/getPosition`, `setScale`, `setVelocity/getVelocity`,
`isGrounded`, `getLayer/setLayer`, `setUseGravity`, `setMass`, `setKinematic`,
`setColor`, `getRotation/setRotation` (graus), `getTag/setTag`, `setName`,
`setParent`, `addComponent/removeComponent` (por nome), `addScript`.

### Input
`isKeyDown/isKeyPressed/isKeyUp(Key.X)`, `isActionDown/isActionPressed("...")`,
`getAxis("Horizontal"/"Vertical")`, `getMousePosition()`, `getMouseDelta()`,
`setCursorCaptured(bool)`, `isCursorCaptured()`.

Teclas: `Key.Space, Escape, Enter, Tab, E, R, W, A, S, D, Left, Right, Up, Down,
MouseLeft, MouseRight, LeftShift`.

### UI
| Método | Descrição |
|--------|-----------|
| `loadPrefab(path)` | Carrega um prefab `.ui` para a árvore de UI |
| `setText(element, text)` | Atualiza texto pelo nome (busca em toda a árvore) |
| `setVisible(element, bool)` | Mostra/esconde painéis (máquinas de estado de menu/HUD) |

Botões de UI disparam books pelo campo `"script"` do prefab.

### Physics
`overlaps(a, b)` (AABB, respeita a matriz de layers), `raycast(origin, dir,
maxDist?) -> GameObject`, `setGravity/getGravity`,
`setLayerCollision/getLayerCollision`.

### Camera
`setPosition/getPosition`, `setTarget/getTarget`, `getForward`, `move`,
`setRotation`, `setFOV`, `setOrthoHeight`, `setProjection("perspective"|"ortho")`,
`attachTo(obj)`, `setActive(name)`.

### Time / Application
| Método | Descrição |
|--------|-----------|
| `Time.setScale(f)` | 0 = pausa total (física, `dt()`, `time()`); a UI continua animando |
| `Time.getScale()` | Fator atual |
| `Application.quit()` | Encerra o jogo |

### Assets / Trigger / Inspector
`Assets.resolve/loadModel/loadTexture`; `Trigger.bindKey/fire`; `Inspector.dump`.

---

## 4. Padrões de jogo completos

### 4.1 Máquina de estados (menu → jogo → fim)

```velv
lock state = "menu";

book StartGame()
{
    state = "playing";
    Time.setScale(1);
    UI.setVisible("Menu", false);
    UI.setVisible("HUD", true);
    SceneManager.loadScene("Level1");
}

book InGame()
{
    if (state == "paused") {
        if (Input.isKeyPressed(Key.Escape)) { ResumeGame(); }
        return;
    }
    if (state != "playing") { return; }
    if (Input.isKeyPressed(Key.Escape)) {
        state = "paused";
        Time.setScale(0);              // congela o mundo, UI segue viva
        UI.setVisible("Pause", true);
        return;
    }
    // ...regras do jogo...
}
```

### 4.2 Pause real

`Time.setScale(0)` zera `dt()` e `time()` e congela a física — inimigos,
projéteis e timers param juntos. A UI é atualizada com o tempo real, então
menus de pause continuam responsivos.

### 4.3 Spawning de inimigos

```velv
lock e = Instantiate(GameObject.find("EnemyProto"), vec3(x, 1, z));
GameObject.setName(e, "Enemy" + str(id));   // nomes únicos p/ overlaps
```

### 4.4 Tiro por raycast

```velv
lock hit = Physics.raycast(Camera.getPosition(), Camera.getForward(), 120);
if (hit) {
    if (GameObject.getTag(hit) == "enemy") { Destroy(hit); }
}
```

---

## 5. Erros e depuração

- Erro de parse: o script não carrega; o console mostra arquivo:linha e o
  token esperado. O Player cai no fallback nativo apenas se não houver script.
- Erro de runtime: o book é interrompido, o script é marcado como quebrado e
  o console mostra arquivo:linha + mensagem (ex.: aridade errada, variável
  não declarada, função desconhecida).
- `print(...)` e o painel de problemas do Script IDE mostram o estado.

## 6. Estilo recomendado

- Um **GameManager** global (`mainScript`) com a máquina de estados.
- Um script por comportamento anexado ao objeto (`PlayerController`,
  `EnemyChase`, `CameraFollow`).
- `unlock` para tudo que um designer ajustaria (speed, dano, vidas).
- Nomes de objetos estáveis (`Coin1..N`, `Enemy1..N`) para loops de `overlaps`.