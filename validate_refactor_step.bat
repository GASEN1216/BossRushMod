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

echo === [3/3] 人工冒烟提示 ===
echo 请手动执行 Runtime_Smoke:
echo   - cmd.exe /c test_bossrush_smoke_manual.bat
echo   - 主线 Boss Rush 一局
echo   - Mode E 一局
echo   - Zombie Mode 一局
echo   - 触发一次近战命中，确认 slashFx / hitFx 视觉无变化
echo After Runtime_Smoke, scan latest log:
echo   - python3 tests/SmokeLogScan.py
echo 确认无异常后，本步骤验收通过。
echo.
echo [PASS] 自动化验收全部通过 (step=%1)
