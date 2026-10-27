@echo off
dotnet publish KnobFlyout\KnobFlyout.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish
echo.
echo Fertig: publish\KnobFlyout.exe
pause
