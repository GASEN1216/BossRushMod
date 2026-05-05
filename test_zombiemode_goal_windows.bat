@echo off
chcp 65001 >nul
cd /d "%~dp0"

echo ===================================
echo ZombieMode Goal Verification
echo ===================================
echo.

echo [1/2] Running official compile...
call compile_official.bat
if errorlevel 1 (
    echo.
    echo compile_official.bat failed. Fix compile errors before in-game smoke.
    exit /b %errorlevel%
)

echo.
echo [2/2] Running deploy smoke helper...
call test_bossrush_official.bat
if errorlevel 1 (
    echo.
    echo test_bossrush_official.bat failed. Fix deploy errors before in-game smoke.
    exit /b %errorlevel%
)

echo.
echo Manual in-game smoke required:
echo - Use Zombie Tide Invitation and enter ZombieMode.
echo - Confirm cash prompt shows 100 cash = 1 run-only purification point.
echo - Verify starter melee/gunner grants core weapon, and gunner grants ammo.
echo - Use Zombie Tide Beacon during preparation and confirm it starts the next wave.
echo - Clear a normal wave and confirm no old enemies chase during rewards/preparation.
echo - Reach wave 5 boss, then verify both Extract Now and Continue paths.
echo - Check reward fallback, terminal naming, and no obvious UI overlap at target resolutions.
echo - If anything fails in-game, preserve the latest Player.log and this console output.
echo.
echo Script checks finished; production readiness still requires the manual smoke above.
exit /b 0
