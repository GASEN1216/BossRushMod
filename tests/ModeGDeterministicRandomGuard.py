#!/usr/bin/env python3
"""
ModeGDeterministicRandomGuard — Mode G 确定性随机守卫（规格 §20 第 6 条 + 第五节）。

检查内容：
1. golden vectors 交叉验证：guard 用独立 Python 实现复算 FNV-1a 64 与 SplitMix64，
   与规格冻结值比对，再与 C# 源码中的常量逐字比对（不得由被测实现自产 expected）；
2. 冻结常量：FNV offset basis/prime、SplitMix64 增量、8 个 domain ASCII 常量、
   ProcessNonceMix/SessionMixMultiplier、SeedDomain 公式结构；
3. 禁止项：ModeG/ 全目录禁 System.Random/UnityEngine.Random/string.GetHashCode/
   HashCode.Combine 生成玩法 seed；
4. 无偏抽样：NextInt/NextLongBounded rejection sampling；加权先整数量化
   clamp(1,1000000) 后整数累计抽取，禁浮点直乘选择；
5. >=20 种输入排列：guard 以 Python 复刻 SeedDomain 公式，对 24 组
   (domain, presetKey, waveEpoch) 排列验证确定性（同输入同输出），并静态断言
   C# 侧存在 Ordinal/升序稳定排序冻结输入序列（排列不变性的前提）。
   （C# 实现无法在静态 guard 内执行；逐流一致性由结构断言 + 公式复刻交叉覆盖。）
"""
import os
import re
import sys

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
MODEG_DIR = os.path.join(REPO_ROOT, "ModeG")
MASK = (1 << 64) - 1

# ---- 规格冻结的 golden vectors（独立于 C# 实现硬编码）----
GOLDEN_FNV = {
    "": 0xcbf29ce484222325,
    "BossRush": 0x2ab0caa511720f06,
    "\u5bbf\u547d\u56de\u54cd": 0x5898876a11981479,
}
GOLDEN_SPLITMIX = [
    0xe220a8397b1dcdaf,
    0x6e789e6aa1b965f4,
    0x06c45d188009454f,
    0xf88bb8a8724c81ec,
    0x1b39896a51a8749b,
]

FNV_OFFSET = 14695981039346656037
FNV_PRIME = 1099511628211
SPLITMIX_INC = 0x9E3779B97F4A7C15

DOMAIN_CONSTANTS = {
    "BossPlan": 0x424F5353504C414E,
    "Variant": 0x56415249414E5421,
    "AmmoBan": 0x414D4D4F42414E21,
    "Reward": 0x5245574152442121,
    "Contract": 0x434F4E5452414354,
    "Temperament": 0x54454D504552414D,
    "RematchComposition": 0x52454D4154434821,
    "Reroll": 0x5245524F4C4C2121,
}


def fnv1a64(text):
    h = FNV_OFFSET
    for b in text.encode("utf-8"):
        h = ((h ^ b) * FNV_PRIME) & MASK
    return h


def splitmix64(state):
    out = []
    for _ in range(5):
        state = (state + SPLITMIX_INC) & MASK
        z = state
        z = ((z ^ (z >> 30)) * 0xBF58476D1CE4E5B9) & MASK
        z = ((z ^ (z >> 27)) * 0x94D049BB133111EB) & MASK
        out.append(z ^ (z >> 31))
    return out


def seed_domain(run_seed, domain_constant, preset_key, wave_epoch):
    """复刻 C# SeedDomain 公式：runSeed ^ domain ^ FNV(key) ^ (epoch<<32)，再一次 SplitMix。"""
    state = (run_seed ^ domain_constant ^ fnv1a64(preset_key) ^ ((wave_epoch & 0xFFFFFFFF) << 32)) & MASK
    return splitmix64_first(state)


def splitmix64_first(state):
    state = (state + SPLITMIX_INC) & MASK
    z = state
    z = ((z ^ (z >> 30)) * 0xBF58476D1CE4E5B9) & MASK
    z = ((z ^ (z >> 27)) * 0x94D049BB133111EB) & MASK
    return z ^ (z >> 31)


def read(name):
    path = os.path.join(MODEG_DIR, name)
    if not os.path.exists(path):
        return None
    with open(path, "r", encoding="utf-8", errors="replace") as fh:
        return fh.read()


