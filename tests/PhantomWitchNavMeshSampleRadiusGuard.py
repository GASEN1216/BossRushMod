"""
Guard the Phantom Witch teleport NavMesh sample radius tuning.

Requirements:
- NavMeshSampleRadius must be 2.0
- tracked teleport/navmesh sampling must continue to read the shared config constant
"""

from pathlib import Path
import re
import sys


CONFIG = Path("Integration/PhantomWitch/PhantomWitchConfig.cs")
ABILITY = Path("Integration/PhantomWitch/PhantomWitchAbilityController.cs")


def fail(message: str) -> int:
    print(message)
    return 1


def main() -> int:
    config_text = CONFIG.read_text(encoding="utf-8")
    ability_text = ABILITY.read_text(encoding="utf-8")

    missing: list[str] = []

    if re.search(r"public\s+const\s+float\s+NavMeshSampleRadius\s*=\s*2f\s*;", config_text) is None:
        missing.append("NavMeshSampleRadius is not set to 2f")

    if "PhantomWitchConfig.NavMeshSampleRadius" not in ability_text:
        missing.append("ability sampling no longer uses PhantomWitchConfig.NavMeshSampleRadius")

    if missing:
        return fail("PhantomWitchNavMeshSampleRadiusGuard: " + " | ".join(missing))

    print("PhantomWitchNavMeshSampleRadiusGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
