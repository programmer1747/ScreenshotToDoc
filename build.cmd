@echo off
rem Builds dist\ScreenshotToDoc.exe using the C# compiler that ships with
rem Windows. No SDK, no NuGet, no downloads.

setlocal
cd /d "%~dp0"

set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not exist "%CSC%" (
    echo ERROR: could not find csc.exe ^(.NET Framework 4.x^).
    exit /b 1
)

if not exist dist mkdir dist

echo [1/2] Generating icon...
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "tools\make-icon.ps1"
if errorlevel 1 (
    echo ERROR: icon generation failed.
    exit /b 1
)

echo [2/2] Compiling...
"%CSC%" /nologo /target:winexe /platform:anycpu /optimize+ ^
    /win32icon:dist\app.ico ^
    /out:dist\ScreenshotToDoc.exe ^
    /reference:System.dll ^
    /reference:System.Drawing.dll ^
    /reference:System.Windows.Forms.dll ^
    /reference:System.Runtime.Serialization.dll ^
    src\ScreenshotToDoc.cs
if errorlevel 1 (
    echo ERROR: compile failed.
    exit /b 1
)

echo.
echo Built dist\ScreenshotToDoc.exe
endlocal
