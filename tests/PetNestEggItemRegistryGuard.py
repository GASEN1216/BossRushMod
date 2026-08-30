#!/usr/bin/env python3
"""
PetNestEggItemRegistryGuard — 遗种蛋注册与台账守卫（实施计划 步骤 4）。

漏登记的后果是硬伤：官方存档/商店/UI 会按 TypeID 查 prefab，查不到就变
FallbackItem，玩家背包里的蛋在重启后变成一块占位砖。因此三处注册点 + 台账
必须同时齐全：
1. Integration/Items/ItemContentRegistry.cs 注册 configurator；
2. Integration/BossRushDynamicItemRegistry.cs BuildPlans 登记按需注册计划；
3. Integration/BossRushIntegration_StartAndScene.cs 挂本地化注入；
4. docs/Bossrush使用物品ID表.md 与 AGENTS.md 台账登记 500059。

另外守：
- TypeID 常量在 BossRushItemIds 里，禁止散落魔法数；
- MaxStack = 1（堆叠合并会让不同血脉的蛋并成一枚，血脉信息丢失）；
- 血脉走 KV `PetNest_Lineage`，读不到时返回 null 供孵化侧 fail-closed；
- 零新增 bundle：计划里不得出现 ItemBundles/EquipmentBundles。
"""
import os
import re
import sys

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(REPO_ROOT, "tests"))

from petnest_guard_util import read_text, repo_path, report, strip_cs_comments  # noqa: E402

GUARD = "PetNestEggItemRegistryGuard"
EGG_TYPE_ID = "500059"


def check_config(errors):
    text = read_text(repo_path("Integration", "Items", "RelicEggConfig.cs"))
    if text is None:
        errors.append("[File] 缺少 Integration/Items/RelicEggConfig.cs")
        return
    code = strip_cs_comments(text)

    if "public const int TYPE_ID = BossRushItemIds.RelicEgg;" not in code:
        errors.append("[TypeID] TYPE_ID 必须引用 BossRushItemIds.RelicEgg，禁止魔法数")
    if "public const int MAX_STACK = 1;" not in code:
        errors.append("[堆叠] MaxStack 必须为 1，否则不同血脉的蛋会被合并")
    if "item.MaxStackCount = MAX_STACK;" not in code:
        errors.append("[堆叠] ConfigureItem 必须写 MaxStackCount")
    if 'public const string VAR_LINEAGE = "PetNest_Lineage";' not in code:
        errors.append("[KV] 血脉 KV 键必须是 PetNest_Lineage")
    if "Variables.Set(VAR_LINEAGE, lineageKey, true)" not in code:
        errors.append("[KV] 必须把血脉写进 Item.Variables")
    if "Variables.SetDisplay(VAR_LINEAGE, true)" not in code:
        errors.append("[KV] 血脉必须开启 tooltip 展示")
    if 'LocalizationHelper.InjectLocalization("Var_" + VAR_LINEAGE' not in code:
        errors.append("[本地化] KV 展示名必须注入 Var_ 前缀键")
    if 'item.DisplayNameRaw = LOC_KEY_DISPLAY;' not in code:
        errors.append("[本地化] 必须设置 DisplayNameRaw")
    if 'LOC_KEY_DISPLAY = "BossRush_PetNest_RelicEgg"' not in code:
        errors.append("[本地化] DisplayNameRaw 必须走 BossRush_PetNest_ 前缀")

    # ReadLineage fail-closed
    read_fn = re.search(r"public static string ReadLineage\(Item egg\)[\s\S]{0,500}?\n        \}", code)
    if read_fn is None:
        errors.append("[KV] 缺少 ReadLineage(Item) 读取入口")
    elif "return string.IsNullOrEmpty(value) ? null : value;" not in read_fn.group(0):
        errors.append("[fail-closed] 血脉缺失必须返回 null 供孵化侧拒绝消耗蛋")


