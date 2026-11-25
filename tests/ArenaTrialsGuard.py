#!/usr/bin/env python3
"""ArenaTrialsGuard — 出征令战规机制化（2026-11-25 owner 拍板）在局内的服务对象 ArenaTrials 的结构不变式。

战规表（哪档几条、叫什么）由 ArenaExpeditionOrderGuard 钉；本守卫钉局内实现里容易被后续改动悄悄打断的点：

1. 归属：唯一 owner 是 WavesArenaRuntimeModule_Expedition——BeginExpeditionRun 在开本局快照之后 Begin，
   EndExpeditionRun 第一句 End（切图、模块销毁都走它）；IsExpeditionTrialLive 认竞技场激活、非 Mode D 借用、快照还在。
2. 接线：开波在 SpawnNextEnemy 出完标题幕之后、收波在 ProceedAfterWaveFinished 推进波次之前；
   普通 Boss 与内容 Boss 两处乘战帖的入口都把首领登记进战规；掉落入口加词缀件数、断补减半弹药（两条路径都要）。
3. 生命周期（AGENTS §4.6 / §4.12）：全局 Health.OnHurt / OnDead 由私有布尔守着只订一次、Shutdown 退订；
   Shutdown 幂等、摘掉玩家与敌人身上的全部修饰；OnDestroy 也走 Shutdown；Update 第一件事问 isLive，不活了当帧自毁。
   收波不清 waveBosses（官方先发实例 OnDeadEvent → 掉落 → HandleBossDeath → 收波，后发静态 OnDead）。
4. 补丁：每个前缀第一句 `ArenaTrials.Active == null` 早返（不是出征令的局 O(1) 放行）。
5. 档位：ProfileFor 的困难 / 噩梦数值与悬赏令文案同源（ArenaExpeditionTuning 从 ProfileFor 取数）；轻松 / 标准只有天色。
6. 天色 / 天象自绘，不碰官方 WeatherManager / TimeOfDay（官方雨天改元素系数、Volume 驱动寒冷炎热与夜视，与「天色只换画面」冲突）；
   降水粒子室内停掉（官方 WeatherFxControl 同样只在室外下雨）。
7. 表现层：色调层画布挂在 ArenaExpeditionHud（已登记 PersistentHudVisibilityGuard），随角标一起过官方 HUD 显隐与暂停双门。
8. 诅咒：血债只让击杀回血开闸（开闸在 finally 里关）；断补禁药挡在 CA_UseItem.OnStart；失明的血条量化只看本局敌方身体。
9. 天灾：Tick 第一句按档早返、只在本波开打期间排程；收波 / 收尾当帧还原冰封减速、雾里 AI 视觉参数（记原值写回）与推力；
   推力每人每帧只下一次「自身输入 + 推力」、翻滚中不推；伤害只走 HurtNeutral；不放碰撞体；落点贴 NavMesh；
   九种都能起；暴雨减半毒雾 / 熔缝；墨潮翻滚可穿；瞬时灯淡入为 0（否则当帧自毁）。
10. 词缀：HandleBossDeath 两处、刷怪失败与批量刷怪最终校验的推波之前都先问 ExpeditionHoldsWaveEnd（分身 / 双生还在就压着），10 秒自检压着时不强推、
   战规没了还压着就当场放开；放开只在 ArenaTrials.Update 最后一句交回模块推波；压收波有上限、超时散掉剩下的身体。
   词缀身体走阵营安全网、不进本波座位、不登记掉落、死了不掉箱；额外掉落只算本体；内容 Boss 只挂身法 / 死相；
   几条词缀取自不同类；护驾减伤落在官方七个元素系数上；本体死亡结算幂等、亲卫影从随主将散；双生一对最多拉起两次；
   亡魂 / 殉爆只走 HurtNeutral、翻滚可躲；不放碰撞体；血条名缀补丁第一句早返；收尾散掉身体、摘掉生命修饰。
   身体血量在落地登记时按首领当时的上限折（变异词条入场后才挂）；狂暴体型以进入狂暴那刻为底；拉起途中这一对已封住，
   在路上的那只作废；身体移出列表时丢掉它那条生命修饰记录。
11. 焚城：开波第一句按档早返、Tick 第一句没火圈就返回；圈心是刷新点重心贴 NavMesh（落不下用最近刷新点）、
   半径是最远刷新点（开波时玩家更远按玩家）+ 边距且有下限；按本波开打时长先预告落点再收紧，平常 / 压轴两档倍数；
   只在燃起之后、退去之前烧人，只烧玩家、按跳结算、进出圈不清计时、火伤翻滚躲不掉、只经 HurtNeutral；
   收波退去时摘 M 图标记与热浪，Shutdown 全收；不放碰撞体、不 new 材质；火舌只在看得见的那段弧上发、地面射线有每帧上限；
   燃起结束那帧补画整圈；火头按秒发粒子；灯关前亮度清零；收紧横扫演不出来时只擂鼓、下回再写。

文本守卫防不住「保留 token、杀掉执行路径」，行为正确性仍要靠 L3 实机清单。
"""
import io
import os
import re
import sys

from cs_source_util import clean_source

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))


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


def require(errors, ok, message):
    if not ok:
        errors.append(message)


TRIALS = ("WavesArena", "Trials")
MODULE = "WavesArenaRuntimeModule_"


