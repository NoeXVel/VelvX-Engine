# VelvX Engine 0.3.0 — Bigorna

A build que resolve os itens da [issue #1](https://github.com/NoeXVel/VelvX-Engine/issues/1) e consolida a base para jogos 2D e 3D. “Bigorna” é a versão que transforma o editor da Forja em uma ferramenta com GameObject, componentes, câmera nativa, física simples, triggers e uma janela de script de verdade.

## Correções da issue #1

| Item | Situação na 0.3.0 |
| --- | --- |
| Gizmo não acompanhava o objeto | Corrigido: a projeção parte da posição do objeto e o arraste atualiza o transform em tempo real. |
| Clique no viewport não selecionava | Corrigido: hit-test no modelo visual e nos componentes seleciona o objeto. |
| GameObject + Transform obrigatório | Implementado: todo objeto tem transformação, camada, pai e componentes. O tipo **Objeto vazio** cria um GameObject puro. |
| Câmera nativa | Implementada: `Camera3D`/`Camera2D` e componente **Câmera** com FOV, near, far e câmera principal do Play. |
| Janela de script profissional | Implementada: `ScriptEditorWindow` com autocomplete, auto-indent, realce, lista de scripts, referência rápida e atalhos. |
| Texto dos botões cortado | Corrigido: botões usam `MinHeight` + `Padding` em vez de altura fixa. |
| Criação de projeto vazia confusa | Corrigido: templates **Vazio 3D** e **Vazio 2D** e proteção contra objetos duplicados/cena inexistente. |
| Escala/textura de modelo | Implementado: campos de escala e textura no inspetor e no Mesh Renderer. |
| Triggers | Implementado: `OnTriggerEnter`, `OnTriggerStay` e `OnTriggerExit`. |
| Navegação da hierarquia | Implementado: `get_parent()`, `get_children()`, `find_child()` e `is_child_of()`; pai editável no inspetor. |
| Scripts globais e por objeto | Implementado: lista de scripts globais nas configurações e componentes de script por objeto. |
| Gerenciador de assets | Implementado: ícones por tipo, pastas do projeto e arrastar-soltar. |
| Sistema de UI | Planejado para a 0.4.0. |
| Matriz de colisão e camadas | Camadas e pares de colisão já existem em Configurações → Camadas e scripts; a matriz visual fica para a 0.4.0. |
| Jogo de exemplo simples | Planejado para a 0.4.0. |

## Novidades

### Editor

- GameObject + Transform como base; **Objeto vazio** no menu de criação.
- Gizmos coloridos que seguem o objeto, com snap por `Ctrl`/`Shift`.
- Seleção de objeto pelo viewport 3D.
- Inspetor com **camada**, **pai**, escala/textura de modelo, câmera e componentes.
- Janela grande de script (`Ampliar` / menu **Editor de script**):
  - autocomplete com `Ctrl+Espaço`;
  - auto-indent de 4 espaços e fechamento de chaves;
  - realce da VelvX, números de linha e status Ln/Col;
  - lista dos scripts do projeto e referência rápida com trechos clicáveis;
  - abrir, salvar, salvar como e sincronização com o painel do editor.
- Configurações do Projeto → **Camadas e scripts**: camadas, pares que não colidem e scripts globais.
- Botões sem corte vertical e recursos com ícones e arrastar-soltar.

### Runtime

- Componentes: Script, Câmera, Mesh Renderer, Box/Sphere Collider, Rigidbody, Audio Source, Rotator, SpriteAnimator, PointLight e BoxVolume.
- Física simples com AABBs, gravidade e triggers.
- Câmera principal usada no modo Play.
- Hierarquia em scripts e scripts globais.
- Filtro de colisão por camada.

### Projetos e assets

- Templates **Vazio 3D** e **Vazio 2D**.
- Escala e textura por modelo 3D.
- Projetos novos sem cena pré-populada duplicada.

## Requisitos

- Windows 10 ou 11, 64 bits.
- 4 GB de RAM (8 GB recomendado), GPU com DirectX 11 (GTX 1650 roda com folga).
- Nada de .NET instalado: a build é autossuficiente.

## Instalação

1. Baixe `VelvX-Engine-win-x64.zip` na página de releases.
2. Extraia em qualquer pasta.
3. Execute `VelvX.Engine.exe`.
4. Em **Novo projeto**, escolha **3D**, **2D**, **Vazio 3D** ou **Vazio 2D**.

## Limitações conhecidas

- A hierarquia visual ainda é plana; o vínculo de pai existe no inspetor e nos scripts.
- A matriz de colisão é representada por uma lista de pares `A|B`.
- Sistema de UI, jogo de exemplo e exportação de jogo independente ficam para a 0.4.0.
- Física é simples (AABB), sem raycasts ou juntas.

## Publicação

O pacote é gerado por:

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

O resultado fica em `dist\VelvX-Engine-win-x64.zip`. A release `v0.3.0 — Bigorna` deve ser publicada em <https://github.com/NoeXVel/VelvX-Engine/releases> com o zip anexado. O arquivo `SHA256.txt` acompanha o pacote para conferência.
