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
   一路经宿主 → MutatorModeFlow → MutatorManager 透传，关掉随机词条也照样上；
   出征令开的局不抽随机词条（owner 2026-11-25「打出征令时不开变异词条吧」：allowRandom = expeditionOrder == null），
   战帖借用的词条是战规，不进变异词条栏；标准档不加任何战规。
   同日第二次拍板「战规机制化」：只有轻松的「治疗 +50%」还借 player_swift_heal（在词条池里、不限模式、`healing` 组）；
   困难 6 条 / 噩梦 10 条全是竞技场自己的机制（局内服务对象由 ArenaTrialsGuard 钉），撤掉的六条借用词条不得回来。
9. 波间休整倍率进倒计时（两处）与每 5 波额外休整；失明（原盲战）挡在烽烟圈的竞技场来源入口；两者都经
   `LiveExpeditionTier`，Mode D 借用本模块时不受影响。
10. 面板：卡片下不再写结论，悬赏令列出战规与赏格，按钮是「等等 / 立誓」（「收起」改名），ESC = 等等。
11. 加码（2026-11-27 owner「杀戮尖塔那种，战规简单到难排序，玩家自己选择加码」）：标准 5 级借困难战规、困难 8 级借噩梦战规，
   轻松 / 噩梦不加；梯子顺序、每级取的是哪条战规（按名字核对）、每级奖励 +5% 钉死；局内机制由 ProfileFor(tier, raise)
   改一份副本（共享配置不许被改），休整 / 额外休整 / 烽烟圈经 LiveExpeditionRaise（Mode D 借用时为 0）；
   通关箱与无间奖金带上加码；面板立誓把加码带进出征令、悬赏令写出加码战规并略去被换掉的档位战规。

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
    for name, values in (("HealthMultipliers", "0.6f,1f,1.3f,2.5f"), ("DamageMultipliers", "0.7f,1f,1.15f,1.75f"),
                         ("ReactionDivisors", "1f,1f,1.15f,1.5f"), ("HellCashMultipliers", "0.5f,1f,1.25f,2f"),
                         ("RestMultipliers", "1.5f,1f,0.5f,0.5f")):
        require(errors, ("float[]" + name + "={" + values + "}") in c,
                "[Tuning] %s 与 owner 拍板的四档表不一致（应为 {%s}）：改数值先改方案与本守卫" % (name, values))
    require(errors, "returnMath.Max(1,count);" in c, "[Tuning] VictoryChestCount 必须保证通关箱至少 1 件")
    require(errors, "privatestaticreadonlyArenaExpeditionRule[]StandardRules=newArenaExpeditionRule[0];" in c,
            "[Rules] 标准档不加任何战规（标准 = 改版前的竞技场原样）")
    require(errors, "KeepsMilestoneRest(ArenaExpeditionTiertier){returntier<=ArenaExpeditionTier.Standard;}" in c
            and "HidesMapBeacons(ArenaExpeditionTiertier){returntier==ArenaExpeditionTier.Nightmare;}" in c,
            "[Rules] 急行军只取消困难 / 噩梦的额外休整，失明只在噩梦")

    # 借用的变异词条（2026-11-25 战规机制化后只剩轻松的 player_swift_heal）：
    # 在词条池里、不限模式（否则 RollAndApply 会静默跳过，战规写了却没生效），归 healing 组。
    defs = read("Integration", "Mutators", "MutatorDefinitions.cs")
    ids = re.findall(r'ArenaExpeditionRule\.Mutator\(\s*ArenaExpeditionRuleKind\.\w+\s*,\s*(?:true|false)\s*,\s*"([a-z_]+)"\s*\)', tuning)
    require(errors, sorted(set(ids)) == ["player_swift_heal"],
            "[Rules] 战帖借用的变异词条只应剩轻松的 player_swift_heal（实际：%s）" % sorted(set(ids)))
    for mutator_id in sorted(set(ids)):
        block = mutator_block(defs, mutator_id)
        require(errors, block is not None, "[Rules] 战规借用的变异词条 %s 不在 MutatorPool 里" % mutator_id)
        require(errors, block is None or "IsAllowedForMode" not in block,
                "[Rules] 战规借用的变异词条 %s 限定了模式，竞技场里会被静默跳过" % mutator_id)
        require(errors, 'ExclusiveGroup="healing",' in compact(block),
                "[Rules] %s 必须归入 healing 互斥组（免得随机再抽一条同类治疗词条叠上去）" % mutator_id)
    for retired in ("enemy_sharpshooter", "heal_reduced", "boss_regen", "lifesteal_on_kill", "blood_pact", "explode_on_death"):
        require(errors, '"' + retired + '"' not in tuning,
                "[Rules] 撤掉的借用词条 %s 又回到了战帖（owner 2026-11-25：困难 / 噩梦改成竞技场自己的机制）" % retired)
    require(errors, '"盲战"' not in tuning, "[Rules] 盲战已并入失明，不再单列一条")

    def rule_names(array):
        body = body_of(tuning, "private static readonly ArenaExpeditionRule[] " + array + " =")
        if body is None:
            return None
        names = []
        for m in re.finditer(r'ArenaExpeditionRule\.Own\(\s*ArenaExpeditionRuleKind\.\w+\s*,\s*false\s*,\s*"([^"]+)"|Hex\((true|false)\)|(ForcedMarch)|(?:Omens|Calamity)\(\s*"([^"]+)"', body):
            if m.group(1):
                names.append(m.group(1))
            elif m.group(2):
                names.append("厄运（重）" if m.group(2) == "true" else "厄运")
            elif m.group(3):
                names.append("急行军")
            else:
                names.append(m.group(4))
        return names

    hard = ["磨刀", "凶相", "厄运", "急行军", "天象无常", "天灾"]
    nightmare = ["杀心", "凶煞", "失明", "断补", "血债", "厄运（重）", "急行军", "焚城", "天翻地覆", "天灾连环"]
    require(errors, rule_names("HardRules") == hard,
            "[Rules] 困难战规应为 %s（实际 %s）" % ("、".join(hard), rule_names("HardRules")))
    require(errors, rule_names("NightmareRules") == nightmare,
            "[Rules] 噩梦战规应为 %s（实际 %s）" % ("、".join(nightmare), rule_names("NightmareRules")))
    require(errors, '"急行军","ForcedMarch"' in c, "[Rules] 急行军战规缺失")

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
    tier_at = legacy.find("ApplyExpeditionTierToSpawnedBoss(character,preset);")
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
    require(errors, 'owner.TryRollMutatorsForArena(InfiniteHellMode?"InfiniteHell":"BossRush",ExpeditionForcedMutators(expeditionOrder),expeditionOrder==null);' in start,
            "[Rules] 开波抽词条必须带上战帖指定的词条（ExpeditionForcedMutators），且出征令开的局不抽随机词条（allowRandom = expeditionOrder == null）")
    rest = compact(body_of(expedition, "private float ExpeditionRestSeconds(float seconds)"))
    milestone = compact(body_of(expedition, "private float ExpeditionMilestoneRestSeconds(float seconds)"))
    require(errors, "returnseconds*ArenaExpeditionTuning.RestMultiplier(LiveExpeditionTier(),LiveExpeditionRaise());" in rest
            and "returnArenaExpeditionTuning.KeepsMilestoneRest(LiveExpeditionTier(),LiveExpeditionRaise())?seconds:0f;" in milestone,
            "[Rules] 休整倍率与额外休整必须按 LiveExpeditionTier / LiveExpeditionRaise 取（Mode D 借用时退回标准档、不加码）")
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
    require(errors, beacon.startswith("if(ArenaExpeditionTuning.HidesMapBeacons(LiveExpeditionTier(),LiveExpeditionRaise()))return;"),
            "[Rules] 失明必须挡在烽烟圈竞技场来源的入口（CopyMapBeaconBossesTo 第一句）")

    arena_host = compact(read("WavesArena", "WavesArena.cs"))
    require(errors, "internalvoidTryRollMutatorsForArena(stringmode,System.Collections.Generic.IList<string>forcedIds=null,boolallowRandom=true){TryRollMutatorsForMode(mode,forcedIds,allowRandom);}" in arena_host,
            "[Rules] 宿主 TryRollMutatorsForArena 必须把战帖词条与「本局抽不抽随机词条」透传下去")
    compat = compact(read("Integration", "IntegrationHostCompatibility.cs"))
    require(errors, "MutatorModeFlow.TryRollMutatorsForMode(modeTag,allowRandom&&config!=null&&config.enableMutators," in compat
            and "config!=null?config.mutatorCount:0,MutatorCountMin,MutatorCountMax,forcedIds);" in compat,
            "[Rules] 宿主 TryRollMutatorsForMode 必须把战帖词条透传给 MutatorModeFlow，allowRandom=false 时随机词条关掉")
    flow = compact(read("Integration", "Mutators", "MutatorModeFlow.cs"))
    require(errors, "boolhasForced=forcedIds!=null&&forcedIds.Count>0;if(!enabled&&!hasForced)return;" in flow
            and "intcount=enabled?Mathf.Clamp(requestedCount,minimumCount,maximumCount):0;MutatorManager.RollAndApply(player,count,null,modeTag,forcedIds);" in flow,
            "[Rules] 关掉随机词条时战帖词条照样上、随机条数为 0（MutatorModeFlow）")
    banner = compact(body_of(read("Integration", "Mutators", "MutatorUI.cs"), "public static void ShowBanner()"))
    require(errors, "if(MutatorManager.IsForcedMutator(mutators[i]))continue;" in banner
            and banner.find("if(_cachedInfos.Count==0)return;") < banner.find("_cornerVisible=true;")
            and banner.find("if(_cachedInfos.Count==0)return;") >= 0,
            "[Rules] 战帖借用的词条不进变异词条栏；只有战帖词条时词条栏不出现")

    panel = compact(read("WavesArena", "ArenaExpeditionOrderPanel.cs"))
    require(errors, 'L10n.T("等等","Wait")' in panel and 'L10n.T("立誓","Swear")' in panel and '"收起"' not in panel,
            "[Panel] 出征令按钮应为「等等 / 立誓」（owner 2026-11-25：收起改名等等）")
    require(errors, "PetNestCancelKey.Attach(canvas.gameObject,Dismiss,null);" in panel,
            "[Panel] ESC / 手柄取消必须等同「等等」")
    notice = body_of(read("WavesArena", "ArenaExpeditionOrderPanel.cs"), "private void RebuildNotice(bool animate)")
    require(errors, notice is not None and "ArenaExpeditionTuning.RulesFor(tier)" in notice
            and "ArenaExpeditionTuning.RewardLines(tier, lineup, length, poolCount, raise)" in notice,
            "[Panel] 悬赏令必须列出这档的战规与赏格")
    refresh = compact(body_of(read("WavesArena", "ArenaExpeditionOrderPanel.cs"), "private void RefreshAll(bool animate)"))
    require(errors, refresh.endswith("RebuildNotice(animate);"), "[Panel] 选择一变悬赏令必须跟着重写（RefreshAll 收尾 RebuildNotice）")

    interact = compact(read("Interactables", "BossRushInteractables.cs"))
    require(errors, "if(opensExpeditionOrder){ArenaExpeditionOrderPanel.Open(this);return;}" in interact,
            "[Entry] 竞技场路牌的出征令选项必须打开出征令面板")
    # 2026-11-26：生小鸡子选项的碰撞体开着时，官方按距离挑交互目标会挑到它（与路牌同点），
    # 路牌菜单只剩「哎哟~你干嘛~」、出征令主交互看不到。子选项一律关碰撞体。
    egg_awake = compact(body_of(read("Interactables", "BossRushSignOptionInteractables.cs"),
                                "public class BossRushEntryInteractable"))
    require(errors, egg_awake is not None and "this.interactCollider.enabled=false;" in egg_awake
            and "this.interactCollider.enabled=true;" not in egg_awake,
            "[Entry] 生小鸡子选项必须关掉自身交互碰撞体，否则会抢走路牌交互组、出征令消失")


