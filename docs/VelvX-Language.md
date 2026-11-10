# VelvX Language

A VelvX Language é a linguagem de script da VelvX Engine. Ela foi pensada para ser curta, legível e integrada à cena: o mesmo arquivo roda no modo **Play** do editor e no executor headless.

Scripts usam a extensão `.velv`. O ponto de entrada padrão de um projeto é `Scripts/Main.velv`, mas cada ator também pode executar seu próprio script por meio do componente **Script**.

> Este documento descreve a linguagem da VelvX Engine 0.4.0. Se você está começando, leia as seções na ordem e use os exemplos como base.

---

## 1. Primeiro script

```velv
# Executado uma vez quando o Play começa.
func Main()
{
    console.show("Olá, VelvX!");
}

# Executado a cada quadro enquanto o jogo estiver rodando.
func InGame()
{
}
```

`Main()` é obrigatória para iniciar o jogo. `InGame()` é opcional. Se ocorrer um erro dentro de `InGame()`, o Play é encerrado e a mensagem aparece no painel **Saída** com linha e coluna.

---

## 2. Comentários

```velv
# Comentário de uma linha
// Também é comentário de uma linha
/* Comentário de bloco */
#* Comentário de bloco no estilo VelvX *#
```

Use comentários para explicar **por que** uma regra existe, não para repetir o código.

---

## 3. Variáveis

```velv
var vida : Int = 100;
var nome : String = "Gean";
var ativo : Bool = true;
var velocidade := 6.5;             # o tipo é inferido pelo valor
var posicao : Vector3 = new.Vector3(0, 1, 0);
```

A anotação de tipo é opcional:

- `var contador = 0;` — o tipo vem do valor (`Int`).
- `var vida : Int = 10.9;` — o valor é convertido para o tipo indicado (`11`).
- `var nome : String = 42;` — converte para texto (`"42"`).
- Um tipo desconhecido é mantido sem conversão.

### Tipos disponíveis

| Tipo | Aceita |
| --- | --- |
| `Bool` / `Boolean` | `true`, `false` |
| `String` / `Text` | textos entre aspas duplas ou simples |
| `Int` / `Integer` | números inteiros (arredondados ao atribuir) |
| `Float` / `Double` / `Number` | números decimais |
| `Vector2` / `Vec2` | `x`, `y` |
| `Vector3` / `Vec3` | `x`, `y`, `z` |

### Atribuição e operadores de atribuição

```velv
var pontos = 0;
pontos = 10;
pontos += 5;    # 15
pontos -= 2;    # 13
pontos *= 2;    # 26
pontos /= 2;    # 13
```

Uma variável criada dentro de uma função existe apenas naquela função. Variáveis globais podem ser declaradas no topo do arquivo.

---

## 4. Strings e interpolação

```velv
var tempTest : Int = 3;
console.show("Rodei {tempTest} vezes");
console.show("Posição: {this.position.x}, {this.position.y}");
```

Tudo que estiver entre `{` e `}` é avaliado como expressão. Use `{{` e `}}` para escrever chaves literais.

Strings podem ser concatenadas com `+`:

```velv
var nome = "VelvX";
console.show("Bem-vindo ao " + nome);
```

---

## 5. Funções

```velv
func Main()
{
    var total = Somar(2, 3);
    console.show("Total: {total}");
}

func Somar(a : Int, b : Int)
{
    return a + b;
}

func InGame()
{
    # roda a cada quadro
}
```

Regras importantes:

- Funções são globais e podem ser chamadas antes da declaração.
- Parâmetros aceitam anotação de tipo opcional; a conversão é aplicada no momento da chamada.
- `return` sem valor encerra a função.
- A função não declara o tipo de retorno: o valor devolvido mantém o tipo da expressão.
- Se precisar de um tipo específico no retorno, atribua a uma variável tipada:

```velv
func Metade(valor : Float)
{
    return valor / 2;
}

var metade : Float = Metade(9);
```

Chamadas aninhadas são permitidas até 64 níveis.

---

## 6. Controle de fluxo

### if / else

```velv
if (vida > 0)
{
    console.show("Vivo");
}
else
{
    console.show("Derrotado");
}
```

A condição pode ser escrita com ou sem parênteses. Também existe `else if`:

```velv
if (vida >= 70)
{
    console.show("Forte");
}
else if (vida >= 30)
{
    console.show("Ferido");
}
else
{
    console.show("Crítico");
}
```

### while

```velv
var contador = 0;
while (contador < 10)
{
    contador += 1;
}
```

Nesta versão não existe `for`. Use `while` com um contador ou percorra posições conhecidas.

Para evitar travamentos, um `while` pode executar no máximo **500.000 iterações por quadro**. Ao atingir o limite, o Play é interrompido com uma mensagem de erro.

---

## 7. Operadores

