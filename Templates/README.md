# Templates da VelvX Engine

Cada pasta aqui com um `template.json` aparece na tela **Novo projeto** do launcher. Os templates oficiais desta versão são:

| Pasta | Modo | Fase 1 | Fase 2 |
| --- | --- | --- | --- |
| `Starter-3D` | 3D | **Salão de Entrada**: salão fechado, pilares, plataforma, núcleo giratório, portal roxo com partículas e luz própria | **Arena do Núcleo**: arena aberta, núcleo pulsante com partículas, quatro pilares, portal laranja de retorno |
| `Starter-2D` | 2D | **Clareira**: fundo em camadas, plataformas, orbe, cristal giratório, portal roxo com partículas | **Floresta**: névoa, árvores, vaga-lumes, raiz elevada, portal laranja de volta |
| `Empty-3D` | 3D | cenário vazio: apenas a câmera do jogo e o script de entrada | — |
| `Empty-2D` | 2D | cenário vazio: apenas a câmera do jogo e o script de entrada | — |

Os templates Starter são mini-jogos funcionais: **WASD/setas** movem o Player, o portal tem uma `BoxVolume` (3D) ou uma elipse luminosa (2D) com emissor de partículas, e **E** troca de fase. Cada cena tem o seu próprio Player, então o `this` do script aponta para o jogador da fase atual.

## Como a troca de fase funciona

Abra `Scripts/Main.velv` de qualquer Starter. O fluxo é sempre o mesmo:

```velv
if not Scene("Arena").is_loaded()
{
    Scene("Arena").load();       # carrega o arquivo .scene.json em memória
}

Scene("Arena").set_active();     # a cena vira ativa e o "this" passa ao Player dela
this.position = new.Vector3(0, 0.1, -9);   # ponto de entrada da fase
```

- `load()` faz **streaming**: a cena fica na memória e aparece no rodapé da hierarquia.
- `set_active()` troca o viewport, o Player e os inputs para a nova cena.
- `Scenes.current` devolve o nome da fase atual; use isso para separar a lógica de cada sala.
- `espera` (uma variável `global`) evita trocar de fase várias vezes enquanto **E** continua pressionado.

Para criar uma terceira fase, copie `Scenes/Arena.scene.json`, renomeie o arquivo (por exemplo `Chefe.scene.json`), ajuste os atores e adicione o novo portal no script com `Scene("Chefe").load()` / `set_active()`.

## Iluminação, volumes e efeitos nos exemplos

- **3D**: `PointLight` no salão, no portal e nos cantos da arena; `GlobalVolume` para tingir o ambiente; `BoxVolume` nos portais com pulsação ao tocar um corpo; `ParticleEmitter` nas faíscas do portal e do núcleo; névoa (`environment.fogDensity`) em cada fase.
- **2D**: fundos em camadas (céu, colinas, névoa), sprites GIF animados, `ParticleEmitter` nos portais e um `Rotator` girando o cristal.
- **Hierarquia**: os portais são atores `GameObject` com filhos (`BoxVolume`, luz e partículas). Mova o portal pai e os filhos acompanham.

## Criar o seu template

1. Crie um projeto pela Engine e monte nele a estrutura inicial que deseja reutilizar (cenas, scripts, pastas).
2. Copie a pasta do projeto para dentro de `Templates`, com um nome novo.
3. Remova o arquivo `<Nome>.velvxproj` se não quiser que ele vá para os projetos criados (ele é copiado como qualquer outro arquivo).
4. Adicione um `template.json` na raiz da pasta:

```json
{
  "id": "meu-template",
  "name": "Meu template",
  "description": "Minha base de jogo personalizada.",
  "mode": "3D",
  "version": "0.4.5"
}
```

Campos:

| Campo | Obrigatório | Descrição |
| --- | --- | --- |
| `id` | sim | identificador único; sem ele o template é ignorado |
| `name` | não | nome exibido (padrão: nome da pasta) |
| `description` | não | texto do cartão |
| `mode` | não | `2D` (padrão) ou `3D`; define a ordem e o badge |
| `version` | não | versão do template, exibida nos metadados |

## Como a cópia funciona

- Todo o conteúdo da pasta é copiado para o novo projeto.
- `template.json`, `.gitkeep` e pastas ocultas (começando com `.`) **não** são copiados.
- A Engine cria o arquivo `<NomeDoProjeto>.velvxproj` com a cena inicial `Scenes/Main.scene.json` e o script de entrada `Scripts/Main.velv`.
- Todas as cenas `*.scene.json` encontradas em `Scenes/` são registradas no projeto automaticamente, então os portais já funcionam na primeira execução.

Se o seu template usar outros nomes de cena/script, ajuste `StartScene` e `EntryScript` no `.velvxproj` gerado ou mantenha o padrão acima.

## Onde a Engine procura os templates

- **No repositório (desenvolvimento):** pasta `Templates` na raiz do projeto, localizada a partir do executável.
- **Na build portátil:** pasta `Templates` ao lado de `VelvX.Engine.exe`.

Depois de adicionar ou alterar um template, use **Recarregar** na barra lateral do launcher.