RAISE_LADDERS = {
    "StandardLadder": ["FickleSkies", "Hex", "ForcedMarch", "Calamity", "FellAspect"],
    "HardLadder": ["Upheaval", "Blinded", "CutSupply", "ChainedCalamities", "DireHex", "CityAblaze", "BloodDebt", "DreadAspect"],
}
# 每级加码在悬赏令上就是下一档战规表里的那一条（名字与 check_tuning 里两张表一致）
RAISE_RULE_NAMES = {
    "FickleSkies": "天象无常", "Hex": "厄运", "ForcedMarch": "急行军", "Calamity": "天灾", "FellAspect": "凶相",
    "Upheaval": "天翻地覆", "Blinded": "失明", "CutSupply": "断补", "ChainedCalamities": "天灾连环", "DireHex": "厄运（重）",
    "CityAblaze": "焚城", "BloodDebt": "血债", "DreadAspect": "凶煞",
}
HARD_NAMES = ["磨刀", "凶相", "厄运", "急行军", "天象无常", "天灾"]
NIGHTMARE_NAMES = ["杀心", "凶煞", "失明", "断补", "血债", "厄运（重）", "急行军", "焚城", "天翻地覆", "天灾连环"]


def check_raise(errors):
    raise_src = read("WavesArena", "ArenaExpeditionRaise.cs")
    if raise_src is None:
        errors.append("[Raise] 缺少 WavesArena/ArenaExpeditionRaise.cs")
        return
    c = compact(raise_src)
    for array, rungs in RAISE_LADDERS.items():
        body = body_of(raise_src, "private static readonly ArenaExpeditionRung[] " + array + " =")
        found = re.findall(r"ArenaExpeditionRung\.(\w+)", body or "")
        require(errors, found == rungs, "[Raise] %s 应由易到难为 %s（实际 %s）：改梯子先改方案与本守卫" % (array, rungs, found))
    ladder_for = compact(body_of(raise_src, "internal static IList<ArenaExpeditionRung> LadderFor(ArenaExpeditionTier tier)"))
    require(errors, ladder_for == "switch(tier){caseArenaExpeditionTier.Standard:returnStandardLadder;"
            "caseArenaExpeditionTier.Hard:returnHardLadder;default:returnNoLadder;}",
            "[Raise] 只有标准、困难能加码（轻松是宽限档、噩梦已是顶档）")
    require(errors, "internalconstfloatRewardPerRung=0.05f;" in c, "[Raise] 每级加码奖励 +5%（改数值先改方案与本守卫）")
    apply = compact(body_of(raise_src, "internal static void ApplyTo(ArenaTrialProfile profile, ArenaExpeditionTier tier, int raise)"))
    for token in ("caseArenaExpeditionRung.FickleSkies:CopyOmens(profile,hard);", "caseArenaExpeditionRung.Upheaval:CopyOmens(profile,nightmare);",
                  "caseArenaExpeditionRung.Calamity:CopyHazards(profile,hard);", "caseArenaExpeditionRung.ChainedCalamities:CopyHazards(profile,nightmare);",
                  "caseArenaExpeditionRung.FellAspect:CopyAffixes(profile,hard);", "caseArenaExpeditionRung.DreadAspect:CopyAffixes(profile,nightmare);",
                  "caseArenaExpeditionRung.Blinded:profile.Blind=true;", "caseArenaExpeditionRung.CutSupply:profile.CutSupply=true;",
                  "caseArenaExpeditionRung.CityAblaze:profile.FireRing=true;", "caseArenaExpeditionRung.BloodDebt:profile.BloodDebt=true;"):
        require(errors, token in apply, "[Raise] ApplyTo 缺少 %s：加码这一级写在悬赏令上却没在局内生效" % token)
    require(errors, "profile.CurseLevel=Mathf.Max(profile.CurseLevel,hard.CurseLevel);" in apply
            and "profile.CurseLevel=Mathf.Max(profile.CurseLevel,nightmare.CurseLevel);" in apply,
            "[Raise] 厄运 / 厄运（重）两级必须把诅咒等级抬到困难 / 噩梦那一档")
    for key in ('"arena.expedition.raise."', '"arena.expedition.raise_best."'):
        require(errors, key in raise_src, "[Raise] 加码偏好键 %s 被改名：玩家上次的选择与「最高」标记会丢" % key)

    tuning = read("WavesArena", "ArenaExpeditionTuning.cs")
    rule_map = compact(body_of(tuning, "internal static ArenaExpeditionRule RaiseRule(ArenaExpeditionRung rung)"))
    tables = {"HardRules": HARD_NAMES, "NightmareRules": NIGHTMARE_NAMES}
    mapped = {}
    for m in re.finditer(r"caseArenaExpeditionRung\.(\w+):return(HardRules|NightmareRules)\[(\d+)\];", rule_map or ""):
        names = tables[m.group(2)]
        index = int(m.group(3))
        mapped[m.group(1)] = names[index] if index < len(names) else None
    if "caseArenaExpeditionRung.ForcedMarch:returnForcedMarch;" in (rule_map or ""):
        mapped["ForcedMarch"] = "急行军"
    dm = re.search(r"default:return(HardRules|NightmareRules)\[(\d+)\];", rule_map or "")
    if dm:
        names = tables[dm.group(1)]
        index = int(dm.group(2))
        mapped["DreadAspect"] = names[index] if index < len(names) else None
    for rung, name in RAISE_RULE_NAMES.items():
        require(errors, mapped.get(rung) == name,
                "[Raise] RaiseRule(%s) 应取战规「%s」（实际 %s）：战规表改了顺序要同步下标" % (rung, name, mapped.get(rung)))
    tc = compact(tuning)
    for token, why in (
            ("returnArenaExpeditionRaise.Has(tier,raise,ArenaExpeditionRung.ForcedMarch)?RestMultipliers[(int)ArenaExpeditionTier.Hard]:RestMultiplier(tier);",
             "标准加到急行军照困难的休整倍率"),
            ("returnKeepsMilestoneRest(tier)&&!ArenaExpeditionRaise.Has(tier,raise,ArenaExpeditionRung.ForcedMarch);", "急行军一级取消额外休整"),
            ("returnHidesMapBeacons(tier)||ArenaExpeditionRaise.Has(tier,raise,ArenaExpeditionRung.Blinded);", "困难加到失明 M 图不标首领"),
            ("returnHellCashMultiplier(tier)*ArenaExpeditionRaise.RewardFactor(ArenaExpeditionRaise.Clamp(tier,raise));", "无间奖金乘加码倍率"),
            ("intcount=VictoryChestCount(baseCount,tier,waves,poolCount);", "通关箱先按四参数版本算再乘加码"),
    ):
        require(errors, token in tc, "[Raise] ArenaExpeditionTuning：" + why)

    trials_tuning = read("WavesArena", "Trials", "ArenaTrialsTuning.cs")
    profile_for = compact(body_of(trials_tuning, "internal static ArenaTrialProfile ProfileFor(ArenaExpeditionTier tier, int raise)"))
    require(errors, profile_for == "ArenaTrialProfileprofile=ProfileFor(tier);if(ArenaExpeditionRaise.Clamp(tier,raise)<=0)returnprofile;"
            "profile=profile.Clone();ArenaExpeditionRaise.ApplyTo(profile,tier,raise);returnprofile;",
            "[Raise] ProfileFor(tier, raise) 必须先复制再改（共享的困难 / 噩梦配置被改，下一局与悬赏令文案都会跟着变）")
    trials = compact(read("WavesArena", "Trials", "ArenaTrials.cs"))
    require(errors, "Profile=ArenaTrialsTuning.ProfileFor(Tier,run.Order.Raise);" in trials,
            "[Raise] 局内战规必须按本局加码取配置")

    expedition = read("WavesArena", "WavesArenaRuntimeModule_Expedition.cs")
    live = compact(body_of(expedition, "private int LiveExpeditionRaise()"))
    require(errors, live == "if(ArenaExpeditionState.Current==null||!IsActive||owner==null||owner.IsModeDActive)return0;"
            "returnArenaExpeditionState.CurrentRaise;",
            "[Raise] LiveExpeditionRaise 必须在没有本局快照、竞技场未激活或 Mode D 借用时为 0")
    ec = compact(expedition)
    require(errors, "ArenaExpeditionTuning.VictoryChestCount(baseCount,run.Order.Tier,run.Waves,run.PoolCount,run.Order.Raise);" in ec
            and "ArenaExpeditionTuning.HellCashMultiplier(ArenaExpeditionState.CurrentTier,ArenaExpeditionState.CurrentRaise);" in ec,
            "[Raise] 通关箱件数与无间奖金必须带上本局加码")
    recorded = compact(body_of(expedition, "private void RecordExpeditionRaiseCleared()"))
    require(errors, "if(run==null||run.Order.IsHell||owner==null||owner.IsModeDActive)return;" in recorded
            and "ArenaExpeditionRaise.RecordCleared(run.Order.Tier,run.Order.Raise)" in recorded,
            "[Raise] 通关记最高加码只认非无间、非 Mode D 借用的出征令局")
    victory = compact(read("WavesArena", "WavesArenaRuntimeModule_VictoryRewards.cs"))
    require(errors, "UnlockExpeditionNightmareClear();RecordExpeditionRaiseCleared();" in victory,
            "[Raise] 通关结算必须记下这一档通关过的最高加码")

    order = compact(read("WavesArena", "ArenaExpeditionOrder.cs"))
    require(errors, "Raise=ArenaExpeditionRaise.Clamp(tier,raise);" in order and "ArenaExpeditionRaise.Remember(Tier,Raise);" in order,
            "[Raise] 出征令构造时按档夹取加码级数，立誓时记下")

    panel_src = read("WavesArena", "ArenaExpeditionOrderPanel.cs")
    panel = compact(panel_src)
    require(errors, "newArenaExpeditionOrder(lineup,tier,length,CurrentRaise);" in panel,
            "[Raise] 立誓必须把所选加码带进出征令")
    refresh = compact(body_of(panel_src, "private void RefreshAll(bool animate)"))
    show_at = refresh.find("raiseRow.Show(tier,CurrentRaise,animate);")
    require(errors, 0 <= show_at < refresh.find("RebuildNotice(animate);"),
            "[Raise] RefreshAll 必须在重写悬赏令之前把加码台阶演到当前级")
    notice = compact(body_of(panel_src, "private void RebuildNotice(bool animate)"))
    require(errors, "ArenaExpeditionTuning.IsSuperseded(tier,raise,rules[i])" in notice
            and "ArenaExpeditionTuning.RaiseRule(ladder[i])" in notice,
            "[Raise] 悬赏令必须列出加码战规，并略去被加码换掉的档位战规")
    row = read("WavesArena", "ArenaExpeditionRaiseRow.cs")
    require(errors, row is not None and "if(blocked!=null&&blocked())return;" in compact(row),
            "[Raise] 加码台阶在立誓 / 等等之后不再响应点击")


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
    check_raise(errors)
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
