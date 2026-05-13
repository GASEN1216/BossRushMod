"""Property test: Mode E grid spawn filtering must match the old O(N^2) filter."""

from __future__ import annotations

from pathlib import Path
import json
import math
import random
import re
import sys


PROJECT_ROOT = Path(__file__).resolve().parent.parent
SOURCE = PROJECT_ROOT / "ModeE" / "ModeESpawnAllocation.cs"
SPAWN_DIR = PROJECT_ROOT / "Assets" / "SpawnPoints"
MIN_DISTANCE = 10.0
MIN_DISTANCE_SQR = MIN_DISTANCE * MIN_DISTANCE

Point = tuple[float, float, float]


def fail(message: str) -> int:
    print("ModeESpawnAllocationParityPropertyTest: FAIL - " + message)
    return 1


def sqr_distance(a: Point, b: Point) -> float:
    dx = a[0] - b[0]
    dy = a[1] - b[1]
    dz = a[2] - b[2]
    return dx * dx + dy * dy + dz * dz


def sort_by_player_distance(points: list[Point], player: Point) -> list[Point]:
    return sorted(points, key=lambda point: sqr_distance(point, player))


def old_filter(sorted_points: list[Point]) -> list[Point]:
    filtered: list[Point] = []
    for candidate in sorted_points:
        too_close = False
        for accepted in filtered:
            if sqr_distance(candidate, accepted) < MIN_DISTANCE_SQR:
                too_close = True
                break
        if not too_close:
            filtered.append(candidate)
    return filtered


def grid_cell(point: Point) -> tuple[int, int]:
    return (
        math.floor(point[0] / MIN_DISTANCE),
        math.floor(point[2] / MIN_DISTANCE),
    )


def grid_filter(sorted_points: list[Point]) -> list[Point]:
    filtered: list[Point] = []
    accepted_by_cell: dict[tuple[int, int], list[Point]] = {}

    for candidate in sorted_points:
        cell = grid_cell(candidate)
        too_close = False
        for dx in range(-1, 2):
            if too_close:
                break
            for dz in range(-1, 2):
                for accepted in accepted_by_cell.get((cell[0] + dx, cell[1] + dz), []):
                    if sqr_distance(candidate, accepted) < MIN_DISTANCE_SQR:
                        too_close = True
                        break
                if too_close:
                    break

        if too_close:
            continue

        filtered.append(candidate)
        accepted_by_cell.setdefault(cell, []).append(candidate)

    return filtered


def parse_point(value) -> Point | None:
    if not isinstance(value, list) or len(value) != 3:
        return None
    return (float(value[0]), float(value[1]), float(value[2]))


def load_map_cases() -> list[tuple[str, list[Point], list[Point]]]:
    cases: list[tuple[str, list[Point], list[Point]]] = []
    for path in sorted(SPAWN_DIR.glob("*.json")):
        data = json.loads(path.read_text(encoding="utf-8"))
        points = [parse_point(value) for value in data.get("modeESpawnPoints") or []]
        points = [point for point in points if point is not None]
        if not points:
            continue

        players: list[Point] = [(0.0, 0.0, 0.0), points[0], points[len(points) // 2]]
        player_pos = parse_point(data.get("modeEPlayerSpawnPos"))
        if player_pos is not None:
            players.append(player_pos)

        cases.append((path.name, points, players))
    return cases


def assert_equal(case_name: str, sorted_points: list[Point]) -> int | None:
    old = old_filter(sorted_points)
    new = grid_filter(sorted_points)
    if old != new:
        return fail(
            f"{case_name}: grid filter diverged old_count={len(old)} new_count={len(new)}"
        )
    return None


def verify_source_shape() -> int | None:
    text = SOURCE.read_text(encoding="utf-8")
    required = [
        "FilterModeESpawnPointsByDistanceGrid(sorted)",
        "acceptedByCell",
        "dx <= 1",
        "dz <= 1",
        "(candidate - acceptedPoint).sqrMagnitude < MODE_E_SPAWN_MIN_DISTANCE_SQR",
        "filtered.Add(candidate);",
    ]
    missing = [needle for needle in required if needle not in text]
    if missing:
        return fail("source helper shape missing: " + ", ".join(missing))

    if not re.search(r"private\s+const\s+float\s+MODE_E_SPAWN_MIN_DISTANCE\s*=\s*10f\s*;", text):
        return fail("MODE_E_SPAWN_MIN_DISTANCE must remain 10f")

    return None


def run_map_cases() -> int | None:
    cases = load_map_cases()
    if len(cases) != 9:
        return fail(f"expected 9 JSON map cases, found {len(cases)}")

    for name, points, players in cases:
        for idx, player in enumerate(players):
            result = assert_equal(f"{name}/player{idx}", sort_by_player_distance(points, player))
            if result is not None:
                return result

    return None


def run_random_cases() -> int | None:
    rng = random.Random(20260513)
    for case_idx in range(500):
        point_count = rng.randint(0, 220)
        points: list[Point] = []
        for _ in range(point_count):
            if rng.random() < 0.25 and points:
                base = rng.choice(points)
                points.append((
                    base[0] + rng.uniform(-9.9, 9.9),
                    base[1] + rng.uniform(-2.0, 2.0),
                    base[2] + rng.uniform(-9.9, 9.9),
                ))
            else:
                points.append((
                    rng.uniform(-700.0, 700.0),
                    rng.uniform(-20.0, 40.0),
                    rng.uniform(-700.0, 700.0),
                ))

        player = (
            rng.uniform(-500.0, 500.0),
            rng.uniform(-5.0, 5.0),
            rng.uniform(-500.0, 500.0),
        )
        result = assert_equal(f"random{case_idx}", sort_by_player_distance(points, player))
        if result is not None:
            return result

    return None


def main() -> int:
    for check in (verify_source_shape, run_map_cases, run_random_cases):
        result = check()
        if result is not None:
            return result

    print("ModeESpawnAllocationParityPropertyTest: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
