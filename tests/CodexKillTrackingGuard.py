"""Guard: 鸭皇图鉴击杀采集器（CodexKillCollector）的热路径与生命周期不变式。

背景：`Health.OnDead` / `Health.OnHurt` 是官方**静态多播**事件，会被每一次敌人
受伤与死亡触达（一局丧尸潮就是几千次）。这条路径上任何一次日志、字符串拼接、
落盘或漏清的集合，代价都是全局性的：卡顿只在长局末期显现，泄漏只在切图后显现，
两者人工冒烟都抓不到。

守卫内容：
  1. 实例去重集与最快击杀计时表必须存**实例 ID**（int），不存对象引用——
     存引用会把已销毁的角色钉在内存里，且相等比较要走 Unity 重载的 ==。
  2. 两张表必须有 run 级清空入口，且切场景真的调用到（CodexRuntimeModule.OnSceneLoaded）；
     切档 / 删档 / 宿主销毁三条路径同样要有幂等清理。
  3. 采集器内**严禁**任何物理落盘：SaveFile / SaveGlobal 一律只能在
     CodexSaveCoordinator，采集器只调 CodexPersistence.Store() 入队。
  4. 两个 handler 的热路径零日志零字符串拼接（AGENTS.md 4.7：每帧热路径不加噪声日志）。
  5. 两个 handler 的第一条语句必须是开关早返（AGENTS.md 4.12：未使用状态零成本）。
  6. 丧尸 marker 的 GetComponent 必须被 IsZombieModeActive 门控（AGENTS.md 4.12），
     否则每一次普通击杀都白付一次组件查找。
  7. 过滤序必须保留全部身份闸：玩家自己、非玩家击杀、遗种巢随从、友军、基地场景。
  8. Integration/Codex/ 下零新增 Harmony patch 与零新增反射绑定策略。
  9. Health 的两个静态事件由 Utilities/PlayerLifecycleRuntimeHooks.cs 统一成对
     注册/退订（AGENTS.md 4.6，命名方法、禁 lambda）。接线由主控完成，因此这里做的是
     **全有或全无**断言：一条都没接时只告警，接了一半就是真泄漏。
"""

from pathlib import Path
import re
import sys


CODEX_DIR = Path("Integration/Codex")
COLLECTOR = CODEX_DIR / "CodexKillCollector.cs"
RUNTIME_MODULE = CODEX_DIR / "CodexRuntimeModule.cs"
CATALOG = CODEX_DIR / "CodexBossCatalog.cs"
HOOKS = Path("Utilities/PlayerLifecycleRuntimeHooks.cs")

HOT_HANDLERS = ("OnGlobalDead", "OnGlobalHurt")

# 采集器过滤序里不可删除的身份闸：删掉任何一条都会把不该进图鉴的东西记进去
REQUIRED_FILTERS = (
    ("target.IsMainCharacterHealth", "必须排除玩家自己的死亡"),
    ("info.fromCharacter", "必须只记玩家亲手击杀（环境伤害/随从击杀不算）"),
    ("IsMainCharacter", "必须校验击杀者是主角"),
    ("PetNestCompanionAgent.IsCompanionHealth", "必须排除遗种巢随从"),
    ("Teams.player", "必须排除友军（宠物 / 雇佣兵 / 临时同伴）"),
    ("IsBaseLevelSafe", "必须排除基地场景（靶子与演示角色不进战绩）"),
    ("isBossCharacter", "必须只记 Boss，杂兵不得进图鉴"),
    # owner 2026-09-03 定：Mode H 是观战模式，其击杀不计入图鉴。
    # 这条不能靠 fromCharacter 判定兜住——ERROR 完整互换期间官方会把击杀来源
    # 改写成主角，那一次击杀会伪装成"玩家亲手击杀"混进来。
    ("IsModeHRunInProgressSafe", "必须排除 Mode H（ERROR 互换期间归属被改写成主角）"),
)


def fail(message):
    print("CodexKillTrackingGuard: FAIL - " + message)
    return 1


def warn(message):
    print("CodexKillTrackingGuard: WARN - " + message)