def check_ownership(errors):
    expedition = read("WavesArena", MODULE + "Expedition.cs")
    begin = compact(body_of(expedition, "private void BeginExpeditionRun(ArenaExpeditionOrder order, List<EnemyPresetInfo> filtered)"))
    state_at = begin.find("ArenaExpeditionState.Begin(run);")
    trials_at = begin.find("ArenaTrials.Begin(this,run,IsExpeditionTrialLive);")
    require(errors, 0 <= state_at < trials_at,
            "[Owner] BeginExpeditionRun 必须在 ArenaExpeditionState.Begin 之后 ArenaTrials.Begin(this, run, IsExpeditionTrialLive)")
    end = compact(body_of(expedition, "private static void EndExpeditionRun()"))
    require(errors, end.startswith("ArenaTrials.End();"),
            "[Owner] EndExpeditionRun 第一句必须 ArenaTrials.End()（切图 / 模块销毁都经这里收掉战规）")
    live = compact(body_of(expedition, "private bool IsExpeditionTrialLive()"))
    require(errors, live == "returnIsActive&&owner!=null&&!owner.IsModeDActive&&ArenaExpeditionState.Current!=null;",
            "[Owner] IsExpeditionTrialLive 必须认竞技场激活、非 Mode D 借用、本局快照还在")

    for sig, preset in (("private void ApplyExpeditionTierToSpawnedBoss(CharacterMainControl character, EnemyPresetInfo preset)", "preset"),
                        ("private void ApplyExpeditionTierToContentBoss(CharacterMainControl character)", "null")):
        body = compact(body_of(expedition, sig))
        require(errors, body == "ArenaExpeditionBossScaling.ApplyNow(character,LiveExpeditionTier());ArenaTrials.NotifyBossJoined(character,"
                + preset + ");",
                "[Wiring] %s 必须在乘战帖之后把首领登记进战规（内容 Boss 不给预设：不叫分身 / 护卫）" % sig.split("(")[0].split()[-1])

    wave = compact(body_of(expedition, "private void BeginExpeditionWave()"))
    require(errors, "ArenaTrials.NotifyWaveStart(CurrentEnemyIndex,finale);" in wave and "run.IsFinaleWave(CurrentEnemyIndex)" in wave,
            "[Wiring] BeginExpeditionWave 必须按当前波号与压轴通知战规开波")
    require(errors, compact(body_of(expedition, "private static void EndExpeditionWave()")) == "ArenaTrials.NotifyWaveEnd();",
            "[Wiring] EndExpeditionWave 必须通知战规收波")

    spawning = compact(read("WavesArena", MODULE + "BossSpawning.cs"))
    require(errors, "PlayExpeditionTitleCard(preset);BeginExpeditionWave();" in spawning,
            "[Wiring] SpawnNextEnemy 出完标题幕之后必须紧跟 BeginExpeditionWave()")
    proceed = compact(body_of(read("WavesArena", MODULE + "WaveDeaths.cs"), "internal void ProceedAfterWaveFinished()"))
    end_at = proceed.find("EndExpeditionWave();")
    index_at = proceed.find("CurrentEnemyIndex++;")
    require(errors, 0 <= end_at < index_at, "[Wiring] ProceedAfterWaveFinished 必须在推进波次之前 EndExpeditionWave()")

    loot = compact(read("WavesArena", MODULE + "BossLootEvent.cs"))
    clamp_at = loot.find("baseCount=Mathf.Clamp(baseCount,7,15);baseCount+=ExpeditionAffixBonusLoot(bossMain);")
    randomize_at = loot.find("RandomizeBossLoot_LootAndRewards(")
    require(errors, 0 <= clamp_at < randomize_at,
            "[Loot] 词缀额外掉落必须在 [7,15] 夹取之后、RandomizeBossLoot 之前加进 baseCount")
    require(errors, loot.count("ExpeditionAfterBossLoot(bossMain);") == 2
            and "FinalizeBossRushLootboxPathTracking(bossMain);ExpeditionAfterBossLoot(bossMain);return;" in loot,
            "[Loot] 断补减半弹药必须覆盖保持原版掉落与随机掉落两条路径")
    require(errors, compact(body_of(expedition, "private static void ExpeditionAfterBossLoot(CharacterMainControl boss)"))
            == "ArenaTrialstrials=ArenaTrials.Active;if(trials!=null&&trials.Curses!=null)trials.Curses.HalveAmmo(boss);",
            "[Loot] ExpeditionAfterBossLoot 必须只在战规活着时调断补减半")


def check_lifecycle(errors):
    trials = read(*TRIALS, "ArenaTrials.cs")
    if trials is None:
        errors.append("[Lifecycle] 缺少 WavesArena/Trials/ArenaTrials.cs")
        return
    setup = compact(body_of(trials, "private void Setup(WavesArenaRuntimeModule owner, ArenaExpeditionRun run, Func<bool> live)"))
    require(errors, "if(!hurtSubscribed){Health.OnHurt+=OnAnyHurt;hurtSubscribed=true;}" in setup
            and "if(!deadSubscribed){Health.OnDead+=OnAnyDeath;deadSubscribed=true;}" in setup,
            "[Lifecycle] Health.OnHurt / OnDead 订阅必须由私有布尔守着只订一次")
    shutdown = compact(body_of(trials, "private void Shutdown()"))
    require(errors, shutdown.startswith("if(shutDown)return;shutDown=true;"), "[Lifecycle] Shutdown 必须幂等")
    require(errors, "if(hurtSubscribed){Health.OnHurt-=OnAnyHurt;hurtSubscribed=false;}" in shutdown
            and "if(deadSubscribed){Health.OnDead-=OnAnyDeath;deadSubscribed=false;}" in shutdown,
            "[Lifecycle] Shutdown 必须退订 Health.OnHurt / OnDead")
    require(errors, 'RuntimeStatModifierTracker.RemoveAll(playerRecords,"ArenaTrialsplayer");' in shutdown
            and "RuntimeStatModifierTracker.RemoveAll(pair.Value," in shutdown and "foeRecords.Clear();" in shutdown,
            "[Lifecycle] Shutdown 必须摘掉玩家与每个敌人身上的战规修饰（角色 Item 跨场景存活）")
    require(errors, "parts[i].Shutdown();" in shutdown, "[Lifecycle] Shutdown 必须逐个收掉各部分")
    require(errors, "ArenaExpeditionHud.SetAmbience(ArenaExpeditionAmbience.None);" in shutdown
            and 'ArenaExpeditionHud.SetTrialLines(string.Empty,string.Empty,false);' in shutdown,
            "[Lifecycle] Shutdown 必须收掉色调层与角标")
    destroy = compact(body_of(trials, "private void OnDestroy()"))
    require(errors, destroy.startswith("Shutdown();"), "[Lifecycle] OnDestroy 必须走 Shutdown（切图时对象可能先于 End 被销毁）")
    update = compact(body_of(trials, "private void Update()"))
    live_at = update.find("live=isLive!=null&&isLive();")
    end_at = update.find("if(Active==this)End();")
    tick_at = update.find(".Tick(")
    require(errors, update.startswith("if(shutDown)return;") and 0 <= live_at < end_at < tick_at
            and "parts[i].Tick(dt);" in update,
            "[Lifecycle] Update 必须先问 isLive，竞技场结束当帧自毁，之后才驱动各部分")
    wave_end = compact(body_of(trials, "internal static void NotifyWaveEnd()"))
    require(errors, "waveBosses.Clear();" not in wave_end,
            "[Lifecycle] 收波不能清 waveBosses：官方先发实例 OnDeadEvent（→ 收波）、后发静态 OnDead，清了就认不出刚倒下的首领")
    begin = compact(body_of(trials, "internal static void Begin(WavesArenaRuntimeModule arena, ArenaExpeditionRun run, Func<bool> isLive)"))
    require(errors, begin.startswith("End();"), "[Lifecycle] Begin 第一句必须 End()（同一时间只有一份战规）")


