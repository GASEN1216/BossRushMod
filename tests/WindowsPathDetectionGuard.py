"""
Guard: Windows build/deploy helpers must not force a stale local GAME_PATH.

The scripts may list D:/E:/Steam candidates, but they must first validate any
existing GAME_PATH and auto-detect a real Duckov_Data/Managed folder. This keeps
compile_official.bat from passing an invalid /lib path to csc.
"""

from pathlib import Path
import sys


SCRIPTS = [
    Path("compile_official.bat"),
    Path("test_bossrush_official.bat"),
    Path("test_bossrush_smoke_manual.bat"),
]


def fail(message: str) -> int:
    print("WindowsPathDetectionGuard: " + message)
    return 1


def main() -> int:
    for path in SCRIPTS:
        if not path.exists():
            return fail(f"missing {path}")

        raw = path.read_bytes()
        if b"\n" in raw.replace(b"\r\n", b""):
            return fail(f"{path} must use CRLF line endings")

        text = raw.decode("utf-8", errors="ignore")
        lower = text.lower()

        if 'set game_path=d:\\sofrware\\steam\\steamapps\\common\\escape from duckov' in lower:
            return fail(f"{path} must not unconditionally force stale D: GAME_PATH")

        if ":ensure_game_path" not in lower:
            return fail(f"{path} missing :ensure_game_path")

        if "duckov_data\\managed\\assembly-csharp.dll" not in lower:
            return fail(f"{path} must validate the managed assembly folder")

    compile_text = Path("compile_official.bat").read_text(encoding="utf-8", errors="ignore").lower()
    if ":ensure_workshop_path" not in compile_text:
        return fail("compile_official.bat missing :ensure_workshop_path")
    if "if not defined bossrush_no_pause pause" not in compile_text:
        return fail("compile_official.bat must not pause in BOSSRUSH_NO_PAUSE mode")

    print("WindowsPathDetectionGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
