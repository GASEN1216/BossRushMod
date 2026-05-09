"""SmokeLogScan: scan the latest Duckov log for BossRush-related errors.

This helper supports manual in-game smoke tests. It does not prove gameplay
behavior by itself; it only catches obvious BossRush stack traces after a run.
"""

from dataclasses import dataclass
from pathlib import Path
import os
import re
import sys
from typing import Iterable, List, Optional


ERROR_LINE = re.compile(r"(Exception|\bERROR\b|\bError\b|\[Error\])")
BOSSRUSH_LINE = re.compile(r"BossRush", re.IGNORECASE)
ERROR_CONTEXT_LINES = 24


@dataclass
class SmokeLogScanResult:
    total_error_blocks: int
    bossrush_error_blocks: List[str]


def scan_log_text(text: str) -> SmokeLogScanResult:
    lines = text.splitlines()
    bossrush_blocks: List[str] = []
    total_blocks = 0

    for index, line in enumerate(lines):
        if not ERROR_LINE.search(line):
            continue

        total_blocks += 1
        block_lines = lines[index:index + ERROR_CONTEXT_LINES]
        block = "\n".join(block_lines)
        if BOSSRUSH_LINE.search(block):
            bossrush_blocks.append(block)

    return SmokeLogScanResult(
        total_error_blocks=total_blocks,
        bossrush_error_blocks=bossrush_blocks,
    )


def find_latest_log(game_path: Path) -> Optional[Path]:
    logs = [path for path in game_path.glob("*.log") if path.is_file()]
    if not logs:
        return None
    return max(logs, key=lambda path: path.stat().st_mtime)


def resolve_game_path(argv: Iterable[str]) -> Path:
    args = list(argv)
    if args:
        return normalize_path(args[0])

    env_path = os.environ.get("DUCKOV_GAME_PATH")
    if env_path:
        return normalize_path(env_path)

    return find_game_path_from_repo(Path.cwd())


def normalize_path(value: str) -> Path:
    windows_match = re.match(r"^([A-Za-z]):[\\/](.*)$", value)
    if windows_match:
        drive = windows_match.group(1).lower()
        rest = windows_match.group(2).replace("\\", "/")
        return Path("/mnt") / drive / rest

    return Path(value)


def find_game_path_from_repo(start: Path) -> Path:
    current = start.resolve()
    while current.parent != current:
        if current.name == "Escape from Duckov":
            return current
        current = current.parent

    return start.resolve()


def print_bossrush_blocks(blocks: List[str]) -> None:
    for index, block in enumerate(blocks, start=1):
        print("")
        print("BossRush-related error block #{0}:".format(index))
        print(block)


def main(argv: List[str]) -> int:
    game_path = resolve_game_path(argv)
    latest_log = find_latest_log(game_path)
    if latest_log is None:
        print("SmokeLogScan: NO_LOG")
        print("No .log file found under: {0}".format(game_path))
        return 2

    text = latest_log.read_text(encoding="utf-8", errors="ignore")
    result = scan_log_text(text)

    print("SmokeLogScan: latest log: {0}".format(latest_log))
    print("SmokeLogScan: error blocks found: {0}".format(result.total_error_blocks))
    print("SmokeLogScan: BossRush-related error blocks: {0}".format(len(result.bossrush_error_blocks)))

    if result.bossrush_error_blocks:
        print_bossrush_blocks(result.bossrush_error_blocks)
        print("")
        print("SmokeLogScan: REVIEW")
        return 1

    print("SmokeLogScan: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
