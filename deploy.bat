@echo off
rem Copy every folder under .\mods\ into the RimWorld Mods folder.
rem Source/ and obj/ are stripped afterwards - the game only needs
rem About, Defs, Patches, Assemblies, Languages, Textures, Sounds.
rem
rem Edit DST below if your game is installed somewhere else.

setlocal
set "SRC=%~dp0mods"
set "DST=F:\SteamLibrary\steamapps\common\RimWorld\Mods"

if not exist "%SRC%\" (
  echo [ERROR] Source folder not found: %SRC%
  exit /b 1
)

for /d %%D in ("%SRC%\*") do (
  echo Syncing %%~nxD ...
  robocopy "%%D" "%DST%\%%~nxD" /MIR /NFL /NDL /NJH /NJS /NP >nul
  if errorlevel 8 (
    echo [ERROR] robocopy failed on %%~nxD
    exit /b 1
  )
  if exist "%DST%\%%~nxD\Source" rmdir /s /q "%DST%\%%~nxD\Source"
  if exist "%DST%\%%~nxD\obj" rmdir /s /q "%DST%\%%~nxD\obj"
)

echo.
echo [OK] Synced to %DST%
echo Restart RimWorld - mod content is not reloaded at runtime.
