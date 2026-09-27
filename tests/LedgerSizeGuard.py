"""Bound the current ledgers while keeping their monthly archives discoverable."""

from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
LIMITS = {"FIX_TRACKER.md": 1500, "CODE_REVIEW_FINDINGS.md": 1200}


def main():
    errors = []
    for name, limit in LIMITS.items():
        text = (ROOT / name).read_text(encoding="utf-8-sig")
        count = len(text.splitlines())
        if count > limit:
            errors.append("{} 为 {} 行，超过 {} 行".format(name, count, limit))
        if "archive/" not in text[:1000]:
            errors.append("{} 顶部缺少 archive/ 索引".format(name))
    for prefix in ("FIX_TRACKER", "CODE_REVIEW_FINDINGS"):
        if not list((ROOT / "archive").glob(prefix + "_2026-*.md")):
            errors.append("缺少 {} 月份归档".format(prefix))
    if errors:
        for error in errors:
            print("LedgerSizeGuard: " + error)
        return 1
    print("LedgerSizeGuard: PASS")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