def check_registration_points(errors):
    # 1) configurator
    reg = read_text(repo_path("Integration", "Items", "ItemContentRegistry.cs"))
    if reg is None:
        errors.append("[File] 缺少 Integration/Items/ItemContentRegistry.cs")
    elif "RelicEggConfig.RegisterConfigurator();" not in strip_cs_comments(reg):
        errors.append("[注册点1] ItemContentRegistry 缺少 RelicEggConfig.RegisterConfigurator()")

    # 2) 动态注册计划
    dyn = read_text(repo_path("Integration", "BossRushDynamicItemRegistry.cs"))
    if dyn is None:
        errors.append("[File] 缺少 Integration/BossRushDynamicItemRegistry.cs")
    else:
        dcode = strip_cs_comments(dyn)
        if "BossRushItemIds.RelicEgg" not in dcode:
            errors.append("[注册点2] BuildPlans 缺少 BossRushItemIds.RelicEgg 计划（漏登记会变 FallbackItem）")
        plan = re.search(r"Add\(plans, new RegistrationPlan\s*\{[\s\S]{0,400}?\}, BossRushItemIds\.RelicEgg\);", dcode)
        if plan is None:
            errors.append("[注册点2] 无法解析遗种蛋的注册计划")
        else:
            body = plan.group(0)
            if "RelicEggConfig.EnsureRuntimeFallbackRegistrationShell()" not in body:
                errors.append("[注册点2] 遗种蛋计划必须带运行时兜底 FallbackLoader")
            if "ItemBundles" in body or "EquipmentBundles" in body:
                errors.append("[零资源] 遗种蛋是零新增 bundle 物品，计划里不得声明 bundle")

    # 3) 本地化挂接
    start = read_text(repo_path("Integration", "BossRushIntegration_StartAndScene.cs"))
    if start is None:
        errors.append("[File] 缺少 Integration/BossRushIntegration_StartAndScene.cs")
    else:
        scode = strip_cs_comments(start)
        if "PetNestLocalization.Inject();" not in scode:
            errors.append("[注册点3] InjectLocalization_Extra_Integration 缺少 PetNestLocalization.Inject()")
        inject = re.search(r"private void InjectLocalization_Extra_Integration\(\)[\s\S]{0,3000}?\n        \}", scode)
        if inject is not None and "PetNestLocalization.Inject();" not in inject.group(0):
            errors.append("[注册点3] PetNestLocalization.Inject() 必须在 InjectLocalization_Extra_Integration 体内")

    # 本地化文件自身
    loc = read_text(repo_path("Localization", "PetNestLocalization.cs"))
    if loc is None:
        errors.append("[File] 缺少 Localization/PetNestLocalization.cs")
    else:
        lcode = strip_cs_comments(loc)
        if "RelicEggConfig.InjectLocalization();" not in lcode:
            errors.append("[本地化] PetNestLocalization 必须转发遗种蛋物品的注入")
        if "PetNestTuning.LocalizationPrefix" not in lcode:
            errors.append("[本地化] 键前缀必须走 PetNestTuning.LocalizationPrefix")


def check_ledger(errors):
    # TypeID 常量（BossRushItemIds 是 partial class，遗种巢的号在 Config/ConfigPetNest.cs）
    cfg = read_text(repo_path("Config", "ConfigPetNest.cs"))
    if cfg is None:
        errors.append("[File] 缺少 Config/ConfigPetNest.cs")
    elif "public const int RelicEgg = 500059;" not in strip_cs_comments(cfg):
        errors.append("[台账] BossRushItemIds 缺少 RelicEgg = 500059")

    # 物品 ID 表（docs 是 local-only，缺失只告警不判红）
    table = read_text(repo_path("docs", "Bossrush使用物品ID表.md"))
    if table is not None:
        if EGG_TYPE_ID not in table:
            errors.append("[台账] docs/Bossrush使用物品ID表.md 未登记 500059")

    agents = read_text(repo_path("AGENTS.md"))
    if agents is None:
        errors.append("[File] 缺少 AGENTS.md")
    else:
        # 范围上界会随后续系统继续推进（当前 500061），这里只校验它确实覆盖了遗种蛋。
        import re as _re
        _rng = _re.search(r"`500001-500(\d{3})`", agents)
        if not _rng or int(_rng.group(1)) < 59:
            errors.append("[台账] AGENTS.md 的 TypeID 登记范围未覆盖 500059")
        # 下一可用只要越过 500059 即可：本 guard 守的是「遗种蛋这个号已被占用」，
        # 不是「它必须是最后一个」——后续系统会继续往后登记（当前已到 500061）。
        import re as _re
        _next = _re.search(r"下一可用[：:]\s*`?500(\d{3})`?", agents)
        if not _next or int(_next.group(1)) <= 59:
            errors.append("[台账] AGENTS.md 的下一可用 TypeID 未越过 500059")


def main():
    errors = []
    check_config(errors)
    check_registration_points(errors)
    check_ledger(errors)
    return report(GUARD, errors)


if __name__ == "__main__":
    sys.exit(main())
