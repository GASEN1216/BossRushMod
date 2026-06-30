"""Guard: Mode F bounty radar should cache Transform/position reads per refresh."""

from pathlib import Path
import sys


SOURCES = [
    Path("ModeF/ModeFUI.cs"),
    Path("ModeF/ModeFUI_BountyRadarAndHealthBars.cs"),
]


def fail(message: str) -> int:
    print("ModeFBountyRadarTransformCacheGuard: FAIL - " + message)
    return 1


def extract_method_body(text: str, signature: str) -> str | None:
    start = text.find(signature)
    if start < 0:
        return None

    brace_start = text.find("{", start)
    if brace_start < 0:
        return None

    depth = 0
    for idx in range(brace_start, len(text)):
        ch = text[idx]
        if ch == "{":
            depth += 1
        elif ch == "}":
            depth -= 1
            if depth == 0:
                return text[brace_start : idx + 1]

    return None


def main() -> int:
    text = "\n".join(path.read_text(encoding="utf-8-sig") for path in SOURCES)
    update_ui = extract_method_body(text, "private void UpdateModeFBountyRadarUI()")
    update_entry = extract_method_body(text, "private void UpdateModeFBountyRadarEntry(")
    visible = extract_method_body(text, "private static bool IsModeFBountyRadarTargetVisible(")
    if update_ui is None:
        return fail("missing UpdateModeFBountyRadarUI")
    if update_entry is None:
        return fail("missing UpdateModeFBountyRadarEntry")
    if visible is None:
        return fail("missing IsModeFBountyRadarTargetVisible")

    required_update = [
        "Transform playerTransform = player != null ? player.transform : null;",
        "if (playerTransform == null || radarCamera == null)",
        "Vector3 playerPos = playerTransform.position;",
        "Transform bossTransform = boss != null ? boss.transform : null;",
        "if (boss == null || bossTransform == null || boss.Health == null || boss.Health.IsDead)",
        "Vector3 bossPos = bossTransform.position;",
        "if (IsModeFBountyRadarTargetVisible(radarCamera, bossPos))",
        "position = bossPos,",
        "target.position,",
        "Transform leaderTransform = leader != null ? leader.transform : null;",
        "Vector3 leaderPos = leaderTransform != null ? leaderTransform.position : Vector3.zero;",
        "!IsModeFBountyRadarTargetVisible(radarCamera, leaderPos)",
        "float leaderDisplayDistanceSqr = (leaderPos - playerPos).sqrMagnitude;",
        "leaderPos,",
    ]
    for snippet in required_update:
        if snippet not in update_ui:
            return fail("UpdateModeFBountyRadarUI missing cached-transform snippet -> " + snippet)

    required_text = [
        "public Vector3 position;",
        "Vector3 targetPos,",
    ]
    for snippet in required_text:
        if snippet not in text:
            return fail("missing radar target position plumbing -> " + snippet)

    if "boss.transform.position" in update_ui or "player.transform.position" in update_ui:
        return fail("UpdateModeFBountyRadarUI should not directly reread transform.position")

    if "boss.transform" in update_entry:
        return fail("UpdateModeFBountyRadarEntry should use the supplied target position")
    if "GetModeFBountyRadarDirection(playerPos, targetPos, radarForward, radarRight)" not in update_entry:
        return fail("UpdateModeFBountyRadarEntry should use supplied targetPos")

    if "CharacterMainControl boss" in visible or "boss.transform" in visible:
        return fail("visibility helper should use a supplied target position, not a CharacterMainControl")
    if "camera.WorldToViewportPoint(targetPos + Vector3.up * MODEF_BOUNTY_RADAR_WORLD_HEIGHT)" not in visible:
        return fail("visibility helper should project supplied targetPos")

    print("ModeFBountyRadarTransformCacheGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