def check_patches(errors):
    patches = read(*TRIALS, "ArenaTrialsPatches.cs")
    if patches is None:
        errors.append("[Patch] 缺少 WavesArena/Trials/ArenaTrialsPatches.cs")
        return
    targets = re.findall(r'\[HarmonyPatch\(typeof\((\w+)\),\s*"(\w+)"\)\]', patches)
    expected = [("CA_UseItem", "OnStart"), ("CA_UseItem", "OnUpdateAction"), ("HealthBar", "Refresh"),
                ("HealthBar", "ShowDamageBar"), ("HealthBar", "RefreshCharacterIcon"), ("Health", "AddHealth")]
    require(errors, targets == expected, "[Patch] 战规补丁目标应为 %s（实际 %s）" % (expected, targets))
    heads = re.findall(r"\[Harmony(Prefix|Postfix)\]\s*internal static \w+ (?:Prefix|Postfix)\([^)]*\)\s*\{\s*([^\n]*)", patches)
    require(errors, len(heads) == len(expected) and all(h[1].startswith("if (ArenaTrials.Active == null") for h in heads),
            "[Patch] 每个战规补丁第一句必须 ArenaTrials.Active == null 早返（不是出征令的局 O(1) 放行）")
    postfixes = [i for i, h in enumerate(heads) if h[0] == "Postfix"]
    require(errors, len(heads) == len(expected) and postfixes == [expected.index(("HealthBar", "RefreshCharacterIcon"))],
            "[Patch] 只有血条名缀用后缀（官方在方法里写名字，要接在它后面），其余一律前缀")

    curses = read(*TRIALS, "ArenaTrialsCurses.cs")
    death = compact(body_of(curses, "internal override void OnAnyDeath(Health health, DamageInfo info)"))
    require(errors, "allowHeal=true;try{" in death and "finally{allowHeal=false;}" in death,
            "[Curse] 血债的击杀回血开闸后必须在 finally 里关上")
    require(errors, "if(killer==null||!killer.IsMainCharacter)return;" in death,
            "[Curse] 血债只认主玩家亲手击杀")
    blocked = compact(body_of(curses, "internal bool HealBlockedFor(Health health)"))
    require(errors, blocked == "returnTrials.Profile.BloodDebt&&!allowHeal&&health!=null&&health.IsMainCharacterHealth;",
            "[Curse] 血债挡回血只挡主玩家、且击杀回血能过")
    blind = compact(body_of(curses, "internal static int BlindStepsFor(Health health)"))
    require(errors, "trials.IsFoe(character)?ArenaTrialsTuning.BlindHealthBarSteps:0" in blind and "!trials.Profile.Blind" in blind,
            "[Curse] 失明的血条量化只看本局敌方身体")
    halve = compact(body_of(curses, "internal void HalveAmmo(CharacterMainControl boss)"))
    require(errors, "GameplayDataSettings.Tags.Bullet" in halve and "Mathf.Max(1," in halve and "!Trials.Profile.CutSupply" in halve,
            "[Curse] 断补减半只动带子弹标签的堆叠、至少留 1 发、只在断补下")


def check_profiles(errors):
    tuning = compact(read(*TRIALS, "ArenaTrialsTuning.cs"))
    hard = ("privatestaticreadonlyArenaTrialProfileHard=newArenaTrialProfile{OmenWavesMin=4,OmenWavesMax=5,"
            "HazardSecondsMin=30f,HazardSecondsMax=45f,HazardDoubleChance=0f,Affixes=1,FinaleAffixes=2,AffixDistinctKinds=true,CurseLevel=1,};")
    nightmare = ("privatestaticreadonlyArenaTrialProfileNightmare=newArenaTrialProfile{OmenWavesMin=2,OmenWavesMax=3,"
                 "HazardSecondsMin=15f,HazardSecondsMax=25f,HazardDoubleChance=0.25f,Affixes=3,FinaleAffixes=4,AffixDistinctKinds=true,"
                 "CurseLevel=2,Blind=true,CutSupply=true,BloodDebt=true,FireRing=true,};")
    require(errors, hard in tuning, "[Profile] 困难档：每 4–5 波变天、30–45 秒一阵天灾、词缀 1 / 压轴 2、轻厄运（改数值先改方案与本守卫）")
    require(errors, nightmare in tuning,
            "[Profile] 噩梦档：每 2–3 波变天、15–25 秒一阵且 25% 叠两种、词缀 3 / 压轴 4、重厄运、失明 / 断补 / 血债 / 焚城")
    require(errors, "privatestaticreadonlyArenaTrialProfileSkiesOnly=newArenaTrialProfile{Skies=true};" in tuning
            and "default:returnSkiesOnly;" in tuning,
            "[Profile] 轻松 / 标准只有天色（只换画面，不改数值）")
    expedition_tuning = compact(read("WavesArena", "ArenaExpeditionTuning.cs"))
    require(errors, "HardTrials=ArenaTrialsTuning.ProfileFor(ArenaExpeditionTier.Hard);" in expedition_tuning
            and "NightmareTrials=ArenaTrialsTuning.ProfileFor(ArenaExpeditionTier.Nightmare);" in expedition_tuning,
            "[Profile] 悬赏令上的战规文案必须从 ProfileFor 取数（数值只写一处）")
    require(errors, tuning.count("internalconstfloatCutSupplyAmmoDropFactor=0.5f;") == 1
            and "internalconstfloatCutSupplyUseTimeMultiplier=2f;" in tuning
            and "internalconstfloatBloodDebtMaxHealthPerHit=0.03f;" in tuning
            and "internalconstfloatBloodDebtMaxHealthCap=0.30f;" in tuning,
            "[Profile] 断补（读条 ×2、弹药 ×0.5）与血债（每下 3%、封顶 30%）与 owner 拍板不一致")


