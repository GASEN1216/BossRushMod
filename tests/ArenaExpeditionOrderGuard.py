#!/usr/bin/env python3
"""ArenaExpeditionOrderGuard — 出征令（2026-11-24 玩家许愿第 1、2、4 项）的接线不变式。

竞技场路牌的「出征令」面板让玩家选阵容 × 战帖（轻松 / 标准 / 困难 / 噩梦）× 征程（短征 / 长征 / 全征）。
本守卫钉住这些容易被后续改动悄悄打断的点：

1. 战帖倍率只在 `ArenaExpeditionTuning.cs`，是本局临时倍率：不进 ModConfig，不在 Mode D 借用竞技场模块时生效。
2. 出场、倒计时预告、完成判定三处都经 `ResolveRunPresets` 读本局队列，否则短征 / 长征会和全池对不上（打完 12 波不结算，
   或倒计时预告的不是下一只）。
3. 普通 Boss 在全局倍率之后乘战帖；内容 Boss（龙裔、龙皇、女巫、冠军之影、蚀日花冠）在 `RegisterContentWaveBoss` 登记时乘
   （该方法体由 NonWaveBossSpawnGuard 逐字钉住）。
4. Boss 自带掉落与无间奖金按未乘战帖的生命算，不让「更肉」再翻一倍奖励。
5. 旧通关 / 无间成就只认标准及以上（通关类还要求全征）；噩梦通关成就已登记。
6. 本局快照随切图与模块销毁收掉；`StartFirstWave` 第一件事取走面板放下的选择。
7. 变异词条按权重不放回抽取，同互斥组只出一个；群鼠战术权重 0.5、与巨灵兵同在 `enemy_size` 组。
8. 战帖战规（2026-11-25 难度多元化）：借用的变异词条由 `ExpeditionForcedMutators` 交给开波抽词条先上，
   一路经宿主 → MutatorModeFlow → MutatorManager 透传，关掉随机词条也照样上，HUD 角标标「战帖」；
   每条借用的词条都要在词条池里、不限模式；四条治疗类词条同在 `healing` 互斥组；标准档不加任何战规。
9. 波间休整倍率进倒计时（两处）与每 5 波额外休整；盲战挡在烽烟圈的竞技场来源入口；两者都经
   `LiveExpeditionTier`，Mode D 借用本模块时不受影响。
10. 面板：卡片下不再写结论，悬赏令列出战规与赏格，按钮是「等等 / 立誓」（「收起」改名），ESC = 等等。

文本守卫防不住「保留 token、杀掉执行路径」，行为正确性仍要靠实机清单。
"""
import io
import os
import re
import sys

from cs_source_util import clean_source

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ARENA = os.path.join(ROOT, "WavesArena")


def read(*parts):
    path = os.path.join(ROOT, *parts)
    if not os.path.isfile(path):
        return None
    with io.open(path, "r", encoding="utf-8-sig", errors="ignore") as fh:
        return clean_source(fh.read())


def compact(text):
    return re.sub(r"\s+", "", text or "")


def body_of(source, signature):
    if source is None:
        return None
    at = source.find(signature)
    if at < 0 or source.find(signature, at + 1) >= 0:
        return None
    brace = source.find("{", at)
    if brace < 0:
        return None
    depth = 0
    for i in range(brace, len(source)):
        if source[i] == "{":
            depth += 1
        elif source[i] == "}":
            depth -= 1
            if depth == 0:
                return source[brace + 1:i]
    return None


def mutator_block(source, mutator_id):
    """从 `Id = "<id>"` 切到下一条定义的 `Id = "`。"""
    if source is None:
        return None
    m = re.search(r'Id\s*=\s*"' + re.escape(mutator_id) + '"', source)
    if not m:
        return None
    nxt = re.search(r'\bId\s*=\s*"', source[m.end():])
    return source[m.start():m.end() + nxt.start()] if nxt else source[m.start():]


def require(errors, ok, message):
    if not ok:
        errors.append(message)


