# Templates da VelvX Engine

Cada pasta aqui com um `template.json` aparece na tela **Novo projeto** do launcher. Os templates oficiais desta versão são:

| Pasta | Modo | Conteúdo |
| --- | --- | --- |
| `Starter-3D` | 3D | chão, pilares, plataforma, esfera animada, Player, luz pontual, Global Volume e streaming para a cena `Arena` |
| `Starter-2D` | 2D | fundo, chão, plataforma, orbe GIF, estrela, spritesheet do herói, Player e streaming para `Floresta` |

Os dois templates também trazem cenas de streaming, pastas de assets, um prefab de exemplo e scripts comentados com inputs personalizados.

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
  "version": "0.2.0"
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

Se o seu template usar outros nomes de cena/script, ajuste `StartScene` e `EntryScript` no `.velvxproj` gerado ou mantenha o padrão acima.

## Onde a Engine procura os templates

- **No repositório (desenvolvimento):** pasta `Templates` na raiz do projeto, localizada a partir do executável.
- **Na build portátil:** pasta `Templates` ao lado de `VelvX.Engine.exe`.

Depois de adicionar ou alterar um template, use **Recarregar** na barra lateral do launcher.