| Categoria | Operadores |
| --- | --- |
| Aritmética | `+` `-` `*` `/` `%` |
| Comparação | `==` `!=` `<` `<=` `>` `>=` |
| Lógica | `&&` `\|\|` `!` — ou `and`, `or`, `not` |
| Agrupamento | `( ... )` |

### Valores verdadeiros e falsos

São considerados falsos:

- `false`
- `0`
- `""` (texto vazio)
- `null`
- vetores de magnitude zero

Qualquer outro valor é verdadeiro.

### Vetores

Dois vetores são iguais quando todos os componentes são iguais. Um vetor pode ser:

- somado, subtraído, multiplicado ou dividido por outro vetor;
- multiplicado ou dividido por um número.

```velv
var a = new.Vector3(1, 0, 0);
var b = new.Vector3(0, 1, 0);
var soma = a + b;          # (1, 1, 0)
var deslocamento = a * 6;  # (6, 0, 0)
```

---

## 8. Ciclo de vida

| Função | Quando roda |
| --- | --- |
| `func Main()` | uma vez, ao iniciar o Play |
| `func InGame()` | a cada quadro, enquanto o Play estiver ativo |

Em um componente **Script**, `this` é o ator dono do script. Vários atores podem usar o mesmo arquivo — cada um recebe seu próprio `this`.

Ordem de execução em cada quadro:

1. Script de entrada (`Main.velv`).
2. Componentes de script dos atores.
3. Componentes nativos (como `Rotator`).
4. Física, colisões e triggers.

---

## 9. APIs da Engine

### console

```velv
console.show("mensagem");
console.warn("atenção");
console.error("falhou");
```

As mensagens aparecem no painel **Saída** do editor e no console do executor headless.

### game

```velv
var delta = game.delta_time;
var direcao = game.get_input_vectors("direita", "esquerda", "baixo", "cima");
var atacando = game.get_input("atacar");
```

`get_input_vectors(...)` devolve um `Vector2` com `x` e `y` entre `-1` e `1`. A ordem documentada é: positivo X, negativo X, positivo Y, negativo Y. Os nomes são reconhecidos por significado (`direita`/`right`, `esquerda`/`left`, `cima`/`up`/`frente`, `baixo`/`down`/`trás`), então a ordem não altera o resultado.

`get_input("ação")` devolve `true` enquanto alguma tecla mapeada para a ação estiver pressionada. Use o nome exato criado em **Configurações do projeto → Entradas**. Se a ação não existir, os nomes de direção continuam caindo no padrão **WASD** e **setas**.

### Entradas personalizadas

As ações são definidas no projeto (`<Projeto>.velvxproj`, campo `inputs`), não no script. Abra **Configurações do projeto** (`Ctrl+,`) → aba **Entradas**, clique em **Adicionar entrada**, escolha o nome e as teclas (por exemplo `Q`, `Espace`, `LeftShift`, `F5`) e salve.

```velv
func InGame()
{
    if game.get_input("atacar")
    {
        console.show("Ataque!");
    }

    if game.get_input("interagir")
    {
        Scene("Arena").load();
    }
}
```

Teclas aceitas incluem letras (`A`–`Z`), números, setas, `Espace`/`Space`, `Enter`, `Shift`, `Ctrl`, `Alt`, `Tab`, `Backspace` e `F1`–`F12`.

### this

`this` representa o ator dono do script. No script de entrada ou em um componente de script, `this` é o ator ao qual o script foi associado — o jogador, no caso da entrada.

```velv
this.position = new.Vector3(0, 2, 0);
this.visible = false;
this.CharacterBody3D.velocity = new.Vector3(6, 0, 0);
```

Todo ator expõe `id`, `name`, `position`, `rotation`, `scale`, `visible` e `tag`. O componente `CharacterBody3D`/`CharacterBody2D` expõe `velocity`, `position` e `on_ground`. A `velocity` do componente é integrada à posição do jogador a cada quadro pela Engine.

### Scene

```velv
var cena = Scene("Main");
var objeto = cena.object.Pilar;          # pelo nome do ator
var outro = Scene("Main").object.player; # pelo id (sem diferenciar maiúsculas)
```

`Scene(nome)` devolve a cena ativa; `object` é a raiz da cena e cada ator aparece nela pelo **nome** ou pelo **id**.

### Streaming de cenas

```velv
func Main()
{
    console.show("Cena inicial carregada.");
}

func InGame()
{
    # Carrega a arena quando o jogador se aproxima da porta.
    if this.position.z < -4 and not Scene("Arena").is_loaded()
    {
        Scene("Arena").load();
        console.show("Arena carregada.");
    }

    # Descarrega quando o jogador volta para a praça.
    if this.position.z > 2 and Scene("Arena").is_loaded()
    {
        Scene("Arena").unload();
    }

    # Troca a cena ativa (a que recebe `this` e os inputs do jogo).
    if game.get_input("entrar_arena")
    {
        Scene("Arena").set_active();
    }
}
```

