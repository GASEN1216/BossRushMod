@echo off
setlocal
cd /d "%~dp0"

call :ensure_game_path
if not defined GAME_PATH (
    echo [ERROR] GAME_PATH was not found.
    echo         Set GAME_PATH to your Escape from Duckov install root, e.g.
    echo         set "GAME_PATH=E:\SteamLibrary\steamapps\common\Escape from Duckov"
    exit /b 1
)
set "GAME_EXE=%GAME_PATH%\Duckov.exe"
set "STEAM_APP_ID=3167020"
set "STEAM_LAUNCH_URL=steam://rungameid/%STEAM_APP_ID%"
set "MOD_DLL=%GAME_PATH%\Duckov_Data\Mods\BossRush\BossRush.dll"
set "SMOKE_DIR=docs\testing"
set "SMOKE_NOTE=docs\testing\2026-05-14-final-runtime-smoke.md"

echo ==========================================
echo Boss Rush Mod - Manual In-Game Smoke Test
echo ==========================================
echo.
echo This script does not automate gameplay. It prints the smoke checklist and can launch Duckov.
echo Record the result in:
echo   %SMOKE_NOTE%
echo.

if not exist "%SMOKE_DIR%" (
    mkdir "%SMOKE_DIR%" >nul 2>nul
)

if not exist "%SMOKE_NOTE%" (
    >"%SMOKE_NOTE%" echo # 2026-05-14 Final Runtime Smoke Record
    >>"%SMOKE_NOTE%" echo.
    >>"%SMOKE_NOTE%" echo Conclusion: Not run
    >>"%SMOKE_NOTE%" echo.
    >>"%SMOKE_NOTE%" echo - Tester:
    >>"%SMOKE_NOTE%" echo - Game version:
    >>"%SMOKE_NOTE%" echo - Start time:
    >>"%SMOKE_NOTE%" echo - End time:
    >>"%SMOKE_NOTE%" echo - Issues found:
    >>"%SMOKE_NOTE%" echo - Related log file:
    >>"%SMOKE_NOTE%" echo - SmokeLogScan.py result after smoke:
    >>"%SMOKE_NOTE%" echo - BossRush-related error blocks:
    >>"%SMOKE_NOTE%" echo.
    >>"%SMOKE_NOTE%" echo Checklist:
    >>"%SMOKE_NOTE%" echo - [ ] Base_SceneV2 loads.
    >>"%SMOKE_NOTE%" echo - [ ] Merchant inventory is unchanged.
    >>"%SMOKE_NOTE%" echo - [ ] Map selection opens and a JSON-backed map enters/exits.
    >>"%SMOKE_NOTE%" echo - [ ] Standard BossRush full run works.
    >>"%SMOKE_NOTE%" echo - [ ] Main menu to Base_SceneV2 transition is smooth and does not show a new obvious hitch.
    >>"%SMOKE_NOTE%" echo - [ ] Reward and lootbox drops are unchanged after boss kill.
    >>"%SMOKE_NOTE%" echo - [ ] Death return-to-base carry/back animation remains smooth and still leaves the expected tomb/wraith data.
    >>"%SMOKE_NOTE%" echo - [ ] Mode D and Mode F entry, combat, reward, and cleanup paths work.
    >>"%SMOKE_NOTE%" echo - [ ] Mode E full run works.
    >>"%SMOKE_NOTE%" echo - [ ] Zombie Mode full run works.
    >>"%SMOKE_NOTE%" echo - [ ] Courier storage/sweep and Wish Fountain open/close paths work.
    >>"%SMOKE_NOTE%" echo - [ ] Wedding chapel / Wish Fountain placed buildings restore correctly after returning to base.
    >>"%SMOKE_NOTE%" echo - [ ] Refactored melee FX hit/slash visuals are unchanged.
)

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
echo   3. Open map selection, choose at least one JSON-backed map, enter, and exit.
echo   4. Pay attention to the main menu -> base load and note whether a new obvious
echo      hitch appears before you can move in Base_SceneV2.
echo   5. Enter a standard BossRush arena through the existing map selection flow.
echo   6. Confirm arena setup, sign options, first wave, enemy spawn, kill resolution,
echo      reward/lootbox drops, and arena exit behave as before.
echo   7. Die once in a normal gameplay map, watch the carry/back animation, return to
echo      base, then re-enter the same map and confirm tomb/wraith behavior still works.
echo   8. Run one Mode D and one Mode F smoke path if entry conditions are available,
echo      covering combat start, reward flow, and cleanup.
echo   9. Run one Mode E session and confirm solo flow, spawn points, scaling,
echo      merchant/service UI, and cleanup behave as before.
echo  10. Run one Zombie Mode session and confirm entry, first waves, reward UI,
echo      safe-zone/extraction prompts, and cleanup behave as before.
echo  11. Open and close Courier storage/sweep and Wish Fountain UI once, confirming
echo      no item loss, stuck UI, or save callback error.
echo  12. If wedding chapel / Wish Fountain buildings are already placed, return to base
echo      after at least one map transition and confirm they restore without a new stall.
echo  13. Trigger a melee hit with FenHuangHalberd, Frostmourne, or PhantomWitch scythe
echo      and confirm slashFx / hitFx visuals are unchanged.
echo  14. Optional: verify DebugTools hotkeys only in DevMode and achievement hotkey opens UI.
echo.
echo After completing the in-game checklist, scan the latest log from WSL:
echo   python3 tests/SmokeLogScan.py
echo SmokeLogScan returns STALE_LOG until the game produces a new log after the deployed DLL.
echo If Steam shows a cloud sync failure prompt, choose the option to ignore/continue
echo and launch anyway; otherwise Duckov.exe will not start and no new smoke log appears.
echo.

if not exist "%GAME_EXE%" (
    echo [ERROR] Game executable not found:
    echo   %GAME_EXE%
    exit /b 1
)

set "LATEST_LOG="
for /f "delims=" %%F in ('dir /b /a:-d /o:-d "%GAME_PATH%\*.log" 2^>nul') do (
    if not defined LATEST_LOG set "LATEST_LOG=%GAME_PATH%\%%F"
)

if defined LATEST_LOG (
    echo Latest game log before launch:
    echo   %LATEST_LOG%
    echo.
)

choice /M "Launch Duckov now"
if errorlevel 2 (
    echo Skipped launching Duckov.
    exit /b 0
)

start "" "%STEAM_LAUNCH_URL%"
echo Duckov Steam launch requested. Complete the checklist in-game and record the result.
exit /b 0

:ensure_game_path
if defined GAME_PATH (
    if exist "%GAME_PATH%\Duckov_Data\Managed\Assembly-CSharp.dll" goto :eof
    echo [WARN] Ignoring invalid GAME_PATH: %GAME_PATH%
    set "GAME_PATH="
)
call :try_game_path "%~dp0..\..\..\.."
if defined GAME_PATH goto :eof
call :try_game_path "E:\SteamLibrary\steamapps\common\Escape from Duckov"
if defined GAME_PATH goto :eof
call :try_game_path "D:\sofrware\steam\steamapps\common\Escape from Duckov"
if defined GAME_PATH goto :eof
call :try_game_path "C:\Program Files (x86)\Steam\steamapps\common\Escape from Duckov"
goto :eof

:try_game_path
if exist "%~1\Duckov_Data\Managed\Assembly-CSharp.dll" (
    for %%P in ("%~1") do set "GAME_PATH=%%~fP"
)
goto :eof
