# VelvX Engine 0.3.2 — Bigorna (Fix)

Build de correção dos problemas de layout e desenvolvimento apontados na
[issue #2](https://github.com/NoeXVel/VelvX-Engine/issues/2).

| Item da issue | O que foi feito na 0.3.2 |
| --- | --- |
| Layout confuso, difícil de se localizar | Editor reorganizado em workspace com abas **Cena**, **Script** e **Jogo**; hierarquia e recursos à esquerda, inspetor à direita e console embaixo. |
| Janela de script pequena no rodapé | O script agora abre no workspace central, em tela cheia, com aba própria, indicador de arquivo e botão de ajuda. |
| Aba de jogo separada | O Play abre na aba **Jogo**, com HUD, FPS, reiniciar e parar. A viewport é movida para essa aba automaticamente. |
| Hierarquia sem pai/filho | Hierarquia em árvore com expandir/recolher, arrastar para reparentar, **Tornar objeto raiz** e proteção contra ciclos. |
| Não criar pastas/arrastar recursos | Botões **Nova pasta** e **Novo script**, renomear, mostrar no Explorer e arrastar arquivos/pastas para qualquer pasta do projeto. Prefabs podem ser gravados em qualquer pasta. |
| Troca de cenas ruim | Seletor de cenas com cena ativa destacada, cena inicial marcada e sincronização com o modo Play (streaming via `Scene`). |
| Grade aparece no Play | A grade 2D e a grade 3D somem durante a execução; rótulos, gizmos e chrome do editor também são escondidos. |
| Câmera “em degraus” | Zoom suave com interpolação em 2D e 3D, navegação por WASD/pan contínua a 60 Hz e foco animado no objeto. |
| Câmera sem indicação de direção | Câmeras 3D mostram um frustum colorido com direção, abertura (FOV) e plano distante. |
| Inputs sem “Escutar” | Em **Configurações → Entradas**, o botão **Escutar tecla** captura a próxima tecla e adiciona à ação; **Esc** cancela. |
| Player predefinido | `CharacterBody` virou componente. O Player é um **GameObject** com `CharacterBody` + script; o runtime aceita o componente e mantém compatibilidade com `CharacterBody3D/2D` antigos. |
| Sem partículas | Componente **Emissor de partículas** com quantidade, vida, velocidade, tamanho, cor e gravidade, renderizado em 2D e 3D. |
| Iluminação fraca | Iluminação de três pontos (sol + preenchimento frio + recorte lilás + toque quente) e materiais com especular. |
| Modelos 3D sem textura | A importação copia `.mtl` e texturas referenciadas, detecta textura de mesmo nome do modelo e usa o material/textura do arquivo. |
| Dificuldade de criar qualquer coisa | Menu **Adicionar objeto** simplificado, menu **Adicionar componente** com todos os tipos nativos, prefabs a partir do objeto e recursos arrastáveis. |

## Detalhes técnicos

- `EditorWindow.xaml` foi reescrito em três workspaces; o viewport é reparentado
  entre `SceneHost` e `PlayHost` sem recriar a câmera.
- A seleção da `TreeView` usa `IsSelected` com `INotifyPropertyChanged`, pois
  `TreeView.SelectedItem` é somente leitura.
- `ComponentData` ganhou `Lifetime` e `ParticleSize`; `ParticleEmitter` e
  `CharacterBody` são componentes serializados no `.scene.json`.
- `Scene2DView` e `Scene3DView` desenham partículas de forma determinística a
  partir do tempo e de uma semente por objeto.
- A versão do assembly, do launcher, dos templates e do build portátil foi
  atualizada para **0.3.2**.

## Limitações conhecidas

- Partículas são visualmente determinísticas (sem colisão própria) e o limite
  prático é de 600 partículas por emissor no editor.
- O arrastar de recursos move arquivos do projeto; arquivos externos são
  copiados.
- A compatibilidade com cenas antigas mantém os tipos `CharacterBody3D/2D` e a
  hierarquia por script, mas o fluxo novo recomendado é componente.
