"""Guard: Boss_Red projectile warmup should log parse failures only after all retries fail."""

from pathlib import Path


SOURCE = Path("Integration/DragonKing/Weapons/DragonKingBossGunRuntime.cs")


def fail(message):
    print("DragonKingBossRedWarmupLogGuard: FAIL - " + message)
    raise SystemExit(1)


def main():
    text = SOURCE.read_text(encoding="utf-8")
    if "Exception lastError = null;" not in text:
        fail("warmup does not keep the last retry error")
    if "lastError = e;" not in text:
        fail("warmup catch does not defer retry errors")
    if "if (lastError != null)\n            {\n                ModBehaviour.DevLog(\"[DragonKingBossGun] 解析 Boss_Red 预设武器失败: \" + lastError.Message);\n            }\n\n            return null;" not in text:
        fail("warmup must log parse failure only immediately before returning null")

    print("DragonKingBossRedWarmupLogGuard: PASS")


if __name__ == "__main__":
    main()
