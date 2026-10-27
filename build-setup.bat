@echo off
rem Baut dist\ModernFlyout-Setup.exe:
rem  1. App self-contained als eine Datei veröffentlichen (eigener Ordner, die laufende App in publish\ bleibt unberührt)
rem  2. als ModernFlyout.exe in Setup\payload.zip packen
rem  3. Installer (Setup\Setup.csproj, .NET Framework 4.8) mit eingebettetem payload.zip bauen
cd /d "%~dp0"

dotnet publish KnobEarTrumpet\KnobEarTrumpet.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o Setup\obj\publish
if errorlevel 1 goto :error

powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "$d = 'Setup\obj\payload'; Remove-Item $d -Recurse -Force -ErrorAction SilentlyContinue; New-Item -ItemType Directory -Force $d | Out-Null;" ^
  "Copy-Item 'Setup\obj\publish\KnobEarTrumpet.exe' (Join-Path $d 'ModernFlyout.exe') -Force;" ^
  "Remove-Item 'Setup\payload.zip' -ErrorAction SilentlyContinue;" ^
  "Compress-Archive -Path (Join-Path $d '*') -DestinationPath 'Setup\payload.zip' -CompressionLevel Optimal"
if errorlevel 1 goto :error

dotnet build Setup\Setup.csproj -c Release -o dist
if errorlevel 1 goto :error

echo.
echo Fertig: dist\ModernFlyout-Setup.exe
pause
exit /b 0

:error
echo.
echo Fehler beim Bauen des Setups.
pause
exit /b 1