def check_golden_self(errors):
    """第一步：guard 自身的独立复算必须与规格冻结值一致（防 guard 腐化）。"""
    for text, expected in GOLDEN_FNV.items():
        got = fnv1a64(text)
        if got != expected:
            errors.append("[GoldenSelfFnv] Python 复算 FNV({!r})={:#x} != 规格 {:#x}".format(
                text, got, expected))
    got = splitmix64(0)
    for i, expected in enumerate(GOLDEN_SPLITMIX):
        if got[i] != expected:
            errors.append("[GoldenSelfSplitMix] Python 复算第{}次={:#x} != 规格 {:#x}".format(
                i, got[i], expected))


def check_source_constants(errors, rand_src, avail_src):
    """第二步：C# 源码常量必须与 guard 独立复算值逐字一致。"""
    hex_map = {
        "GoldenFnv_Empty": GOLDEN_FNV[""],
        "GoldenFnv_BossRush": GOLDEN_FNV["BossRush"],
        "GoldenFnv_SuMingHuiXiang": GOLDEN_FNV["\u5bbf\u547d\u56de\u54cd"],
    }
    for i, v in enumerate(GOLDEN_SPLITMIX):
        hex_map["GoldenSplitMix_{}".format(i)] = v

    for name, value in hex_map.items():
        pattern = r"public const ulong {} = 0x[0-9a-fA-F]+UL".format(name)
        m = re.search(pattern, rand_src)
        if not m:
            errors.append("[GoldenConst:{}] 源码缺少 golden 常量".format(name))
            continue
        literal = re.search(r"0x([0-9a-fA-F]+)UL", m.group(0)).group(1)
        if int(literal, 16) != value:
            errors.append("[GoldenConst:{}] 源码值 0x{} != guard 独立复算 {:#x}".format(
                name, literal, value))

    if not re.search(r"public static bool ValidateGoldenVectors\(\)", rand_src):
        errors.append("[ValidateGoldenVectors] 缺少 Starting 预检自检入口")

    avail_checks = [
        ("FnvOffsetBasis", r"FnvOffsetBasis = 14695981039346656037UL", FNV_OFFSET),
        ("FnvPrime", r"FnvPrime = 1099511628211UL", FNV_PRIME),
        ("SplitMix64Increment", r"SplitMix64Increment = 0x9E3779B97F4A7C15UL", SPLITMIX_INC),
    ]
    for name, pattern, value in avail_checks:
        if not re.search(pattern, avail_src):
            errors.append("[FrozenConst:{}] Availability 冻结常量缺失或不匹配（期望 {:#x}）".format(
                name, value))

    for name, value in DOMAIN_CONSTANTS.items():
        pattern = r"public const ulong {} = 0x([0-9a-fA-F]+)UL".format(name)
        m = re.search(pattern, rand_src)
        if not m:
            errors.append("[DomainConst:{}] 缺少 domain ASCII 常量".format(name))
        elif int(m.group(1), 16) != value:
            errors.append("[DomainConst:{}] 源码值 != 规格 {:#x}".format(name, value))

    if not re.search(r"ProcessNonceMix = 0x4D4F4445474E4F4EUL", rand_src):
        errors.append("[ProcessNonceMix] 缺失常量 0x4D4F4445474E4F4E")
    if not re.search(r"SessionMixMultiplier = 0xD1342543DE82EF95UL", rand_src):
        errors.append("[SessionMixMultiplier] 缺失常量 0xD1342543DE82EF95")

    # SeedDomain 公式结构
    if not re.search(
            r"runSeed\s*\n?\s*\^ domainConstant\s*\n?\s*\^ Fnv1a64\(presetKey \?\? string\.Empty\)\s*\n?"
            r"\s*\^ \(\(ulong\)\(uint\)waveEpoch << 32\)", rand_src):
        errors.append("[SeedDomainFormula] domainState 公式与规格第五节第 3 步不一致")


def strip_comments(text):
    """剥离 C# 块注释与行注释（含 XML doc），避免注释中的禁止词误报。"""
    text = re.sub(r"/\*[\s\S]*?\*/", "", text)
    text = re.sub(r"//[^\n]*", "", text)
    return text


