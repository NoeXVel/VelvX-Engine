@echo off
rem ============================================================================
rem  VelvX Engine - gera o pacote PORTABLE (zip).
rem  Uso:  packaging\make_portable.bat
rem  Saida: dist\VelvXEngine-<versao>-portable.zip
rem  Usa o mesmo payload do instalador (Source/Setup/payload.zip), garantindo
rem  conteudo identico entre as duas distribuicoes.
rem ============================================================================
setlocal EnableExtensions
set "VERSION=1.0.0"
for %%I in ("%~dp0..") do set "ROOT=%%~fI"
set "OUT=%ROOT%\dist"
set "PAYLOAD=%ROOT%\Source\Setup\payload.zip"

if not exist "%PAYLOAD%" (
    echo [1/2] Gerando payload via CMake...
    call "%ROOT%\Source\build_release.bat" VelvXPayload
    if errorlevel 1 exit /b 1
)
if not exist "%PAYLOAD%" (
    echo [ERRO] payload.zip nao encontrado.
    exit /b 1
)

echo [2/2] Publicando em dist...
if not exist "%OUT%" mkdir "%OUT%"
copy /y "%PAYLOAD%" "%OUT%\VelvXEngine-%VERSION%-portable.zip" >nul
if errorlevel 1 exit /b 1

echo.
echo OK: %OUT%\VelvXEngine-%VERSION%-portable.zip
endlocal