def check_sky(errors):
    for name in sorted(os.listdir(os.path.join(ROOT, *TRIALS))):
        if not name.endswith(".cs"):
            continue
        src = read(*TRIALS, name)
        for banned in ("WeatherManager", "SetForceWeather", "TimeOfDayController", "TimeOfDayPost"):
            require(errors, banned not in src,
                    "[Sky] %s 引用了官方 %s：天色 / 天象自绘，不改官方天气（雨天元素系数、寒冷炎热与夜视都会跟着变）" % (name, banned))
    hud = read("WavesArena", "ArenaExpeditionHud.cs")
    for banned in ("WeatherManager", "SetForceWeather"):
        require(errors, banned not in (hud or ""), "[Sky] ArenaExpeditionHud 引用了官方 %s" % banned)
    sky = read(*TRIALS, "ArenaTrialsSky.cs")
    require(errors, sky is not None and "IsInDoor" in sky, "[Sky] 降水粒子必须在室内停掉（同官方 WeatherFxControl）")
    sky_tick = compact(body_of(sky, "internal override void Tick(float deltaTime)"))
    require(errors, "if(ArenaInkTitleCard.Playing)" in sky_tick and "ArenaExpeditionHud.PlayOmenSweep(ArenaTrialsTuning.OmenName(omen));" in sky_tick,
            "[Sky] 变天横扫必须等压轴 / 噩梦标题幕收完再出（一次只说一件事，两幅大字叠在一起显廉价）")
    require(errors, 'CreateFullScreen(root,"Flash",BossRushInkSkin.GetGlowSprite());' in compact(hud),
            "[Sky] 雷暴闪光是天边一团辉光，不是整屏平涂的白（owner：无塑料感）")
    update = compact(body_of(hud, "private void Update()"))
    require(errors, "canvas.enabled=visible;" in update and "ambienceCanvas.enabled=visible;" in update,
            "[HUD] 色调层画布必须和角标同一个显隐门（官方 HUD 显隐与暂停）")
    require(errors, "BossRushUILayers.ScreenAmbience" in compact(hud),
            "[HUD] 色调层画布必须在 ScreenAmbience 层（压在 HUD 之下，不挡交互）")


HAZARD_RUNS = ("ArenaTrialMeteorRain", "ArenaTrialLightning", "ArenaTrialMiasma", "ArenaTrialRift", "ArenaTrialInkTide",
               "ArenaTrialGale", "ArenaTrialVortex", "ArenaTrialFrost", "ArenaTrialFogmire")


def check_hazards(errors):
    hazards = read(*TRIALS, "ArenaTrialsHazards.cs")
    fx = read(*TRIALS, "ArenaTrialsFx.cs")
    run_files = ("ArenaTrialsHazardRuns.cs", "ArenaTrialsHazardFields.cs")
    run_sources = [read(*TRIALS, name) for name in run_files]
    if hazards is None or fx is None or None in run_sources:
        errors.append("[Hazard] 缺少 WavesArena/Trials/ 下的天灾文件（Hazards / HazardRuns / HazardFields / Fx）")
        return
    runs = "\n".join(run_sources)
    flat = compact(hazards)
    runs_flat = compact(runs)
    require(errors, "privateboolEnabled{get{returnTrials.Profile.HasHazards;}}" in flat,
            "[Hazard] Enabled 必须只看本档有没有天灾")
    tick = compact(body_of(hazards, "internal override void Tick(float deltaTime)"))
    require(errors, tick.startswith("if(!Enabled)return;if(Trials.WaveLive)Schedule(deltaTime);"),
            "[Hazard] Tick 第一句必须 Enabled 早返（轻松 / 标准 O(1)），只在本波开打期间排程")
    for sig in ("internal override void OnWaveEnd()", "internal override void Shutdown()"):
        body = compact(body_of(hazards, sig))
        require(errors, "RetireAll(" in body and "ClearTerrainEffects();" in body,
                "[Hazard] %s 必须收掉所有天灾并当帧还原冰封 / 雾泽 / 推力" % sig.split("(")[0].split()[-1])
    clear = compact(body_of(hazards, "private void ClearTerrainEffects()"))
    require(errors, "pushes.Clear();knocks.Clear();" in clear and "RestoreAllSight();" in clear
            and "frostedFoes.Clear();Trials.MarkFoeStatsDirty();" in clear and "Trials.MarkPlayerStatsDirty();" in clear,
            "[Hazard] ClearTerrainEffects 必须清推力、摘冰封减速、还原雾里 AI 的视觉参数")
    blind = compact(body_of(hazards, "internal ArenaTrialFogBlind(AICharacterController ai)"))
    restore = compact(body_of(hazards, "internal void Restore()"))
    require(errors, "forceTrace=ai.forceTracePlayerDistance;" in blind and "sight=ai.sightDistance;" in blind
            and "Ai.forceTracePlayerDistance=forceTrace;" in restore and "Ai.sightDistance=sight;" in restore,
            "[Hazard] 雾泽改的 AI 强制追踪距离与视距必须记原值、原样写回（竞技场首领原本是 500 的强制追踪）")
    fog = compact(body_of(hazards, "private void RefreshFog()"))
    require(errors, "state.Restore();blinded.Remove(foe);" in fog and "!Trials.IsFoe(foe)" in fog,
            "[Hazard] 出雾的、死掉的、不再是本局敌人的 AI 都要还原")
    pushes = compact(body_of(hazards, "private void ApplyPushes()"))
    require(errors, "character.Dashing)continue;" in pushes
            and "character.SetForceMoveVelocity(input*speed+push);" in pushes and pushes.endswith("pushes.Clear();"),
            "[Hazard] 推力每帧每人只下一次「自身输入 + 推力」的强制位移，翻滚中的不推，用完清表")
    require(errors, ".Hurt(" not in flat and ".Hurt(" not in runs_flat,
            "[Hazard] 天灾伤害只能经 ArenaTrials.HurtNeutral（不吃难度倍率、不看护甲、只波及玩家与本局敌方身体）")
    sources = [("ArenaTrialsHazards.cs", flat), ("ArenaTrialsFx.cs", compact(fx))]
    sources += [(name, compact(src)) for name, src in zip(run_files, run_sources)]
    for name, src in sources:
        require(errors, re.search(r"AddComponent<\w*(Collider|Rigidbody)\w*>", src) is None and "NavMeshObstacle" not in src,
                "[Hazard] %s 不许放碰撞体 / 刚体 / 导航障碍（天灾只是表现 + 结算，不做逐图审核）" % name)
    require(errors, "SpawnPositionHelper.TrySampleNavMesh(raw,outresolved,0f,2.5f)" in flat,
            "[Hazard] 落点必须贴 NavMesh")
    unleash = compact(body_of(hazards, "private void Unleash(ArenaTrialHazard kind)"))
    for run in HAZARD_RUNS:
        require(errors, ("run=new%s(this);" % run) in unleash and ("internalsealedclass%s:ArenaTrialHazardRun" % run) in runs_flat,
                "[Hazard] 九种天灾都要能起：缺 %s" % run)
    require(errors, runs_flat.count("life=ArenaTrialsTuning.MiasmaSeconds*Hazards.DurationFactor;") == 1
            and runs_flat.count("ArenaTrialsTuning.RiftSeconds*Hazards.DurationFactor;") == 1,
            "[Hazard] 暴雨要把毒雾与熔缝的持续时间减半（读 Hazards.DurationFactor）")
    require(errors, "ElementTypes.physics,true)" in compact(body_of(runs, "private void Sweep(float along)")),
            "[Hazard] 墨潮必须翻滚可穿（HurtShare 的 dodgeable=true）")
    flash = compact(body_of(fx, "internal static void Flash(Vector3 at, Color color, float intensity, float range, float seconds)"))
    require(errors, "BossRushFxLightFade.Attach(light,intensity,0f);" in flash,
            "[Fx] 瞬时灯的淡入必须是 0：带淡入时 level 从 0 起步，紧跟 FadeOut 会当帧自毁、一下都不亮")
    neutral = compact(body_of(read(*TRIALS, "ArenaTrials.cs"),
                              "internal static bool HurtNeutral(Health health, float damage, Vector3 from, bool dodgeable,"))
    require(errors, "if(element!=ElementTypes.physics&&info.elementFactors!=null)" in neutral
            and "info.ignoreDifficulty=true;" in neutral and "info.ignoreArmor=true;" in neutral,
            "[Hazard] HurtNeutral 必须不吃难度倍率、不看护甲，元素只在非物理时追加")


