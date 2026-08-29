#!/usr/bin/env python3
"""
ModeHReachabilityGuard — Mode H 可达性守卫。

**这条 guard 的存在本身是一次事故的产物。**

2026-08-29 的审查发现：Mode H 有 55 个源文件、29 条结构 guard 全绿、编译通过、
契约文档写着「已一次性完整实现」，但整个模式在游戏里根本进不去——
约 25/55 个文件是不可达代码。原因是既有 guard 全部只断言「构件内部长什么样」
（字段冻结、转换表、调用顺序、命名），没有任何一条断言「这个构件有人调用」。
于是入口零调用方、四个 partial 方法只有声明没有实现体（被 C# 编译器静默删除）、
存档写入接口从未被调用、地图数据从未落盘，全部安然穿过了 29 条 guard。

因此本 guard 只做一件事：**断言关键接缝有调用方**。它不关心实现得对不对，
那是别的 guard 的职责；它只回答「这段代码到底会不会被执行」。
"""
import os
import re
import sys

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
MODEH_DIR = os.path.join(REPO_ROOT, "ModeH")
SPAWNPOINTS_DIR = os.path.join(REPO_ROOT, "Assets", "SpawnPoints")

EXCLUDED_DIRS = {".git", ".codex_tmp", "Build", "tests", "wiki-site", "鸭科夫源码", "docs"}

GUARD = "ModeHReachabilityGuard"

# 四个 partial 接入点。只有声明没有实现体时，C# 会把方法连同**所有调用点**一起删掉，
# 编译不报错、guard 不报错，运行时什么都不会发生。
PARTIAL_HOOKS = [
    "OnSceneLoadedInternal",
    "OnUpdateInternal",
    "OnTransitionApplied",
    "ShutdownRuntimeInternal",
]

MODEH_MAP_FIELDS = [
    "modeHSpawnPoints",
    "modeHStagingPos",
    "modeHSpectatorPos",
    "modeHPlayerSpawnPos",
    "modeHExitPos",
]


def strip_cs_comments(code):
    code = re.sub(r"/\*[\s\S]*?\*/", "", code)
    code = re.sub(r"//[^\n]*", "", code)
    return code


def read_text(path):
    try:
        with open(path, "r", encoding="utf-8", errors="replace") as fh:
            return fh.read()
    except IOError:
        return None


def iter_production_cs():
    """遍历生产源码（排除 Build/tests/反编译源码等非产物目录）。"""
    for root, dirs, files in os.walk(REPO_ROOT):
        dirs[:] = [d for d in dirs if d not in EXCLUDED_DIRS]
        for name in files:
            if not name.endswith(".cs"):
                continue
            path = os.path.join(root, name)
            rel = os.path.relpath(path, REPO_ROOT).replace("\\", "/")
            text = read_text(path)
            if text is None:
                continue
            yield rel, strip_cs_comments(text)


def check_partial_hooks(errors, sources):
    """四个 partial 方法必须各有恰好一个带方法体的实现。"""
    for hook in PARTIAL_HOOKS:
        declarations = 0
        implementations = 0
        for rel, code in sources:
            if not rel.startswith("ModeH/"):
                continue
            # 声明形态：`partial void X(...);`
            declarations += len(re.findall(
                r"partial void " + hook + r"\([^)]*\)\s*;", code))
            # 实现形态：`partial void X(...) {`
            implementations += len(re.findall(
                r"partial void " + hook + r"\([^)]*\)\s*\{", code))

        if declarations == 0:
            errors.append("[Partial] 缺少 partial 声明: " + hook)
        if implementations == 0:
            errors.append(
                "[Partial] {} 只有声明没有实现体 —— C# 会静默删掉它和它的全部调用点，"
                "整段编排在运行时不会执行".format(hook))
        elif implementations > 1:
            errors.append("[Partial] {} 出现 {} 个实现体（C# 只允许一个）".format(
                hook, implementations))


