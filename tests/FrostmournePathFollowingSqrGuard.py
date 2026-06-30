"""Guard: Frostmourne summoned ally path-following should avoid duplicate sqrt work."""

from pathlib import Path
import sys


SOURCE = Path("Integration/Frostmourne/FrostmourneAction.cs")


def fail(message: str) -> int:
    print("FrostmournePathFollowingSqrGuard: FAIL - " + message)
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
    body = extract_method_body(text, "private bool UpdatePathFollowing(bool isRunning)")
    if body is None:
        return fail("missing UpdatePathFollowing body")

    if "distanceToWaypoint = toWaypoint.magnitude;" in body:
        return fail("still uses magnitude for waypoint threshold checks")

    if "direction.Normalize();" in body:
        return fail("still normalizes path movement with Vector3.Normalize")

    required = [
        "float nextWaypointDistanceSqr = NextWaypointDistance * NextWaypointDistance;",
        "float distanceToWaypointSqr;",
        "distanceToWaypointSqr = toWaypoint.sqrMagnitude;",
        "if (distanceToWaypointSqr < nextWaypointDistanceSqr)",
        "if (distanceToWaypointSqr <= 0.0001f)",
        "float distanceToWaypoint = Mathf.Sqrt(distanceToWaypointSqr);",
        "float inverseDistance = 1f / distanceToWaypoint;",
    ]
    for snippet in required:
        if snippet not in body:
            return fail("missing squared path-following snippet -> " + snippet)

    print("FrostmournePathFollowingSqrGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