AFFIX_FILES = ("ArenaTrialsAffixes.cs", "ArenaTrialsAffixFx.cs")


def check_affixes(errors):
    expedition = read("WavesArena", MODULE + "Expedition.cs")
    deaths = read("WavesArena", MODULE + "WaveDeaths.cs")
    handle = compact(body_of(deaths, "internal void HandleBossDeath(CharacterMainControl bossMain, DamageInfo damageInfo)"))
    require(errors, handle.count("ProceedAfterWaveFinished();") == 2
            and handle.count("if(ExpeditionHoldsWaveEnd(bossMain))return;ProceedAfterWaveFinished();return;") == 2,
            "[Affix] HandleBossDeath 两处推波之前都必须先问 ExpeditionHoldsWaveEnd(bossMain)（分身 / 双生还在就压着收波）")
    failed = compact(body_of(deaths, "internal void OnBossSpawnFailed(EnemyPresetInfo preset)"))
    require(errors, failed.count("ProceedAfterWaveFinished();") == 2
            and failed.count("if(ExpeditionHoldsWaveEnd(null))return;ProceedAfterWaveFinished();") == 2,
            "[Affix] OnBossSpawnFailed 两处推波之前也要先问 ExpeditionHoldsWaveEnd(null)（同波另一只首领刷不出来时，先倒下那只的分身还在）")
    batch = compact(body_of(read("WavesArena", MODULE + "BossSpawning.cs"), "private async UniTaskVoid SpawnMultipleBossesWithVerificationAsync("))
    require(errors, batch.count("ProceedAfterWaveFinished();") == 1
            and "if(!ExpeditionHoldsWaveEnd(null))owner.ProceedAfterWaveFinished();" in batch,
            "[Affix] 批量刷怪最终校验推波之前也要先问 ExpeditionHoldsWaveEnd(null)")
    tick = compact(read("WavesArena", MODULE + "Tick.cs"))
    require(errors, "if(AdvanceWaveIntegrityCheck(deltaTime)){if(!ExpeditionHoldsWave())owner.TryFixStuckWaveIfNoBossAlive();}" in tick,
            "[Affix] 10 秒自检在词缀压着收波时不得强推")
    held = compact(body_of(expedition, "private bool ExpeditionHoldsWave()"))
    require(errors, held == "if(!expeditionWaveHeld)returnfalse;if(ArenaTrials.Active!=null)returntrue;ExpeditionReleaseHeldWave();returntrue;",
            "[Affix] ExpeditionHoldsWave：战规还在才继续压；战规没了却还压着的当场放开推波（否则多首领档剩余数为 0、自检也救不回来）")
    release = compact(body_of(expedition, "internal void ExpeditionReleaseHeldWave()"))
    require(errors, release.startswith("if(!expeditionWaveHeld)return;expeditionWaveHeld=false;") and release.endswith("ProceedAfterWaveFinished();"),
            "[Affix] ExpeditionReleaseHeldWave 只在压着时推一次波")
    hold = compact(body_of(expedition, "private bool ExpeditionHoldsWaveEnd(CharacterMainControl boss)"))
    require(errors, "if(!trials.Affixes.HoldWaveFor(boss))returnfalse;expeditionWaveHeld=true;" in hold,
            "[Affix] ExpeditionHoldsWaveEnd 必须按 Affixes.HoldWaveFor 置压波标记")
    for sig in ("private void BeginExpeditionRun(ArenaExpeditionOrder order, List<EnemyPresetInfo> filtered)", "private void BeginExpeditionWave()"):
        require(errors, "expeditionWaveHeld=false;" in compact(body_of(expedition, sig)),
                "[Affix] %s 必须清掉压波标记" % sig.split("(")[0].split()[-1])
    spawn = compact(body_of(expedition, "internal async UniTask<CharacterMainControl> SpawnExpeditionAffixBodyAsync("))
    net_at = spawn.find("if(!Team.IsEnemy(Teams.player,character.Team))character.SetTeam(Teams.wolf);")
    active_at = spawn.find("character.gameObject.SetActive(true);")
    require(errors, 0 <= net_at < active_at, "[Affix] 词缀身体激活之前必须过阵营安全网（AGENTS §4.5）")
    require(errors, "character.dropBoxOnDead=false;" in spawn and "SpawnedEnemyActivationHelper.ReleaseFromPlayerDistanceSleep(character);" in spawn,
            "[Affix] 词缀身体死了不掉箱、要解除官方距离休眠")
    for banned in ("CurrentBoss", "CurrentWaveBosses", "RegisterBossRandomLootTracking", "BossesInCurrentWave"):
        require(errors, banned not in spawn, "[Affix] 词缀身体不进本波座位、不登记掉落（出现了 %s）" % banned)

    trials = read(*TRIALS, "ArenaTrials.cs")
    update = compact(body_of(trials, "private void Update()"))
    require(errors, update.endswith("if(Affixes!=null&&Affixes.ConsumeWaveRelease()&&arena!=null)arena.ExpeditionReleaseHeldWave();"),
            "[Affix] 压着的收波只能在 ArenaTrials.Update 最后一句交回模块（推波可能当帧通关、收掉战规）")
    foe = compact(body_of(trials, "private void ApplyFoeStats(CharacterMainControl foe)"))
    require(errors, "for(inti=0;i<ElementFactorStats.Length;i++)Multiply(foe,ElementFactorStats[i],taken,records);" in foe,
            "[Affix] 护驾减伤必须乘在官方元素系数上")
    stats = re.findall(r'"(ElementFactor_\w+)"', trials or "")
    require(errors, sorted(set(stats)) == sorted(["ElementFactor_Physics", "ElementFactor_Fire", "ElementFactor_Poison",
                                                "ElementFactor_Electricity", "ElementFactor_Space", "ElementFactor_Ghost", "ElementFactor_Ice"]),
            "[Affix] 受伤倍数要覆盖官方 Health.ElementFactor 读的全部七个元素系数")

    affix = read(*TRIALS, "ArenaTrialsAffixes.cs")
    fx = read(*TRIALS, "ArenaTrialsAffixFx.cs")
    if affix is None or fx is None:
        errors.append("[Affix] 缺少 ArenaTrialsAffixes.cs / ArenaTrialsAffixFx.cs")
        return
    require(errors, compact(body_of(affix, "internal int BonusLootFor(CharacterMainControl boss)"))
            == "ArenaAffixBearerbearer=FindBearer(boss);returnbearer!=null?ArenaTrialsTuning.AffixBonusLoot(bearer.Affixes.Count):0;",
            "[Affix] 额外掉落只算挂词缀的波次首领本体（身体一律 0）")
    roll = compact(body_of(affix, "private void Roll(ArenaAffixBearer bearer, int count)"))
    require(errors, "if(bearer.Preset!=null){kindScratch.Add(ArenaAffixKind.Clone);kindScratch.Add(ArenaAffixKind.Guard);}" in roll,
            "[Affix] 内容 Boss（不给预设）只挂身法 / 死相两类")
    require(errors, "inttake=Mathf.Min(count,kindScratch.Count);" in roll
            and "bearer.Affixes.Add((ArenaAffix)((int)kindScratch[i]*2+UnityEngine.Random.Range(0,2)));" in roll,
            "[Affix] 几条词缀必须取自打乱后的不同类别（每类两个里随机一个）")
    require(errors, compact(re.search(r"internal bool HoldsWave \{[^}]*\}", affix).group(0) if re.search(r"internal bool HoldsWave \{[^}]*\}", affix) else "")
            == "internalboolHoldsWave{get{returnRole==ArenaAffixRole.Shard||Role==ArenaAffixRole.Twin;}",
            "[Affix] 只有分身 / 双生压收波（亲卫、影从不压）")
    active = compact(body_of(affix, "private bool HoldActive()"))
    require(errors, active.startswith("if(pendingHoldSpawns>0)returntrue;") and "bodies[i].HoldsWave&&!bodies[i].Dead" in active
            and "(pairs[i].Countdown>=0f||pairs[i].Reviving)" in active,
            "[Affix] 压收波要算在路上的刷怪、场上的分身 / 双生与双生倒计时")
    holdfor = compact(body_of(affix, "internal bool HoldWaveFor(CharacterMainControl boss)"))
    require(errors, "if(bearer!=null)HandleBearerDeath(bearer);boolhold=HoldActive();" in holdfor,
            "[Affix] HoldWaveFor 必须先结算这次死亡再看压不压（实例 OnDeadEvent 先于静态 Health.OnDead）")
    death = compact(body_of(affix, "private void HandleBearerDeath(ArenaAffixBearer bearer)"))
    require(errors, death.startswith("if(bearer.DeathHandled)return;bearer.DeathHandled=true;") and "DisperseRetinue(bearer);" in death,
            "[Affix] 本体死亡结算幂等，亲卫 / 影从随主将散")
    tick = compact(body_of(affix, "internal override void Tick(float deltaTime)"))
    require(errors, "holdClock>=ArenaTrialsTuning.AffixHoldMaxSeconds" in tick and "releasePending=true;" in tick,
            "[Affix] 压收波必须有上限，超时散掉剩下的身体照常收波")
    require(errors, "HandleBearerDeath(bearer);" in tick and "HandleBodyDeath(body);" in tick,
            "[Affix] 每帧兜底认死亡（OnDead 可能被别的订阅者截断）")
    fell = compact(body_of(affix, "private void MemberFell(ArenaAffixTwinPair pair, CharacterMainControl who, Vector3 at, float share)"))
    require(errors, "if(pair.Revives>=ArenaTrialsTuning.TwinMaxRevives)return;" in fell,
            "[Affix] 双生一对的拉起次数必须有上限（否则两只轮流倒下永远打不完）")
    contribute = compact(body_of(affix, "internal override void ContributeFoe(CharacterMainControl foe, ref ArenaTrialFoeStats stats)"))
    require(errors, "if(bearer.GuardUp&&CountAlive(bearer.Guards)>0)stats.Taken*=1f-ArenaTrialsTuning.EscortDamageReduction;" in contribute,
            "[Affix] 护驾减伤只在亲卫还活着时生效")
    shutdown = compact(body_of(affix, "internal override void Shutdown()"))
    require(errors, shutdown.startswith("closed=true;") and "Dissolve(bodies[i]);" in shutdown
            and 'RuntimeStatModifierTracker.RemoveAll(bodyRecords,"ArenaTrialsaffixbody");' in shutdown and "aftermaths[i].Retire();" in shutdown,
            "[Affix] 收尾必须散掉身体、摘掉身体生命修饰、收掉亡魂殉爆")
    spawn_async = compact(body_of(affix, "private async UniTaskVoid SpawnAsync("))
    require(errors, "if(closed||ArenaTrials.Active!=trials||stale||masterGone||pairSealed)" in spawn_async
            and "boolpairSealed=role==ArenaAffixRole.Twin&&(bearer.Twin==null||bearer.Twin.Done);" in spawn_async,
            "[Affix] 刷回来时战规已收、压波已超时、主将已倒或这一对双生已封住（拉起途中另一只也倒了）的身体要当场散掉")
    register = compact(body_of(affix, "private void Register(ArenaAffixBearer bearer, ArenaAffixRole role, CharacterMainControl body, float share, float scale,"))
    require(errors, "record.MaxHealth=Mathf.Max(1f,share*MasterMaxHealth(bearer));" in register
            and "record.HealthRecord=SetBodyHealth(body,record.MaxHealth,healthFraction);" in register,
            "[Affix] 身体血量在落地登记时按首领此刻的生命上限折算（变异词条在入场通知之后才挂），并记下自己那条生命修饰")
    require(errors, "MasterMaxHealth(bearer);" in death,
            "[Affix] 本体倒下那刻再读一次生命上限（分身按它折，双生拉起时本体可能已被官方销毁）")
    require(errors, "bodies.RemoveAt(" not in compact(affix).replace("bodies.RemoveAt(index);", ""),
            "[Affix] 身体移出列表只经 ForgetBodyAt（一并丢掉它那条生命修饰记录，无间长局不攒已销毁角色的 Stat）")
    forget = compact(body_of(affix, "private void ForgetBodyAt(int index)"))
    require(errors, "bodies.RemoveAt(index);" in forget and "bodyRecords.Remove(body.HealthRecord);" in forget,
            "[Affix] ForgetBodyAt 必须把身体的生命修饰记录从 bodyRecords 里摘掉")
    frenzy = compact(body_of(affix, "private void TickFrenzy(ArenaAffixBearer bearer, float dt)"))
    require(errors, "bearer.Frenzied=true;" in frenzy and "bearer.BaseScale=bearer.Body.transform.localScale;" in frenzy
            and frenzy.find("bearer.BaseScale=bearer.Body.transform.localScale;") < frenzy.find("bearer.Body.transform.localScale=bearer.BaseScale*k;"),
            "[Affix] 狂暴以进入狂暴那刻的体型为底长大（巨灵兵 / 群鼠的缩放在入场通知之后才挂）")
    require(errors, "bearer.BaseScale=" not in compact(body_of(affix, "internal override void OnFoeJoined(CharacterMainControl foe, bool waveBoss)")),
            "[Affix] 登记时不记狂暴体型（那时变异词条的缩放还没挂）")
    for name, src in (("ArenaTrialsAffixes.cs", affix), ("ArenaTrialsAffixFx.cs", fx)):
        require(errors, ".Hurt(" not in src, "[Affix] %s 里的伤害只能经 ArenaTrials.HurtNeutral" % name)
        require(errors, not re.search(r"AddComponent<\w*(Collider|Rigidbody|NavMeshObstacle)\w*>", src),
                "[Affix] %s 不许放碰撞体 / 刚体 / 挡路体" % name)
    require(errors, "ArenaTrials.HurtNeutral(player.Health,damage,position,true)" in compact(fx),
            "[Affix] 亡魂贴身伤害翻滚可躲")
    require(errors, "ArenaTrials.HurtNeutral(health,ArenaTrials.ShareOfMaxHealth(health,ArenaTrialsTuning.VolatileDamage),center,true,ElementTypes.fire);"
            in compact(fx), "[Affix] 殉爆按生命上限比例结算火伤、翻滚可躲")
    tuning = compact(read(*TRIALS, "ArenaTrialsTuning.cs"))
    require(errors, "internalconstintTwinMaxRevives=2;" in tuning and "internalconstfloatAffixHoldMaxSeconds=" in tuning,
            "[Affix] 双生拉起上限与压收波上限必须在 ArenaTrialsTuning")


