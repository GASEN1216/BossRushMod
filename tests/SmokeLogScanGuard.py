"""Guard: SmokeLogScan unit tests must stay wired into validate_refactor_step."""

import sys
import unittest

import SmokeLogScanTests


def main() -> int:
    suite = unittest.defaultTestLoader.loadTestsFromModule(SmokeLogScanTests)
    result = unittest.TextTestRunner(verbosity=0).run(suite)
    if not result.wasSuccessful():
        print("SmokeLogScanGuard: FAIL")
        return 1

    print("SmokeLogScanGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
