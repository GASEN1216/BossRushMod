@echo off
setlocal
chcp 65001 >nul
cd /d "%~dp0"
set "BOSSRUSH_DEV_BUILD=1"

rem Sync the private repo (docs, Assets, ArtSource) before building, so the deploy step
rem copies the latest bundles: stage + commit + pull --rebase + push, see
rem docs\guides\private-sync.ps1. Set BOSSRUSH_SKIP_PRIVATE_SYNC=1 to skip.
rem A failed sync only warns; the build still runs.
if defined BOSSRUSH_SKIP_PRIVATE_SYNC goto build
set "PRIVATE_GIT_DIR=%BOSSRUSH_PRIVATE_GIT_DIR%"
if not defined PRIVATE_GIT_DIR set "PRIVATE_GIT_DIR=%~dp0..\BossRushMod-private.git"
if not exist "%~dp0docs\guides\private-sync.ps1" (
    echo [private-sync] Skipped: docs\guides\private-sync.ps1 not found.
    goto build
)
if not exist "%PRIVATE_GIT_DIR%\HEAD" (
    echo [private-sync] Skipped: no private repo at %PRIVATE_GIT_DIR%
    goto build
)
echo [private-sync] Syncing private repo...
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0docs\guides\private-sync.ps1" sync
if errorlevel 1 (
    echo.
    echo [WARN] Private repo sync FAILED. Bundles and docs may be stale; the build continues.
    echo [WARN] Fix it, then run: docs\guides\private-sync.ps1 sync
    echo.
)

:build
call compile_official.bat
