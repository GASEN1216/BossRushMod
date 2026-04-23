r"""
Guard: 纯逻辑 C# 测试必须接入官方测试流程。

要求：
- 仓库根目录存在 test_logic_official.bat
- test_logic_official.bat 在运行任何项目之前会清理 tests\bin 和 tests\obj，避免旧 Release 产物污染结果
- test_logic_official.bat 运行：
  - LegacyBossLootProbabilityTests.csproj
  - PhantomWitchPerformancePolicyTests.csproj
  - SimpleJsonHelperTests.csproj
  - AffinityJsonSerializerTests.csproj
  - F3DebugCheatMathTests.csproj
  - F3DebugCheatLifecycleTests.csproj
- test_bossrush_official.bat 会调用 test_logic_official.bat
"""

from pathlib import Path
import re
import sys


TEST_BAT = Path("test_bossrush_official.bat")
LOGIC_BAT = Path("test_logic_official.bat")


def fail(message: str) -> int:
    print(message)
    return 1


def main() -> int:
    if not LOGIC_BAT.exists():
        return fail("TestLogicWiringGuard: missing test_logic_official.bat")

    logic_text = LOGIC_BAT.read_text(encoding="utf-8")
    test_text = TEST_BAT.read_text(encoding="utf-8")

    if "LegacyBossLootProbabilityTests.csproj" not in logic_text:
        return fail("TestLogicWiringGuard: test_logic_official.bat does not run LegacyBossLootProbabilityTests.csproj")

    if "PhantomWitchPerformancePolicyTests.csproj" not in logic_text:
        return fail("TestLogicWiringGuard: test_logic_official.bat does not run PhantomWitchPerformancePolicyTests.csproj")

    if "SimpleJsonHelperTests.csproj" not in logic_text:
        return fail("TestLogicWiringGuard: test_logic_official.bat does not run SimpleJsonHelperTests.csproj")

    if "AffinityJsonSerializerTests.csproj" not in logic_text:
        return fail("TestLogicWiringGuard: test_logic_official.bat does not run AffinityJsonSerializerTests.csproj")

    if "F3DebugCheatMathTests.csproj" not in logic_text:
        return fail("TestLogicWiringGuard: test_logic_official.bat does not run F3DebugCheatMathTests.csproj")

    if "F3DebugCheatLifecycleTests.csproj" not in logic_text:
        return fail("TestLogicWiringGuard: test_logic_official.bat does not run F3DebugCheatLifecycleTests.csproj")

    if re.search(r"if\s+exist\s+tests\\bin\s+rd\s+/s\s+/q\s+tests\\bin", logic_text, re.IGNORECASE) is None:
        return fail("TestLogicWiringGuard: test_logic_official.bat does not clear tests\\bin before running logic tests")

    if re.search(r"if\s+exist\s+tests\\obj\s+rd\s+/s\s+/q\s+tests\\obj", logic_text, re.IGNORECASE) is None:
        return fail("TestLogicWiringGuard: test_logic_official.bat does not clear tests\\obj before running logic tests")

    if re.search(r"call\s+test_logic_official\.bat", test_text, re.IGNORECASE) is None:
        return fail("TestLogicWiringGuard: test_bossrush_official.bat does not call test_logic_official.bat")

    print("TestLogicWiringGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