def check_fire_ring(errors):
    ring = read(*TRIALS, "ArenaTrialsFireRing.cs")
    fx = read(*TRIALS, "ArenaTrialsFireRingFx.cs")
    if ring is None or fx is None:
        errors.append("[FireRing] 缺少 WavesArena/Trials/ 下的焚城文件（FireRing / FireRingFx）")
        return
    flat = compact(ring)
    fx_flat = compact(fx)
    start = compact(body_of(ring, "internal override void OnWaveStart(int wave, bool finale)"))
    require(errors, start.startswith("if(!Trials.Profile.FireRing)return;DropRing("),
            "[FireRing] OnWaveStart 第一句按档早返（不带焚城的档 O(1)），再先收掉上一波没退完的火墙")
    require(errors, "targetRadius=ringRadius*(finale?ArenaTrialsTuning.FireRingFinaleRadiusFactor:ArenaTrialsTuning.FireRingRadiusFactor);" in start,
            "[FireRing] 收紧落点：平常 FireRingRadiusFactor 倍、压轴 FireRingFinaleRadiusFactor 倍")
    tick = compact(body_of(ring, "internal override void Tick(float deltaTime)"))
    require(errors, tick.startswith("if(stage==Stage.Idle)return;"), "[FireRing] Tick 第一句没有火圈就返回（O(1)）")
    require(errors, "if(stage==Stage.Burning||stage==Stage.Closing||stage==Stage.Closed)TickBurn(deltaTime);" in tick,
            "[FireRing] 只在火墙燃起之后、退去之前烧人（燃起与收波退去期间不烧）")
    burning = compact(body_of(ring, "private void TickBurning()"))
    require(errors, "floatclock=Trials.WaveClock;" in burning
            and "clock>=ArenaTrialsTuning.FireRingStartDelay-ArenaTrialsTuning.FireRingTelegraphSeconds" in burning
            and "if(clock>=ArenaTrialsTuning.FireRingStartDelay)BeginClosing();" in burning,
            "[FireRing] 按本波开打时长：先预告落点、到 FireRingStartDelay 才收紧")
    resolve = compact(body_of(ring, "private bool ResolveRing(out Vector3 ringCenter, out float ringRadius)"))
    require(errors, "Trials.SpawnPoints();" in resolve
            and "NavMesh.SamplePosition(centroid,outhit,ArenaTrialsTuning.FireRingCenterSnap,NavMesh.AllAreas)" in resolve
            and "if(!snapped)" in resolve
            and "if(TryPlayer(outplayer))far=Mathf.Max(far,ArenaTrials.FlatSqr(player,ringCenter));" in resolve
            and "ringRadius=Mathf.Max(ArenaTrialsTuning.FireRingMinRadius,Mathf.Sqrt(far)+ArenaTrialsTuning.FireRingMargin);" in resolve,
            "[FireRing] 圈心 = 刷新点重心贴 NavMesh（落不下改用最近刷新点），半径 = 最远刷新点（开波时玩家更远就按玩家）+ 边距，且有下限")
    burn_tick = compact(body_of(ring, "private void TickBurn(float deltaTime)"))
    require(errors, "CharacterMainControl.Main" in burn_tick and "exposure=" not in burn_tick.replace("exposure+=", "").replace("exposure-=", "")
            and "while(exposure>=tick&&guard++<4)" in burn_tick,
            "[FireRing] 只烧玩家；按跳结算，进出圈不清计时（贴着圈边进进出出蹭不掉伤害）")
    burn = compact(body_of(ring, "private void Burn(Health health, Vector3 at, float distance)"))
    require(errors, "ArenaTrialsTuning.FireRingDamagePerSecond*ArenaTrialsTuning.FireRingTickSeconds" in burn
            and "ArenaTrials.HurtNeutral(health,damage,edge,false,ElementTypes.fire);" in burn,
            "[FireRing] 圈外灼烧按生命上限比例结算火伤、翻滚躲不掉，只经 HurtNeutral")
    require(errors, "Trials.Foes" not in flat and "CollectVictims" not in flat,
            "[FireRing] 火墙不烧敌人（不让首领被风筝到圈外烧死）")
    end = compact(body_of(ring, "internal override void OnWaveEnd()"))
    require(errors, "stage=Stage.Receding;" in end and "HideMap();" in end and "ArenaExpeditionHud.SetHeat(0f);" in end,
            "[FireRing] 收波火墙退去：摘 M 图标记、收热浪")
    require(errors, compact(body_of(ring, "internal override void Shutdown()")) == "DropRing(0.6f);",
            "[FireRing] Shutdown 必须收掉火墙、M 图标记与热浪")
    drop = compact(body_of(ring, "private void DropRing(float fadeSeconds)"))
    require(errors, drop.startswith("HideMap();if(fx!=null)fx.Retire(fadeSeconds);fx=null;") and "stage=Stage.Idle;" in drop,
            "[FireRing] DropRing 先摘 M 图、再让火墙淡出退场")
    require(errors, "RemoveArea(refringPoi);RemoveArea(reftargetPoi);" in compact(body_of(ring, "private void HideMap()")),
            "[FireRing] HideMap 要摘两枚区域标记")
    require(errors, "LocalizationHelper.InjectLocalization(MapLabelKey,ArenaTrialsTuning.FireRingName());"
            in compact(body_of(ring, "private void RefreshRingOnMap()")),
            "[FireRing] M 图标签按当前语言注入（玩家可能刚切过语言）")
    for name, src in (("ArenaTrialsFireRing.cs", flat), ("ArenaTrialsFireRingFx.cs", fx_flat)):
        require(errors, ".Hurt(" not in src, "[FireRing] %s 里的伤害只能经 ArenaTrials.HurtNeutral" % name)
        require(errors, not re.search(r"AddComponent<\w*(Collider|Rigidbody|NavMeshObstacle)\w*>", src),
                "[FireRing] %s 不许放碰撞体 / 刚体 / 挡路体（火墙只是表现 + 结算）" % name)
        require(errors, "newMaterial(" not in src, "[FireRing] %s 不许 new 材质（取共享工厂）" % name)
    step = compact(body_of(fx, "internal void Step(float dt, float currentRadius, Vector3 player, bool hasPlayer)"))
    require(errors, step.startswith("if(root==null)return;") and "for(intk=0;k<HeightSamplesPerFrame&&settle>0;k++,settle--)" in step,
            "[FireRing] 表现退场后 Step 当帧返回；地面高度每帧至多 HeightSamplesPerFrame 次射线、圈不动就停")
    emit = compact(body_of(fx, "private void Emit(float dt, float bearing, float amount, Vector3 player, bool hasPlayer)"))
    require(errors, "if(amount<=0.001f)return;" in emit and "floathalf=Mathf.Min(Mathf.PI,ArcHalfLength/Mathf.Max(1f,radius));" in emit,
            "[FireRing] 火舌、浓烟、火星只在玩家看得见的那段弧上发，看不见就不发")
    require(errors, "boolwasIgniting=igniting;" in step and "if(moved||wasIgniting||settle>0||!drawn)DrawLines();" in step
            and step.find("boolwasIgniting=igniting;") < step.find("igniting=false;"),
            "[FireRing] 燃起结束那一帧也要重画（否则停在开口的弧，留缺口直到收紧）")
    heads = compact(body_of(fx, "private void EmitHeads(float dt, Vector3 player)"))
    require(errors, "HeadFlamesPerSecond*dt" in heads and "HeadEmbersPerSecond*dt" in heads
            and "for(intk=0;k<flameCount;k++)EmitFlame(" in heads,
            "[FireRing] 燃起火头按秒发粒子（按帧发会在高帧率占满粒子上限、低帧率变稀）")
    lights = compact(body_of(fx, "private void TickLights(float bearing, float amount)"))
    require(errors, "light.intensity=0f;light.enabled=false;continue;" in lights,
            "[FireRing] 关灯前亮度清零（退场淡出按灯此刻亮度起算，并会把关着的灯打开）")
    closing = compact(body_of(ring, "private void BeginClosing()"))
    require(errors, "&&ArenaExpeditionHud.PlayOmenSweep(ArenaTrialsTuning.FireRingName(),\"战场\",BossRushInkSkin.SfxWarDrum);" in closing
            and "if(swept)announcedOnce=true;elseBossRushInkSkin.PlaySfx(BossRushInkSkin.SfxWarDrum);" in closing,
            "[FireRing] 收紧横扫真演出来才算写过；演不出来（HUD 不在、正放标题卡）只擂鼓，下回再写")
    require(errors, "internalstaticboolPlayOmenSweep(" in compact(read("WavesArena", "ArenaExpeditionHud.cs")),
            "[FireRing] PlayOmenSweep 要返回是否真的演出来了")
    tuning = compact(read(*TRIALS, "ArenaTrialsTuning.cs"))
    for const in ("FireRingStartDelay=30f;", "FireRingRadiusFactor=0.6f;", "FireRingFinaleRadiusFactor=0.4f;",
                  "FireRingMargin=10f;", "FireRingDamagePerSecond=0.04f;", "FireRingRecedeSeconds=4f;"):
        require(errors, ("internalconstfloat" + const) in tuning,
                "[FireRing] 方案数值（开波 30 秒收到 60%%、压轴 40%%，最远刷新点 +10 米，圈外每秒 4%%，4 秒退去）缺 %s" % const)
    hud = read("WavesArena", "ArenaExpeditionHud.cs")
    require(errors, "heatGoal=0f;" in compact(body_of(hud, "internal static void Hide()"))
            and "heat.color=" in compact(body_of(hud, "private void TickAmbience(float dt)")),
            "[FireRing] 热浪在色调层的同一个 Update 里推进（跟着 HUD 显隐与暂停双门），角标收起时归零")


def check_registration(errors):
    bat = io.open(os.path.join(ROOT, "compile_official.bat"), "r", encoding="utf-8", errors="ignore").read()
    for name in sorted(os.listdir(os.path.join(ROOT, *TRIALS))):
        if name.endswith(".cs"):
            require(errors, ("echo(WavesArena\\Trials\\" + name) in bat,
                    "[Build] WavesArena/Trials/%s 未登记 compile_official.bat" % name)


def main():
    errors = []
    check_ownership(errors)
    check_lifecycle(errors)
    check_patches(errors)
    check_profiles(errors)
    check_sky(errors)
    check_hazards(errors)
    check_affixes(errors)
    check_fire_ring(errors)
    check_registration(errors)
    if errors:
        print("ArenaTrialsGuard: FAIL (%d errors)" % len(errors))
        for e in errors:
            print("  - " + e)
        return 1
    print("ArenaTrialsGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
