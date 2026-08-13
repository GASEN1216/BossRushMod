#!/usr/bin/env python3
"""
ModeGEncounterVariationGuard — Mode G 遭遇编排变异守卫（规格 §20 第 7 条）。

静态可验证不变式：
- 6 段冻结 fingerprint：runFormat | rematchCompositionId | contractId |
  temperamentId | 九波 (variant:key...)；
- runFormat 双态：FirstClearNarrative（3 署名）/ RematchMix（保留 2 署名 + 1 官方 wildcard）；
- rematchCompositionId 表达被替换署名槽（单槽，-1=首胜）；
- 同进程 fingerprint 完整重复时专用 Reroll domain 最多重建一次；
- 编排变体三态 Split/Pincer/Arc，单 Boss 波固定 Split，选择走 NextInt 无偏抽样；
- 确定性洗牌（Fisher-Yates）；托管署名 key 三常量冻结。

降级说明（任务方法要求 5）：规格的 1000-seed 分布门槛
（替换槽 25%-42% / 变体·性格 20%-45% / 众数<=2% / 唯一>=90% / 比值<=1.5）
无法在静态正则 guard 内实现，此处降级为上述编排/轮换结构断言；
分布门槛须由运行时回归测试覆盖。
"""
import os
import re
import sys

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
MODEG_DIR = os.path.join(REPO_ROOT, "ModeG")


def read(name):
    path = os.path.join(MODEG_DIR, name)
    if not os.path.exists(path):
        return None
    with open(path, "r", encoding="utf-8", errors="replace") as fh:
        return fh.read()


def main():
    errors = []
    variation = read("ModeGEncounterVariation.cs")
    waveplan = read("ModeGWavePlan.cs")
    if variation is None:
        errors.append("ModeGEncounterVariation.cs 不存在")
    if waveplan is None:
        errors.append("ModeGWavePlan.cs 不存在")

    if variation:
        for key in ["managed_dragon_descendant", "managed_dragon_king", "managed_phantom_witch"]:
            if '"{}"'.format(key) not in variation:
                errors.append("[ManagedKey:{}] 托管署名 key 常量缺失".format(key))
        checks = [
            ("EligibilityRegistry",
             r"public static void SetSignatureEligibility\(string key, bool eligible\)",
             "eligibility 登记入口存在"),
            ("EligibilityQuery",
             r"public static bool IsSignatureEligible\(string key\)",
             "eligibility 查询入口存在"),
            ("EligibilityCacheReset",
             r"public static void ResetStaticCaches\(\)[\s\S]{0,120}?_eligibility\.Clear\(\);",
             "eligibility 静态缓存具备生命周期清理入口"),
            ("DevPoolFallbackGated",
             r"!ModeGAvailability\.IsProductionReady\s+&&\s*ModeGAvailability\.AllowDevTestEntry",
             "开发池兜底仅 !IsProductionReady && AllowDevTestEntry"),
            ("SpawnOffsets",
             r"GetSpawnOffsets\(",
             "Split/Pincer/Arc 落点偏移入口存在"),
        ]
        for name, pattern, desc in checks:
            if not re.search(pattern, variation):
                errors.append("[{}] 不满足: {}".format(name, desc))

    if waveplan:
        checks = [
            ("RunFormatDual",
             r"FirstClearNarrative,[\s\S]*?RematchMix",
             "runFormat 双态（首胜叙事/复战混编）"),
            ("RematchCompositionDoc",
             r"保留 2 署名 \+ 1 官方 wildcard",
             "复战 2 署名 + 1 wildcard 规则冻结"),
            ("ReplacedSlotField",
             r"public readonly int rematchCompositionId",
             "被替换署名槽单槽字段"),
            ("FingerprintSixSegments",
             r"sb\.Append\(\(int\)runFormat\)\.Append\('\|'\);"
             r"[\s\S]*?sb\.Append\(rematchCompositionId\)\.Append\('\|'\);"
             r"[\s\S]*?sb\.Append\(selectedFateContractId\)\.Append\('\|'\);"
             r"[\s\S]*?sb\.Append\(nemesisTemperamentId\)\.Append\('\|'\);"
             r"[\s\S]*?sb\.Append\(\(int\)waves\[w\]\.variant\)",
             "fingerprint 六段冻结（runFormat|rematch|contract|temperament|九波 variant:keys）"),
            ("RerollOnce",
             r"seenFingerprints != null && seenFingerprints\.Contains\(plan\.planFingerprint\)",
             "fingerprint 重复 Reroll 判重存在"),
            ("VariantUnbiased",
             r"ModeGDeterministicRandom\.NextInt\(ref variantState, 3\)",
             "编排变体走无偏整数抽样"),
            ("SingleBossSplit",
             r"if \(bossCount <= 1\) return ModeGPlanVariant\.Split;",
             "单 Boss 波固定 Split"),
            ("FisherYates",
             r"Fisher-Yates",
             "确定性洗牌（Fisher-Yates）"),
        ]
        for name, pattern, desc in checks:
            if not re.search(pattern, waveplan):
                errors.append("[{}] 不满足: {}".format(name, desc))

        # Reroll 最多重建一次：判重分支内只调用一次 BuildCore
        m = re.search(
            r"seenFingerprints != null && seenFingerprints\.Contains\(plan\.planFingerprint\)\)"
            r"[\s\S]{0,400}?return", waveplan)
        if m and len(re.findall(r"BuildCore\(", m.group(0))) != 1:
            errors.append("[RerollOnceBuild] Reroll 分支必须恰好重建一次")

    if errors:
        print("ModeGEncounterVariationGuard: FAIL ({} errors)".format(len(errors)))
        for e in errors:
            print("  - " + e)
        return 1

    print("ModeGEncounterVariationGuard: PASS")
    print("  注: 1000-seed 分布门槛已按任务要求降级为结构断言（运行时回归另行覆盖）")
    return 0


if __name__ == "__main__":
    sys.exit(main())
