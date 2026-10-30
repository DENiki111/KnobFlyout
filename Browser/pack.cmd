@echo off
rem Baut die Pakete der YT-Bridge-Erweiterungen (Firefox + Chrome/Opera) zum Hochladen nach dist\
"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -File "%~dp0pack.ps1"
echo.
pause