Cada cena mantém seu próprio arquivo `.scene.json` em `Scenes/`. O nome usado no script é o nome do arquivo, sem extensão. A cena ativa é a que o `this` do script de entrada enxerga e a que recebe os comandos de direção.

O gerenciador global `Scenes` oferece as mesmas operações por nome:

```velv
Scenes.load("Arena");
Scenes.is_loaded("Arena");
Scenes.unload("Arena");
Scenes.set_active("Arena");
console.show(Scenes.current);   # nome da cena ativa
console.show(Scenes.loaded());  # lista separada por vírgulas
```

Uma cena carregada continua na memória (e aparece no rodapé da hierarquia) até ser descarregada ou até o Play terminar. Carregar duas vezes a mesma cena não duplica nada.

### Assets

```velv
var objeto = Assets.pathObject("Pilar Esquerdo");
var objeto2 = Assets.pathObject("Objetos/Caixa");   # procura pelo último segmento
objeto.position = new.Vector3(0, 3, 0);
```

Se o ator não existir, é devolvido um objeto temporário com `position` e `visible`, para que o script continue rodando.

### Vetores

```velv
var a = new.Vector2(1, 2);
var b = new.Vector3(0, 1, 0);

console.show("{a.x} {a.y} {a.magnitude}");
console.show("{b.x} {b.y} {b.z} {b.length}");

var deslocamento = new.Vector3(1, 0, 0) * 6;
b.x += 1;
```

---

## 10. Componentes e colisão

Cada ator pode receber componentes pelo inspetor (**+ Componente**):

| Componente | O que faz |
| --- | --- |
| `Script` | executa um arquivo `.velv` com `Main()` e `InGame()` no escopo do ator |
| `Rotator` | gira o ator continuamente em torno de um eixo |
| `PointLight` | adiciona uma luz pontual ao ator |
| `Rigidbody` | aplica gravidade e velocidade ao ator |
| `CharacterBody` | corpo controlado por script (jogador) |
| `StaticBody` | corpo imóvel que participa de colisões |
| `BoxCollider` / `SphereCollider` | colisão por forma geométrica |
| `MeshCollider` | deriva a colisão das dimensões do modelo importado, sem desenhar a caixa manualmente |

Atores com **CharacterBody3D**, **CharacterBody2D** ou o componente **CharacterBody** colidem com **StaticBody3D** e com atores que tenham `BoxCollider`, `SphereCollider` ou `MeshCollider` sem **Trigger**. Triggers continuam disparando `OnTriggerEnter`, `OnTriggerStay` e `OnTriggerExit` nos scripts.

```velv
func OnTriggerEnter(outro)
{
    console.show("Entrou: {outro.name}");
}

func OnTriggerExit(outro)
{
    console.show("Saiu: {outro.name}");
}
```

As camadas do projeto controlam quais pares colidem (**Configurações do projeto → Camadas**).

---

## 11. Exemplos completos

### Jogador 3D

```velv
func Main()
{
    console.show("Use WASD para andar.");
}

func InGame()
{
    var direcao = game.get_input_vectors("direita", "esquerda", "cima", "baixo");
    this.CharacterBody3D.velocity = new.Vector3(direcao.x * 5, 0, direcao.y * 5);
}
```

### Jogador 2D com objeto girando

```velv
func Main()
{
    console.show("VelvX 2D pronta. Use WASD ou as setas.");
}

func InGame()
{
    var direcao = game.get_input_vectors("direita", "esquerda", "cima", "baixo");
    this.CharacterBody2D.velocity = new.Vector2(direcao.x * 5, direcao.y * 5);

    var orbe = Scene("Main").object.Orbe;
    orbe.rotation.z += game.delta_time * 45;
}
```

### Contador com while

```velv
func Main()
{
    var numero = 1;
    while (numero <= 5)
    {
        console.show("Contagem: {numero}");
        numero += 1;
    }
}
```

---

## 12. Limites e diagnóstico

- Profundidade máxima de chamadas: **64**.
- Um `while` pode executar no máximo **500.000 iterações por quadro**.
- Erros trazem linha, coluna e uma mensagem amigável no painel **Saída**.
- O interpretador executa a árvore sintática; não há compilação para código nativo nesta versão.

Mensagens de erro comuns:

| Mensagem | Causa provável |
| --- | --- |
| `Variável não declarada` | nome digitado errado ou fora do escopo |
| `Esperado ';'` | falta de ponto e vírgula ao final da instrução |
| `Chamada muito profunda` | recursão sem condição de parada |
| `Ação de entrada não encontrada` | nome diferente do cadastrado nas Configurações |

---

## 13. Ainda não disponível

Arrays/listas com indexação, `for`, classes e tipos personalizados, funções anônimas, `switch`, importação de módulos, áudio por script, animação controlada por script (modelos 3D já tocam animações embutidas), câmera de jogo dedicada e salvamento de estado. Essas peças entram nas próximas etapas da Engine.
