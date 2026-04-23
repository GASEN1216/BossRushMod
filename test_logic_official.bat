@echo off
chcp 65001 >nul
cd /d "%~dp0"
echo ==========================================
echo Boss Rush Mod - Logic Tests
echo ==========================================
echo.

where dotnet >nul 2>nul
if %ERRORLEVEL% NEQ 0 (
    echo [FAIL] dotnet not found, cannot run logic tests.
    exit /b 1
)

if exist tests\bin rd /s /q tests\bin
if exist tests\obj rd /s /q tests\obj

echo [1/6] Running LegacyBossLootProbabilityTests...
dotnet run --project tests\LegacyBossLootProbabilityTests.csproj -c Release --nologo
if %ERRORLEVEL% NEQ 0 (
    echo [FAIL] LegacyBossLootProbabilityTests failed.
    exit /b 1
)

echo.
echo [2/6] Running PhantomWitchPerformancePolicyTests...
dotnet run --project tests\PhantomWitchPerformancePolicyTests.csproj -c Release --nologo
if %ERRORLEVEL% NEQ 0 (
    echo [FAIL] PhantomWitchPerformancePolicyTests failed.
    exit /b 1
)

echo.
echo [3/6] Running SimpleJsonHelperTests...
dotnet run --project tests\SimpleJsonHelperTests.csproj -c Release --nologo
if %ERRORLEVEL% NEQ 0 (
    echo [FAIL] SimpleJsonHelperTests failed.
    exit /b 1
)

echo.
echo [4/6] Running AffinityJsonSerializerTests...
dotnet run --project tests\AffinityJsonSerializerTests.csproj -c Release --nologo
if %ERRORLEVEL% NEQ 0 (
    echo [FAIL] AffinityJsonSerializerTests failed.
    exit /b 1
)

echo.
echo [5/6] Running F3DebugCheatMathTests...
dotnet run --project tests\F3DebugCheatMathTests.csproj -c Release --nologo
if %ERRORLEVEL% NEQ 0 (
    echo [FAIL] F3DebugCheatMathTests failed.
    exit /b 1
)

echo.
echo [6/6] Running F3DebugCheatLifecycleTests...
dotnet run --project tests\F3DebugCheatLifecycleTests.csproj -c Release --nologo
if %ERRORLEVEL% NEQ 0 (
    echo [FAIL] F3DebugCheatLifecycleTests failed.
    exit /b 1
)

echo.
echo ==========================================
echo Logic tests passed.
echo ==========================================
exit /b 0