def check_callers(errors, sources, symbol, owner_prefixes, label):
    """
    断言 symbol 在 owner 文件之外存在调用点。
    owner_prefixes 是「定义它的文件」，自引用不算调用方。
    """
    for rel, code in sources:
        if any(rel.startswith(p) or rel == p for p in owner_prefixes):
            continue
        if symbol in code:
            return
    errors.append("[Unreachable] {}：{} 在定义文件之外没有任何调用方".format(label, symbol))


def check_entry_reachable(errors, sources):
    """
    入口必须能被玩家触达。挂载与程序化调用二选一，都没有就是「模式进不去」。
    """
    mounted = False
    programmatic = False
    for rel, code in sources:
        if rel.startswith("ModeH/"):
            continue
        if re.search(r"AddComponent<\s*ModeHInteractable\s*>", code):
            mounted = True
        if "ModeHInteractable.TryOpenEntry" in code:
            programmatic = True

    if not mounted and not programmatic:
        errors.append(
            "[Unreachable] 入口：ModeH/ 之外既没有 AddComponent<ModeHInteractable>()，"
            "也没有 ModeHInteractable.TryOpenEntry 调用 —— 玩家在游戏里找不到这个模式")


def check_map_data(errors):
    """
    声明支持 Mode H 的地图必须五点位齐全。
    只要有任何一张图带了 modeHStagingPos，就说明作者打算支持它——
    缺字段会让 ModeHMapSupportRegistry 静默拒绝该图，入口永远报 map_unsupported。
    """
    if not os.path.isdir(SPAWNPOINTS_DIR):
        errors.append("[Map] 缺少 Assets/SpawnPoints 目录")
        return

    declared = 0
    for name in sorted(os.listdir(SPAWNPOINTS_DIR)):
        if not name.endswith(".json"):
            continue
        text = read_text(os.path.join(SPAWNPOINTS_DIR, name))
        if text is None or "modeH" not in text:
            continue
        declared += 1
        for field in MODEH_MAP_FIELDS:
            if '"' + field + '"' not in text:
                errors.append("[Map] {} 声明了 Mode H 支持但缺字段 {}".format(name, field))

    if declared == 0:
        errors.append(
            "[Map] 没有任何地图带 Mode H 五点位 —— SupportedMapCount 恒为 0，"
            "入口即便接通也会被 map_unsupported 拒绝")


def main():
    errors = []
    sources = list(iter_production_cs())

    check_partial_hooks(errors, sources)

    # 入口可达性：玩家必须能在游戏里打开这个模式。
    # 两条合法路径任选其一即可：
    #   a) 把 ModeHInteractable 挂进某个交互组（船坞/建筑），由 OnTimeOut 触发；
    #   b) 由外部代码程序化调用 TryOpenEntry（自建短命 presenter）。
    # 两条都没有 = 模式在游戏里根本进不去，这正是本 guard 诞生的那次事故。
    check_entry_reachable(errors, sources)

    # 存档：没有调用方 = 赛季永远写不下去，恢复流程永远读到 null
    check_callers(errors, sources, "RequestSeasonWrite",
                  ["ModeH/ModeHSaveFlushCoordinator.cs"], "赛季落盘")

    # 入场意图：没有消费方 = 冻结了却没人认，切图后不会开局
    check_callers(errors, sources, "TryMatchModeHSceneIntent",
                  ["MapSelection/BossRushMapSelectionHelper.cs"], "入场意图消费")

    # 恢复壳：没有实例化 = 玩家遇到挂起状态时看不到任何东西
    check_callers(errors, sources, "ModeHRecoveryPanel",
                  ["ModeH/ModeHRecoveryPanel.cs"], "恢复壳")

    check_map_data(errors)

    if errors:
        print("{}: FAIL ({} errors)".format(GUARD, len(errors)))
        for e in errors:
            print("  - " + e)
        return 1

    print("{}: PASS".format(GUARD))
    return 0


if __name__ == "__main__":
    sys.exit(main())
