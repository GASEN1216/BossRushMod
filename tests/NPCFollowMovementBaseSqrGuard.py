"""Guard: shared NPC follow movement should avoid per-update distance square roots."""

from pathlib import Path
import sys


SOURCE = Path("Integration/Utils/NPCFollowMovementBase.cs")


def fail(message: str) -> int:
    print("NPCFollowMovementBaseSqrGuard: FAIL - " + message)
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
    text = SOURCE.read_text(encoding="utf-8-sig")

    update_body = extract_method_body(text, "protected void UpdateFollowDecision(bool moving, bool waitingForPathResult, bool hasPath)")
    if update_body is None:
        return fail("missing UpdateFollowDecision body")

    run_body = extract_method_body(text, "protected bool ShouldRunWhileFollowing()")
    if run_body is None:
        return fail("missing ShouldRunWhileFollowing body")

    distance_body = extract_method_body(text, "private float GetDistanceToPlayerSqr()")
    if distance_body is None:
        return fail("missing GetDistanceToPlayerSqr helper")

    speed_body = extract_method_body(text, "private void SyncFollowSpeedSqr(float distanceToPlayerSqr)")
    if speed_body is None:
        return fail("missing SyncFollowSpeedSqr body")

    combined = update_body + "\n" + run_body + "\n" + distance_body + "\n" + speed_body
    forbidden = [
        "GetDistanceToPlayer()",
        "toPlayer.magnitude",
        ".magnitude",
    ]
    for snippet in forbidden:
        if snippet in combined:
            return fail("follow runtime still uses scalar distance -> " + snippet)

    required = [
        (update_body, "float distanceToPlayerSqr = GetDistanceToPlayerSqr();"),
        (update_body, "SyncFollowSpeedSqr(distanceToPlayerSqr);"),
        (update_body, "float followTeleportDistance = FollowTeleportDistance;"),
        (update_body, "float followTeleportDistanceSqr = followTeleportDistance * followTeleportDistance;"),
        (update_body, "if (distanceToPlayerSqr >= followTeleportDistanceSqr)"),
        (update_body, "float followStopDistance = FollowStopDistance;"),
        (update_body, "float followStopDistanceSqr = followStopDistance * followStopDistance;"),
        (update_body, "if (distanceToPlayerSqr <= followStopDistanceSqr)"),
        (run_body, "float followRunDistance = FollowRunDistance;"),
        (run_body, "float followRunDistanceSqr = followRunDistance * followRunDistance;"),
        (run_body, "&& GetDistanceToPlayerSqr() > followRunDistanceSqr;"),
        (distance_body, "Vector3 toPlayer = playerTransform.position - SelfTransform.position;"),
        (distance_body, "toPlayer.y = 0f;"),
        (distance_body, "cachedFollowDistanceSqr = toPlayer.sqrMagnitude;"),
        (distance_body, "return cachedFollowDistanceSqr;"),
        (speed_body, "float followSpeedBoostDistance = FollowSpeedBoostDistance;"),
        (speed_body, "float followSpeedBoostDistanceSqr = followSpeedBoostDistance * followSpeedBoostDistance;"),
        (speed_body, "float followSpeedResetDistance = FollowSpeedResetDistance;"),
        (speed_body, "float followSpeedResetDistanceSqr = followSpeedResetDistance * followSpeedResetDistance;"),
    ]
    for body, snippet in required:
        if snippet not in body:
            return fail("missing squared follow movement snippet -> " + snippet)

    print("NPCFollowMovementBaseSqrGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
