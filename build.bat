@echo off
setlocal
REM FrameForge - portable build + installer. Creado por leandrosepulveda.com
REM Requirements: .NET 8 SDK (https://dotnet.microsoft.com/download) and Inno Setup 6 (optional, for the Setup)
cd /d "%~dp0"

where dotnet >nul 2>nul
if errorlevel 1 (
  echo ERROR: 'dotnet' not found. Install .NET 8 SDK (https://dotnet.microsoft.com/download)
  pause & exit /b 1
)

dotnet publish src\FrameForge.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish
if errorlevel 1 (
  echo Build ERROR. Check the errors above.
  pause & exit /b 1
)
if not exist "publish\FrameForge.exe" (
  echo ERROR: publish\FrameForge.exe was not generated.
  pause & exit /b 1
)
echo Built: publish\FrameForge.exe

set "ISCC=%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe"
if not exist "%ISCC%" set "ISCC=%ProgramFiles%\Inno Setup 6\ISCC.exe"
if exist "%ISCC%" (
  "%ISCC%" installer\setup.iss
  if errorlevel 1 (
    echo ERROR generating the installer.
    pause & exit /b 1
  )
  echo.
  echo Setup created at dist\FrameForge-Setup.exe
) else (
  echo.
  echo Install Inno Setup 6 (https://jrsoftware.org/isinfo.php) to generate the installer.
  echo The portable .exe is at publish\FrameForge.exe
  echo Quick headless test: publish\FrameForge.exe --selftest
)
pause & exit /b 0
