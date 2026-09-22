# -*- coding: utf-8 -*-
"""官方任务投影核心守卫：`Utilities/OfficialQuests/` 是全仓库唯一一份「Mod 事实为权威、官方 Quest 只做投影」的实现。

2026-09-22 由天空岛桥（`SkyIslandOfficialQuestBridge`）原样抽出，天空岛与鸭王征程各作为一个客户端登记定义。
原本钉在 `tests/SkyIslandPreludeGuard.py` 上的「桥」断言逐条搬到这里（路径与类名改成核心），并加严：
- 唯一活动实例：`new OfficialQuestProjection(` 全仓库只在 `OfficialQuestRuntimeModule` 出现一次，
  且核心模块必须先于天空岛与征程注册（host 按注册顺序回调 OnAwake，两者在 OnAwake 里登记定义）。
- 四个 Harmony 目标（MeetsPrerequisit / TryComplete / GenerateSaveData / SetupSaveData）各只装一次，都在核心。
- 核心 Tick 只有一个调用点，在核心模块的 OnUpdate 里，前面没有任何早退门（旧写法「排在岛上会话早退之后」会让岛上任务根本不跑）。
- 给予者的全局扫描只在 `OfficialQuestGiverLocator` 一处；头顶标记的反射刷新也只在那里。
- 所有权 = ID + 对象名 + 专用 Task 组件；每条目各自 fail-closed；四类官方快照逐 id 剥离；模板不得停用；
  奖励不用官方 QuestReward_Money；读档重建先无声采样；换槽先整清再重建；事实未就绪不清不建。
反向探针在内存里恢复旧写法，确保每条断言真的抓得住。
"""
import re
from pathlib import Path

from cs_source_util import clean_source

ROOT = Path(__file__).resolve().parents[1]
CORE_DIR = "Utilities/OfficialQuests/"
BINDING = CORE_DIR + "OfficialQuestBinding.cs"
CORE = CORE_DIR + "OfficialQuestProjection.cs"
COMPONENTS = CORE_DIR + "OfficialQuestComponents.cs"
LOCATOR = CORE_DIR + "OfficialQuestGiverLocator.cs"
MODULE = CORE_DIR + "OfficialQuestRuntimeModule.cs"
REGISTRATION = "Common/Lifecycle/BossRushRuntimeModuleRegistration.cs"
SKY_BRIDGE = "DebugAndTools/SkyIsland/SkyIslandOfficialQuestBridge.cs"
SKY_GIVERS = "DebugAndTools/SkyIsland/SkyIslandOfficialQuestGivers.cs"
SKY_PRELUDE = "DebugAndTools/SkyIsland/SkyIslandPreludeFlow.cs"
COMPILE = "compile_official.bat"
PATHS = (BINDING, CORE, COMPONENTS, LOCATOR, MODULE, REGISTRATION, SKY_BRIDGE, SKY_GIVERS, SKY_PRELUDE, COMPILE)

HARMONY_TARGETS = (
    "HarmonyPatch(typeof(Quest), nameof(Quest.MeetsPrerequisit))",
    "HarmonyPatch(typeof(Quest), nameof(Quest.TryComplete))",
    "HarmonyPatch(typeof(QuestManager), nameof(QuestManager.GenerateSaveData))",
    "HarmonyPatch(typeof(QuestManager), nameof(QuestManager.SetupSaveData)",
)
EXCLUDED_DIRS = ("Build", "tmp", "鸭科夫源码", "tests", ".git", "node_modules", "wiki-site", ".qoder", "docs", "skills", "codex-skills",
                 ".codex_tmp", ".claude", ".kiro", "ArtSource", "Assets")


def production_sources():
    """全仓库生产 .cs（排除构建产物、临时目录、官方源、测试夹具），**已去注释**：整棵树只清理一次。"""
    result = {}
    for path in ROOT.rglob("*.cs"):
        rel = path.relative_to(ROOT).as_posix()
        if rel.split("/", 1)[0] in EXCLUDED_DIRS:
            continue
        result[rel] = clean_source(path.read_text(encoding="utf-8-sig", errors="replace").replace("\r\n", "\n"))
    return result