def check_tuning(errors):
    tuning = read("WavesArena", "ArenaExpeditionTuning.cs")
    if tuning is None:
        errors.append("[Tuning] 缺少 WavesArena/ArenaExpeditionTuning.cs")
        return
    c = compact(tuning)
    for name, values in (("HealthMultipliers", "0.6f,1f,1.3f,1.6f"), ("DamageMultipliers", "0.7f,1f,1.15f,1.3f"),
                         ("ReactionDivisors", "1f,1f,1.15f,1.3f"), ("HellCashMultipliers", "0.5f,1f,1.25f,1.5f"),
                         ("RestMultipliers", "1.5f,1f,0.5f,0.5f")):
        require(errors, ("float[]" + name + "={" + values + "}") in c,
                "[Tuning] %s 与 owner 拍板的四档表不一致（应为 {%s}）：改数值先改方案与本守卫" % (name, values))
    require(errors, "returnMath.Max(1,count);" in c, "[Tuning] VictoryChestCount 必须保证通关箱至少 1 件")
    require(errors, "privatestaticreadonlyArenaExpeditionRule[]StandardRules=newArenaExpeditionRule[0];" in c,
            "[Rules] 标准档不加任何战规（标准 = 改版前的竞技场原样）")
    require(errors, "KeepsMilestoneRest(ArenaExpeditionTiertier){returntier<=ArenaExpeditionTier.Standard;}" in c
            and "HidesMapBeacons(ArenaExpeditionTiertier){returntier==ArenaExpeditionTier.Nightmare;}" in c,
            "[Rules] 急行军只取消困难 / 噩梦的额外休整，盲战只在噩梦")

    # 借用的变异词条：每条都要在词条池里、不限模式（否则 RollAndApply 会静默跳过，战规写了却没生效）
    defs = read("Integration", "Mutators", "MutatorDefinitions.cs")
    ids = re.findall(r'ArenaExpeditionRule\.Mutator\(\s*ArenaExpeditionRuleKind\.\w+\s*,\s*(?:true|false)\s*,\s*"([a-z_]+)"\s*\)', tuning)
    require(errors, len(set(ids)) >= 5, "[Rules] 战帖借用的变异词条少于 5 条，战规表可能被改坏了")
    for mutator_id in sorted(set(ids)):
        block = mutator_block(defs, mutator_id)
        require(errors, block is not None, "[Rules] 战规借用的变异词条 %s 不在 MutatorPool 里" % mutator_id)
        require(errors, block is None or "IsAllowedForMode" not in block,
                "[Rules] 战规借用的变异词条 %s 限定了模式，竞技场里会被静默跳过" % mutator_id)
    for mutator_id in ("player_swift_heal", "heal_reduced", "lifesteal_on_kill", "blood_pact"):
        require(errors, 'ExclusiveGroup="healing",' in compact(mutator_block(defs, mutator_id)),
                "[Rules] %s 必须归入 healing 互斥组（免得随机再抽一条同类治疗词条叠上去）" % mutator_id)

    for rel in (("Config", "Config.cs"), ("Config", "ModConfigApi.cs")):
        src = read(*rel)
        if src is not None and re.search(r"ArenaExpedition|expedition", src, re.IGNORECASE):
            errors.append("[Tuning] %s 引用了出征令：战帖倍率是本局临时倍率，不进 ModConfig" % "/".join(rel))


