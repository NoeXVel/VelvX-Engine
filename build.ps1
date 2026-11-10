param(
    [string]$Runtime = "win-x64",
    [string]$Configuration = "Release",
    [switch]$NoZip
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $root "VelvX.Engine.csproj"
$dist = Join-Path $root "dist"
$output = Join-Path $dist "VelvX-Engine-$Runtime"
$zip = "$output.zip"

$version = "0.4.0"
$match = [regex]::Match((Get-Content $project -Raw), "<Version>([^<]+)</Version>")
if ($match.Success) { $version = $match.Groups[1].Value }

Write-Host ""
Write-Host "  VelvX Engine $version - build portatil ($Runtime)" -ForegroundColor Magenta
Write-Host "  -------------------------------------------------" -ForegroundColor DarkGray

if (Test-Path $output) {
    Write-Host "  Limpando $output ..." -ForegroundColor DarkGray
    Remove-Item -Recurse -Force $output
}
if (Test-Path $zip) { Remove-Item -Force $zip }

New-Item -ItemType Directory -Force -Path $output | Out-Null

Write-Host "  Publicando..." -ForegroundColor Cyan
& dotnet publish $project `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -p:GenerateDocumentationFile=false `
    -o $output

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish falhou com codigo $LASTEXITCODE."
}

$exe = Join-Path $output "VelvX.Engine.exe"
if (-not (Test-Path $exe)) {
    throw "O executavel nao foi gerado em $output."
}

# Garante os templates ao lado do executavel, mesmo se o publish nao os copiar.
$sourceTemplates = Join-Path $root "Templates"
$targetTemplates = Join-Path $output "Templates"
if ((Test-Path $sourceTemplates) -and -not (Test-Path $targetTemplates)) {
    Write-Host "  Copiando Templates..." -ForegroundColor DarkGray
    Copy-Item -Recurse -Force $sourceTemplates $targetTemplates
}

$readme = @"
VelvX Engine $version - Bigorna
===============================

1. Extraia esta pasta em qualquer lugar do computador.
2. Execute VelvX.Engine.exe.
3. Crie um projeto em Novo projeto: 3D, 2D, Vazio 3D ou Vazio 2D.

Novidades da 0.4.0 (Fix):
  - gizmos de mover, girar e escalar com atalhos 1, 2 e 3;
  - movimento livre no viewport 3D e hierarquia que segue o pai;
  - escala de modelos corrigida, com ou sem rig;
  - inspetor redesenhado e atores com LOD e distancia de renderizacao;
  - primitivas 3D, MeshCollider automatico e StaticBody com colisao;
  - materiais reutilizaveis com textura, emissao, brilho e opacidade;
  - efeitos visuais pulsantes nos Box Volumes e luz pontual aprimorada;
  - documentacao completa da VelvX Language dentro do editor.

Teclas no editor:
  F5 ........ iniciar o jogo
  Esc ....... parar o jogo
  Ctrl+S .... salvar o projeto
  Ctrl+, .... abrir Configuracoes do Projeto
  F ......... focar o ator selecionado
  WASD ...... mover a camera 3D no editor
  1 / 2 / 3 . alternar gizmo de mover, girar e escalar
  Setas ..... mover o Player durante o Play

Seus projetos ficam em Documentos\VelvX Projects.
Os templates ficam na pasta Templates ao lado deste executavel.
Nao e necessario instalar o .NET.

https://github.com/NoeXVel/VelvX-Engine
"@
Set-Content -Path (Join-Path $output "LEIA-ME.txt") -Value $readme -Encoding UTF8

if (-not $NoZip) {
    Write-Host "  Compactando..." -ForegroundColor Cyan
    Compress-Archive -Path (Join-Path $output "*") -DestinationPath $zip -CompressionLevel Optimal

    Write-Host "  Gerando SHA-256..." -ForegroundColor Cyan
    $hash = (Get-FileHash -Path $zip -Algorithm SHA256).Hash.ToLowerInvariant()
    Set-Content -Path "dist/SHA256.txt" -Value ("{0}  {1}" -f $hash, (Split-Path $zip -Leaf)) -Encoding ASCII
}

$size = (Get-ChildItem -Recurse $output | Measure-Object -Property Length -Sum).Sum / 1MB
Write-Host "  -------------------------------------------------" -ForegroundColor DarkGray
Write-Host ("  Pasta: {0}  ({1:0.0} MB)" -f $output, $size) -ForegroundColor Green
if (-not $NoZip) {
    $zipSize = (Get-Item $zip).Length / 1MB
    Write-Host ("  Zip:   {0}  ({1:0.0} MB)" -f $zip, $zipSize) -ForegroundColor Green
}
Write-Host "  Pronto." -ForegroundColor Green
Write-Host ""