def check(sources, tree):
    errors = []

    def require(condition, message):
        if not condition:
            errors.append(message)

    def ordered(text, first, second, message):
        a = text.find(first)
        b = text.find(second)
        require(a >= 0 and b >= 0 and a < b, message)

    binding = clean_source(sources[BINDING])
    core = clean_source(sources[CORE])
    components = clean_source(sources[COMPONENTS])
    locator = clean_source(sources[LOCATOR])
    module = clean_source(sources[MODULE])
    registration = clean_source(sources[REGISTRATION])
    sky_bridge = clean_source(sources[SKY_BRIDGE])
    sky_givers = clean_source(sources[SKY_GIVERS])
    sky_prelude = clean_source(sources[SKY_PRELUDE])

    # ---- 1) 定义面与剧情事实无关 ----
    for banned in ("using UnityEngine", "using Duckov", "SkyIsland", "Campaign"):
        require(banned not in binding, BINDING + " 出现了 " + banned + "：定义类型必须与任何子系统和 Unity 无关")
    for token in ("Func<bool> CanOffer", "Func<bool> CanDeliver", "Func<bool> IsAccepted", "Func<bool> IsDelivered",
                  "OfficialQuestCommit Accept", "OfficialQuestCommit Deliver", "Action PayReward", "Func<int> StateStamp",
                  "IOfficialQuestClient Client", "bool Ready { get; }", "int Slot { get; }", "void BeginTick();", "void EndTick(bool dirty);"):
        require(token in binding, BINDING + " 缺 " + token)

    # ---- 2) 唯一活动实例、唯一 owner、注册顺序 ----
    creations = [rel for rel, text in tree.items() if "new OfficialQuestProjection(" in text]
    require(creations == [MODULE], "new OfficialQuestProjection( 只能出现在 " + MODULE + "，实际: " + ", ".join(creations))
    require(tree.get(MODULE, "").count("new OfficialQuestProjection(") == 1, MODULE + " 必须恰好 new 一次核心")
    require("private static OfficialQuestProjection active;" in core and "active = this;" in core,
            CORE + " 静态入口必须经唯一活动实例")
    require("officialQuestRuntime = new OfficialQuestRuntimeModule()" in registration and
            "runtimeModuleHost.Register(officialQuestRuntime)" in registration and
            "internal OfficialQuestRuntimeModule OfficialQuestRuntime { get { return officialQuestRuntime; } }" in registration,
            REGISTRATION + " 缺核心模块的单实例注册 / 只读门面")
    ordered(registration, "runtimeModuleHost.Register(officialQuestRuntime)", "Register(new SkyIslandRuntimeModule())",
            REGISTRATION + " 核心模块必须先于天空岛模块注册（天空岛在 OnAwake 里向核心登记定义）")
    ordered(registration, "runtimeModuleHost.Register(officialQuestRuntime)", "campaignRuntime = new CampaignRuntimeModule()",
            REGISTRATION + " 核心模块必须先于征程模块注册（征程在 OnAwake / bootstrap 里向核心登记定义）")
    module_creations = [rel for rel, text in tree.items() if "new OfficialQuestRuntimeModule(" in text]
    require(module_creations == [REGISTRATION], "new OfficialQuestRuntimeModule( 只能出现在 " + REGISTRATION + "，实际: " + ", ".join(module_creations))

    # ---- 3) 核心 Tick：唯一调用点、无早退门 ----
    tick_callers = [rel for rel, text in tree.items() if "projection.Tick()" in text or "Projection.Tick()" in text]
    require(tick_callers == [MODULE], "核心 Tick 只能由 " + MODULE + " 驱动，实际: " + ", ".join(tick_callers))
    update = module.split("public override void OnUpdate(", 1)[1].split("public override", 1)[0] if "public override void OnUpdate(" in module else ""
    require("projection.Tick();" in update, MODULE + " OnUpdate 没有驱动核心 Tick")
    body_before_tick = update.split("projection.Tick();", 1)[0]
    require("return;" not in body_before_tick and "GetComponent<" not in body_before_tick and "IsSceneLoading" not in body_before_tick,
            MODULE + " 核心 Tick 之前不得有任何早退门：岛上会话在不在、征程开没开都必须照跑（0.25 秒节流在核心内）")
    require("if (disposed || Time.unscaledTime < nextTick) return;" in core and "private const float TickInterval = 0.25f;" in core,
            CORE + " 缺 0.25 秒节流")
    require("quests.Tick()" not in tree.get("DebugAndTools/SkyIsland/SkyIslandRuntimeModule.cs", ""),
            "天空岛模块不得再自己 Tick 任务桥：驱动只在核心模块")

    # ---- 4) Harmony 只装一次，且都在核心 ----
    for target in HARMONY_TARGETS:
        owners = [rel for rel, text in tree.items() if target in text]
        require(owners == [COMPONENTS], target + " 只能在 " + COMPONENTS + " 装一次，实际: " + ", ".join(owners))
        require(tree.get(COMPONENTS, "").count(target) == 1, COMPONENTS + " " + target + " 必须恰好出现一次")
    require("HarmonyPatch" not in sky_bridge, SKY_BRIDGE + " 不得再装自己的 Harmony 补丁（投影核心只有一份）")

    # ---- 5) 给予者扫描唯一 ----
    scanners = [rel for rel, text in tree.items() if "FindObjectsOfType<QuestGiver>" in text]
    require(scanners == [LOCATOR], "FindObjectsOfType<QuestGiver> 只能在 " + LOCATOR + "，实际: " + ", ".join(scanners))
    require(locator.count("FindObjectsOfType<QuestGiver>(true)") == 1, LOCATOR + " 全局扫描必须只有一处")
    refreshers = [rel for rel, text in tree.items() if '"RefreshInspectionIndicator"' in text]
    require(refreshers == [LOCATOR], "官方 RefreshInspectionIndicator 的反射只能在 " + LOCATOR + "，实际: " + ", ".join(refreshers))
    for token, why in (
        ("private const int ScanBudgetPerScene = 12;", "扫描没有有界预算"),
        ("if (scans >= ScanBudgetPerScene || Time.unscaledTime < nextScan) return false;", "扫描没有节流 / 预算门"),
        ("int key = (int)candidate.ID;", "必须按官方公开枚举识别给予者，不能猜名字或层级"),
        ("internal static void NotifySceneChanged()", "场景切换没有作废缓存"),
        ("internal static void ResetStaticCaches()", "静态缓存没有唯一清理 owner"),
    ):
        require(token in locator, LOCATOR + "：" + why + "（缺 " + token + "）")
    require("OfficialQuestGiverLocator.RefreshMarker(giver)" in sky_givers, SKY_GIVERS + " 标记刷新必须转发定位器")
    require("OfficialQuestGiverLocator.TryFind(QuestGiverID.Jeff, out candidate)" in sky_prelude,
            SKY_PRELUDE + " 找 Jeff 必须走定位器并按官方枚举识别")
    require("OfficialQuestGiverLocator.NotifySceneChanged()" in module and "OfficialQuestGiverLocator.ResetStaticCaches()" in module,
            MODULE + " 缺定位器的场景通知 / 销毁清理")

    # ---- 6) 注册表、所有权、逐条目 fail-closed、四类快照、读档与换槽（由天空岛桥搬来）----
    for token, why in (
        ("QuestCollection collection = GameplayDataSettings.QuestCollection", "没有向官方任务 prefab 集合登记"),
        ("manager.ActivateQuest(entry.Binding.QuestId, (QuestGiverID)entry.Binding.GiverId)", "没有通过官方 QuestManager 激活任务 / 给予者没有走定义"),
        ("Quest.onQuestActivated += OnQuestActivated", "官方接取没有写回 Mod 事实"),
        ("Quest.onQuestCompleted += OnQuestCompleted", "官方完成没有接回核心"),
        ("Quest.onQuestActivated -= OnQuestActivated", "接取事件没有退订"),
        ("Quest.onQuestCompleted -= OnQuestCompleted", "完成事件没有退订"),
        ("entry.Prefab.gameObject.name == entry.Binding.ObjectName", "所有权只比整数 ID，会误伤占用相同 ID 的其它 Mod"),
        ("entry.Prefab.GetComponent<OfficialQuestProjectionTask>() != null", "所有权没有验专用 Task 组件"),
        ("if (!OwnsRegisteredQuest(entry.Binding.QuestId)) continue;", "存档过滤没有逐条按所有权放行冲突 ID"),
        ("RemoveSavedQuest(data.activeQuestsData, id)", "没有剥离 active 快照"),
        ("RemoveSavedQuest(data.historyQuestsData, id)", "没有剥离 history 快照"),
        ("data.completedQuests.Remove(id)", "没有剥离 completedQuests（残留会把 IsQuestAvaliable 永久钉死）"),
        ("data.everInspectedQuest.Remove(id)", "没有剥离 everInspected"),
        ("ReferenceEquals(entry.CleanedFreshManager, manager)", "空白槽的官方列表清理必须按任务管理器与槽位缓存"),
        ("entry.CleanedFreshSlot == slot", "空白槽的官方列表清理必须按任务管理器与槽位缓存"),
        ("bool slotChanged = slot != state.LastSlot", "换槽没有先按新槽整清再重建"),
        ("if (slotChanged) { ClearProjection(entry, manager);", "换槽没有先按新槽整清再重建"),
        ("internal bool Blocked, CollisionReported;", "ID 冲突 / 注册失败必须按条目 fail-closed，不能整核心停摆"),
        ("if (committed && !wasDelivered && binding.PayReward != null) binding.PayReward();", "奖金不是在「未交付 → 已交付」那一拍发的"),
        ("bool wasDelivered = binding.IsDelivered();", "交付前没有采样「是否已交付」"),
        ("if (!binding.CanDeliver())", "交付必须先验客户端的门"),
        ("if (!ReferenceEquals(entry.Binding.Client, client) || !Owns(entry)) continue;", "同步必须按客户端分组"),
        ("internal void UnregisterClient(IOfficialQuestClient client)", "客户端不能整体撤销"),
        ("if (binding.Client == null || binding.Accept == null || binding.Deliver == null ||", "不完整的定义没有被拒绝"),
    ):
        require(token in core, CORE + "：" + why + "（缺 " + token + "）")
    tick_body = core.split("internal void Tick()", 1)[1].split("internal void PrepareGiver(", 1)[0] if "internal void Tick()" in core else ""
    ordered(tick_body, "client.BeginTick();", "if (!client.Ready) continue;",
            CORE + " Tick 里事实暂缺（离岛的一两拍、征程未 bootstrap）时不能清也不能建，且 BeginTick 必须在 Ready 判定之前")
    require(core.count("entry.Blocked = true") >= 2, CORE + " Quest ID 冲突或结构性注册失败后仍会每秒重试并制造异常")
    require("root.SetActive(false)" not in core and "PrefabRoot.SetActive(false)" not in core,
            CORE + " 的 Quest 模板不能停用，否则官方克隆出的 Quest/Task 会继承停用状态")
    require("QuestReward_Money" not in core and "QuestReward_Money" not in components,
            "用了官方 QuestReward_Money：它的「已领取」写在实例上，而投影每次加载都重建，玩家能反复领同一笔钱")
    ordered(core, "rewardHost.SetActive(false);", "RewardMasterField.SetValue(reward, quest);",
            CORE + " 奖励组件在 master 就位前就被激活：官方 Reward.Awake 会拿 null 的 Master 订阅事件")
    require("registrationBlocked" not in core, CORE + " 回到了整核心级 fail-closed：一条 ID 冲突不该让整条主线消失")
    ordered(core, "owned[i] = Owns(entries[i]);", "disposed = true;", CORE + " 销毁核心时没有先冻结每条的所有权，会清掉冲突 Mod 的同 ID 任务")
    unregister_client = core.split("internal void UnregisterClient(", 1)[1].split("private ClientState FindClient(", 1)[0]
    ordered(unregister_client, "flags.Add(Owns(entries[i]));", "Release(owned[i], flags[i]);",
            CORE + " 撤销客户端时必须先冻结每条所有权再清")
    delivery = core.split("internal static bool TryCommitDelivery(", 1)[1].split("private static void ApplyRequiredItem(", 1)[0]
    ordered(delivery, "if (!binding.CanDeliver())", "bool committed = binding.Deliver(out reason);", CORE + " 交付必须先验门再提交")

    # ---- 7) 组件：官方 Task / Reward 基类、只带整数回查、读档无声采样 ----
    for token, why in (
        ("class OfficialQuestProjectionTask : Duckov.Quests.Task", "没有使用官方 Task 基类"),
        ("class OfficialQuestProjectionReward : Duckov.Quests.Reward", "奖励行没有用官方 Reward 基类"),
        ("public int questId;", "Task 必须只带整数回查表（委托不随 Instantiate 克隆）"),
        ("public int taskId;", "Task 必须只带整数回查表（委托不随 Instantiate 克隆）"),
        ("if (!initialized)", "读档时目标已完成会被误报成刚完成，导致每次重建重复通知"),
        ("lastKnown = current", "读档时目标已完成会被误报成刚完成"),
        ("OfficialQuestProjection.IsRewardPaid(questId)", "奖励的「已领取」不是读 Mod 交付事实"),
        ("OfficialQuestProjection.FilterSaveSnapshot(__result)", "没有隔离官方 Quest 存档快照"),
        ("dataObj = OfficialQuestProjection.FilterSaveSnapshot(dataObj)", "读档时没有防御性过滤旧残留"),
        ("OfficialQuestProjection.ReportDeliveryFailure(reason)", "交付被拦下时没有告诉玩家原因"),
    ):
        require(token in components, COMPONENTS + "：" + why + "（缺 " + token + "）")
    require("Prefab.GetComponent<OfficialQuestProjectionTask>()" in core, CORE + " 所有权必须验核心专用 Task 组件")

    # ---- 8) 天空岛客户端：只做适配，不再持有第二份投影 ----
    for token, why in (
        ("class SkyIslandOfficialQuestBridge : IDisposable, IOfficialQuestClient", "天空岛桥必须是核心的客户端"),
        ("owner.OfficialQuestRuntime.Projection", "天空岛桥必须取共享核心，不得自己 new"),
        ("projection.Register(BuildBinding(definition))", "定义必须交给核心登记"),
        ("projection.UnregisterClient(this)", "销毁时必须整体撤销自己的定义"),
    ):
        require(token in sky_bridge, SKY_BRIDGE + "：" + why + "（缺 " + token + "）")
    for banned in ("QuestCollection", "FilterSaveSnapshot", "manager.ActivateQuest", "Quest.onQuestActivated"):
        require(banned not in sky_bridge, SKY_BRIDGE + " 出现了 " + banned + "：注册 / 投影 / 过滤只能在核心")

    # ---- 9) 编译清单 ----
    for rel in (BINDING, CORE, COMPONENTS, LOCATOR, MODULE):
        require("echo(" + rel.replace("/", "\\") in sources[COMPILE], COMPILE + " 未登记 " + rel)
    return errors