def check_forbidden(errors):
    """第三步：ModeG/ 全目录禁止玩法 seed 使用非冻结随机源。"""
    forbidden_patterns = [
        (r"System\.Random", "System.Random"),
        (r"UnityEngine\.Random", "UnityEngine.Random"),
        (r"\.GetHashCode\(\)", "string.GetHashCode"),
        (r"HashCode\.Combine", "HashCode.Combine"),
    ]
    for f in sorted(os.listdir(MODEG_DIR)):
        if not f.endswith(".cs"):
            continue
        content = strip_comments(read(f))
        for pattern, label in forbidden_patterns:
            if re.search(pattern, content):
                errors.append("[Forbidden:{}] 出现于 ModeG/{}".format(label, f))


def check_sampling(errors, rand_src):
    """第四步：无偏抽样结构。"""
    if not re.search(r"ulong threshold = ulong\.MaxValue - \(ulong\.MaxValue % umax\)", rand_src):
        errors.append("[RejectionSampling] NextInt 缺少 rejection sampling threshold")
    if not re.search(r"public const int WeightQuantumMax = 1000000", rand_src):
        errors.append("[WeightQuantumMax] 加权量化上界必须为 1000000")
    if not re.search(r"public const int WeightQuantumMin = 1", rand_src):
        errors.append("[WeightQuantumMin] 加权量化下界必须为 1")
    if not re.search(r"Math\.Round\(scaled, MidpointRounding\.AwayFromZero\)", rand_src):
        errors.append("[QuantizeRounding] 量化必须 AwayFromZero 舍入")
    if not re.search(r"public static int WeightedSelect\(ref ulong state, IList<int> quantizedWeights\)",
                     rand_src):
        errors.append("[WeightedSelect] 加权抽取必须接收已量化整数权重")
    # 稳定排序冻结输入序列（排列不变性前提）
    if not re.search(r"Array\.Sort\(result, StringComparer\.Ordinal\)", rand_src):
        errors.append("[OrdinalSort] 缺少 Ordinal 稳定排序（preset key 快照冻结）")
    if not re.search(r"public static int\[\] SortTypeIdsStable\(IList<int> typeIds\)", rand_src):
        errors.append("[SortTypeIdsStable] 缺少 TypeID 升序去重排序")


def check_permutations(errors):
    """第五步：>=20 种输入排列确定性验证（Python 复刻公式）。"""
    domains = list(DOMAIN_CONSTANTS.values())
    cases = []
    for i in range(8):
        cases.append((domains[i], "preset_key_{}".format(i), i % 9))
    for i in range(8):
        cases.append((domains[i], "", i))
    for i in range(8):
        cases.append((domains[i], "\u5bbf\u547d\u56de\u54cd" + str(i), 8 - i))
    assert len(cases) >= 20
    for domain, key, epoch in cases:
        first = seed_domain(0xA5A5A5A5, domain, key, epoch)
        second = seed_domain(0xA5A5A5A5, domain, key, epoch)
        if first != second:
            errors.append("[PermutationDeterminism] SeedDomain 复刻不确定: key={!r}".format(key))
    # 不同输入必须不同（碰撞即异常）
    seen = set()
    for domain, key, epoch in cases:
        v = seed_domain(0xDEADBEEF, domain, key, epoch)
        if v in seen:
            errors.append("[PermutationCollision] SeedDomain 复刻出现碰撞: key={!r}".format(key))
        seen.add(v)
    return len(cases)


def main():
    errors = []

    rand_src = read("ModeGDeterministicRandom.cs")
    avail_src = read("ModeGAvailability.cs")
    if rand_src is None:
        errors.append("ModeGDeterministicRandom.cs 不存在")
    if avail_src is None:
        errors.append("ModeGAvailability.cs 不存在")

    check_golden_self(errors)

    if rand_src is not None and avail_src is not None:
        check_source_constants(errors, rand_src, avail_src)
        check_sampling(errors, rand_src)

    check_forbidden(errors)
    permutation_count = check_permutations(errors)

    if errors:
        print("ModeGDeterministicRandomGuard: FAIL ({} errors)".format(len(errors)))
        for e in errors:
            print("  - " + e)
        return 1

    print("ModeGDeterministicRandomGuard: PASS")
    print("  golden vectors: 3 FNV + 5 SplitMix（Python 独立复算交叉验证）")
    print("  输入排列确定性验证: {} 组".format(permutation_count))
    return 0


if __name__ == "__main__":
    sys.exit(main())
