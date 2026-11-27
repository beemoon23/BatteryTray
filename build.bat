@echo off
setlocal
cd /d "%~dp0"
where dotnet >nul 2>nul || (echo .NET 8 SDK nao encontrado. Instale em https://dotnet.microsoft.com/download/dotnet/8.0 & pause & exit /b 1)
echo Restaurando dependencias...
dotnet restore BatteryTray.sln
if errorlevel 1 goto :error
echo Publicando BatteryTray para Windows 11 x64...
dotnet publish src\BatteryTray\BatteryTray.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish
if errorlevel 1 goto :error
echo.
echo Pronto! Executavel: %CD%\publish\BatteryTray.exe
pause
exit /b 0
:error
echo.
echo A compilacao falhou. Confira as mensagens acima.
pause
exit /b 1
