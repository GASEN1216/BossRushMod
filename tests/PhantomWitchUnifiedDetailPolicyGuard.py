"""
Guard: Phantom Witch FX detail policy must be unified across machines.

Requirements:
- ResolveFxDetailLevel no longer accepts a hardware-class boolean
- runtime detail selection no longer calls IsLowSpecHardware
- PhantomWitchAssetManager no longer defines IsLowSpecHardware
- the logic test file is updated to the new signature
"""

from pathlib import Path
import re
import sys


POLICY = Path("Integration/PhantomWitch/PhantomWitchPerformancePolicy.cs")
ASSET = Path("Integration/PhantomWitch/PhantomWitchAssetManager.cs")
ASSET_PARTS = [
    ASSET,
    Path("Integration/PhantomWitch/PhantomWitchAssetManager_RuntimeComponents.cs"),
]
TEST = Path("tests/PhantomWitchPerformancePolicyTests.cs")


def fail(message: str) -> int:
    print(message)
    return 1


def main() -> int:
    policy_text = POLICY.read_text(encoding="utf-8")
    asset_text = "\n".join(path.read_text(encoding="utf-8") for path in ASSET_PARTS)
    test_text = TEST.read_text(encoding="utf-8")

    if re.search(r"ResolveFxDetailLevel\s*\(\s*int\s+activeRootCount\s*,\s*bool\s+isLowSpecHardware", policy_text):
        return fail("PhantomWitchUnifiedDetailPolicyGuard: ResolveFxDetailLevel still accepts isLowSpecHardware")

    if "isLowSpecHardware" in policy_text:
        return fail("PhantomWitchUnifiedDetailPolicyGuard: performance policy still branches on machine hardware")

    if "IsLowSpecHardware()" in asset_text:
        return fail("PhantomWitchUnifiedDetailPolicyGuard: runtime detail selection still calls IsLowSpecHardware()")

    if re.search(r"private\s+static\s+bool\s+IsLowSpecHardware\s*\(", asset_text):
        return fail("PhantomWitchUnifiedDetailPolicyGuard: PhantomWitchAssetManager still defines IsLowSpecHardware")

    if "ResolveFxDetailLevel(0, 6, 10)" not in test_text:
        return fail("PhantomWitchUnifiedDetailPolicyGuard: logic test file was not updated to the unified signature")

    if "low spec prefers minimal" in test_text:
        return fail("PhantomWitchUnifiedDetailPolicyGuard: stale low-spec expectation remains in logic tests")

    print("PhantomWitchUnifiedDetailPolicyGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
