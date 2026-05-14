@echo off
chcp 65001 >nul
setlocal
REM validate_refactor_step.bat - refactor verification pipeline
REM Usage: validate_refactor_step.bat [step_name]
REM Flow: compile -> deploy -> guard suite -> manual smoke prompt

echo === [1/3] Build_Deploy_Smoke ===
set "BOSSRUSH_NO_PAUSE=1"
call test_bossrush_official.bat
if %ERRORLEVEL% NEQ 0 (
    echo [FAIL] 构建或部署失败
    exit /b 1
)

echo === [2/3] Guard_Suite ===
py -3 --version >nul 2>nul
if errorlevel 1 (
    echo [FAIL] 未找到 Windows Python launcher: py -3
    exit /b 1
)

for %%f in (tests\*Guard.py tests\*PropertyTest.py) do (
    py -3 "%%f"
    if errorlevel 1 (
        echo [FAIL] Guard/PropertyTest 失败: %%f
        exit /b 1
    )
)

echo === [3/3] Manual_Runtime_Smoke_Prompt ===
echo Run Runtime_Smoke manually:
echo   - cmd.exe /c test_bossrush_smoke_manual.bat
echo   - Standard Boss Rush full run
echo   - JSON-backed map selection enter/exit once
echo   - Boss reward and lootbox drop once
echo   - Mode D and Mode F available paths once
echo   - Mode E full run
echo   - Zombie Mode full run
echo   - Courier storage/sweep and Wish Fountain open/close once
echo   - Confirm melee slashFx / hitFx visuals unchanged
echo After Runtime_Smoke, scan latest log:
echo   - python3 tests/SmokeLogScan.py
echo   - STALE_LOG means the latest game log is older than deployed BossRush.dll.
echo This automated step covers build, deploy, guard, and property tests only.
echo.
echo [PASS] Automated validation passed (step=%1)
