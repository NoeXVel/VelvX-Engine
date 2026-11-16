@echo off
rem ============================================================================
rem  VelvX Engine - publica o INSTALADOR nativo (VelvXSetup.exe).
rem  Uso:  packaging\make_installer.bat
rem  Saida: dist\VelvXEngine-<versao>-setup.exe
rem  O instalador e compilado pelo CMake (Source/Setup): wizard grafico com
rem  payload embutido, atalhos, associacoes e desinstalador integrados.
rem ============================================================================
setlocal EnableExtensions
set "VERSION=1.0.0"
for %%I in ("%~dp0..") do set "ROOT=%%~fI"
set "OUT=%ROOT%\dist"
set "LOG=%OUT%\build_setup.log"

echo [1/2] Compilando VelvXSetup (payload + wizard)...
if not exist "%OUT%" mkdir "%OUT%"
call "%ROOT%\Source\build_release.bat" VelvXSetup > "%LOG%" 2>&1
findstr /i /c:"error C" /c:"error LNK" /c:"fatal error" /c:"FAILED" /c:"error RC" "%LOG%" >nul
if not errorlevel 1 (
    echo [ERRO] build do VelvXSetup falhou. Veja %LOG%
    exit /b 1
)
if not exist "%ROOT%\Build Release\VelvXSetup.exe" (
    echo [ERRO] VelvXSetup.exe nao foi gerado. Veja %LOG%
    exit /b 1
)

echo [2/2] Publicando em dist...
copy /y "%ROOT%\Build Release\VelvXSetup.exe" "%OUT%\VelvXEngine-%VERSION%-setup.exe" >nul
if errorlevel 1 exit /b 1

echo.
echo OK: %OUT%\VelvXEngine-%VERSION%-setup.exe
endlocal