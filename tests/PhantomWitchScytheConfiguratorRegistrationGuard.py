"""
Guard: 噬魂挽歌的 ItemFactory configurator 必须无条件注册。

Reason:
- 掉落链路和运行时配置都依赖 TypeID 500044 的 configurator
- 条件注册会让资源存在但注册时机不匹配的问题变成静默失效
"""

from pathlib import Path
import re
import sys


SOURCE = Path("Integration/Items/ItemContentRegistry.cs")


def fail(message: str) -> int:
    print(message)
    return 1


def main() -> int:
    text = SOURCE.read_text(encoding="utf-8")

    if "ItemFactory.RegisterConfigurator(PhantomWitchConfig.ReservedScytheTypeId, OnPhantomWitchScytheLoaded);" not in text:
        return fail("PhantomWitchScytheConfiguratorRegistrationGuard: missing Phantom Witch scythe configurator registration")

    if re.search(
        r"if\s*\(\s*ItemAssetsCollection\.GetPrefab\s*\(\s*PhantomWitchConfig\.ReservedScytheTypeId\s*\)\s*!=\s*null\s*\)",
        text,
    ):
        return fail("PhantomWitchScytheConfiguratorRegistrationGuard: configurator registration is still gated by GetPrefab(...) != null")

    print("PhantomWitchScytheConfiguratorRegistrationGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
