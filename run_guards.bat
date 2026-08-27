@echo off
chcp 65001 >nul
cd /d "%~dp0"
REM run_guards.bat - 聚合式 guard 运行入口（Windows 包装）
REM
REM 全量:        run_guards.bat
REM 只跑相关:    run_guards.bat --changed-only
REM 看失败详情:  run_guards.bat --verbose
REM 按名字筛选:  run_guards.bat --filter ModeG
REM
REM 退出码 0 = 无新增失败；1 = 有新增失败或已知红项基线过期。
REM 已知红项登记在 tests\known_red_guards.txt。

set "PYTHONIOENCODING=utf-8"
set "PYTHONUTF8=1"

py -3 --version >nul 2>nul
if errorlevel 1 (
    python --version >nul 2>nul
    if errorlevel 1 (
        echo [FAIL] 未找到 Python（既没有 py -3 也没有 python）
        exit /b 1
    )
    python "tools\run_guards.py" %*
    exit /b %ERRORLEVEL%
)

py -3 "tools\run_guards.py" %*
exit /b %ERRORLEVEL%
