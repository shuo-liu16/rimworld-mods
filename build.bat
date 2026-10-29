@echo off
rem Rebuild every mod in this repo.
rem build.bat = compile; deploy.bat = copy into the game's Mods folder.
rem
rem The projects pull no NuGet packages, but NuGet's restore step fails on this
rem machine (LOCALAPPDATA cannot be resolved), so we generate an empty
rem project.assets.json first and then build with --no-restore.

setlocal
set "DOTNET=%USERPROFILE%\.dotnet\dotnet.exe"
if not exist "%DOTNET%" set "DOTNET=dotnet"

cd /d "%~dp0"

call :build GridThermal
if errorlevel 1 exit /b 1

call :build AutoClean
if errorlevel 1 exit /b 1

call :build WorkSorter
if errorlevel 1 exit /b 1

echo.
echo [OK] All mods built.
echo Next: run deploy.bat to copy them into the game's Mods folder.
exit /b 0

:build
echo.
echo ==== Building %1 ====
python "tools\gen-assets.py" "mods\%1\Source\%1" net10.0
if errorlevel 1 exit /b 1

"%DOTNET%" build "mods\%1\Source\%1\%1.csproj" -t:Rebuild --no-restore -v q --nologo
if errorlevel 1 exit /b 1
exit /b 0
