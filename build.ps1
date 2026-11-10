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

$version = "0.2.0"
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
VelvX Engine $version - Forja
=============================

1. Extraia esta pasta em qualquer lugar do computador.
2. Execute VelvX.Engine.exe.
3. Crie um projeto em Novo projeto (2D ou 3D) e comece a construir.

Novidades da 0.2.0:
  - setas coloridas para mover os objetos na cena;
  - camera 3D com WASD (E/Q sobem e descem);
  - editor de script com cores, ajuda e numeracao;
  - Configuracoes do Projeto com Inputs personalizados, renderizacao,
    iluminacao e cenas;
  - recursos, prefabs, modelos 3D, sprites, GIFs e spritesheets;
  - componentes, luzes pontuais, Global Volume e Box Volume;
  - streaming de cenas com Scene("Nome").load()/unload().

Teclas no editor:
  F5 ........ iniciar o jogo
  Esc ....... parar o jogo
  Ctrl+S .... salvar o projeto
  Ctrl+, .... abrir Configuracoes do Projeto
  F ......... focar o objeto selecionado
  WASD ...... mover a camera 3D no editor
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