def main():
    sources = {path: (ROOT / path).read_text(encoding="utf-8-sig").replace("\r\n", "\n") for path in PATHS}
    tree = production_sources()
    errors = check(sources, tree)
    probes = (
        (MODULE, "if (projection != null) projection.Tick();",
         "if (owner_session_exists_stub()) return;\n            if (projection != null) projection.Tick();"),
        (REGISTRATION, "officialQuestRuntime = new OfficialQuestRuntimeModule();\n            runtimeModuleHost.Register(officialQuestRuntime);\n", ""),
        (LOCATOR, "int key = (int)candidate.ID;", "int key = candidate.name == \"Jeff\" ? 1 : 0;"),
        (LOCATOR, "if (scans >= ScanBudgetPerScene || Time.unscaledTime < nextScan) return false;", ""),
        (SKY_PRELUDE, "OfficialQuestGiverLocator.TryFind(QuestGiverID.Jeff, out candidate)", "SkyIslandPreludeFlow.TryFindJeffStub(out candidate)"),
        (SKY_GIVERS, "OfficialQuestGiverLocator.RefreshMarker(giver)", "RefreshMarkerStub(giver)"),
        (SKY_BRIDGE, "owner.OfficialQuestRuntime.Projection", "new OfficialQuestProjection(owner)"),
        (SKY_BRIDGE, "projection.UnregisterClient(this)", "projection.Unregister(0)"),
        (CORE, "if (committed && !wasDelivered && binding.PayReward != null) binding.PayReward();", ""),
        (CORE, "rewardHost.SetActive(false);", "rewardHost.SetActive(true);"),
        (CORE, "manager.ActivateQuest(entry.Binding.QuestId, (QuestGiverID)entry.Binding.GiverId)", ""),
        (CORE, "collection.Add(quest);", "root.SetActive(false);\n                collection.Add(quest);"),
        (CORE, "RemoveSavedQuest(data.activeQuestsData, id)", ""),
        (CORE, "data.everInspectedQuest.Remove(id)", "false"),
        (CORE, "if (!OwnsRegisteredQuest(entry.Binding.QuestId)) continue;", ""),
        (CORE, "entry.Prefab.gameObject.name == entry.Binding.ObjectName", "true"),
        (CORE, "internal bool Blocked, CollisionReported;", "internal bool CollisionReported;"),
        (CORE, "if (slotChanged) { ClearProjection(entry, manager);", "if (false) {"),
        (CORE, "if (!client.Ready) continue;\n                // 先按新槽整清", "// 先按新槽整清"),
        (CORE, "flags.Add(Owns(entries[i]));", "flags.Add(true);"),
        (CORE, "owned[i] = Owns(entries[i]);", "owned[i] = true;"),
        (COMPONENTS, "if (!initialized)", "if (false)"),
        (COMPONENTS, "HarmonyPatch(typeof(Quest), nameof(Quest.TryComplete))", "HarmonyPatch(typeof(Quest), nameof(Quest.ForceComplete))"),
        (COMPONENTS, "class OfficialQuestProjectionTask : Duckov.Quests.Task", "class OfficialQuestProjectionTask : MonoBehaviour"),
        (COMPONENTS, "OfficialQuestProjection.IsRewardPaid(questId)", "false"),
        (COMPILE, "echo(Utilities\\OfficialQuests\\OfficialQuestProjection.cs", ""),
    )
    for path, before, after in probes:
        if before not in sources[path]:
            errors.append("反向检查锚点失效：" + path + " -> " + before)
            continue
        altered = dict(sources)
        altered[path] = altered[path].replace(before, after, 1)
        altered_tree = dict(tree)
        if path in altered_tree:
            altered_tree[path] = clean_source(altered[path])
        if not check(altered, altered_tree):
            errors.append("未拦截旧写法：" + path + " -> " + before)
    # 结构性探针：第二份核心 / 第二份补丁 / 第二处扫描
    for extra_rel, extra_text, label in (
        ("Campaign/CampaignOfficialQuestClientProbe.cs", "namespace BossRush { class X { void F(ModBehaviour o) { var p = new OfficialQuestProjection(o); } } }", "第二份核心实例"),
        ("Campaign/CampaignQuestPatchProbe.cs", "using HarmonyLib; using Duckov.Quests; namespace BossRush { [HarmonyPatch(typeof(Quest), nameof(Quest.MeetsPrerequisit))] static class P { } }", "第二套 Harmony 补丁"),
        ("Campaign/CampaignJeffProbe.cs", "using UnityEngine; using Duckov.Quests; namespace BossRush { static class J { static void F() { var g = Object.FindObjectsOfType<QuestGiver>(true); } } }", "第二处给予者扫描"),
    ):
        altered_tree = dict(tree)
        altered_tree[extra_rel] = extra_text
        if not check(sources, altered_tree):
            errors.append("未拦截" + label + "：" + extra_rel)
    if errors:
        print("OfficialQuestProjectionGuard: FAIL\n  " + "\n  ".join(errors))
        return 1
    print("OfficialQuestProjectionGuard: PASS（唯一核心实例 + 四补丁只装一次 + Tick 无早退 + 给予者扫描唯一 + 所有权 / 快照 / 读档 / 换槽；%d 个反向检查 + 3 个结构探针）" % len(probes))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
