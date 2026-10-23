@echo off
rem Builds monohost.exe (tools\monohost\bin) with the Visual Studio C++ compiler (vswhere finds it).
rem   tools\monohost\build.bat
setlocal
set VSWHERE="%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
for /f "usebackq tokens=*" %%i in (`%VSWHERE% -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath`) do set VS=%%i
if not defined VS (echo Visual Studio with C++ tools not found & exit /b 1)
call "%VS%\VC\Auxiliary\Build\vcvars64.bat" >nul
if not exist "%~dp0bin" mkdir "%~dp0bin"
cl /nologo /O2 "%~dp0monohost.c" /Fo"%~dp0bin\\" /Fe"%~dp0bin\monohost.exe"
