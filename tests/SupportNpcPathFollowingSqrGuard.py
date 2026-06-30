"""Guard: support NPC path-following should avoid duplicate sqrt work per update."""

from pathlib import Path
import sys


SOURCES = [
    Path("Integration/NPCs/Nurse/NurseMovement.cs"),
    Path("Integration/NPCs/Goblin/GoblinMovement.cs"),
    Path("Integration/NPCs/Courier/CourierMovement.cs"),
]


def fail(message: str) -> int:
    print("SupportNpcPathFollowingSqrGuard: FAIL - " + message)
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
    for source in SOURCES:
        text = source.read_text(encoding="utf-8-sig")
        signature = (
            "private bool UpdatePathFollowing()"
            if source.name == "CourierMovement.cs"
            else "private bool UpdatePathFollowing(bool isRunning)"
        )
        body = extract_method_body(text, signature)
        if body is None:
            return fail(f"missing UpdatePathFollowing body in {source}")

        if "distanceToWaypoint = toWaypoint.magnitude;" in body:
            return fail(f"{source} still uses magnitude for waypoint threshold checks")

        if ".normalized" in body:
            return fail(f"{source} still normalizes path movement with Vector3.normalized")

        required = [
            "float nextWaypointDistanceSqr = nextWaypointDistance * nextWaypointDistance;",
            "float distanceToWaypointSqr;",
            "distanceToWaypointSqr = toWaypoint.sqrMagnitude;",
            "if (distanceToWaypointSqr < nextWaypointDistanceSqr)",
            "distanceToWaypoint = Mathf.Sqrt(distanceToWaypointSqr);",
            "float inverseDistance = 1f / distanceToWaypoint;",
        ]
        for snippet in required:
            if snippet not in body:
                return fail(f"{source} missing squared path-following snippet -> {snippet}")

    print("SupportNpcPathFollowingSqrGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
