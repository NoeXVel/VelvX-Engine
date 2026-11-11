# VelvX Engine 0.4.5 — Bigorna (Fix)

Build de correção da [issue #3 — "Documentação e Engine"](https://github.com/NoeXVel/VelvX-Engine/issues/3).
O foco foi fechar os oito problemas relatados: estabilidade da documentação,
editor de script, variáveis persistentes, hierarquia no Play, Undo/Redo,
mapeamento UV, renomeação de atores, seletor de cores e templates jogáveis de
duas fases.

| Problema da issue #3 | O que foi feito na 0.4.5 |
| --- | --- |
| Clicar nos tópicos da documentação derrubava a Engine | A janela de documentação não reutiliza mais índices inválidos ao trocar de tópico e cada navegação é protegida por tratamento de erro. Todos os tópicos abrem em sequência sem fechar o editor. |
| Autocomplete e auto-indentação "removidos" | O autocomplete (`Ctrl+Espaço`), o auto-indent ao pressionar `Enter` e a lista de sugestões voltaram a funcionar na janela de script e no painel lateral. |
| `var` só criava variáveis temporárias | A VelvX agora tem a declaração **`global`**: `global fase : Int = 1;` ou `global pontos := 0;`. A variável é criada uma única vez e mantém o valor entre `Main()`, `InGame()`, funções e trocas de cena. `var` continua sendo local ao escopo e à chamada. |
| Filhos não acompanhavam o pai durante o Play | O runtime aplica a matriz do pai a cada quadro também no modo Play. Mover, girar ou escalar um ator com filhos arrasta a árvore inteira, exatamente como no editor. |
| `Ctrl+Z` e `Ctrl+Y` não funcionavam | Undo/Redo por snapshot da cena cobre transformar, renomear, importar modelo/sprite, aplicar/remover material, adicionar partículas, criar, duplicar e excluir atores, instanciar prefab, definir câmera do jogo e alterações de cena. `Ctrl+Shift+Z` também refaz. |
| Textura de material não seguia o UV Mapping | A textura agora usa as coordenadas UV gravadas no modelo (`TextureCoordinates`), inclusive em modelos importados com rig. O material não estica mais a imagem pela caixa do objeto. |
| Não era possível renomear atores na hierarquia | Renomeie com `F2`, duplo clique no nome ou **Renomear ator** no menu de contexto. A edição é inline, nomes repetidos recebem sufixo automático e um aviso aparece no console; a ação entra no histórico de Undo/Redo. |
| Sem templates de jogo 3D/2D com fases, efeitos e luzes | Os templates **Starter 3D** e **Starter 2D** viraram jogos de duas fases: salão/clareira inicial e arena/floresta, ligados por portais com partículas e luz. As cenas têm iluminação, volumes, objetos rotativos, hierarquia pai/filho e um Player com script em cada fase. |
| Seleção de cor só por hexadecimal | A nova janela **seletor de cores** tem roda HSV, controle de brilho, campo hexadecimal, amostra e paleta rápida. Ela abre ao clicar na cor do ator, da partícula, do material (albedo/emissão) e dos campos `COR` do inspetor. |
| "Como funciona a troca de salas?" | O fluxo completo está documentado em `docs/VelvX-Language.md` (seção **Fases com um Player por cena**) e em `Templates/README.md`, com o código comentado dos templates como exemplo real. |

## Detalhes técnicos

- **Documentação**: a lista de tópicos usa um índice incrementado e a troca de
  conteúdo é isolada em `try/catch`, então um tópico com problema não derruba o
  processo.
- **`global`**: o interpretador mantém um `Scope` global separado. Uma
  declaração `global` já existente preserva o valor atual; para reiniciar, use
  uma atribuição comum (`fase = 1;`).
- **Hierarquia no Play**: `SceneGraph` fornece a matriz de mundo; a sessão de
  jogo grava a posição local dos filhos e recalcula o mundo depois de mover o
  pai, sem alterar os valores locais salvos na cena.
- **Undo/Redo**: cada operação registra `SceneDocument.Clone()` antes da
  alteração. As pilhas são limitadas e restauram também a seleção.
- **UV Mapping**: `Scene3DView` monta o `ImageBrush` usando
  `MeshGeometry3D.TextureCoordinates`; materiais com repetição multiplicam as
  coordenadas em vez de reamostrar a malha.
- **Renomear**: o nome é editado no próprio item da hierarquia e validado
  contra os nomes da cena. O nome final é aplicado ao ator, ao inspetor, à
  hierarquia e aos vínculos de pai/filho.
- **Seletor de cores**: janela própria com roda HSV desenhada em
  `WriteableBitmap`, marcador arrastável, barra de brilho, `#RRGGBB` e paleta de
  18 cores; usada pelo ator, partículas, materiais e inspetor.
- **Trocas de fase**: ao chamar `set_active()`, `this` passa a apontar para o
  Player da cena ativada, os scripts de componentes da cena carregada começam a
  rodar e a posição do jogador pode ser definida logo após a troca. Ao
  descarregar a cena ativa, o runtime rebinda o Player da próxima cena.
- A versão do assembly, do launcher, dos templates e do build portátil foi
  atualizada para **0.4.5**.

## Verificação

- `dotnet build VelvX.Engine.csproj -c Debug` e `-c Release` sem erros nem
  avisos.
- Executor headless: `tests/global-vars.velv` confirma que `global` mantém o
  valor entre quadros; `tests/language-smoke.velv` continua passando.
- Sonda de templates: cria projetos a partir de **Starter 3D** e **Starter 2D**,
  simula o movimento até o portal, confirma a troca para Arena/Floresta, o spawn
  do novo Player e o retorno para a cena inicial.
- Build portátil `dist\VelvX-Engine-win-x64.zip` gerada com checksum SHA-256 em
  `dist\SHA256.txt`.

## Limitações conhecidas

- O seletor de cores trabalha em HSV/RGB; não há conta-gotas sobre o viewport.
- Undo/Redo cobre operações de cena; digitação dentro do editor de script usa o
  desfazer nativo do próprio editor de texto.
- O UV Mapping depende de o modelo importado ter coordenadas UV. Modelos sem UV
  continuam recebendo o mapeamento padrão da primitiva.
- A troca de fase dos templates recarrega a cena de destino se ela ainda não
  estiver carregada; cenas grandes podem ter uma pequena pausa no primeiro
  acesso.
