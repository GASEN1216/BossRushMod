"""
Guard: 噬魂挽歌左键挥砍特效不能复用 Frostmourne 的冰雾烟雾链。

原因：
- 当前黑色烟雾过重的问题来自 `PhantomWitchScytheSwingFx` 直接借用了
  `FrostmourneWeaponConfig.TryAddIceEffectsToGraphic(...)`
- 这会把 Frostmourne 的 `Frostmourne_IceAura` 粒子根一起挂到镰刀左键挥砍上，
  再被重染成紫色后，视觉上容易变成一层偏黑的暗雾

要求：
- 左键挥砍实现必须只使用噬魂挽歌自己的拖尾/星屑粒子
- 不允许在 `PhantomWitchScytheSwingFx.cs` 中直接引用 Frostmourne 的 aura 根或构建 helper
"""

from pathlib import Path
import sys


SOURCE = Path("Integration/PhantomWitch/PhantomWitchScytheSwingFx.cs")


def fail(message: str) -> int:
    print(message)
    return 1


def main() -> int:
    text = SOURCE.read_text(encoding="utf-8")

    if "FrostmourneWeaponConfig.TryAddIceEffectsToGraphic(" in text:
        return fail(
            "PhantomWitchScytheSwingFxSmokeGuard: left-click swing still borrows Frostmourne aura smoke"
        )

    if "BorrowedAuraRootName" in text:
        return fail(
            "PhantomWitchScytheSwingFxSmokeGuard: borrowed aura root marker still exists in scythe swing fx"
        )

    if "IsBorrowedIceParticle" in text:
        return fail(
            "PhantomWitchScytheSwingFxSmokeGuard: borrowed ice particle helper still exists in scythe swing fx"
        )

    if "BuildCustomScytheParticles" not in text:
        return fail(
            "PhantomWitchScytheSwingFxSmokeGuard: custom scythe particle builder missing"
        )

    print("PhantomWitchScytheSwingFxSmokeGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
