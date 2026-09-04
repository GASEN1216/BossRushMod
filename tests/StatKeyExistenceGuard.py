#!/usr/bin/env python3
"""
StatKeyExistenceGuard — 角色 stat key 存在性守卫（AGENTS §14）。

不变式：
- `ZombieModeStatNames` 里每个常量的**字面值**都必须能在官方反编译源码
  `鸭科夫源码/` 里找到同名字符串；找不到就说明这个 stat 不存在，
  挂上去会被 `RuntimeStatModifierTracker` 当缺失 stat 静默丢弃；
- 已知不存在的 key（`ReloadSpeedMultiplier`）不得复活；
- `"MoveSpeed"` 是 Animator 参数名不是角色 stat：只有在同一处同时挂了
  `WalkSpeed` 与 `RunSpeed` 兜底时才允许出现（`ApplyZombieModePlayerAttributeModifiers`
  的扇出写法），单独挂一律判红。

历史：焚心椒「换弹更利索」与丧尸模式「换弹速度」奖励都挂在了
`"ReloadSpeedMultiplier"` 上，官方源码零命中，两处效果完全不存在。
既有先例见 `tests/ModeFBloodfireOverloadGuard.py:128-142`（防同一个坑）。
"""
import os
import re
import sys

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

GUARD = "StatKeyExistenceGuard"

TUNING = os.path.join(REPO_ROOT, "ZombieMode", "ZombieModeTuning.cs")
OFFICIAL_DIR = os.path.join(REPO_ROOT, "鸭科夫源码")

# 官方源码零命中、且历史上真的坑过人的 key。复活即红。
KNOWN_PHANTOM_KEYS = ["ReloadSpeedMultiplier"]

# "MoveSpeed" 确实出现在官方源码里（Animator 参数名），存在性检查抓不到它，
# 单列一条专项断言。
ANIMATOR_ONLY_KEYS = ["MoveSpeed"]


def read_text(path):
    with open(path, "r", encoding="utf-8") as handle:
        return handle.read()


def strip_cs_comments(code):
    code = re.sub(r"/\*.*?\*/", "", code, flags=re.S)
    code = re.sub(r"//[^\n]*", "", code)
    return code


def collect_official_text():
    """把官方反编译源码拼成一大坨用于存在性检索。"""
    chunks = []
    for root, _dirs, files in os.walk(OFFICIAL_DIR):
        for name in files:
            if not name.endswith(".cs"):
                continue
            try:
                with open(os.path.join(root, name), "r", encoding="utf-8", errors="ignore") as handle:
                    chunks.append(handle.read())
            except OSError:
                continue
    return "\n".join(chunks)


def parse_stat_names(code):
    """解析 ZombieModeStatNames 里的 `public const string X = "Y";`。"""
    block = re.search(
        r"class\s+ZombieModeStatNames\s*\{(.*?)\n\s*\}", code, flags=re.S)
    if block is None:
        return None
    pairs = re.findall(
        r"public\s+const\s+string\s+(\w+)\s*=\s*\"([^\"]+)\"\s*;", block.group(1))
    return pairs


def check_stat_names_exist(errors):
    if not os.path.isdir(OFFICIAL_DIR):
        # 官方源码是 local-only 参考资料，缺失时不判红，只跳过存在性检查。
        print(GUARD + ": 跳过存在性检查（未找到 鸭科夫源码/）")
        return None

    code = strip_cs_comments(read_text(TUNING))
    pairs = parse_stat_names(code)
    if pairs is None:
        errors.append("[解析] 未找到 ZombieModeStatNames 类体")
        return None
    if not pairs:
        errors.append("[解析] ZombieModeStatNames 里没有解析到任何 const string")
        return None

    official = collect_official_text()
    for name, value in pairs:
        if '"' + value + '"' not in official:
            errors.append(
                "[不存在] ZombieModeStatNames.{} = \"{}\" 在官方源码里零命中；"
                "挂 Modifier 会被静默丢弃（AGENTS §14）".format(name, value))
    return pairs


def check_phantom_keys_absent(errors):
    """已知幽灵 key 不得在全仓任何 .cs 里作为 stat 字面量复活。"""
    skip_dirs = ("鸭科夫源码", ".codex_tmp", "Build", ".git", "tests")
    for root, dirs, files in os.walk(REPO_ROOT):
        dirs[:] = [d for d in dirs if d not in skip_dirs]
        for name in files:
            if not name.endswith(".cs"):
                continue
            path = os.path.join(root, name)
            rel = os.path.relpath(path, REPO_ROOT)
            try:
                code = strip_cs_comments(read_text(path))
            except (OSError, UnicodeDecodeError):
                continue
            for phantom in KNOWN_PHANTOM_KEYS:
                if '"' + phantom + '"' in code:
                    errors.append(
                        "[幽灵] {} 出现字面量 \"{}\"，该 stat 官方不存在，"
                        "换弹请用 ReloadSpeedGain".format(rel, phantom))


# 会把 stat 名传给 Stat/Modifier 管线的调用点。只有这些位置上的 "MoveSpeed"
# 才是缺陷；`Animator.StringToHash("MoveSpeed")` / `animator.SetFloat(...)`
# 是**正确**用法（那本来就是 Animator 参数名），不得误报。
STAT_MODIFIER_CALLS = (
    "AddModifier",
    "TryAdd",
    "AddStatModifier",
    "TryAddVerifiedStatModifier",
    "AddZombieModeAttributeModifier",
    "TryAddZombieModeOptionModifier",
    "ApplyZombieModeAttributeReward",
    "GetStat",
)


def check_move_speed_has_fallback(errors):
    """
    只在**挂 Modifier 的调用点**上判 MoveSpeed。
    合法写法只有扇出兜底一种：同一文件里 MoveSpeed 与 WalkSpeed、RunSpeed 一起挂
    （见 ZombieModeRewardEffectsAndNpc.ApplyZombieModePlayerAttributeModifiers）。
    """
    skip_dirs = ("鸭科夫源码", ".codex_tmp", "Build", ".git", "tests")
    for root, dirs, files in os.walk(REPO_ROOT):
        dirs[:] = [d for d in dirs if d not in skip_dirs]
        for name in files:
            if not name.endswith(".cs"):
                continue
            path = os.path.join(root, name)
            rel = os.path.relpath(path, REPO_ROOT)
            try:
                code = strip_cs_comments(read_text(path))
            except (OSError, UnicodeDecodeError):
                continue

            for key in ANIMATOR_ONLY_KEYS:
                offenders = []
                for line in code.splitlines():
                    if ("StatNames." + key) not in line and ('"' + key + '"') not in line:
                        continue
                    if "Animator" in line or "SetFloat" in line or "StringToHash" in line:
                        continue
                    if not any(call in line for call in STAT_MODIFIER_CALLS):
                        continue
                    offenders.append(line.strip())
                if not offenders:
                    continue
                if "WalkSpeed" in code and "RunSpeed" in code:
                    continue
                errors.append(
                    "[Animator] {} 把 {} 挂给了 Stat Modifier 但没有 WalkSpeed/RunSpeed 兜底；"
                    "\"MoveSpeed\" 是 Animator 参数名，不是角色 stat：{}".format(
                        rel, key, offenders[0]))


def main():
    errors = []
    check_stat_names_exist(errors)
    check_phantom_keys_absent(errors)
    check_move_speed_has_fallback(errors)

    if errors:
        print("{}: FAIL ({} errors)".format(GUARD, len(errors)))
        for line in errors:
            print("  - " + line)
        return 1
    print(GUARD + ": PASS")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