def check_run_wiring(errors):
    module = "WavesArenaRuntimeModule_"
    expedition = read("WavesArena", module + "Expedition.cs")
    live = body_of(expedition, "private ArenaExpeditionTier LiveExpeditionTier()")
    require(errors, live is not None and all(t in compact(live) for t in
                                             ("ArenaExpeditionState.Current==null", "!IsActive", "owner.IsModeDActive",
                                              "returnArenaExpeditionTier.Standard;")),
            "[Scope] LiveExpeditionTier 必须在没有本局快照、竞技场未激活或 Mode D 借用时退回标准档")

    for file_name, token, why in (
            ("BossSpawning.cs", "varrunPresets=ResolveRunPresets(filteredPresets);", "出场"),
            ("Countdown.cs", "ResolveRunPresets(", "倒计时预告下一只"),
            ("WaveDeaths.cs", "ResolveRunPresets(", "完成判定"),
    ):
        src = read("WavesArena", module + file_name)
        require(errors, src is not None and token in compact(src),
                "[Queue] %s%s 的%s没有经 ResolveRunPresets 读本局队列" % (module, file_name, why))

    legacy = compact(read("WavesArena", module + "LegacySpawn.cs"))
    stat_at = legacy.find("ApplyBossStatMultiplier(character);")
    tier_at = legacy.find("ApplyExpeditionTierToSpawnedBoss(character);")
    require(errors, stat_at >= 0 and tier_at > stat_at,
            "[Scale] LegacySpawn 必须在 ApplyBossStatMultiplier 之后调用 ApplyExpeditionTierToSpawnedBoss")

    deaths = compact(read("WavesArena", module + "WaveDeaths.cs"))
    require(errors, "maxHp*10f*ExpeditionHellCashFactor(bossMain)" in deaths,
            "[Reward] 无间奖金必须乘 ExpeditionHellCashFactor（先除掉战帖生命，再乘战帖奖金倍率）")
    loot = compact(read("WavesArena", module + "BossLootEvent.cs"))
    require(errors, "maxHealth=ExpeditionLootHealth(bossMain,bossMain.Health.MaxHealth);" in loot,
            "[Reward] Boss 自带掉落必须按未乘战帖的生命算（ExpeditionLootHealth）")

    victory = compact(read("WavesArena", module + "VictoryRewards.cs"))
    require(errors, "if(ExpeditionCountsForClearAchievements()){owner.CheckClearAchievementsForArena();}" in victory,
            "[Achievement] 旧通关类成就必须由 ExpeditionCountsForClearAchievements 门控")
    require(errors, "UnlockExpeditionNightmareClear();" in victory, "[Achievement] 通关结算缺少噩梦通关解锁")
    require(errors, "ExpeditionVictoryChestCount(" in victory and "ExpeditionVictoryQualityMin(" in victory,
            "[Reward] 通关箱件数与品质下限必须经出征令折算")
    hell = compact(read("WavesArena", module + "InfiniteHellCompletion.cs"))
    require(errors, "ExpeditionCountsForHellAchievements()" in hell,
            "[Achievement] 无间成就必须由 ExpeditionCountsForHellAchievements 门控（轻松档不计）")

    order = compact(read("WavesArena", "ArenaExpeditionOrder.cs"))
    for token, why in (("CountsForClearAchievements{get{return!Order.IsHell&&Queue==null&&Order.Tier>=ArenaExpeditionTier.Standard;}}",
                        "通关类成就只认全征 + 标准及以上"),
                       ("CountsForHellAchievements{get{returnOrder.IsHell&&Order.Tier>=ArenaExpeditionTier.Standard;}}",
                        "无间成就只认标准及以上")):
        require(errors, token in order, "[Achievement] ArenaExpeditionRun：" + why)

    achievements = read("Achievement", "BossRushAchievementManager.cs")
    require(errors, achievements is not None and '"nightmare_clear"' in achievements,
            "[Achievement] 噩梦通关成就 nightmare_clear 未登记")

    start = compact(read("WavesArena", module + "Start.cs"))
    require(errors, "if(!owner.IsActive){ArenaExpeditionOrderexpeditionOrder=ArenaExpeditionState.TakePending();" in start,
            "[Lifecycle] StartFirstWave 进入开局分支后第一件事必须取走出征令面板放下的选择")
    require(errors, "BeginExpeditionRun(expeditionOrder," in start, "[Lifecycle] StartFirstWave 必须开本局出征快照")

    runtime = read("WavesArena", "WavesArenaRuntimeModule.cs")
    scene = body_of(runtime, "public override void OnSceneLoaded(")
    destroy = body_of(runtime, "public override void OnDestroy()")
    require(errors, scene is not None and "EndExpeditionRun();" in scene, "[Lifecycle] 切图必须 EndExpeditionRun")
    require(errors, destroy is not None and all(t in destroy for t in (
        "EndExpeditionRun();", "ArenaExpeditionState.ResetStaticCaches();", "ArenaExpeditionOrderPanel.ResetStaticCaches();")),
        "[Lifecycle] 模块销毁必须收掉本局快照、角标与面板")

    forced = compact(body_of(expedition, "private List<string> ExpeditionForcedMutators(ArenaExpeditionOrder order)"))
    require(errors, "if(order==null||order.IsHell!=InfiniteHellMode||owner==null||owner.IsModeDActive)returnnull;" in forced
            and "returnArenaExpeditionTuning.ForcedMutatorIds(order.Tier);" in forced,
            "[Rules] ExpeditionForcedMutators 必须在出征令与模式对不上或 Mode D 借用时不指定词条")
    require(errors, 'owner.TryRollMutatorsForArena(InfiniteHellMode?"InfiniteHell":"BossRush",ExpeditionForcedMutators(expeditionOrder));' in start,
            "[Rules] 开波抽词条必须带上战帖指定的词条（ExpeditionForcedMutators）")
    rest = compact(body_of(expedition, "private float ExpeditionRestSeconds(float seconds)"))
    milestone = compact(body_of(expedition, "private float ExpeditionMilestoneRestSeconds(float seconds)"))
    require(errors, "returnseconds*ArenaExpeditionTuning.RestMultiplier(LiveExpeditionTier());" in rest
            and "returnArenaExpeditionTuning.KeepsMilestoneRest(LiveExpeditionTier())?seconds:0f;" in milestone,
            "[Rules] 休整倍率与额外休整必须按 LiveExpeditionTier 取（Mode D 借用时退回标准档）")
    countdown = compact(read("WavesArena", module + "Countdown.cs"))
    require(errors, "floatinterval=ExpeditionRestSeconds(owner.GetWaveIntervalSeconds());" in countdown
            and "floatmilestoneBonus=ExpeditionMilestoneRestSeconds(owner.GetMilestoneRestBonusSeconds());" in countdown,
            "[Rules] 波间倒计时必须乘战帖休整倍率，每 5 波额外休整经 ExpeditionMilestoneRestSeconds")
    tick = compact(read("WavesArena", module + "Tick.cs"))
    require(errors, "floatinterval=ExpeditionRestSeconds(owner.GetWaveIntervalSeconds());" in tick and
            "floatinterval=owner.GetWaveIntervalSeconds();" not in tick,
            "[Rules] Tick 里的波间间隔也必须乘战帖休整倍率")
    beacon = compact(body_of(read("WavesArena", module + "BossAccess.cs"),
                             "internal void CopyMapBeaconBossesTo(ICollection<CharacterMainControl> destination)"))
    require(errors, beacon.startswith("if(ArenaExpeditionTuning.HidesMapBeacons(LiveExpeditionTier()))return;"),
            "[Rules] 盲战必须挡在烽烟圈竞技场来源的入口（CopyMapBeaconBossesTo 第一句）")

    arena_host = compact(read("WavesArena", "WavesArena.cs"))
    require(errors, "internalvoidTryRollMutatorsForArena(stringmode,System.Collections.Generic.IList<string>forcedIds=null){TryRollMutatorsForMode(mode,forcedIds);}" in arena_host,
            "[Rules] 宿主 TryRollMutatorsForArena 必须把战帖词条透传下去")
    compat = compact(read("Integration", "IntegrationHostCompatibility.cs"))
    require(errors, "config!=null?config.mutatorCount:0,MutatorCountMin,MutatorCountMax,forcedIds);" in compat,
            "[Rules] 宿主 TryRollMutatorsForMode 必须把战帖词条透传给 MutatorModeFlow")
    flow = compact(read("Integration", "Mutators", "MutatorModeFlow.cs"))
    require(errors, "boolhasForced=forcedIds!=null&&forcedIds.Count>0;if(!enabled&&!hasForced)return;" in flow
            and "intcount=enabled?Mathf.Clamp(requestedCount,minimumCount,maximumCount):0;MutatorManager.RollAndApply(player,count,null,modeTag,forcedIds);" in flow,
            "[Rules] 关掉随机词条时战帖词条照样上、随机条数为 0（MutatorModeFlow）")
    ui = compact(read("Integration", "Mutators", "MutatorUI.cs"))
    require(errors, 'MutatorManager.IsForcedMutator(mutators[i])?L10n.T("战帖","Writ")' in ui,
            "[Rules] 词条 HUD 必须把战帖指定的词条标成「战帖」")

    panel = compact(read("WavesArena", "ArenaExpeditionOrderPanel.cs"))
    require(errors, 'L10n.T("等等","Wait")' in panel and 'L10n.T("立誓","Swear")' in panel and '"收起"' not in panel,
            "[Panel] 出征令按钮应为「等等 / 立誓」（owner 2026-11-25：收起改名等等）")
    require(errors, "PetNestCancelKey.Attach(canvas.gameObject,Dismiss,null);" in panel,
            "[Panel] ESC / 手柄取消必须等同「等等」")
    notice = body_of(read("WavesArena", "ArenaExpeditionOrderPanel.cs"), "private void RebuildNotice(bool animate)")
    require(errors, notice is not None and "ArenaExpeditionTuning.RulesFor(tier)" in notice
            and "ArenaExpeditionTuning.RewardLines(tier, lineup, length, poolCount)" in notice,
            "[Panel] 悬赏令必须列出这档的战规与赏格")
    refresh = compact(body_of(read("WavesArena", "ArenaExpeditionOrderPanel.cs"), "private void RefreshAll(bool animate)"))
    require(errors, refresh.endswith("RebuildNotice(animate);"), "[Panel] 选择一变悬赏令必须跟着重写（RefreshAll 收尾 RebuildNotice）")

    interact = compact(read("Interactables", "BossRushInteractables.cs"))
    require(errors, "if(opensExpeditionOrder){ArenaExpeditionOrderPanel.Open(this);return;}" in interact,
            "[Entry] 竞技场路牌的出征令选项必须打开出征令面板")


