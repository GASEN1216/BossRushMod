@echo off
chcp 65001 >nul
cd /d "%~dp0"
set "BOSSRUSH_DEV_BUILD=1"
call compile_official.bat
