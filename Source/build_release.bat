@echo off
rem ===========================================================================
rem  VelvX Engine - build Release (MSVC + Ninja via Visual Studio)
rem  Gera o executavel em "..\Build Release" junto com shaders/scripts/assets.
rem ===========================================================================
setlocal

set "VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
if not exist "%VSWHERE%" (
    echo [VelvX] vswhere.exe nao encontrado - instale o Visual Studio com C++.
    exit /b 1
)
for /f "usebackq delims=" %%i in (`"%VSWHERE%" -latest -property installationPath`) do set "VSDIR=%%i"

call "%VSDIR%\VC\Auxiliary\Build\vcvars64.bat" || exit /b 1

set "CMAKE=%VSDIR%\Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe"
set "NINJA=%VSDIR%\Common7\IDE\CommonExtensions\Microsoft\CMake\Ninja\ninja.exe"

"%CMAKE%" -S "%~dp0." -B "%~dp0build" -G Ninja -DCMAKE_BUILD_TYPE=Release -DCMAKE_MAKE_PROGRAM="%NINJA%" || exit /b 1
if "%~1"=="" (
    "%CMAKE%" --build "%~dp0build" || exit /b 1
) else (
    "%CMAKE%" --build "%~dp0build" --target %~1 || exit /b 1
)

echo.
echo [VelvX] Build concluido: "%~dp0..\Build Release\VelvXPlayer.exe"
endlocal