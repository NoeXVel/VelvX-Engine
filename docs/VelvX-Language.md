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
var atacando = game.get_input("atacar");
```

`get_input_vectors` devolve um `Vector2` com `x` e `y` entre `-1` e `1`. A ordem documentada é: positivo X, negativo X, positivo Y, negativo Y. Os nomes são reconhecidos por significado (`direita`/`right`, `esquerda`/`left`, `cima`/`up`/`frente`, `baixo`/`down`/`trás`), então a ordem não muda o resultado.

`get_input("ação")` devolve `true` enquanto alguma tecla mapeada para a ação estiver pressionada. Use o nome exato da ação criada em **Project Settings → Entradas**. Se a ação não existir, os nomes de direção continuam caindo no padrão **WASD** e **setas**.

### Inputs personalizados

As ações são definidas no projeto (`<Projeto>.velvxproj`, campo `inputs`), não no script. Abra **Project Settings** (`Ctrl+,`) → aba **Entradas**, clique em **Adicionar entrada**, escolha o nome e as teclas (ex.: `Q`, `Espace`, `LeftShift`, `F5`) e salve. O script então usa:

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

O editor valida o nome da ação e avisa quando ele não existe. Teclas aceitas incluem letras (`A`–`Z`), números, setas, `Espace`/`Space`, `Enter`, `Shift`, `Ctrl`, `Alt`, `Tab`, `Backspace` e `F1`–`F12`.

### this

`this` representa o objeto dono do script. No script de entrada (`EntryScript`) ou em um componente de script, `this` é o objeto ao qual o script foi associado — o jogador, no caso da entrada.

```velv
this.position = new.Vector3(0, 2, 0);
this.visible = false;
this.CharacterBody3D.velocity = new.Vector3(6, 0, 0);
```

Todo objeto expõe `id`, `name`, `position`, `rotation`, `scale`, `visible` e `tag`. O componente `CharacterBody3D`/`CharacterBody2D` expõe `velocity`, `position` e `on_ground`. A `velocity` do componente é integrada à posição do jogador a cada quadro (a Engine cuida do movimento).

### Componentes

Cada objeto pode ter componentes adicionados pelo inspetor (**+ Componente**):

| Componente | O que faz |
| --- | --- |
| `Script` | executa um arquivo `.velv` com `Main()` e `InGame()` no escopo do objeto |
| `Rotator` | gira o objeto continuamente em torno de um eixo, na velocidade configurada |
| `PointLight` | adiciona uma luz pontual ao objeto, com intensidade e alcance |

Componentes de script rodam depois do script de entrada (`Main.velv`) e antes dos componentes nativos, todos no mesmo quadro. Vários objetos podem usar o mesmo arquivo de script — cada instância tem o seu próprio `this`.

### Scene

```velv
var cena = Scene("Main");
var objeto = cena.object.Pilar;      # pelo nome do objeto
var outro = Scene("Main").object.player;  # pelo id (sem diferenciar maiúsculas)
```

`Scene(nome)` devolve a cena ativa; `object` é a raiz da cena e cada objeto aparece nela pelo **nome** ou pelo **id**. Todo objeto expõe `id`, `position`, `rotation`, `scale` e `visible`.

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

Cada cena mantém seu próprio arquivo `.scene.json` em `Scenes/` e o nome usado no script é o nome do arquivo, sem extensão. A cena ativa é a que o `this` do script de entrada enxerga e a que os comandos de direção movimentam.

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

Arrays/listas com indexação, `for`, classes/tipos personalizados, funções anônimas, `switch`, importação de módulos, física e colisões, áudio, controle de animação por script (modelos 3D já tocam animações embutidas), câmera de jogo dedicada e salvamento de estado. Essas peças entram nas próximas etapas da Engine.