def check_mutators(errors):
    defs = read("Integration", "Mutators", "MutatorDefinitions.cs")
    swarm = compact(mutator_block(defs, "enemy_swarm"))
    giant = compact(mutator_block(defs, "enemy_giant"))
    require(errors, "Weight=0.5f," in swarm and 'ExclusiveGroup="enemy_size",' in swarm,
            "[Mutator] 群鼠战术必须权重 0.5 且归入 enemy_size 互斥组")
    require(errors, 'ExclusiveGroup="enemy_size",' in giant, "[Mutator] 巨灵兵必须归入 enemy_size 互斥组")
    require(errors, "localScale*0.75f;" in swarm and swarm.count("ModifierType.PercentageAdd,0.30f,") == 2,
            "[Mutator] 群鼠战术的体型 / 移速应为 ×0.75 / +30%（改数值先改方案与本守卫）")

    manager = read("Integration", "Mutators", "MutatorManager.cs")
    roll = compact(body_of(manager, "public static void RollAndApply("))
    require(errors, "intidx=PickWeightedIndex(pool,rng);" in roll,
            "[Mutator] RollAndApply 必须按权重抽取（PickWeightedIndex）")
    require(errors, "pool.RemoveAt(idx);" in roll and "pool.RemoveAll(other=>other!=null&&other.ExclusiveGroup==group);" in roll,
            "[Mutator] RollAndApply 必须不放回，并在抽中后剔除同互斥组的其余词条")
    require(errors, "rng.Next(pool.Count)" not in roll,
            "[Mutator] RollAndApply 里又出现了均匀抽取 rng.Next(pool.Count)")
    forced_at = roll.find("MutatorDefinitionforced=FindDefinition(forcedIds[f]);")
    pick_at = roll.find("intidx=PickWeightedIndex(pool,rng);")
    require(errors, 0 <= forced_at < pick_at
            and "_activeMutators.Add(forced);_forcedMutators.Add(forced);pool.Remove(forced);" in roll
            and "pool.RemoveAll(other=>other!=null&&other.ExclusiveGroup==forcedGroup);" in roll
            and "intactualCount=Mathf.Min(Mathf.Max(0,count),pool.Count);" in roll,
            "[Mutator] 战帖词条必须先于随机抽取上、不占随机条数，并把同互斥组移出随机候选")
    remove_all = compact(body_of(manager, "public static void RemoveAll("))
    require(errors, "_forcedMutators.Clear();" in remove_all, "[Mutator] RemoveAll 必须清掉战帖词条标记")


def main():
    errors = []
    check_tuning(errors)
    check_run_wiring(errors)
    check_mutators(errors)
    if errors:
        print("ArenaExpeditionOrderGuard: FAIL (%d errors)" % len(errors))
        for e in errors:
            print("  - " + e)
        return 1
    print("ArenaExpeditionOrderGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
