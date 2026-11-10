# VelvX Engine 0.4.0 — Bigorna (Fix)

Build de correção e polimento do fluxo 3D. O foco foi deixar a manipulação
direta simples (mover, girar e escalar), corrigir a escala de modelos
importados, transformar a hierarquia em um grafo de **Atores** de verdade e
dar base para projetos mais pesados rodarem em PCs modestos.

| Pedido | O que foi feito na 0.4.0 |
| --- | --- |
| Escala errada de objetos e modelos exportados | A importação lê os limites reais do modelo e normaliza apenas exportações fora da faixa usual (maior eixo > 100 ou < 0,05). Rigs, skins e animações continuam intactos porque a correção é aplicada na raiz do modelo. A caixa do modelo também alimenta o **MeshCollider**. |
| Gizmo para rotacionar e escalar | O gizmo agora tem três modos: **Mover**, **Girar** e **Escalar**, com anéis de rotação e alças de escala. Atalhos `1`, `2` e `3` (ou teclado numérico) alternam os modos; a barra flutuante no viewport também. |
| Movimento preso na grade | O centro do gizmo arrasta o ator livremente no plano da câmera. O movimento da câmera do editor é contínuo, com aceleração e zoom suave. |
| Filhos não seguiam o pai | `SceneGraph` calcula a matriz de mundo recursivamente. Posição, rotação, escala e arraste de gizmo respeitam o pai, inclusive ao mover um ator pela hierarquia. |
| Inspetor feio | O inspetor foi reorganizado em cartões: transformação, aparência, hierarquia/camada, painel específico do tipo, painel de **Ator** (LOD e distância) e componentes. Materiais e modelos têm ações de importar, limpar e abrir biblioteca. |
| Ajuda esquisita da linguagem | A referência rápida improvisada foi removida. O editor abre a **Documentação da VelvX Language**, com seções navegáveis e exemplos de variáveis, comentários, funções, `if`, `while`, vetores, ciclo de vida, APIs, componentes e colisão. O texto também está em `docs/VelvX-Language.md`. |
| Iluminação fraca | Ambiente + sol direcional + preenchimento frio + recorte lilás + toque quente. Luzes pontuais usam atenuação por alcance e os materiais recebem especular. |
| Box Volumes sem efeito | O volume desenha preenchimento translúcido e arestas com pulso contínuo. Durante o Play ele acende quando um `CharacterBody` entra na área. |
| Sem primitivas 3D | O menu **Adicionar ator** traz Cubo/Caixa, Esfera, Cilindro, Cápsula, Cone e Plano, além de corpos e colisão. |
| MeshCollider manual | O componente **MeshCollider** lê a caixa do modelo importado automaticamente, sem desenhar colisão nem configurar camada manualmente. |
| CharacterBody/StaticBody não colidiam | O runtime resolve colisões AABB entre `CharacterBody` e `StaticBody`/colliders, com múltiplas passadas, camadas do projeto e detecção de chão (`on_ground`). |
| FPS em PCs fracos | A cena passou a tratar cada item como **Ator**, com LOD por distância: malha completa perto, proxy de caixa no meio e corte por distância no fim. O número de atores visíveis aparece no inspetor. |
| Texturas e materiais | Nova biblioteca de materiais em `Assets/Materials`: nome, cor, emissão, textura, suavidade, opacidade, tiling e aplicação direta em modelos. |

## Detalhes técnicos

- `SceneGraph` centraliza matriz local (`escala → rotação → translação`),
  matriz de mundo, posição/axis de mundo, conversão de delta para o espaço do
  pai e caixa de colisão.
- `Scene3DView` usa `ModelVisual3D.Content = null` para ocultar atores, pois
  `ModelVisual3D` não tem `Visibility`. O mapa `_hiddenContents` restaura o
  conteúdo quando o ator volta a ficar visível.
- O LOD mantém um proxy por ator e atualiza as distâncias sem recarregar a
  cena. Ativar ou desativar o LOD no inspetor cria/remove o proxy na hora.
- `SceneObject.ColliderBounds` guarda a caixa local do modelo importado
  (`minX, minY, minZ, maxX, maxY, maxZ`) e é usada pela física.
- `ComponentData` cobre os componentes nativos serializados, incluindo
  `MeshCollider`, `StaticBody`, `CharacterBody`, `ParticleEmitter` e `Camera`.
- A versão do assembly, do launcher, dos templates e do build portátil foi
  atualizada para **0.4.0**.

## Limitações conhecidas

- O **MeshCollider** usa a caixa alinhada do modelo, não a malha triângulo a
  triângulo. É suficiente para colisão de personagem e volumes, mas não
  substitui um motor de física completo.
- Colisões de personagem são resolvidas por eixos AABB e não incluem
  rampas, cápsulas rotacionadas ou empilhamento dinâmico de Rigidbody.
- O LOD usa um proxy de caixa; modelos com silhueta muito diferente da caixa
  podem parecer simplificados no meio da distância.
- Materiais usam os canais difuso, especular e emissivo do WPF 3D; não há
  normal map, PBR ou transparência ordenada por objeto.
