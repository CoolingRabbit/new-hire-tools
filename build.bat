@echo off
REM ============================================================================
REM New Hire Toolbox - one-click build script
REM Output: dist\NewHireToolbox.exe (single file, relies only on the .NET
REM Framework 4.x that ships with Windows)
REM ============================================================================
setlocal

set CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe
set SRC_DIR=%~dp0src
set OUT_DIR=%~dp0dist

if not exist "%CSC%" (
    echo [ERROR] C# compiler not found: %CSC%
    echo Please make sure .NET Framework 4.x is installed ^(built into Windows^).
    pause
    exit /b 1
)

if not exist "%OUT_DIR%" mkdir "%OUT_DIR%"

echo Building NewHireToolbox.exe ...
"%CSC%" -target:winexe -utf8output -codepage:65001 -nologo ^
    -reference:System.dll,System.Drawing.dll,System.Windows.Forms.dll,System.Management.dll ^
    -out:"%OUT_DIR%\NewHireToolbox.exe" ^
    "%SRC_DIR%\NewHireToolbox.cs" "%SRC_DIR%\PasswordGenerator.cs" "%SRC_DIR%\AssemblyInfo.cs"

if errorlevel 1 (
    echo [FAILED] Build error, see messages above.
    pause
    exit /b 1
)

echo [OK] Output: %OUT_DIR%\NewHireToolbox.exe
pause
