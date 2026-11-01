@echo off
REM Build M365Hub Windows app (WinUI 3) for this machine's CPU.
REM x64 -> win-x64, ARM64 -> win-arm64. Requires .NET 8 SDK.
if /I "%PROCESSOR_ARCHITECTURE%"=="ARM64" (set RID=win-arm64) else (set RID=win-x64)
echo Building for %RID% ...
dotnet build src\M365Hub.App\M365Hub.App.csproj -c Release -r %RID%
if errorlevel 1 (
  echo Build failed. Check the error above.
  pause
  exit /b 1
)
echo.
echo OK. Run: src\M365Hub.App\bin\Release\net8.0-windows10.0.19041.0\%RID%\M365Hub.App.exe
pause
