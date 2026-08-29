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


def parse_frozen_transitions():
    """
    从 ModeHStateMachine.BuildTransitions 解析冻结转换表。
    只取 BuildTransitions 段——BuildEarlyRecoveryTargets 用的是同样的 map[...] 写法。
    """
    path = os.path.join(MODEH_DIR, "ModeHStateMachine.cs")
    text = read_text(path)
    if text is None:
        return None
    code = strip_cs_comments(text)
    # 字段初始化 `= BuildTransitions();` 出现在方法定义**之前**，所以取最后一次出现；
    # 再在下一个 BuildEarlyRecoveryTargets 处截断，避免把早期恢复子表混进来。
    start = code.rfind("BuildTransitions()")
    if start < 0:
        return None
    head = code[start:]
    cut = head.find("BuildEarlyRecoveryTargets")
    if cut > 0:
        head = head[:cut]

    table = {}
    for m in re.finditer(
            r"map\[ModeHLifecycle\.(\w+)\]\s*=\s*new ModeHLifecycle\[\]\s*\{([^}]*)\}", head):
        src = m.group(1)
        targets = set(re.findall(r"ModeHLifecycle\.(\w+)", m.group(2)))
        table[src] = targets
    return table


def collect_transition_call_sites(sources):
    """
    收集运行时对状态机门面的调用点。
    返回 (literal_pairs, entered_states, exited_states)：
      - literal_pairs: 两端都是字面量的 (from, to, 文件, 行号)
      - entered_states: 出现在第二参数位置的字面量目标
      - exited_states: 出现在第一参数位置的字面量源
    """
    literal_pairs = []
    entered = set()
    exited = set()

    # 两个参数各自可能是字面量也可能是变量（DriveRecovery 的目标就是变量），
    # 因此两端分别判定，不要求同时是字面量。
    call_re = re.compile(r"TryTransition\(\s*([^,]+?)\s*,\s*([^,]+?)\s*,")
    literal_re = re.compile(r"^ModeHLifecycle\.(\w+)$")

    for rel, code in sources:
        if not rel.startswith("ModeH/"):
            continue
        for line_no, line in enumerate(code.splitlines(), start=1):
            for m in call_re.finditer(line):
                src_literal = literal_re.match(m.group(1).strip())
                dst_literal = literal_re.match(m.group(2).strip())
                if src_literal:
                    exited.add(src_literal.group(1))
                if dst_literal:
                    entered.add(dst_literal.group(1))
                if src_literal and dst_literal:
                    literal_pairs.append(
                        (src_literal.group(1), dst_literal.group(1), rel, line_no))
    return literal_pairs, entered, exited


def check_transition_call_sites_legal(errors, sources, table):
    """
    每个两端都写死的 TryTransition 调用点，其 (from, to) 必须在冻结转换表内。

    2026-08-29 第二次事故：批 2 的新代码按**错记的**转换表写成
    `LoadoutEditing -> LoadoutLocked` 和 `MatchSpawning -> MatchBrief`，
    两条边都不在冻结表里。运行时只打一行 DevLog 就静默返回，
    玩家点锁盘毫无反应、被困在时停页里；编译与全部 33 条 guard 依然全绿。
    """
    if not table:
        errors.append("[StateMachine] 无法解析 ModeHStateMachine 冻结转换表")
        return

    literal_pairs, _, _ = collect_transition_call_sites(sources)
    for src, dst, rel, line_no in literal_pairs:
        if src not in table:
            errors.append("[StateMachine] {}:{} 的源状态 {} 不在冻结表内".format(
                rel, line_no, src))
            continue
        if dst not in table[src]:
            errors.append(
                "[StateMachine] {}:{} 请求了非法转换 {} -> {} —— 冻结表不放行，"
                "该调用在运行时必然被拒绝并静默返回".format(rel, line_no, src, dst))


def check_no_dead_states(errors, sources, table):
    """
    恢复通道的三个状态一旦被进入，就必须有人以它为源把它推出去。

    2026-08-29 第二次事故的另一半：所有技术故障出口都转进 Recovering，
    但全仓没有任何调用点以 Recovering 为源发起转换——玩家进去就出不来，
    面对一个没有按钮的恢复壳只能 ESC 离场。

    为什么只查这三个状态：普通玩法状态可以由 RequestRecovering / RequestSuspended
    这类**动态源**门面（`TryTransition(_runState.Lifecycle, ...)`）退出，静态上无法
    归属到具体源状态，硬查会误报。而恢复通道本身是所有故障路径的终点，
    它的出口只能写死源状态，正好可以静态断言——这也正是出事的那条不变式。
    """
    if not table:
        return

    _, entered, exited = collect_transition_call_sites(sources)

    recovery_channel = ["Recovering", "ErrorRecoveryPending", "Suspended"]

    # ErrorRecoveryPending 的唯一入口 RequestErrorRecoveryPending 目前零调用
    # （战斗接线时才会用到），因此它还不是「活的死态」。一旦它有了调用方，
    # 这条豁免自动失效，必须同时补出口。
    exempt = set()
    if not has_caller(sources, "RequestErrorRecoveryPending"):
        exempt.add("ErrorRecoveryPending")

    for state in recovery_channel:
        if state not in entered or state in exempt:
            continue
        if state not in exited:
            errors.append(
                "[StateMachine] {} 是死态：有调用点把状态转进它，"
                "却没有任何调用点以它为源转出去".format(state))


def has_caller(sources, method_name):
    """该方法在定义之外是否有调用点（定义形如 `void Name(` 或 `bool Name(`）。"""
    call_re = re.compile(r"(?<![\w.])" + method_name + r"\s*\(")
    define_re = re.compile(r"\b(void|bool|int|string)\s+" + method_name + r"\s*\(")
    for rel, code in sources:
        for line in code.splitlines():
            if define_re.search(line):
                continue
            if call_re.search(line):
                return True
    return False


def main():
    errors = []
    sources = list(iter_production_cs())

    check_partial_hooks(errors, sources)

    # 状态机接缝：光有调用方还不够，调用还得能成功。
    # 这两条专治「调用点存在但冻结表不放行」和「进得去出不来」。
    table = parse_frozen_transitions()
    check_transition_call_sites_legal(errors, sources, table)
    check_no_dead_states(errors, sources, table)

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
