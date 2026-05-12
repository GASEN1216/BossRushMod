"""
Guard: SpawnPoints JSON files must be versionable and deployed with the DLL.
"""

from pathlib import Path
import sys


GITIGNORE = Path(".gitignore")
COMPILE = Path("compile_official.bat")
DEPLOY = Path("test_bossrush_official.bat")


def fail(message: str) -> int:
    print("MapSpawnRegistryDeploymentGuard: " + message)
    return 1


def has_spawnpoints_copy(text: str) -> bool:
    normalized = text.replace("/", "\\").lower()
    return "assets\\spawnpoints" in normalized and ("xcopy" in normalized or "robocopy" in normalized or "copy" in normalized)


def main() -> int:
    gitignore = GITIGNORE.read_text(encoding="utf-8", errors="ignore")
    if "!/Assets/SpawnPoints/" not in gitignore:
        return fail(".gitignore must unignore /Assets/SpawnPoints/")
    if "!/Assets/SpawnPoints/*.json" not in gitignore:
        return fail(".gitignore must unignore /Assets/SpawnPoints/*.json")

    compile_text = COMPILE.read_text(encoding="utf-8", errors="ignore")
    if not has_spawnpoints_copy(compile_text):
        return fail("compile_official.bat must deploy Assets\\SpawnPoints JSON")

    deploy_text = DEPLOY.read_text(encoding="utf-8", errors="ignore")
    if not has_spawnpoints_copy(deploy_text):
        return fail("test_bossrush_official.bat must deploy Assets\\SpawnPoints JSON")

    print("MapSpawnRegistryDeploymentGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
