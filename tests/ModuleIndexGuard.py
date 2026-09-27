"""Check the maintained module map and generated navigation table."""

from pathlib import Path
import subprocess
import sys

ROOT = Path(__file__).resolve().parent.parent


def main():
    return subprocess.call([sys.executable, str(ROOT / "tools/task_context.py"), "--check"], cwd=ROOT)


if __name__ == "__main__":
    raise SystemExit(main())
