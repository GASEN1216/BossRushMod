@echo off
chcp 65001 >nul
cd /d "%~dp0"
REM verify_syntax.bat - C# 语法层探针（不是编译验证）
REM
REM 本机没装《鸭科夫》时 compile_official.bat 跑不了（缺 Duckov_Data\Managed 游戏 DLL），
REM 但语法层不需要游戏程序集。本脚本用 .NET SDK 自带 Roslyn 把编译清单里的源码过一遍，
REM 抓括号不配对、关键字拼错、C# 7.3 不支持的语法这类问题。
REM
REM !! 通过 != 编译通过 !!
REM 类型不存在、方法签名不匹配、重载歧义只有真编译才能发现。
REM 引用本脚本结论时必须写明「语法通过，未正式编译」。
REM
REM 用法:
REM   verify_syntax.bat              语法层检查
REM   verify_syntax.bat --with-bcl   额外挂 .NET Framework 引用，多抓一层 BCL 用法错误

set "PYTHONIOENCODING=utf-8"
set "PYTHONUTF8=1"

REM 同 run_guards.bat：if(...) 块内的 %ERRORLEVEL% 在解析期就展开，必须用 goto 分派。
py -3 --version >nul 2>nul
if not errorlevel 1 goto :use_py
python --version >nul 2>nul
if not errorlevel 1 goto :use_python
echo [FAIL] 未找到 Python（既没有 py -3 也没有 python）
exit /b 1

:use_py
py -3 "tools\verify_syntax.py" %*
exit /b %ERRORLEVEL%

:use_python
python "tools\verify_syntax.py" %*
exit /b %ERRORLEVEL%
