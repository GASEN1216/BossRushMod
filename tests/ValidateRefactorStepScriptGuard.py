"""
Guard: validate_refactor_step.bat must run the Python verifier suite reliably.

It must:
  - run from the repository root, because existing guards use root-relative paths
  - use the Windows Python launcher (`py -3`) instead of the Store `python` alias
  - include both *Guard.py and *PropertyTest.py
  - use `if errorlevel 1` inside the loop, not parse-time `%ERRORLEVEL%`
  - call the deploy helper with BOSSRUSH_NO_PAUSE so the validator cannot block
"""

from pathlib import Path
import sys


SCRIPT = Path("validate_refactor_step.bat")


def fail(message: str) -> int:
    print("ValidateRefactorStepScriptGuard: " + message)
    return 1


def main() -> int:
    if not SCRIPT.exists():
        return fail("missing validate_refactor_step.bat")

    raw = SCRIPT.read_bytes()
    if b"\n" in raw.replace(b"\r\n", b""):
        return fail("must use CRLF line endings for cmd.exe compatibility")

    text = raw.decode("utf-8", errors="ignore")
    lower = text.lower()

    if "cd tests" in lower or "pushd tests" in lower:
        return fail("guard suite must run from repo root, not from tests/")

    if "py -3" not in lower:
        return fail("must use Windows Python launcher `py -3`")

    if "*guard.py" not in lower:
        return fail("must run tests\\*Guard.py")

    if "*propertytest.py" not in lower:
        return fail("must run tests\\*PropertyTest.py")

    if "if errorlevel 1" not in lower:
        return fail("must check loop failures with `if errorlevel 1`")

    if "bossrush_no_pause" not in lower:
        return fail("must disable pause prompts while running the build/deploy helper")

    for snippet in [
        "test_bossrush_smoke_manual.bat",
        "tests/smokelogscan.py",
        "slashfx",
        "hitfx",
    ]:
        if snippet not in lower:
            return fail("manual smoke prompt missing snippet -> " + snippet)

    print("ValidateRefactorStepScriptGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
