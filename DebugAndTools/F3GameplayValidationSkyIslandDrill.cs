#if BOSSRUSH_DEV
// ============================================================================
// F3GameplayValidationSkyIslandDrill.cs - 天空岛 Dev 演练套件（**非只读**，2026-09-14）
// ============================================================================
// 为什么单独一套：
//   岛内验收（F3GameplayValidationSkyIsland*.cs）必须只读，于是「云蚋躲不躲得开、叮咬会不会打穿下限、
//   官方对话能不能选能不能取消」这类**得先做点什么才看得到**的判据只能人工去点。这一套专门做这些事：
//   强制夜里、在玩家身边刷一群云蚋、朝它们打合成弹道、打死一只、把血量压到叮咬下限附近看一会儿、弹一次官方多选对话。
//
// 隔离（tests/SkyIslandDrillNoPersistenceGuard.py 与 tests/SkyIslandReadOnlySuiteDrillIsolationGuard.py 守着）：
//   1. 整个文件包在 #if BOSSRUSH_DEV 里：正式构建里没有这些代码，F3 面板上也没有这个按钮。
//   2. 独立入口 TryStartSkyIslandDrill、独立按钮；报告头写 read_only=false / drill=true。只读套件不引用这里任何东西。
//   3. 沿用岛内启动门 CheckSkyIslandStartGate：Dev 构建 + 专用测试档 + 已在岛上且就绪 + 没开着模态界面与官方界面。
//   4. 强制夜里在 finally 里复位到开跑前的值；压低的血量在 finally 里还回去；订阅的受伤事件在 finally 里退订；
//      弹出的官方对话在 finally 里取消。
//   5. **不写存档**：不收录、不记清场、不发物品、不点灯、不合成、不改剧情旗标。击杀只走官方伤害路径，
//      计数由生产代码自己记——驱蚋委托（按出击计、不进存档）会跟着涨一只，这正是「打死会计数」本身，写进报告。
//
// 它证明的仍然只是「这一趟、这一台机器上这几秒里的行为」，不是手感：躲闪看不看得清、叮咬节奏舒不舒服，照旧人工看。
// ============================================================================

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Cysharp.Threading.Tasks;
using Dialogues;
using NodeCanvas.DialogueTrees;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BossRush
{
    internal sealed partial class F3GameplayValidationRunner
    {
        /// <summary>演练套件的用例 ID。装配失败时批量记 SKIP，报告里不留空白。</summary>
        private static readonly string[] SkyIslandDrillCaseIds = { "SKY_DRILL_GNAT_SWARM", "SKY_DRILL_OFFICIAL_DIALOGUE", "SKY_DRILL_BOSS_LOADOUT" };

        /// <summary>刷蚋之后等它们飞近的时间（秒，现实时间）。</summary>
        private const float DrillApproachSeconds = 1.5f;
        /// <summary>合成弹道连打的发数与间隔（秒）。</summary>
        private const int DrillShots = 12;
        private const float DrillShotInterval = 0.12f;
        /// <summary>满血时看叮咬与痒的窗口，以及压到下限附近之后看「不打穿」的窗口（秒）。两段加起来仍在单条用例 60 秒预算内。</summary>
        private const float DrillBiteWatchSeconds = 16f;
        private const float DrillFloorWatchSeconds = 8f;

        internal static bool TryStartSkyIslandDrill(ModBehaviour host, out string reason)
        {
            reason = null;
            EnsureAttached(host);
            if (_instance == null) { reason = "验收运行器未就绪"; return false; }
            if (_instance._running) { reason = "已有验收正在运行"; return false; }
            // 与岛内只读验收同一道门：Dev 构建、专用测试档、人在岛上且会话就绪、没开着模态与官方界面。
            if (!_instance.CheckSkyIslandStartGate(out reason)) return false;
            _instance._cancelRequested = false;
            _instance._fatalAbort = false;
            _instance._skyIslandMode = true;
            _instance._skyIslandDrill = true;
            SkyIslandSession started = _instance.SkyIslandSessionOrNull();
            _instance._skyIslandSceneHandle = started == null ? 0 : started.ValidationScene.handle;
            _instance._skyIslandBaselineLeases = ZombieModeUIHelper.ModalInputLeaseCount;
            if (!_instance.BeginSession(out reason)) return false;
            _instance.WriteRaw("SUITE | SKY_ISLAND_DRILL | read_only=false | scene=" + SceneManager.GetActiveScene().path);
            _instance._routine = _instance.StartCoroutine(_instance.RunSkyIslandDrillSession());
            return true;
        }

        /// <summary>演练只有一个阶段，收尾不切图、不跑主套件清理（CompleteSession 按岛内模式处理）。</summary>
        private IEnumerator RunSkyIslandDrillSession()
        {
            yield return null;
            try
            {
                yield return DriveSessionPhase(RunSkyIslandDrillSuite(), false);
            }
            finally { CompleteSession(); }
        }

        private IEnumerator RunSkyIslandDrillSuite()
        {
            if (SkyIslandSessionOrNull() == null)
            {
                for (int i = 0; i < SkyIslandDrillCaseIds.Length; i++)
                    Record(SkyIslandDrillCaseIds[i], "SKIP", 0L, string.Empty, "sky_island_session_missing");
                yield break;
            }
            SetStage("演练 1/3 云蚋（强制夜里、刷一群、合成弹道、击杀、叮咬下限、痒）");
            yield return RunSkyIslandCase("SKY_DRILL_GNAT_SWARM", RunSkyIslandDrillGnats);
            SetStage("演练 2/3 官方对话（弹出、防重入、选择、取消）");
            yield return RunSkyIslandCase("SKY_DRILL_OFFICIAL_DIALOGUE", RunSkyIslandDrillDialogue);
            SetStage("演练 3/3 头目配装（刷一名、穿全套、结算只留一件）");
            yield return RunSkyIslandCase("SKY_DRILL_BOSS_LOADOUT", RunSkyIslandDrillBossLoadout);
        }

        // ====================================================================
        // 头目 / 岛主：穿上全套 → 只留一件
        // ====================================================================

        /// <summary>
        /// SKY_DRILL_BOSS_LOADOUT（2026-09-14 头目 / 岛主 R1）：在玩家前方刷一名官方拾荒者，按残星匠首的档案穿上三件专属装备，
        /// 核对三个槽位与身上的模型；把耐久压到 1 后按固定抽样值结算一次，核对「只留抽中的那件并补满耐久、另外两件卸下销毁、
        /// 武器与背包里的原版物品原样在、结算上闩」。
        /// **不走死亡**：不建官方尸体箱、不记官方击杀计数、不派发首杀事件，所以不写任何存档；阵营设成 middle、不挂招式控制器，
        /// 演练期间它不会攻击玩家。finally 里销毁角色与 preset 克隆。尸体箱真的收走这一件、箱里其余照原版，只能实机看（清单 2.19.6）。
        /// </summary>
        private IEnumerator RunSkyIslandDrillBossLoadout()
        {
            const float spawnDistance = 6f;
            const float spawnTimeout = 15f;
            const int settleFrames = 10;
            // 权重 35 / 35 / 30：0.5 落在第二件（星炉背甲）。
            const double fixedRoll = 0.5;
            Stopwatch sw = Stopwatch.StartNew();
            CharacterMainControl player = CharacterMainControl.Main;
            SkyIslandBossProfile profile = SkyIslandBossRules.Find("G", 0);
            if (player == null || profile == null)
            {
                Record("SKY_DRILL_BOSS_LOADOUT", "FAIL", 0L, "player=" + (player != null) + ",profile=" + (profile != null), "主角或残星匠首档案不可用");
                yield break;
            }
            CharacterRandomPreset source = null;
            foreach (CharacterRandomPreset preset in Resources.FindObjectsOfTypeAll<CharacterRandomPreset>())
                if (preset != null && !preset.isBoss && !preset.isZombie && preset.team == Teams.scav &&
                    preset.name.IndexOf("Dummy", StringComparison.OrdinalIgnoreCase) < 0 &&
                    !preset.name.StartsWith("BossRush_", StringComparison.Ordinal) &&
                    (source == null || string.CompareOrdinal(preset.name, source.name) < 0)) source = preset;
            if (source == null)
            {
                Record("SKY_DRILL_BOSS_LOADOUT", "FAIL", 0L, string.Empty, "没有可用的官方拾荒者 preset（遭遇 owner 同一口径）");
                yield break;
            }
            Vector3 forward = player.transform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.01f) forward = Vector3.forward;
            forward.Normalize();
            RaycastHit ground;
            Vector3 probe = player.transform.position + forward * spawnDistance + Vector3.up * 2f;
            if (!Physics.Raycast(probe, Vector3.down, out ground, 6f, ~0, QueryTriggerInteraction.Ignore))
            {
                Record("SKY_DRILL_BOSS_LOADOUT", "SKIP", 0L, "probe=" + probe, "玩家前方 6 米找不到地面：换个开阔的地方再演练");
                yield break;
            }

            int[] gearIds = SkyIslandBossRules.AllGearTypeIds;
            Func<ItemStatsSystem.Item, int> countVanilla = delegate (ItemStatsSystem.Item root)
            {
                int count = 0;
                if (root == null) return count;
                if (root.Inventory != null)
                    foreach (ItemStatsSystem.Item item in root.Inventory)
                        if (item != null && Array.IndexOf(gearIds, item.TypeID) < 0) count++;
                if (root.Slots != null)
                    foreach (ItemStatsSystem.Items.Slot slot in root.Slots)
                        if (slot != null && slot.Content != null && Array.IndexOf(gearIds, slot.Content.TypeID) < 0) count++;
                return count;
            };
            List<string> errors = new List<string>();
            List<string> notes = new List<string>();
            CharacterRandomPreset clone = UnityEngine.Object.Instantiate(source);
            clone.name = "BossRush_SkyIslandDrill_Boss";
            clone.dropBoxOnDead = false;
            clone.setActiveByPlayerDistance = false;
            CharacterMainControl created = null;
            try
            {
                UniTask<CharacterMainControl>.Awaiter spawning = clone.CreateCharacterAsync(ground.point + Vector3.up * 0.1f, -forward, -1, null, false).GetAwaiter();
                float spawnUntil = Time.realtimeSinceStartup + spawnTimeout;
                while (!spawning.IsCompleted && Time.realtimeSinceStartup < spawnUntil && !ShouldAbort()) yield return null;
                // 完成状态先读进局部变量再取结果，取完不再碰 awaiter（UniTaskAwaiterReuseGuard）。
                bool spawnCompleted = spawning.IsCompleted;
                if (spawnCompleted)
                {
                    try { created = spawning.GetResult(); }
                    catch (Exception e) { errors.Add("spawn_threw_" + e.GetType().Name); }
                }
                if (created == null)
                {
                    if (errors.Count == 0) errors.Add(spawnCompleted ? "spawn_returned_null" : "spawn_timeout");
                }
                else
                {
                    SpawnedEnemyActivationHelper.ReleaseFromPlayerDistanceSleep(created);
                    // middle 阵营：官方 AI 只打敌对阵营，演练期间不会朝玩家开火。
                    created.SetTeam(Teams.middle);
                    string loadoutReason;
                    SkyIslandBossLoot loot = SkyIslandBossForge.DevLoadoutForDrill(created, profile, out loadoutReason);
                    for (int frame = 0; frame < settleFrames; frame++) yield return null;

                    ItemStatsSystem.Item characterItem = created.CharacterItem;
                    int worn = 0, models = 0;
                    Transform[] parts = created.GetComponentsInChildren<Transform>(true);
                    for (int i = 0; i < profile.Gear.Length; i++)
                    {
                        ItemStatsSystem.Items.Slot slot = SkyIslandBossForge.FindSlot(characterItem, profile.Gear[i].Slot);
                        ItemStatsSystem.Item piece = slot == null ? null : slot.Content;
                        if (piece == null || piece.TypeID != profile.Gear[i].TypeId) continue;
                        worn++;
                        if (piece.UseDurability) piece.Durability = 1f;
                        SkyIslandBossGearSpec spec = SkyIslandBossRules.GearSpec(profile.Gear[i].TypeId);
                        for (int p = 0; p < parts.Length && spec != null; p++)
                            if (parts[p] != null && parts[p].name.IndexOf(spec.ModelBaseName, StringComparison.Ordinal) >= 0) { models++; break; }
                    }
                    int vanillaBefore = countVanilla(characterItem);
                    notes.Add("preset=" + source.name + ",loadout=" + (loadoutReason ?? "ok") + ",worn=" + worn + "/" + profile.Gear.Length
                        + ",models=" + models + "/" + profile.Gear.Length + ",vanilla_before=" + vanillaBefore);
                    if (loadoutReason != null) errors.Add(loadoutReason);
                    if (worn != profile.Gear.Length) errors.Add("worn_" + worn + "_of_" + profile.Gear.Length);
                    else if (models != profile.Gear.Length) errors.Add("gear_model_missing_" + models + "_of_" + profile.Gear.Length);

                    string outcome = loot == null ? "loot_component_missing" : loot.DevResolveForDrill(fixedRoll);
                    yield return null;
                    int chosen = loot == null ? -1 : loot.ChosenTypeId;
                    int left = 0, leftFull = 0;
                    if (characterItem != null)
                    {
                        List<ItemStatsSystem.Item> remaining = new List<ItemStatsSystem.Item>();
                        if (characterItem.Inventory != null)
                            foreach (ItemStatsSystem.Item item in characterItem.Inventory) if (item != null) remaining.Add(item);
                        if (characterItem.Slots != null)
                            foreach (ItemStatsSystem.Items.Slot slot in characterItem.Slots) if (slot != null && slot.Content != null) remaining.Add(slot.Content);
                        foreach (ItemStatsSystem.Item item in remaining)
                        {
                            if (Array.IndexOf(gearIds, item.TypeID) < 0) continue;
                            left++;
                            if (item.TypeID == chosen && (!item.UseDurability || item.Durability >= item.MaxDurability - 0.001f)) leftFull++;
                        }
                    }
                    int vanillaAfter = countVanilla(characterItem);
                    string again = loot == null ? "loot_component_missing" : loot.DevResolveForDrill(0.99);
                    notes.Add("outcome=" + outcome + ",chosen=" + chosen + ",gear_left=" + left + ",kept_full=" + leftFull
                        + ",vanilla_after=" + vanillaAfter + ",second_resolve=" + again);
                    if (outcome != "slot") errors.Add("outcome_" + outcome);
                    if (chosen != BossRushItemIds.SkyIslandStarfurnaceHarness) errors.Add("fixed_roll_picked_" + chosen);
                    if (left != 1) errors.Add("gear_left_" + left);
                    if (leftFull != 1) errors.Add("kept_piece_not_full_durability");
                    if (vanillaAfter != vanillaBefore) errors.Add("vanilla_items_changed_" + vanillaBefore + "_to_" + vanillaAfter);
                    if (again != "not_bound_or_already_resolved") errors.Add("resolve_not_latched");
                }
            }
            finally
            {
                if (created != null) { created.gameObject.SetActive(false); UnityEngine.Object.Destroy(created.gameObject); }
                if (clone != null) UnityEngine.Object.Destroy(clone, 0.1f);
            }

            string metrics = string.Join(" | ", notes.ToArray());
            if (errors.Count > 0) Record("SKY_DRILL_BOSS_LOADOUT", "FAIL", sw.ElapsedMilliseconds, metrics, "头目配装演练不合格：" + string.Join(",", errors.ToArray()));
            else Record("SKY_DRILL_BOSS_LOADOUT", "PASS", sw.ElapsedMilliseconds, metrics, string.Empty);
        }

        // ====================================================================
        // 云蚋
        // ====================================================================

        private IEnumerator RunSkyIslandDrillGnats()
        {
            Stopwatch sw = Stopwatch.StartNew();
            SkyIslandSession session = SkyIslandSessionOrNull();
            SkyIslandFieldcraft field = SkyIslandFieldcraft.Current;
            SkyIslandGnats swarm = field == null ? null : field.Gnats;
            CharacterMainControl player = CharacterMainControl.Main;
            Health health = player == null ? null : player.Health;
            if (session == null || swarm == null || !swarm.Usable || health == null)
            {
                Record("SKY_DRILL_GNAT_SWARM", "FAIL", 0L, "fieldcraft=" + (field != null) + ",swarm=" + (swarm != null)
                    + ",usable=" + (swarm != null && swarm.Usable) + ",health=" + (health != null), "云蚋 owner 或主角不可用：演练做不了");
                yield break;
            }
            if (session.ValidationLivingEnemies > 0)
            {
                Record("SKY_DRILL_GNAT_SWARM", "SKIP", 0L, "living_enemies=" + session.ValidationLivingEnemies,
                    "场上有活着的敌人：蚊群会散开、受伤也分不清来源，先清场再演练");
                yield break;
            }

            List<string> errors = new List<string>();
            List<string> notes = new List<string>();
            string skip = null;
            bool previousForceNight = SkyIslandNight.DevForceNight;
            float previousHealth = health.CurrentHealth;
            bool healthLowered = false;
            int bites = 0, otherHits = 0;
            // 官方 Health 的受伤广播是静态事件（全场所有 Health 都走它），只数主角自己的：
            // 云蚋叮咬是 fromCharacter 为空、伤害正好 1 点的真伤；其余来源一律算「别的伤害」，出现了就不判下限。
            Action<Health, DamageInfo> onHurt = delegate (Health hurt, DamageInfo info)
            {
                if (!ReferenceEquals(hurt, health)) return;
                if (info.fromCharacter == null && Mathf.Abs(info.damageValue - SkyIslandMosquitoRules.BiteDamage) < 0.01f) bites++;
                else otherHits++;
            };
            try
            {
                SkyIslandNight.DevForceNight = true;
                Health.OnHurt += onHurt;

                // 1. 刷一群，等它们飞近。
                int spawned = swarm.DevSpawnAround(player.transform.position, SkyIslandMosquitoRules.MaxAlive);
                float approachUntil = Time.realtimeSinceStartup + DrillApproachSeconds;
                while (Time.realtimeSinceStartup < approachUntil && !ShouldAbort()) yield return null;
                notes.Add("spawned=" + spawned + ",alive_after_approach=" + swarm.Alive);
                if (spawned == 0 || swarm.Alive == 0) skip = "蚊群没刷出来或立刻散了（大风、烟、驱风香或刷新位置都可能挡住）";

                // 2. 合成弹道：冲刺 ≤3 m、预算不超过上限、连打时起跳次数有上限。
                if (skip == null)
                {
                    float longest = 0f, highestBudget = 0f;
                    int dodges = 0, counted = 0;
                    float shotsStarted = Time.time;
                    for (int shot = 0; shot < DrillShots && !ShouldAbort(); shot++)
                    {
                        Vector3 muzzle = player.transform.position + Vector3.up * 1.2f;
                        swarm.DevSyntheticShot(muzzle, 60f, 30f, 0.1f);
                        float next = Time.realtimeSinceStartup + DrillShotInterval;
                        while (Time.realtimeSinceStartup < next) yield return null;
                        float dash, budget;
                        int started;
                        int alive = swarm.DevMotorStats(out dash, out budget, out started);
                        if (dash > longest) longest = dash;
                        if (budget > highestBudget) highestBudget = budget;
                        if (started > dodges) dodges = started;
                        if (alive > counted) counted = alive;
                    }
                    float elapsed = Mathf.Max(0f, Time.time - shotsStarted);
                    float dodgeCeiling = counted * (SkyIslandMosquitoRules.DodgeBudgetMax + elapsed / SkyIslandMosquitoRules.DodgeRegenSeconds + 1f);
                    notes.Add("longest_dash_m=" + longest.ToString("F2") + ",max_budget=" + highestBudget.ToString("F2")
                        + ",dodges=" + dodges + ",dodge_ceiling=" + dodgeCeiling.ToString("F1") + ",gnats=" + counted);
                    if (longest > SkyIslandMosquitoRules.MaxDash + 0.01f) errors.Add("dash_over_" + SkyIslandMosquitoRules.MaxDash + "m");
                    if (highestBudget > SkyIslandMosquitoRules.DodgeBudgetMax + 0.001f) errors.Add("dodge_budget_over_max");
                    if (dodges > dodgeCeiling) errors.Add("dodges_not_bounded");
                    if (dodges == 0) notes.Add("no_dodge_triggered(弹道没有威胁到任何一只，或全部在冷却)");
                }

                // 3. 打死一只：计数 +1、不掉东西。
                if (skip == null && !ShouldAbort())
                {
                    int counterBefore = session.Bounty.Counter(SkyIslandBountyKind.Gnats);
                    int aliveBefore = swarm.Alive;
                    int lootBefore = CountLooseLootForDrill();
                    Vector3 at;
                    bool hit = swarm.DevKillOne(out at);
                    float killUntil = Time.realtimeSinceStartup + 1.5f;
                    while (hit && swarm.Alive >= aliveBefore && Time.realtimeSinceStartup < killUntil) yield return null;
                    float settleUntil = Time.realtimeSinceStartup + 0.3f;
                    while (Time.realtimeSinceStartup < settleUntil) yield return null;
                    int counterAfter = session.Bounty.Counter(SkyIslandBountyKind.Gnats);
                    int lootAfter = CountLooseLootForDrill();
                    notes.Add("kill_hit=" + hit + ",alive=" + aliveBefore + "->" + swarm.Alive + ",culled=" + counterBefore + "->" + counterAfter
                        + ",loose_loot=" + lootBefore + "->" + lootAfter);
                    if (!hit) errors.Add("kill_not_applied");
                    else if (counterAfter != counterBefore + 1) errors.Add("kill_not_counted_exactly_once");
                    if (lootAfter > lootBefore) errors.Add("kill_dropped_loot");
                }

                // 4. 满血时让它们叮一阵：痒会不会上身。
                bool itchSeen = false;
                int bitesAtFull = 0;
                if (skip == null && !ShouldAbort())
                {
                    float watchUntil = Time.realtimeSinceStartup + DrillBiteWatchSeconds;
                    while (Time.realtimeSinceStartup < watchUntil && !ShouldAbort() && !itchSeen && swarm.Alive > 0)
                    {
                        yield return null;
                        if (swarm.Itching) itchSeen = true;
                    }
                    bitesAtFull = bites;
                    notes.Add("bites_at_full=" + bitesAtFull + ",itch=" + itchSeen);
                    if (bitesAtFull >= SkyIslandMosquitoRules.ItchOn && !itchSeen) errors.Add("itch_not_triggered_after_" + bitesAtFull + "_bites");
                }

                // 5. 压到叮咬下限附近：再怎么叮也不能打穿（每口 1 点，只在高于下限时下嘴）。
                if (skip == null && !ShouldAbort() && swarm.Alive > 0 && !health.IsDead)
                {
                    float max = health.MaxHealth;
                    float floor = max * SkyIslandMosquitoRules.BiteHealthFloor;
                    health.SetHealth(floor + 1.5f);
                    healthLowered = true;
                    int bitesBefore = bites, otherBefore = otherHits;
                    float lowest = health.CurrentHealth;
                    float floorUntil = Time.realtimeSinceStartup + DrillFloorWatchSeconds;
                    while (Time.realtimeSinceStartup < floorUntil && !ShouldAbort() && !health.IsDead)
                    {
                        yield return null;
                        if (health.CurrentHealth < lowest) lowest = health.CurrentHealth;
                    }
                    notes.Add("floor=" + floor.ToString("F1") + ",lowest=" + lowest.ToString("F1") + ",bites_near_floor=" + (bites - bitesBefore));
                    if (otherHits > otherBefore) notes.Add("floor_check_contaminated(other_damage=" + (otherHits - otherBefore) + ")");
                    else if (lowest <= floor - SkyIslandMosquitoRules.BiteDamage - 0.001f) errors.Add("bite_broke_health_floor");
                }
                else if (skip == null) notes.Add("floor_check_skipped(no_gnats_left)");
            }
            finally
            {
                SkyIslandNight.DevForceNight = previousForceNight;
                Health.OnHurt -= onHurt;
                if (healthLowered && health != null && !health.IsDead)
                    health.SetHealth(Mathf.Max(health.CurrentHealth, previousHealth));
            }

            // 还原结果要读出来才算数：finally 之后读回强制夜里、与开跑前比。旧写法把开跑前的值写成「已还原」，
            // 2026-09-14 实机报告里读成了没还原。不一致一律记红（即便前面判了 SKIP，永夜也不能被 SKIP 盖住）。
            bool forceNightAfter = SkyIslandNight.DevForceNight;
            if (forceNightAfter != previousForceNight) { errors.Add("force_night_not_restored"); skip = null; }
            string metrics = string.Join(" | ", notes.ToArray()) + " | other_hits=" + otherHits
                + ",force_night_before=" + previousForceNight + ",force_night_after=" + forceNightAfter;
            if (skip != null) Record("SKY_DRILL_GNAT_SWARM", "SKIP", sw.ElapsedMilliseconds, metrics, skip);
            else if (errors.Count > 0) Record("SKY_DRILL_GNAT_SWARM", "FAIL", sw.ElapsedMilliseconds, metrics, "云蚋演练不合格：" + string.Join(",", errors.ToArray()));
            else Record("SKY_DRILL_GNAT_SWARM", "PASS", sw.ElapsedMilliseconds, metrics, string.Empty);
        }

        /// <summary>场上散落的可拾取物与箱子总数：击杀前后对比，证明「打死不掉东西」。只数不碰。</summary>
        private static int CountLooseLootForDrill()
        {
            return UnityEngine.Object.FindObjectsOfType<InteractablePickup>().Length
                + UnityEngine.Object.FindObjectsOfType<InteractableLootbox>().Length;
        }

        // ====================================================================
        // 官方对话
        // ====================================================================

        private IEnumerator RunSkyIslandDrillDialogue()
        {
            Stopwatch sw = Stopwatch.StartNew();
            CharacterMainControl player = CharacterMainControl.Main;
            if (player == null) { Record("SKY_DRILL_OFFICIAL_DIALOGUE", "FAIL", 0L, string.Empty, "player_missing"); yield break; }
            if (DialogueUI.instance == null) { Record("SKY_DRILL_OFFICIAL_DIALOGUE", "FAIL", 0L, string.Empty, "本关卡里没有官方 DialogueUI"); yield break; }
            if (DialogueManager.IsDialogueActive) { Record("SKY_DRILL_OFFICIAL_DIALOGUE", "SKIP", 0L, string.Empty, "已有一段对话在进行"); yield break; }

            List<string> errors = new List<string>();
            List<string> notes = new List<string>();
            GameObject host = new GameObject("SkyIslandDrillDialogueActor");
            host.transform.SetParent(player.transform, false);
            CancellationTokenSource first = new CancellationTokenSource();
            CancellationTokenSource second = new CancellationTokenSource();
            try
            {
                IDialogueActor actor = DialogueActorFactory.CreateBilingual(host, "SkyIslandDrill", "演练", "Drill");
                string[][] choices = { new[] { "演练选项一", "Drill option one" }, new[] { "演练选项二", "Drill option two" } };

                // 1. 弹出。
                UniTask<int>.Awaiter pick = DialogueManager.ShowMultipleChoiceBilingual(actor, choices, 0f,
                    "BossRush_SkyIslandDrillChoice", first.Token).GetAwaiter();
                DialogueUIChoice target = null;
                bool? waiting = null;
                float popUntil = Time.realtimeSinceStartup + 3f;
                // 选项控件出现还不算弹好：官方淡入完才进 WaitForChoice、把 confirmedChoice 清成 -1，之前点的会被清掉
                // （2026-09-15 第三轮演练 picked=-2、对话一直挂着）。要等官方真在等玩家选。
                while (Time.realtimeSinceStartup < popUntil && !pick.IsCompleted)
                {
                    yield return null;
                    target = null;
                    foreach (DialogueUIChoice candidate in DialogueUI.instance.GetComponentsInChildren<DialogueUIChoice>(false))
                        if (candidate != null && candidate.Index == 1) target = candidate;
                    waiting = OfficialDialogueWaitingForChoice();
                    if (target != null && waiting != false) break;
                }
                bool popped = DialogueManager.IsDialogueActive && DialogueUI.Active;
                notes.Add("popped=" + popped + ",choice_found=" + (target != null)
                    + ",waiting_for_choice=" + (waiting.HasValue ? waiting.Value.ToString() : "unknown"));
                if (!popped) errors.Add("dialogue_not_shown");

                // 2. 防重入：对话开着时走居民对话的生产入口，必须被挡下（它先判 DialogueManager.IsDialogueActive）。
                SkyIslandResidentDialogue reentry = SkyIslandResidentDialogue.Run("SkyIslandDrill", host.transform,
                    L10n.T("演练", "Drill"), delegate { }, delegate { return true; });
                notes.Add("reentry_blocked=" + (reentry == null));
                if (reentry != null) { errors.Add("reentry_not_blocked"); reentry.Dispose(); }

                // 3. 选：点官方选项控件——与鼠标点击同一条 OnPointerClick → NotifyChoiceConfirmed 路径。
                // UniTask 的任务取完结果就回对象池、令牌作废：完成状态必须在取结果之前读进局部变量，之后只用这个变量。
                // 2026-09-14 实机：取消那一步取完结果又读了一次状态，抛 "Token version is not matched"，整条演练记成 UNHANDLED
                // （UniTaskAwaiterReuseGuard 钉住）。取结果本身抛了别的异常也只记进 errors，不让整条演练崩掉。
                int picked = -2;
                if (target != null && waiting != false)
                {
                    target.OnPointerClick(null);
                    float pickUntil = Time.realtimeSinceStartup + 3f;
                    while (Time.realtimeSinceStartup < pickUntil && !pick.IsCompleted) yield return null;
                    bool pickCompleted = pick.IsCompleted;
                    if (pickCompleted)
                    {
                        try { picked = pick.GetResult(); }
                        catch (OperationCanceledException) { picked = -3; }
                        catch (Exception e) { errors.Add("pick_threw_" + e.GetType().Name); }
                    }
                }
                else first.Cancel();
                float closeUntil = Time.realtimeSinceStartup + 1f;
                while (Time.realtimeSinceStartup < closeUntil && DialogueManager.IsDialogueActive) yield return null;
                notes.Add("picked=" + picked + ",closed_after_pick=" + !DialogueManager.IsDialogueActive);
                if (target == null) errors.Add("official_choice_widget_not_found");
                else if (waiting == false) errors.Add("official_not_waiting_for_choice");
                else if (picked != 1) errors.Add("pick_returned_" + picked);
                if (DialogueManager.IsDialogueActive) errors.Add("still_active_after_pick");

                // 4. 取消：再弹一次，取消令牌，等它以 OperationCanceledException 收场并收起官方界面。
                if (!DialogueManager.IsDialogueActive)
                {
                    UniTask<int>.Awaiter cancelled = DialogueManager.ShowMultipleChoiceBilingual(actor, choices, 0f,
                        "BossRush_SkyIslandDrillChoice", second.Token).GetAwaiter();
                    float openUntil = Time.realtimeSinceStartup + 2f;
                    while (Time.realtimeSinceStartup < openUntil && !DialogueManager.IsDialogueActive && !cancelled.IsCompleted) yield return null;
                    second.Cancel();
                    float cancelUntil = Time.realtimeSinceStartup + 2f;
                    while (Time.realtimeSinceStartup < cancelUntil && !cancelled.IsCompleted) yield return null;
                    bool cancelCompleted = cancelled.IsCompleted;
                    bool threwCancel = false;
                    if (cancelCompleted)
                    {
                        try { cancelled.GetResult(); }
                        catch (OperationCanceledException) { threwCancel = true; }
                        catch (Exception e) { errors.Add("cancel_threw_" + e.GetType().Name); }
                    }
                    float hideUntil = Time.realtimeSinceStartup + 1f;
                    while (Time.realtimeSinceStartup < hideUntil && (DialogueManager.IsDialogueActive || DialogueUI.Active)) yield return null;
                    notes.Add("cancel_completed=" + cancelCompleted + ",threw_cancel=" + threwCancel
                        + ",manager_active=" + DialogueManager.IsDialogueActive + ",official_ui_active=" + DialogueUI.Active);
                    if (!threwCancel) errors.Add("cancel_did_not_throw_operation_canceled");
                    if (DialogueManager.IsDialogueActive) errors.Add("manager_still_active_after_cancel");
                    if (DialogueUI.Active) errors.Add("official_ui_left_open_after_cancel");
                }
                else notes.Add("cancel_step_skipped(previous_dialogue_still_active)");
            }
            finally
            {
                first.Cancel();
                second.Cancel();
                first.Dispose();
                second.Dispose();
                if (host != null) UnityEngine.Object.Destroy(host);
            }

            string metrics = string.Join(" | ", notes.ToArray());
            if (errors.Count > 0) Record("SKY_DRILL_OFFICIAL_DIALOGUE", "FAIL", sw.ElapsedMilliseconds, metrics, "官方对话演练不合格：" + string.Join(",", errors.ToArray()));
            else Record("SKY_DRILL_OFFICIAL_DIALOGUE", "PASS", sw.ElapsedMilliseconds, metrics, string.Empty);
        }
    }
}
#endif
