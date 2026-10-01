"""
Guard: Windows build/deploy helpers must not force a stale local GAME_PATH.

The scripts may list D:/E:/Steam candidates, but they must first validate any
existing GAME_PATH and auto-detect a real Duckov_Data/Managed folder. This keeps
compile_official.bat from passing an invalid /lib path to csc.
"""

from pathlib import Path
import importlib.util
import re
import sys
import tempfile


SCRIPTS = [
    Path("compile_official.bat"),
    Path("test_bossrush_official.bat"),
    Path("test_bossrush_smoke_manual.bat"),
]
GITATTRIBUTES = Path(".gitattributes")
# CR-2026-09-30-011: the Sky Island shader gate must find the game the same way compile_official.bat does
# (valid GAME_PATH first, then the :ensure_game_path candidates read from the bat), never a hard-coded install.
SHADER_TOOL = Path(__file__).resolve().parents[1] / "tools" / "verify_sky_island_bundle_shaders.py"


def check_shader_tool() -> str:
    text = SHADER_TOOL.read_text(encoding="utf-8")
    if "steamapps" in text.lower():
        return "verify_sky_island_bundle_shaders.py must not hard-code a Steam install path"
    spec = importlib.util.spec_from_file_location("verify_sky_island_bundle_shaders", SHADER_TOOL)
    tool = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(tool)
    bat = Path("compile_official.bat").read_text(encoding="utf-8", errors="ignore")
    expected = re.findall(r'call :try_game_path "([^"]+)"', bat)
    got = tool.game_path_candidates()
    if not expected or len(got) != len(expected):
        return "shader tool candidates must be exactly compile_official.bat's :try_game_path list"
    with tempfile.TemporaryDirectory() as tmp:
        good = Path(tmp) / "good"
        marker = good / tool.MANAGED_MARKER
        marker.parent.mkdir(parents=True)
        marker.write_bytes(b"")
        missing = Path(tmp) / "missing"
        if tool.resolve_game_path({"GAME_PATH": str(good)}, [missing])[0] != good.resolve():
            return "shader tool must honour a valid GAME_PATH first"
        if tool.resolve_game_path({"GAME_PATH": str(missing)}, [missing, good])[0] != good.resolve():
            return "shader tool must ignore an invalid GAME_PATH and fall back to the candidates in order"
        if tool.resolve_game_path({}, [missing])[0] is not None:
            return "shader tool must report not-found instead of inventing a game path"
    return ""


def fail(message: str) -> int:
    print("WindowsPathDetectionGuard: " + message)
    return 1


def main() -> int:
    if not GITATTRIBUTES.exists():
        return fail(".gitattributes is missing")

    attributes_text = GITATTRIBUTES.read_text(encoding="utf-8", errors="ignore")
    if "*.bat text eol=crlf" not in attributes_text:
        return fail(".gitattributes must keep Windows .bat helpers on CRLF")

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

    shader_error = check_shader_tool()
    if shader_error:
        return fail(shader_error)

    print("WindowsPathDetectionGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
