# Publicando a VelvX Engine no GitHub

Repositório: <https://github.com/NoeXVel/VelvX-Engine>

O código-fonte fica no Git; a pasta `dist/` (build portátil e zip) **não** é versionada. O pacote pronto vai em **Releases**.

## 1. Antes de enviar

Confirme que o Git está instalado:

```powershell
git --version
```

Se não estiver, instale o [Git for Windows](https://git-scm.com/download/win). Ele já inclui o **Git Credential Manager**, que abre o navegador para autenticar no GitHub na primeira vez.

## 2. Primeiro envio (o repositório já tem um commit remoto)

No repositório já existe o arquivo `.github/workflows/main.yml`. O fluxo abaixo mantém esse arquivo e coloca a Engine por cima, sem reescrever o histórico.

```powershell
cd "C:\Users\Gean\Documents\MyEngine"

git init
git branch -M main

git add .
git commit -m "VelvX Engine 0.3.0 - Bigorna: GameObject, componentes, física, triggers e janela de script"

git remote add origin https://github.com/NoeXVel/VelvX-Engine.git
git fetch origin
git merge origin/main --allow-unrelated-histories -m "Combina com o conteudo inicial do repositorio"

git push -u origin main
```

Na primeira autenticação, o Git Credential Manager abre uma janela do navegador. Entre com a conta **NoeXVel** e autorize. Não use senha da conta como credencial: use o navegador ou um **Personal Access Token** (Settings → Developer settings → Personal access tokens) como senha.

> Se o `git push` for recusado porque o remoto mudou, rode `git pull origin main --allow-unrelated-histories`, resolva os conflitos (se houver) e envie de novo.

## 3. Envios seguintes

```powershell
git status
git add .
git commit -m "Descreva o que mudou"
git push
```

## 4. Publicar a build portátil

1. Gere o pacote:

   ```powershell
   powershell -ExecutionPolicy Bypass -File .\build.ps1
   ```

   O script lê a versão do `.csproj`, publica a Engine para `win-x64` (self-contained), copia os templates e gera `dist\VelvX-Engine-win-x64.zip`.

2. Abra <https://github.com/NoeXVel/VelvX-Engine/releases> e clique em **Draft a new release**.
3. Crie a tag `v0.3.0`, título `VelvX Engine 0.3.0 — Bigorna` e use as notas abaixo.
4. Anexe `dist\VelvX-Engine-win-x64.zip` em **Attach binaries**.
5. Publique a release. Quem baixar só precisa extrair e executar `VelvX.Engine.exe` — não é necessário instalar o .NET.

### Notas sugeridas para a release v0.3.0

```markdown
## VelvX Engine 0.3.0 — Bigorna

A build que corrige os problemas da issue #1 e fecha a base para jogos 2D e 3D.

### Corrigido
- Gizmos de transformação agora seguem o objeto selecionado e funcionam no viewport 3D e 2D.
- Clique no viewport seleciona o objeto correto.
- Textos de botões não são mais cortados verticalmente (MinHeight + Padding).
- Projetos novos 3D/2D não nascem mais com objetos duplicados ou cena inexistente.
- Novos templates Vazio 3D e Vazio 2D para começar do zero, com apenas a câmera e o script de entrada.

### Adicionado
- GameObject + Transform como base: todo objeto tem transformação e pode receber componentes.
- Câmera nativa editável: FOV, near, far, câmera principal e botão para definir a câmera do jogo.
- Janela grande de script: autocomplete, auto-indent, realce, lista de scripts e referência rápida clicável.
- Componentes: Script, Câmera, Mesh Renderer, Box/Sphere Collider, Rigidbody e Audio Source.
- Física simples e triggers OnTriggerEnter/Stay/Exit.
- Hierarquia em scripts: get_parent/get_children/find_child/is_child_of e pai editável no inspetor.
- Camadas, matriz de colisão e scripts globais em Configurações do Projeto → Camadas e scripts.
- Escala e textura por modelo 3D no inspetor.
- Importação de modelos 3D por menu, arraste ou inspetor.

### Já disponível
- Modelos OBJ/FBX/GLTF/GLB com texturas e animações.
- Sprites, GIFs e spritesheets no 2D.
- Streaming de cenas com `Scene("Nome").load()/unload()/set_active()/is_loaded()`.
- Iluminação com ambiente, sol, luzes pontuais, Global Volume e Box Volume.
- Inputs personalizados, limite de FPS e VelvX Language com Main/InGame.

### Requisitos
- Windows 10/11 64 bits, 4 GB de RAM (8 GB recomendado), GPU com DirectX 11 (GTX 1650 roda com folga).

O zip é portátil: extraia e execute `VelvX.Engine.exe`. Projetos de exemplo são criados pelo launcher em **Novo projeto**.
```

## 5. Alternativa: GitHub Desktop

Se preferir interface gráfica:

1. **File → Add local repository** e escolha `C:\Users\Gean\Documents\MyEngine`.
2. Se ele avisar que a pasta não é um repositório, clique em **create a repository**.
3. Faça o primeiro commit, publique em **NoeXVel/VelvX-Engine** e depois use **Push origin**.
4. Use **Repository → Repository settings → Git ignore** para conferir as regras, ou mantenha o `.gitignore` do projeto.

## O que não deve ir para o Git

O `.gitignore` já cobre:

- `bin/`, `obj/`, `.vs/`, arquivos `*.user`;
- `dist/` e `*.zip` (binários grandes, vão nas Releases);
- capturas e saídas geradas em `tests/`;
- arquivos de sistema (`Thumbs.db`, `.DS_Store`).

## Problemas comuns

| Sintoma | Causa provável | Solução |
| --- | --- | --- |
| `Authentication failed` | senha comum ou credencial antiga | use o Git Credential Manager, um token ou SSH |
| `rejected (fetch first)` | o remoto tem commits novos | `git pull origin main` e `git push` |
| `remote already exists` | o remoto foi adicionado duas vezes | `git remote set-url origin https://github.com/NoeXVel/VelvX-Engine.git` |
| `src refspec main does not match any` | ainda não existe commit | rode `git add .` e `git commit` antes do push |
| arquivos enormes no commit | `dist/` foi adicionado antes do `.gitignore` | remova do índice com `git rm -r --cached dist` e faça novo commit |

## SSH (opcional)

```powershell
ssh-keygen -t ed25519 -C "seu-email@exemplo.com"
# adicione o conteúdo de ~/.ssh/id_ed25519.pub em GitHub → Settings → SSH and GPG keys
git remote set-url origin git@github.com:NoeXVel/VelvX-Engine.git
git push -u origin main
```