def strip_comments(text):
    """去掉 // 行注释与 /* */ 块注释。

    采集器的注释里成段写着「这里不做字符串拼接、不 DevLog」，不剥注释
    就会把这些解释本身误判成违规。
    """
    text = re.sub(r"/\*.*?\*/", "", text, flags=re.S)
    text = re.sub(r"//[^\n]*", "", text)
    return text


def extract_method_body(code, name):
    """抓 `name(` 之后第一对配平大括号里的内容。code 必须是已剥注释的源码。"""
    match = re.search(r"\b" + re.escape(name) + r"\s*\([^)]*\)\s*\{", code)
    if not match:
        return None

    start = code.index("{", match.end() - 1)
    depth = 0
    for pos in range(start, len(code)):
        char = code[pos]
        if char == "{":
            depth += 1
        elif char == "}":
            depth -= 1
            if depth == 0:
                return code[start + 1:pos]
    return None


def main():
    for path in (COLLECTOR, RUNTIME_MODULE, CATALOG, HOOKS):
        if not path.exists():
            return fail("缺少源文件 " + path.as_posix())

    collector_raw = COLLECTOR.read_text(encoding="utf-8")
    collector = strip_comments(collector_raw)
    module = strip_comments(RUNTIME_MODULE.read_text(encoding="utf-8"))

    # ---- 1) 去重集与计时表存实例 ID，不存对象引用 ----
    if not re.search(r"HashSet<int>\s+_countedInstances", collector):
        return fail(
            "实例去重集必须是 HashSet<int>（CharacterMainControl.GetInstanceID）："
            "存对象引用会把已销毁角色钉在内存里，相等比较还要走 Unity 重载的 ==")
    if not re.search(r"Dictionary<int,\s*float>\s+_fightStart", collector):
        return fail("最快击杀计时表必须是 Dictionary<int, float>（Health.GetInstanceID -> Time.time）")
    if re.search(r"HashSet<\s*(CharacterMainControl|Health)\s*>", collector):
        return fail("去重集不得以 Unity 对象为键")
    if "GetInstanceID()" not in collector:
        return fail("去重与计时必须用 GetInstanceID() 取键")

    # 计时表必须有容量上限，否则长局内会无界增长
    if "CodexTuning.MaxFightStartTracked" not in collector:
        return fail("计时表必须有容量上限 CodexTuning.MaxFightStartTracked（防泄漏）")

    # ---- 2) run 级清空入口存在，且切场景真的调用到 ----
    clear_body = extract_method_body(collector, "ClearRunScoped")
    if clear_body is None:
        return fail("采集器必须提供 run 级清空入口 ClearRunScoped()")
    for field in ("_fightStart.Clear()", "_countedInstances.Clear()"):
        if field not in clear_body:
            return fail("ClearRunScoped 必须清空 " + field)

    for entry in ("NotifySceneChanged", "NotifySlotChanged", "ResetStaticCaches"):
        if extract_method_body(collector, entry) is None:
            return fail("采集器必须提供清理入口 " + entry + "()")

    scene_loaded = extract_method_body(module, "OnSceneLoaded")
    if scene_loaded is None:
        return fail("CodexRuntimeModule 必须实现 OnSceneLoaded")
    if not re.search(r"CodexKillCollector\.(ClearRunScoped|NotifySceneChanged)\(\)", scene_loaded):
        return fail(
            "切场景必须清掉采集器的 run 级状态（上一张图的实例 ID 全部作废），"
            "否则去重集与计时表会跨图累积")

    # ---- 3) 采集器内严禁物理落盘 ----
    for forbidden, message in (
        ("SaveFile(", "采集器不得物理落盘：落盘唯一入口是 CodexSaveCoordinator"),
        ("SaveGlobal(", "图鉴是跟槽位的收藏进度，不得写全局存档"),
        ("ES3.", "采集器不得直接触碰 ES3"),
    ):
        if forbidden in collector:
            return fail(message + " -> " + forbidden)
    if "CodexPersistence.Store(" not in collector:
        return fail("采集器必须通过 CodexPersistence.Store() 入队（战斗帧只入队不写盘）")

    # ---- 4/5) 热路径：零日志、零字符串拼接、开关早返在第一条 ----
    for handler in HOT_HANDLERS:
        body = extract_method_body(collector, handler)
        if body is None:
            return fail("采集器必须提供静态 handler " + handler)

        for noisy in ("DevLog", "Debug.Log", "CriticalLog", "Debug.LogWarning"):
            if noisy in body:
                return fail(
                    handler + " 是每次受伤/死亡都会触达的热路径，不得出现 " + noisy
                    + "（AGENTS.md 4.7）")

        if re.search(r'\+\s*"', body) or re.search(r'"\s*\+', body):
            return fail(handler + " 热路径不得做字符串拼接（每次击杀都要付一次分配）")

        if "string.Format" in body or "ToString()" in body:
            return fail(handler + " 热路径不得做字符串格式化 / 装箱")

        first_statement = body.strip().split("\n", 1)[0].strip()
        if first_statement != "if (!IsActive()) return;":
            return fail(
                handler + " 的第一条语句必须是开关早返 `if (!IsActive()) return;`"
                "（AGENTS.md 4.12：未使用状态零成本），当前是: " + first_statement)

    hurt_body = extract_method_body(collector, "OnGlobalHurt")
    if "target.IsDead" not in hurt_body:
        return fail(
            "OnGlobalHurt 必须早返已死目标：官方致命一击先派发 OnDead 再派发 OnHurt "
            "且 isDead 已置位，不挡住就会给死人重新开计时，造成计时表泄漏")

    # ---- 6) 丧尸 marker 的 GetComponent 必须被模式门控 ----
    resolve_body = extract_method_body(collector, "ResolveBossKey")
    if resolve_body is None:
        return fail("采集器必须提供身份归属 ResolveBossKey")
    if "GetComponent<ZombieModeEnemyRuntimeMarker>()" in resolve_body:
        gate = resolve_body.index("IsZombieModeActive") if "IsZombieModeActive" in resolve_body else -1
        component = resolve_body.index("GetComponent<ZombieModeEnemyRuntimeMarker>()")
        if gate < 0 or gate > component:
            return fail(
                "丧尸 marker 的 GetComponent 必须被 IsZombieModeActive 门控且门控在前"
                "（AGENTS.md 4.12），否则每一次普通击杀都白付一次组件查找")

    # ---- 7) 过滤序的身份闸一条都不能少 ----
    dead_body = extract_method_body(collector, "OnGlobalDead")
    scope = dead_body + "\n" + resolve_body
    for needle, message in REQUIRED_FILTERS:
        if needle not in scope:
            return fail(message + " -> 缺少 " + needle)

    # ---- 8) 零新增 Harmony patch / 零新增反射绑定策略 ----
    for path in sorted(CODEX_DIR.glob("*.cs")):
        text = strip_comments(path.read_text(encoding="utf-8"))
        if "[HarmonyPatch" in text or "HarmonyPatch(" in text:
            return fail(path.as_posix() + " 不得新增 Harmony patch（图鉴只消费既有静态事件）")
        if "AccessTools." in text:
            return fail(path.as_posix() + " 不得新增 AccessTools 反射绑定策略")

    # ---- 9) Health 静态事件由全局 hooks 成对注册 ----
    hooks = HOOKS.read_text(encoding="utf-8")
    wired = 0
    for handler in ("CodexKillCollector.OnGlobalDead", "CodexKillCollector.OnGlobalHurt"):
        adds = hooks.count("+= " + handler)
        removes = hooks.count("-= " + handler)
        if adds == 0 and removes == 0:
            continue
        wired += 1
        if adds != 1 or removes != 1:
            return fail(
                "PlayerLifecycleRuntimeHooks 中 " + handler
                + " 必须成对订阅/退订各一次（订阅 " + str(adds)
                + " 处，退订 " + str(removes) + " 处）")
    if wired == 1:
        return fail(
            "CodexKillCollector 的两个 handler 只接了一个：OnHurt 负责开计时、"
            "OnDead 负责结算并清表，只接一半会让计时表只进不出")
    if wired == 0:
        warn(
            "Utilities/PlayerLifecycleRuntimeHooks.cs 尚未接入 CodexKillCollector 的两个 "
            "handler（等待主控接线）。接线后本 guard 会自动转为强制断言。")

    print("CodexKillTrackingGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
