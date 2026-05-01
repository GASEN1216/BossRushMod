"""OfficialCompileListFileExistenceGuard: 官方编译脚本列出的源文件必须存在。"""

from pathlib import Path
import re
import sys


COMPILE = Path("compile_official.bat")


def fail(message: str) -> int:
    print(message)
    return 1


def main() -> int:
    text = COMPILE.read_text(encoding="utf-8")
    missing: list[str] = []
    for match in re.finditer(r"^\s+([A-Za-z0-9_./\\-]+\.cs)\s+\^", text, re.MULTILINE):
        source = match.group(1).replace("\\", "/")
        if not Path(source).exists():
            missing.append(source)

    if missing:
        return fail("OfficialCompileListFileExistenceGuard: missing compile source(s): " + ", ".join(missing))

    print("OfficialCompileListFileExistenceGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
