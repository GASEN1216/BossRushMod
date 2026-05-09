@echo off
setlocal

set "GAME_PATH=D:\sofrware\steam\steamapps\common\Escape from Duckov"
set "GAME_EXE=%GAME_PATH%\Duckov.exe"
set "MOD_DLL=%GAME_PATH%\Duckov_Data\Mods\BossRush\BossRush.dll"
set "SMOKE_NOTE=docs\testing\2026-05-09-code-structure-smoke.md"

echo ==========================================
echo Boss Rush Mod - Manual In-Game Smoke Test
echo ==========================================
echo.
echo This script does not automate gameplay. It prints the smoke checklist and can launch Duckov.
echo Record the result in:
echo   %SMOKE_NOTE%
echo.

if not exist "%MOD_DLL%" (
    echo [WARNING] Deployed DLL not found:
    echo   %MOD_DLL%
    echo Run test_bossrush_official.bat first, then rerun this smoke helper.
    echo.
) else (
    echo [OK] Deployed DLL found:
    echo   %MOD_DLL%
    echo.
)

echo Smoke checklist:
echo   1. Start game and load into Base_SceneV2.
echo   2. Confirm normal merchant still contains BossRush ticket, adventure journal,
echo      achievement medal, Awen token, brick stone, and zombie invitation.
echo   3. Enter a standard BossRush arena through the existing map selection flow.
echo   4. Confirm arena setup, sign options, first wave, enemy spawn, kill resolution,
echo      and arena exit behave as before.
echo   5. Enter Mode D through the naked + ticket flow.
echo   6. Confirm starter kit, Mode D sign option, first wave, stuck-wave self-check,
echo      and exit cleanup behave as before.
echo   7. Equip or spawn Frostmourne or FenHuangHalberd and confirm ability init/trigger.
echo   8. Optional: verify DebugTools hotkeys only in DevMode and achievement hotkey opens UI.
echo.

if not exist "%GAME_EXE%" (
    echo [ERROR] Game executable not found:
    echo   %GAME_EXE%
    exit /b 1
)

choice /M "Launch Duckov now"
if errorlevel 2 (
    echo Skipped launching Duckov.
    exit /b 0
)

start "" "%GAME_EXE%"
echo Duckov launch requested. Complete the checklist in-game and record the result.
exit /b 0
