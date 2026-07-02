from pathlib import Path
import sys


TARGET = Path("Integration/WishFountain/WishFountainFetchPipeline.cs")


def fail(message: str) -> int:
    print("WishDanmakuJsonEscapeGuard: FAIL - " + message)
    return 1


def main() -> int:
    if not TARGET.exists():
        return fail(f"missing target file: {TARGET}")

    text = TARGET.read_text(encoding="utf-8", errors="ignore")

    if "private static bool IsUnescapedQuote" not in text:
        return fail("missing IsUnescapedQuote helper")

    if "backslashCount % 2 == 0" not in text:
        return fail("quote escaping must be based on trailing backslash parity")

    naive_token = 'json[i - 1] != \'\\\\\''
    if naive_token in text:
        return fail("naive single-backslash quote check reintroduced")

    print("WishDanmakuJsonEscapeGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
