@echo off
dotnet publish KnobEarTrumpet\KnobEarTrumpet.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish
echo.
echo Fertig: publish\KnobEarTrumpet.exe
pause
