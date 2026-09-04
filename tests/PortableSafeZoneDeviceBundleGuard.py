from pathlib import Path
import os
import sys


BUNDLE = Path("Assets/Items/portable_safe_zone_device")
COMPILE_SCRIPT = Path("compile_official.bat")
TEST_DEPLOY_SCRIPT = Path("test_bossrush_official.bat")
CONTRACT_DOC = Path("docs/制作教程/便携安全区装置_Unity资源制作约定.md")

MIN_BUNDLE_BYTES = 1024
MAX_BUNDLE_BYTES = 512 * 1024


def fail(message: str) -> int:
    print("PortableSafeZoneDeviceBundleGuard: " + message)
    return 1


def main() -> int:
    for path in [COMPILE_SCRIPT, TEST_DEPLOY_SCRIPT]:
        if not path.is_file():
            return fail("missing source script -> " + path.as_posix())
        if 'Assets\\Items\\portable_safe_zone_device' not in path.read_text(encoding="utf-8"):
            return fail("missing bundle deploy entry -> " + path.as_posix())
    if os.environ.get("BOSSRUSH_GUARD_SOURCE_ONLY") == "1":
        print("PortableSafeZoneDeviceBundleGuard: PARTIAL (部署接线已验证；本地 bundle 和制作约定未验证)")
        return 2
    required_files = [BUNDLE, COMPILE_SCRIPT, TEST_DEPLOY_SCRIPT, CONTRACT_DOC]
    for path in required_files:
        if not path.is_file():
            return fail("missing required file -> " + path.as_posix())

    bundle_size = BUNDLE.stat().st_size
    if bundle_size < MIN_BUNDLE_BYTES:
        return fail("bundle is unexpectedly small -> " + str(bundle_size) + " bytes")
    if bundle_size > MAX_BUNDLE_BYTES:
        return fail("bundle exceeds 512 KiB budget -> " + str(bundle_size) + " bytes")

    with BUNDLE.open("rb") as handle:
        if handle.read(7) != b"UnityFS":
            return fail("bundle does not start with UnityFS header")

    deploy_snippet = 'Assets\\Items\\portable_safe_zone_device'
    for path in [COMPILE_SCRIPT, TEST_DEPLOY_SCRIPT]:
        text = path.read_text(encoding="utf-8", errors="ignore")
        if deploy_snippet not in text:
            return fail("missing bundle deploy entry -> " + path.as_posix())

    contract_text = CONTRACT_DOC.read_text(encoding="utf-8", errors="ignore")
    for snippet in [
        "`500058`",
        "`portable_safe_zone_device`",
        "`BossRush_PortableSafeZoneDevice`",
        "`Assets/Items/portable_safe_zone_device`",
    ]:
        if snippet not in contract_text:
            return fail("contract doc missing identity -> " + snippet)

    print(
        "PortableSafeZoneDeviceBundleGuard: PASS "
        + "(" + str(bundle_size) + " bytes, UnityFS, deploy entries present)"
    )
    return 0


if __name__ == "__main__":
    sys.exit(main())
