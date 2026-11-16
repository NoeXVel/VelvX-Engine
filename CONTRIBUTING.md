# Contribuindo com a VelvX Engine

Obrigado por querer contribuir! Este documento resume o fluxo de trabalho,
os requisitos de build e as regras de qualidade do projeto.

## Fluxo de contribuição

1. Faça um fork e crie uma branch a partir de `main`
   (`git checkout -b minha-melhoria`).
2. Mantenha o escopo da mudança focado: uma funcionalidade ou correção por PR.
3. Rode a bateria de testes antes de abrir o PR (veja abaixo).
4. Abra o Pull Request descrevendo **o que** muda, **por que** muda e
   **como foi testado** (editor e jogo em execução, quando aplicável).

## Build

Requisitos: Windows 10+, Visual Studio 2022/2026 com workload C++,
CMake ≥ 3.20 e Ninja (ambos acompanham o Visual Studio).
Todas as dependências são vendorizadas em `Source/third_party/`.

```bat
Source\build_release.bat            :: build completo em "Build Release/"
Source\build_release.bat VelvXTests :: apenas um alvo
```

Os binários vão para `Build Release/` (exes + DLLs + shaders + scripts +
demos + templates). O instalador e o portable são gerados por:

```bat
packaging\make_installer.bat  :: dist\VelvXEngine-<versão>-setup.exe
packaging\make_portable.bat   :: dist\VelvXEngine-<versão>-portable.zip
```

## Testes

```bat
"Build Release\VelvXTests.exe"
```

A bateria headless cobre interpretador Velv e núcleo (cenas, componentes,
física, UI, clone/Instantiate). **Todo PR deve manter 100% dos testes
passando**; funcionalidade nova de interpretador/núcleo exige teste novo.
Mudanças de editor/renderização devem ser verificadas manualmente no editor
e em uma demo em execução — capture screenshots quando relevante.

## Regras de código

- C++20, sem extensões de compilador; MSVC com `/W4 /permissive-`.
- Fontes em UTF-8; comentários e mensagens ao usuário em português.
- Módulos independentes em `Source/EngineCore/{Core,Renderer,Physics,
  Scripting,UI,Input,PluginAPI,Utils}`; ferramentas em `Source/<Ferramenta>/`.
- Nada de dependências novas sem discussão prévia na issue: o projeto é
  autocontido (vendorizado) de propósito.
- Não quebre a API pública da Velv Language (`lock`, `unlock`, `book`) sem
  uma proposta de migração documentada.
- Documentação (`Docs/`) só descreve funcionalidade **implementada e
  testada**; atualize-a no mesmo PR da funcionalidade.

## Estilo de commit

Mensagens curtas no imperativo, em português ou inglês, explicando o motivo:
`fisica: corrige penetracao em colisoes AABB`, `editor: atalho para duplicar`.
Commits de build/artefatos gerados não devem entrar no repositório
(`Build Release/`, `dist/`, `Source/build/` e payloads são ignorados).

## Reportando bugs

Abra uma issue com: versão da engine, passos de reprodução, comportamento
esperado vs. observado, e o log relevante (`velvx.log` ou console do editor).
Para crashes, inclua o stack trace se disponível.

## Licença

Ao contribuir, você concorda que suas contribuições serão licenciadas sob a
licença MIT do projeto (veja `LICENSE`).