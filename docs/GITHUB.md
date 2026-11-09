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
git commit -m "VelvX Engine 0.1.0 - fundacao, editor e VelvX Language"

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

2. Abra <https://github.com/NoeXVel/VelvX-Engine/releases> e clique em **Draft a new release**.
3. Crie a tag `v0.1.0`, título `VelvX Engine 0.1.0 — Fundação` e descreva as novidades.
4. Anexe `dist\VelvX-Engine-win-x64.zip` em **Attach binaries**.
5. Publique a release. Quem baixar só precisa extrair e executar `VelvX.Engine.exe` — não é necessário instalar o .NET.

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
