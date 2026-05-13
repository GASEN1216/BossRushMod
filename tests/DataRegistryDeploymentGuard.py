"""Guard: Assets/Data JSON files must be versionable and deployed with the DLL."""

from pathlib import Path
import sys


GITIGNORE = Path(".gitignore")
COMPILE = Path("compile_official.bat")
DEPLOY = Path("test_bossrush_official.bat")


def fail(message: str) -> int:
    print("DataRegistryDeploymentGuard: FAIL - " + message)
    return 1


def has_data_copy(text: str) -> bool:
    normalized = text.replace("/", "\\").lower()
    return "assets\\data" in normalized and ("xcopy" in normalized or "robocopy" in normalized or "copy" in normalized)


def main() -> int:
    gitignore = GITIGNORE.read_text(encoding="utf-8", errors="ignore")
    if "!/Assets/Data/" not in gitignore:
        return fail(".gitignore must unignore /Assets/Data/")
    if "!/Assets/Data/*.json" not in gitignore:
        return fail(".gitignore must unignore /Assets/Data/*.json")

    compile_text = COMPILE.read_text(encoding="utf-8", errors="ignore")
    if not has_data_copy(compile_text):
        return fail("compile_official.bat must deploy Assets\\Data JSON")

    deploy_text = DEPLOY.read_text(encoding="utf-8", errors="ignore")
    if not has_data_copy(deploy_text):
        return fail("test_bossrush_official.bat must deploy Assets\\Data JSON")

    print("DataRegistryDeploymentGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
