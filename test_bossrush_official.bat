@echo off
chcp 65001 >nul
cd /d "%~dp0"
echo ==========================================
echo Boss Rush Mod - Build / Sync / Deploy
echo ==========================================
echo.

call :ensure_game_path
if not defined GAME_PATH (
    echo [FAIL] GAME_PATH was not found.
    echo        Set GAME_PATH to your Escape from Duckov install root, e.g.
    echo        set "GAME_PATH=E:\SteamLibrary\steamapps\common\Escape from Duckov"
    if not defined BOSSRUSH_NO_PAUSE pause
    exit /b 1
)
set MOD_SOURCE_DIR=%cd%
set MOD_TARGET_DIR=%GAME_PATH%\Duckov_Data\Mods\BossRush

echo Source : %MOD_SOURCE_DIR%
echo Target : %MOD_TARGET_DIR%
echo.

:: Step 1 - Logic tests
echo [1/5] Running logic tests...
call test_logic_official.bat
if %ERRORLEVEL% NEQ 0 (
    echo [FAIL] Logic tests failed, aborting.
    if not defined BOSSRUSH_NO_PAUSE pause
    exit /b 1
)

:: Step 2 - Compile
echo [2/5] Compiling Mod...
call compile_official.bat
if %ERRORLEVEL% NEQ 0 (
    echo [FAIL] Compile failed, aborting.
    if not defined BOSSRUSH_NO_PAUSE pause
    exit /b 1
)

:: Step 3 - Sync WikiContent to wiki-site
echo.
echo [3/5] Syncing WikiContent to wiki-site...
set "NODE_EXE="
call :find_node
if not defined NODE_EXE (
    echo [SKIP] Node.js not found, skipping wiki sync.
    echo        Checked PATH, common install folders, and nvm-windows variables.
) else (
    echo [INFO] Using Node.js: %NODE_EXE%
    pushd "%MOD_SOURCE_DIR%\wiki-site"
    "%NODE_EXE%" scripts\sync-content.mjs
    if %ERRORLEVEL% NEQ 0 (
        echo [WARN] Wiki sync failed, continuing deployment...
    )
    popd
)

:: Step 4 - Create mod directory
echo.
echo [4/5] Creating Mod directory...
if not exist "%MOD_TARGET_DIR%" mkdir "%MOD_TARGET_DIR%"

:: Step 5 - Deploy DLL and data files
echo.
echo [5/5] Deploying DLL and data files...
copy /Y "Build\BossRush.dll" "%MOD_TARGET_DIR%\" >nul
if %ERRORLEVEL% NEQ 0 (
    echo [FAIL] DLL copy failed!
    if not defined BOSSRUSH_NO_PAUSE pause
    exit /b 1
)

if exist "Assets\SpawnPoints\*.json" (
    if not exist "%MOD_TARGET_DIR%\Assets\SpawnPoints" mkdir "%MOD_TARGET_DIR%\Assets\SpawnPoints"
    xcopy /Y /I "Assets\SpawnPoints\*.json" "%MOD_TARGET_DIR%\Assets\SpawnPoints\" >nul
    if errorlevel 1 (
        echo [FAIL] SpawnPoints JSON copy failed!
        if not defined BOSSRUSH_NO_PAUSE pause
        exit /b 1
    )
)

if exist "Assets\Data\*.json" (
    if not exist "%MOD_TARGET_DIR%\Assets\Data" mkdir "%MOD_TARGET_DIR%\Assets\Data"
    xcopy /Y /I "Assets\Data\*.json" "%MOD_TARGET_DIR%\Assets\Data\" >nul
    if errorlevel 1 (
        echo [FAIL] Data JSON copy failed!
        if not defined BOSSRUSH_NO_PAUSE pause
        exit /b 1
    )
)

if exist "Assets\Items\fate_echo_relic" (
    if not exist "%MOD_TARGET_DIR%\Assets\Items" mkdir "%MOD_TARGET_DIR%\Assets\Items"
    copy /Y "Assets\Items\fate_echo_relic" "%MOD_TARGET_DIR%\Assets\Items\fate_echo_relic" >nul
    if errorlevel 1 (
        echo [FAIL] Fate Echo relic bundle copy failed!
        if not defined BOSSRUSH_NO_PAUSE pause
        exit /b 1
    )
)

if exist "Assets\Items\portable_safe_zone_device" (
    if not exist "%MOD_TARGET_DIR%\Assets\Items" mkdir "%MOD_TARGET_DIR%\Assets\Items"
    copy /Y "Assets\Items\portable_safe_zone_device" "%MOD_TARGET_DIR%\Assets\Items\portable_safe_zone_device" >nul
    if errorlevel 1 (
        echo [FAIL] Portable safe-zone device bundle copy failed!
        if not defined BOSSRUSH_NO_PAUSE pause
        exit /b 1
    )
)

echo.
echo ==========================================
echo Done! Mod deployed to: %MOD_TARGET_DIR%
echo ==========================================
echo.

if not defined BOSSRUSH_NO_PAUSE pause
exit /b 0

:find_node
for %%I in (node.exe node) do (
    for /f "delims=" %%P in ('where %%I 2^>nul') do (
        set "NODE_EXE=%%P"
        goto :eof
    )
)

if defined NVM_SYMLINK if exist "%NVM_SYMLINK%\node.exe" (
    set "NODE_EXE=%NVM_SYMLINK%\node.exe"
    goto :eof
)

if defined NVM_HOME if exist "%NVM_HOME%\node.exe" (
    set "NODE_EXE=%NVM_HOME%\node.exe"
    goto :eof
)

if exist "%ProgramFiles%\nodejs\node.exe" (
    set "NODE_EXE=%ProgramFiles%\nodejs\node.exe"
    goto :eof
)

if exist "%ProgramFiles(x86)%\nodejs\node.exe" (
    set "NODE_EXE=%ProgramFiles(x86)%\nodejs\node.exe"
    goto :eof
)

if exist "%LocalAppData%\Programs\nodejs\node.exe" (
    set "NODE_EXE=%LocalAppData%\Programs\nodejs\node.exe"
    goto :eof
)

goto :eof

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
