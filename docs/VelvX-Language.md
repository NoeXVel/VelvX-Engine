# VelvX Language

A VelvX Language é a linguagem de script da VelvX Engine. Ela foi desenhada para ser simples de ler, parecida com linguagens modernas e integrada à cena: o mesmo script roda no modo **Play** do editor e no executor headless.

Arquivos de script usam a extensão `.velv`. O ponto de entrada de um projeto é `Scripts/Main.velv`.

## Comentários

```velv
# Comentário de uma linha
// Também é comentário de uma linha
/* Comentário de bloco */
#* Comentário de bloco no estilo VelvX *#
```

## Variáveis

```velv
var vida : Int = 100;
var nome : String = "Gean";
var ativo : Bool = true;
var velocidade := 6.5;          # tipo inferido
var posicao : Vector3 = new.Vector3(0, 1, 0);
```

Tipos disponíveis para anotação:

| Tipo | Valores |
| --- | --- |
| `Bool` / `Boolean` | `true`, `false` |
| `String` / `Text` | textos entre aspas duplas ou simples |
| `Int` / `Integer` | números inteiros (arredondados ao atribuir) |
| `Float` / `Double` / `Number` | números decimais |
| `Vector2` / `Vec2` | `x`, `y` |
| `Vector3` / `Vec3` | `x`, `y`, `z` |

A anotação é opcional. `:=` define o tipo pelo valor inicial; `:` com tipo explícito converte o valor para o tipo. Um tipo desconhecido é mantido sem conversão.

```velv
var contador = 0;
contador += 1;
contador *= 2;
```

Operadores de atribuição: `=`, `+=`, `-=`, `*=`, `/=`.

## Strings e interpolação

```velv
var tempTest : Int = 3;
console.show("Rodei: {tempTest} vezes");
console.show("Posição: {this.position.x}, {this.position.y}");
```

Tudo que estiver entre `{` e `}` é avaliado como expressão. Use `{{` e `}}` para escrever chaves literais. Strings podem ser concatenadas com `+`.

## Funções

```velv
func Main()
{
    console.show("Iniciou");
}

func InGame()
{
    # roda a cada quadro
}

func Somar(a : Int, b : Int)
{
    return a + b;
}
```

Parâmetros aceitam anotação de tipo opcional. `return` sem valor encerra a função. Funções são globais e podem ser chamadas antes da declaração. Esta versão não anota o tipo de retorno: o valor devolvido mantém o tipo que tiver na expressão.

Os tipos dos parâmetros são aplicados aos argumentos na chamada. O valor de retorno não tem anotação de tipo; se precisar de um tipo específico, atribua-o a uma variável tipada.

## Controle de fluxo

```velv
if (vida > 0)
{
    console.show("Vivo");
}
else
{
    console.show("Derrotado");
}

while (contador < 10)
{
    contador += 1;
}
```

`if` e `while` aceitam condição com ou sem parênteses. Existe `else if` (um `if` dentro do `else`). Não há `for` nesta versão.

## Operadores

| Categoria | Operadores |
| --- | --- |
| Aritmética | `+` `-` `*` `/` `%` |
| Comparação | `==` `!=` `<` `<=` `>` `>=` |
| Lógica | `&&` `\|\|` `!` (ou `and`, `or`, `not`) |
| Agrupamento | `( ... )` |

Valores considerados falsos: `false`, `0`, `""`, `null` e vetores de magnitude zero. Qualquer outro valor é verdadeiro. Dois vetores são iguais quando seus componentes são iguais; um vetor pode ser somado, subtraído, multiplicado ou dividido por outro vetor e multiplicado/dividido por um número.

## Ciclo de vida

| Função | Quando roda |
| --- | --- |
| `func Main()` | uma vez, ao iniciar o Play |
| `func InGame()` | a cada quadro, enquanto o Play estiver ativo |

`Main()` é obrigatória para iniciar o jogo. `InGame()` é opcional. Se ocorrer um erro durante o `InGame()`, o Play é encerrado e a mensagem aparece no console de saída.

## APIs da Engine

### console

```velv
console.show("mensagem");
console.warn("atenção");
console.error("falhou");
```

### game

```velv
var delta = game.delta_time;
var direcao = game.get_input_vectors("direita", "esquerda", "baixo", "cima");
```

`get_input_vectors` devolve um `Vector2` com `x` e `y` entre `-1` e `1`. A ordem documentada é: positivo X, negativo X, positivo Y, negativo Y. Os nomes são reconhecidos por significado (`direita`/`right`, `esquerda`/`left`, `cima`/`up`/`frente`, `baixo`/`down`/`trás`), então a ordem não muda o resultado. Os comandos padrão são **WASD** e as **setas**.

### this

`this` representa o objeto do jogador — o objeto cujo script é o ponto de entrada (`EntryScript`) ou, na falta dele, o primeiro `CharacterBody3D`/`CharacterBody2D` da cena.

```velv
this.position = new.Vector3(0, 2, 0);
this.visible = false;
this.CharacterBody3D.velocity = new.Vector3(6, 0, 0);
```

O componente `CharacterBody3D`/`CharacterBody2D` expõe `velocity`, `position` e `on_ground`. A `velocity` do componente é integrada à posição do jogador a cada quadro (a Engine cuida do movimento).

### Scene

```velv
var cena = Scene("Main");
var objeto = cena.object.Pilar;      # pelo nome do objeto
var outro = Scene("Main").object.player;  # pelo id (sem diferenciar maiúsculas)
```

`Scene(nome)` devolve a cena ativa; `object` é a raiz da cena e cada objeto aparece nela pelo **nome** ou pelo **id**. Todo objeto expõe `id`, `position`, `rotation`, `scale` e `visible`.

### Assets

```velv
var objeto = Assets.pathObject("Pilar Esquerdo");
var objeto2 = Assets.pathObject("Objetos/Caixa");   # procura pelo último segmento
objeto.position = new.Vector3(0, 3, 0);
```

Se o objeto não existir, é devolvido um objeto temporário com `position` e `visible`, para que o script continue rodando.

### Vetores

```velv
var a = new.Vector2(1, 2);
var b = new.Vector3(0, 1, 0);

console.show("{a.x} {a.y} {a.magnitude}");
console.show("{b.x} {b.y} {b.z} {b.length}");

var deslocamento = new.Vector3(1, 0, 0) * 6;
b.x += 1;
```

## Exemplo completo

```velv
# Script de entrada do jogador 2D.
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

## Limites e diagnóstico

- Profundidade máxima de chamadas: 64.
- Um `while` pode executar no máximo 500.000 iterações por quadro.
- Erros trazem linha, coluna e uma mensagem amigável no painel **Saída** do editor.
- O interpretador não compila para código nativo nesta versão; ele executa a árvore sintática.

## Ainda não disponível

Arrays/listas com indexação, `for`, classes/tipos personalizados, funções anônimas, `switch`, importação de módulos, física e colisões, áudio, animação e salvamento de estado. Essas peças entram nas próximas etapas da Engine.
