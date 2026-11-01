@echo off
REM Build M365Hub Windows app (WinUI 3) for this machine's CPU.
REM x64 -> win-x64, ARM64 -> win-arm64. Requires .NET 8 SDK.
REM WinUI 3 resource packaging needs Visual Studio's MSBuild: the Pri
REM tasks DLL ships with Visual Studio, not with the .NET SDK, so
REM `dotnet build` alone fails with MSB4062 ("ExpandPriContent task
REM could not be loaded"). Prefer the VS MSBuild found by vswhere and
REM fall back to dotnet build when Visual Studio is not installed.
if /I "%PROCESSOR_ARCHITECTURE%"=="ARM64" (set RID=win-arm64 & set PLAT=ARM64) else (set RID=win-x64 & set PLAT=x64)
echo Building for %RID% ...
set "MSBUILD="
set "VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
if exist "%VSWHERE%" (
  for /f "usebackq tokens=*" %%i in (`"%VSWHERE%" -latest -products * -requires Microsoft.Component.MSBuild -find MSBuild\**\Bin\MSBuild.exe`) do set "MSBUILD=%%i"
)
if defined MSBUILD (
  echo Using Visual Studio MSBuild: %MSBUILD%
  "%MSBUILD%" src\M365Hub.App\M365Hub.App.csproj -restore -p:Configuration=Release -p:Platform=%PLAT% -p:RuntimeIdentifier=%RID%
) else (
  echo Visual Studio not found, trying dotnet build ...
  dotnet build src\M365Hub.App\M365Hub.App.csproj -c Release -r %RID%
)
if errorlevel 1 (
  echo Build failed. Check the error above.
  pause
  exit /b 1
)
echo.
echo OK. Run: src\M365Hub.App\bin\Release\net8.0-windows10.0.19041.0\%RID%\M365Hub.App.exe
pause
