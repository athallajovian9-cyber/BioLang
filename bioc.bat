@echo off
setlocal
cd /d "%~dp0"

if "%~1"=="" (
    echo Usage: bioc ^<file.bio^>
    exit /b 1
)

set "BIO_FILE=%~1"
set "C_FILE=%~dpn1.c"
set "EXE_FILE=%~dpn1.exe"

echo [BIOLANG NATIVE COMPILER] Compiling %BIO_FILE% to native x86 machine code...

"%~dp0src\bin\Debug\net10.0\biolang.exe" --emit-c "%BIO_FILE%" > "%C_FILE%"
if errorlevel 1 (
    echo [ERROR] Failed to emit C code.
    exit /b 1
)

call "C:\Program Files\Microsoft Visual Studio\18\Community\VC\Auxiliary\Build\vcvars64.bat" >nul
cl /std:c17 /O2 /nologo "%C_FILE%" /Fe:"%EXE_FILE%" >nul
if errorlevel 1 (
    echo [ERROR] Native C compilation failed.
    exit /b 1
)

if exist "%~dpn1.obj" del "%~dpn1.obj"

echo [SUCCESS] Native binary created: %EXE_FILE%
"%EXE_FILE%"
