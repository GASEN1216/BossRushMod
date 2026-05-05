"""HardcodedDevModeDefaultGuard: release default must keep hardcoded dev mode disabled."""

from pathlib import Path
import sys


def main() -> int:
    mod_text = Path("ModBehaviour.cs").read_text(encoding="utf-8")
    needle = "private const bool HardcodedDevModeEnabled = false;"
    if needle not in mod_text:
        print("HardcodedDevModeDefaultGuard: FAIL - HardcodedDevModeEnabled must default to false")
        return 1

    print("HardcodedDevModeDefaultGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
