"""官方 Health.Hurt 独占普攻诅咒概率，视觉监听不得再次施加 Buff。"""
from pathlib import Path
from cs_source_util import clean_source

def main():
    source = clean_source(Path("Integration/PhantomWitch/PhantomWitchCurseSweatVfx.cs").read_text(encoding="utf-8-sig"))
    for forbidden in ("Random.value", ".AddBuff(", "TryApplyBuff(", "EnqueueRetry("):
        if forbidden in source:
            print("PhantomWitchCurseSweatVfxFallbackGuard: FAIL - visual hook cannot own curse application: " + forbidden)
            return 1
    print("PhantomWitchCurseSweatVfxFallbackGuard: PASS")
    return 0

if __name__ == "__main__":
    raise SystemExit(main())
