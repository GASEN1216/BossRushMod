using System;
using System.Collections.Generic;
using UnityEngine;
using ItemStatsSystem;
using ItemStatsSystem.Stats;
using Duckov.UI;
using Duckov.Utilities;

namespace BossRush
{
    public partial class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        #region Mode F 阶段常量

        private const float MODEF_PREPARATION_DURATION = 180f;
        private const float MODEF_BOUNTY_DURATION = 180f;
        private const float MODEF_HUNTSTORM_DURATION = 180f;

        private const float MODEF_BLEED_RATE_PREPARATION = 0.01f;
        private const float MODEF_BLEED_RATE_BOUNTY = 0.015f;
        private const float MODEF_BLEED_RATE_HUNTSTORM = 0.02f;
        private const float MODEF_BLEED_RATE_EXTRACTION = 0.03f;

        private const float MODEF_HEAL_NORMAL_KILL = 0.30f;
        private const float MODEF_HEAL_BOUNTY_KILL = 0.45f;
        private const float MODEF_HEAL_BOUNTY_PER_EXTRA_MARK = 0.05f;
        private const float MODEF_HEAL_BOUNTY_MAX = 0.60f;
        // 成长奖励与成长上限必须同为入场最大生命的比例，否则本局击杀量根本无法填满上限、命火永远充不满。
        private const float MODEF_MAX_HP_GROWTH_RATIO_NORMAL = 0.04f;
        private const float MODEF_MAX_HP_GROWTH_CAP_RATIO = 0.50f;
        // 命火过载的常量、状态与流程见 ModeF/ModeFBloodfire.cs。
        private const float MODEF_HUNTSTORM_UNMARKED_SPEED_BONUS = 0.5f;
        private const float MODEF_EXTRACTION_ALL_SPEED_BONUS = 0.5f;
        private const float MODEF_FORCED_TRACE_DISTANCE = 500f;
        private const float MODEF_BOSS_RETARGET_INTERVAL = 1.5f;
        private const float MODEF_BOSS_INTEGRITY_CHECK_INTERVAL = 1f;

        private const float MODEF_PHASE_BROADCAST_INTERVAL = 15f;

        /// <summary>Mode F 最大生命成长 Modifier 引用（用于清理）</summary>
        private Modifier modeFMaxHealthModifier = null;
        /// <summary>
        /// 每只 Boss 的移速 Modifier 记录。一只 Boss 会挂两条（WalkSpeed + RunSpeed），
        /// 所以存 record 列表而不是单个 Modifier：官方角色没有 "MoveSpeed" 这个 stat
        /// （那是 Animator 参数名，AGENTS §14），必须分别挂到两个真实 stat 上。
        /// </summary>
        private readonly Dictionary<CharacterMainControl, List<ZombieModeAttributeModifierRecord>>
            modeFBossMoveSpeedModifiers
            = new Dictionary<CharacterMainControl, List<ZombieModeAttributeModifierRecord>>();
        private readonly Dictionary<CharacterMainControl, float> modeFBossAppliedSpeedBonuses
            = new Dictionary<CharacterMainControl, float>();
        private readonly Dictionary<CharacterMainControl, CharacterMainControl> modeFBossForcedTargets
            = new Dictionary<CharacterMainControl, CharacterMainControl>();
        private readonly Dictionary<CharacterMainControl, AICharacterController> modeFBossAiControllers
            = new Dictionary<CharacterMainControl, AICharacterController>();
        private float modeFBossRetargetTimer = 0f;
        private float modeFBossIntegrityTimer = 0f;
        /// <summary>防止 ApplyModeFBleedDamage 致死 + TickModeF 帧首检测 重复触发 OnModeFPlayerDeath</summary>
        private bool modeFPlayerDeathHandled = false;

        #endregion

        #region Mode F 状态机

        /// <summary>
        /// 初始化并启动 Mode F 状态机
        /// </summary>
        private void StartModeFRun()
        {
            try
            {
                // 上一局若在 ExitModeF 中途抛出，残留的成长 Modifier 只能靠这里摘除：
                // 一旦丢掉引用就再也没有第二条路径可以撤销它。
                CleanupModeFPlayerMaxHealthGrowth();
                modeFState.CurrentPhase = ModeFPhase.None;
                modeFState.PhaseElapsed = 0f;
                modeFState.TempMaxHealthGrowth = 0f;
                modeFState.BloodfireCharge = 0f;
                modeFState.BloodfireOverloadActive = false;
                modeFState.BloodfireOverloadRemaining = 0f;
                modeFState.PlayerKillCount = 0;
                modeFState.PlayerBountyMarks = 0;
                modeFPendingUtilityRewardCounts.Clear();
                ResetModeFUiCaches();
                MarkModeFPlayerNameTagDirty();
                modeFBossRetargetTimer = 0f;
                modeFBossIntegrityTimer = 0f;
                modeFPlayerDeathHandled = false;
                modeFPendingRespawnCount = 0;
                modeFRespawnInFlightCount = 0;
                modeFHandledBossDeathIds.Clear();
                ResetModeFPlayerBountyKillLatch();
                modeFBossDeathHandlers.Clear();
                modeFBossLootHandlers.Clear();
                modeFBossForcedTargets.Clear();
                modeFBossAppliedSpeedBonuses.Clear();
                modeFBossAiControllers.Clear();
                ClearModeFBloodfireOverloadModifiers();
                modeFHasActiveFortificationHighlight = false;
                MarkModeFBountyLeaderDirty();
                EnsureModeFPlayerNameTag();

                EnterModeFPhase(ModeFPhase.Preparation);
                DevLog("[ModeF] 状态机已启动，进入准备阶段");
            }
            catch (Exception e)
            {
                DevLog("[ModeF] [ERROR] StartModeFRun 失败: " + e.Message);
            }
        }

        /// <summary>
        /// 每帧 Tick Mode F 状态机
        /// </summary>
        private void TickModeF(float deltaTime)
        {
            if (!modeFActive || !modeFState.IsActive) return;

            try
            {
                // 检查玩家是否死亡
                CharacterMainControl player = CharacterMainControl.Main;
                if (player == null || player.Health == null || player.Health.IsDead)
                {
                    OnModeFPlayerDeath();
                    return;
                }

                // 推进阶段计时
                modeFState.PhaseElapsed += deltaTime;

                TickModeFBloodfireOverload(deltaTime);

                // 持续掉血
                ApplyModeFBleedDamage(player, deltaTime);
                UpdateModeFPlayerNameTag();
                if (Time.frameCount % 120 == 0)
                {
                    UpdateModeFPendingUtilityRewards();
                }

                RefreshModeFBountyLeaderIfDirty();
                UpdateModeFBountyRadarUI();
                UpdateModeFFortificationHighlights();
                UpdateFortPlacementMode();
                UpdateModeFRepairSelection();

                // 阶段广播计时
                modeFState.PhaseStatusBroadcastTimer += deltaTime;
                if (modeFState.PhaseStatusBroadcastTimer >= MODEF_PHASE_BROADCAST_INTERVAL)
                {
                    modeFState.PhaseStatusBroadcastTimer = 0f;
                    BroadcastModeFPhaseStatus();
                }

                if (modeFState.CurrentPhase != ModeFPhase.Preparation)
                {
                    modeFBossRetargetTimer += deltaTime;
                    if (modeFBossRetargetTimer >= MODEF_BOSS_RETARGET_INTERVAL)
                    {
                        modeFBossRetargetTimer = 0f;
                        RefreshModeFBossTargets();
                    }
                }

                // 检查阶段切换
                CheckModeFPhaseTransition();
                RefreshModeFBountyLeaderIfDirty();

                modeFBossIntegrityTimer += deltaTime;
                if (modeFBossIntegrityTimer >= MODEF_BOSS_INTEGRITY_CHECK_INTERVAL)
                {
                    modeFBossIntegrityTimer = 0f;
                    // Mode F Boss 自检是兜底逻辑，不需要每帧扫描全部 Boss。
                    ModeFBossIntegrityCheck();
                    TryFulfillModeFPendingRespawns();
                }
            }
            catch (Exception e)
            {
                DevLog("[ModeF] [ERROR] TickModeF 失败: " + e.Message);
            }
        }

        /// <summary>
        /// 进入指定阶段
        /// </summary>
        private void EnterModeFPhase(ModeFPhase phase)
        {
            try
            {
                modeFState.CurrentPhase = phase;
                modeFState.PhaseElapsed = 0f;
                modeFState.PhaseStatusBroadcastTimer = 0f;

                switch (phase)
                {
                    case ModeFPhase.Preparation:
                        modeFState.PhaseDuration = MODEF_PREPARATION_DURATION;
                        ShowBigBanner(L10n.T(
                            "<color=red>血猎追击</color> | <color=yellow>准备阶段</color> 开始！持续 180 秒",
                            "<color=red>Bloodhunt</color> | <color=yellow>Preparation Phase</color> started! 180 seconds"
                        ));
                        DevLog("[ModeF] 进入准备阶段 (180s, 1%/s)");
                        break;

                    case ModeFPhase.Bounty:
                        modeFState.PhaseDuration = MODEF_BOUNTY_DURATION;
                        GenerateBountyList();
                        TryFulfillModeFPendingRespawns();
                        ShowBigBanner(L10n.T(
                            "<color=red>血猎追击</color> | <color=orange>悬赏阶段</color> 开始！悬赏名单已生成",
                            "<color=red>Bloodhunt</color> | <color=orange>Bounty Phase</color> started! Bounty list generated"
                        ));
                        DevLog("[ModeF] 进入悬赏阶段 (180s, 1.5%/s)");
                        break;

                    case ModeFPhase.HuntStorm:
                        modeFState.PhaseDuration = MODEF_HUNTSTORM_DURATION;
                        ShowBigBanner(L10n.T(
                            "<color=red>血猎追击</color> | <color=red>猎潮阶段</color> 开始！Boss 全面追杀！",
                            "<color=red>Bloodhunt</color> | <color=red>Hunt Storm</color> started! All bosses hunting you!"
                        ));
                        DevLog("[ModeF] 进入猎潮阶段 (180s, 2%/s)");
                        break;

                    case ModeFPhase.Extraction:
                        modeFState.PhaseDuration = float.MaxValue;
                        SpawnFinalExtractionPoint();
                        ShowBigBanner(L10n.T(
                            "<color=red>血猎追击</color> | <color=green>撤离阶段</color> 开始！撤离点已生成，速速撤离！",
                            "<color=red>Bloodhunt</color> | <color=green>Extraction Phase</color> started! Extraction point spawned, evacuate now!"
                        ));
                        DevLog("[ModeF] 进入撤离阶段 (无限, 3%/s)");
                        break;
                }

                ApplyModeFPhasePressure();
            }
            catch (Exception e)
            {
                DevLog("[ModeF] [ERROR] EnterModeFPhase 失败: " + e.Message);
            }
        }

        /// <summary>
        /// 检查是否需要切换阶段
        /// </summary>
        private void CheckModeFPhaseTransition()
        {
            if (modeFState.PhaseElapsed < modeFState.PhaseDuration) return;

            switch (modeFState.CurrentPhase)
            {
                case ModeFPhase.Preparation:
                    EnterModeFPhase(ModeFPhase.Bounty);
                    break;
                case ModeFPhase.Bounty:
                    EnterModeFPhase(ModeFPhase.HuntStorm);
                    break;
                case ModeFPhase.HuntStorm:
                    EnterModeFPhase(ModeFPhase.Extraction);
                    break;
                // Extraction 无限持续
            }
        }

        /// <summary>当前 Mode F 阶段，供 F3 验收断言阶段推进是否真的发生。</summary>
        internal ModeFPhase ModeFCurrentPhaseForValidation
        {
            get { return modeFState != null ? modeFState.CurrentPhase : ModeFPhase.None; }
        }

        /// <summary>
        /// Dev 验收专用：把当前阶段的计时推到时长上限，然后走既有 CheckModeFPhaseTransition。
        /// 真实阶段机（EnterModeFPhase 的 banner / GenerateBountyList / SpawnFinalExtractionPoint
        /// / ApplyModeFPhasePressure）因此完整执行——这里只是把「等 180 秒」换成「时间到了」，
        /// 不另写一条跳转路径，否则测的就不是玩家那条链了。
        /// </summary>
        internal bool DebugAdvanceModeFPhaseForValidation(out string reason)
        {
            reason = null;
            if (!DevModeEnabled) { reason = "dev_mode_disabled"; return false; }
            if (!modeFActive || modeFState == null || !modeFState.IsActive)
            {
                reason = "mode_f_not_active";
                return false;
            }
            if (modeFState.CurrentPhase == ModeFPhase.Extraction)
            {
                reason = "already_final_phase";
                return false;
            }

            ModeFPhase before = modeFState.CurrentPhase;
            try
            {
                modeFState.PhaseElapsed = modeFState.PhaseDuration;
                CheckModeFPhaseTransition();
            }
            catch (Exception e)
            {
                reason = e.GetType().Name + ":" + e.Message;
                return false;
            }

            if (modeFState.CurrentPhase == before)
            {
                reason = "phase_did_not_advance_from_" + before;
                return false;
            }
            DevLog("[ModeF] [Validation] 阶段推进: " + before + " -> " + modeFState.CurrentPhase);
            return true;
        }

        /// <summary>
        /// 应用持续掉血
        /// </summary>
        private void ApplyModeFBleedDamage(CharacterMainControl player, float deltaTime)
        {
            try
            {
                float rate = GetModeFBleedRate();
                if (rate <= 0f) return;

                // 变异词条：流血速率倍率
                if (MutatorManager.IsActive && MutatorManager.BleedRateMultiplier != 1.0f)
                {
                    rate *= MutatorManager.BleedRateMultiplier;
                }

                if (modeFState.BloodfireOverloadActive)
                {
                    rate *= MODEF_BLOODFIRE_BLEED_MULTIPLIER;
                }

                Health health = player.Health;
                if (health == null || health.IsDead) return;

                if (modeFState.InitialMaxHealthSnapshot <= 0.01f && health.MaxHealth > 0.01f)
                {
                    modeFState.InitialMaxHealthSnapshot = health.MaxHealth;
                    DevLog("[ModeF] [WARNING] 初始最大生命快照缺失，已回填为当前最大生命: " + modeFState.InitialMaxHealthSnapshot);
                }

                float damage = rate * modeFState.InitialMaxHealthSnapshot * deltaTime;
                if (damage <= 0f) return;

                // Mode F 的持续掉血属于模式规则，不应再被护甲/元素减伤抵消。
                float nextHealth = health.CurrentHealth - damage;
                if (nextHealth > 0f)
                {
                    health.CurrentHealth = nextHealth;
                    return;
                }

                health.CurrentHealth = Mathf.Max(health.CurrentHealth, 1f);

                DamageInfo lethalDamage = new DamageInfo(player);
                lethalDamage.damageValue = Mathf.Max(Mathf.Max(modeFState.InitialMaxHealthSnapshot, health.MaxHealth), 100f);
                lethalDamage.damageType = DamageTypes.realDamage;
                lethalDamage.ignoreArmor = true;
                lethalDamage.ignoreDifficulty = true;
                lethalDamage.damagePoint = player.transform.position + Vector3.up;
                lethalDamage.damageNormal = Vector3.up;
                lethalDamage.AddElementFactor(ElementTypes.physics, 1f);
                lethalDamage.AddElementFactor(ElementTypes.fire, 1f);
                lethalDamage.AddElementFactor(ElementTypes.poison, 1f);
                lethalDamage.AddElementFactor(ElementTypes.electricity, 1f);
                lethalDamage.AddElementFactor(ElementTypes.space, 1f);
                lethalDamage.AddElementFactor(ElementTypes.ghost, 1f);
                lethalDamage.AddElementFactor(ElementTypes.ice, 1f);
                health.Hurt(lethalDamage);

                if (!health.IsDead)
                {
                    // 某些兜底死亡路径不会触发 Health.OnDead，这里手动补记亡魂数据。
                    RecordManualDeathWraithData_DeathWraith(
                        player,
                        lethalDamage,
                        "ModeFBleedFallback");
                    health.CurrentHealth = 0f;
                }

                OnModeFPlayerDeath();
            }
            catch (Exception e)
            {
                DevLog("[ModeF] [ERROR] ApplyModeFBleedDamage 失败: " + e.Message);
                // 致死路径异常时仍需确保死亡处理被触发，否则玩家卡在 1HP 不死不活
                try { OnModeFPlayerDeath(); } catch { }
            }
        }

        /// <summary>
        /// 获取当前阶段的掉血率
        /// </summary>
        private float GetModeFBleedRate()
        {
            switch (modeFState.CurrentPhase)
            {
                case ModeFPhase.Preparation: return MODEF_BLEED_RATE_PREPARATION;
                case ModeFPhase.Bounty: return MODEF_BLEED_RATE_BOUNTY;
                case ModeFPhase.HuntStorm: return MODEF_BLEED_RATE_HUNTSTORM;
                case ModeFPhase.Extraction: return MODEF_BLEED_RATE_EXTRACTION;
                default: return 0f;
            }
        }

        /// <summary>
        /// 击杀 Boss 后的回血和最大生命成长
        /// </summary>
        private (float healAmount, float maxHealthGain, float bloodfireGain, bool overloadStarted, float overloadExtension)
            ApplyModeFKillReward(bool isBountyBoss, int victimMarks)
        {
            try
            {
                CharacterMainControl player = CharacterMainControl.Main;
                if (player == null || player.Health == null || player.Health.IsDead)
                {
                    return (0f, 0f, 0f, false, 0f);
                }

                Health health = player.Health;
                int resolvedVictimMarks = Mathf.Max(0, victimMarks);
                float initialMaxHealth = modeFState.InitialMaxHealthSnapshot;
                if (initialMaxHealth <= 0.01f)
                {
                    initialMaxHealth = health.MaxHealth;
                    modeFState.InitialMaxHealthSnapshot = initialMaxHealth;
                }

                // 回血始终按入场生命计算，避免随本局最大生命成长继续放大。
                float healPercent;
                if (isBountyBoss)
                {
                    int extraMarks = Mathf.Max(0, resolvedVictimMarks - 1);
                    healPercent = Mathf.Min(
                        MODEF_HEAL_BOUNTY_MAX,
                        MODEF_HEAL_BOUNTY_KILL + extraMarks * MODEF_HEAL_BOUNTY_PER_EXTRA_MARK);
                }
                else
                {
                    healPercent = MODEF_HEAL_NORMAL_KILL;
                }
                float healAmount = initialMaxHealth * healPercent;
                float newHp = Mathf.Min(health.CurrentHealth + healAmount, health.MaxHealth);
                health.CurrentHealth = newHp;
                DevLog("[ModeF] 击杀回血: " + healAmount.ToString("F1") + " (bounty=" + isBountyBoss + ")");

                float growthUnit = initialMaxHealth * MODEF_MAX_HP_GROWTH_RATIO_NORMAL;
                float growthReward = isBountyBoss
                    ? growthUnit * Mathf.Max(1, resolvedVictimMarks)
                    : growthUnit;
                float growthCap = Mathf.Max(1f, initialMaxHealth * MODEF_MAX_HP_GROWTH_CAP_RATIO);
                float previousGrowth = Mathf.Clamp(modeFState.TempMaxHealthGrowth, 0f, growthCap);
                float appliedGrowth = Mathf.Min(growthReward, Mathf.Max(0f, growthCap - previousGrowth));
                float overflowGrowth = Mathf.Max(0f, growthReward - appliedGrowth);
                float actualMaxHealthGain = 0f;

                if (appliedGrowth > 0.001f)
                {
                    try
                    {
                        Item characterItem = player.CharacterItem;
                        Stat maxHealthStat = characterItem != null ? characterItem.GetStat("MaxHealth") : null;
                        if (maxHealthStat == null)
                        {
                            throw new InvalidOperationException("玩家 MaxHealth Stat 不可用");
                        }

                        float oldMaxHealth = health.MaxHealth;
                        if (modeFMaxHealthModifier != null)
                        {
                            try { modeFMaxHealthModifier.RemoveFromTarget(); } catch { }
                        }

                        modeFState.TempMaxHealthGrowth = previousGrowth + appliedGrowth;
                        modeFMaxHealthModifier = new Modifier(ModifierType.Add, modeFState.TempMaxHealthGrowth, this);
                        maxHealthStat.AddModifier(modeFMaxHealthModifier);

                        float raisedMaxHealth = health.MaxHealth;
                        actualMaxHealthGain = Mathf.Max(0f, raisedMaxHealth - oldMaxHealth);
                        health.CurrentHealth = Mathf.Min(health.CurrentHealth + actualMaxHealthGain, raisedMaxHealth);
                        DevLog("[ModeF] 最大生命成长: +" + actualMaxHealthGain.ToString("F1")
                            + " (bounty=" + isBountyBoss
                            + ", victimMarks=" + resolvedVictimMarks
                            + ", 总计+" + modeFState.TempMaxHealthGrowth.ToString("F1")
                            + ", 上限+" + growthCap.ToString("F1") + ")");
                    }
                    catch (Exception e)
                    {
                        DevLog("[ModeF] [WARNING] 最大生命成长 Modifier 失败: " + e.Message);
                        // 状态必须与实际生效的 Modifier 保持一致，否则后续击杀会按虚高的成长量
                        // 计算上限余量，提前判定到顶。
                        modeFState.TempMaxHealthGrowth = previousGrowth;
                        modeFMaxHealthModifier = null;
                        appliedGrowth = 0f;
                        overflowGrowth = 0f;
                    }
                }

                bool overloadStarted;
                float overloadExtension;
                float bloodfireGain = ApplyModeFBloodfireKillReward(
                    player,
                    overflowGrowth,
                    growthCap,
                    isBountyBoss,
                    out overloadStarted,
                    out overloadExtension);

                return (healAmount, actualMaxHealthGain, bloodfireGain, overloadStarted, overloadExtension);
            }
            catch (Exception e)
            {
                DevLog("[ModeF] [ERROR] ApplyModeFKillReward 失败: " + e.Message);
                return (0f, 0f, 0f, false, 0f);
            }
        }

        /// <summary>
        /// 玩家死亡处理
        /// </summary>
        private void OnModeFPlayerDeath()
        {
            if (modeFPlayerDeathHandled) return;
            modeFPlayerDeathHandled = true;

            bool exitAttempted = false;
            try
            {
                DevLog("[ModeF] 玩家死亡，Mode F 失败");

                ShowBigBanner(L10n.T(
                    "<color=red>血猎追击失败！</color> 你倒在了血猎场上...",
                    "<color=red>Bloodhunt Failed!</color> You fell on the hunting grounds..."
                ));

                // 印记清零、不发奖励
                modeFState.PlayerBountyMarks = 0;
                MarkModeFPlayerNameTagDirty();
                modeFState.TempMaxHealthGrowth = 0f;

                ExitModeF();
                exitAttempted = true;
            }
            catch (Exception e)
            {
                DevLog("[ModeF] [ERROR] OnModeFPlayerDeath 失败: " + e.Message);
            }
            finally
            {
                if (!exitAttempted && modeFActive)
                {
                    try { ExitModeF(); } catch (Exception exitEx) { DevLog("[ModeF] [ERROR] OnModeFPlayerDeath 强制退出失败: " + exitEx.Message); }
                }
            }
        }

        private void CleanupModeFPlayerMaxHealthGrowth()
        {
            Modifier growthModifier = modeFMaxHealthModifier;
            modeFMaxHealthModifier = null;
            // 只有本局确实授予过成长时才钳制，避免把官方超量治疗、其他 Mod 或装备 Buff 造成的
            // 超额生命一并压掉——那些不是 Mode F 的账。
            bool hadGrowth = growthModifier != null || modeFState.TempMaxHealthGrowth > 0.001f;
            modeFState.TempMaxHealthGrowth = 0f;

            if (growthModifier != null)
            {
                try
                {
                    // Modifier 在场景切换清理期间仍保留最初绑定的 Stat 目标。
                    growthModifier.RemoveFromTarget();
                }
                catch (Exception e)
                {
                    DevLog("[ModeF] [WARNING] 最大生命成长 Modifier 清理失败: " + e.Message);
                }
            }

            try
            {
                CharacterMainControl player = CharacterMainControl.Main;
                Health health = player != null ? player.Health : null;
                if (health == null || !hadGrowth)
                {
                    return;
                }

                float restoredMaxHealth = health.MaxHealth;
                if (restoredMaxHealth > 0f && health.CurrentHealth > restoredMaxHealth)
                {
                    float previousHealth = health.CurrentHealth;
                    health.SetHealth(restoredMaxHealth);
                    DevLog("[ModeF] 退出时钳制超额生命: "
                        + previousHealth.ToString("F1") + " -> " + health.CurrentHealth.ToString("F1")
                        + " / " + restoredMaxHealth.ToString("F1"));
                }
            }
            catch (Exception e)
            {
                DevLog("[ModeF] [WARNING] 退出时生命钳制失败: " + e.Message);
            }
        }

        /// <summary>
        /// 退出 Mode F，清理所有临时状态
        /// </summary>
        private void ExitModeF(bool showEndMessage = true)
        {
            try
            {
                if (!modeFActive) return;

                // 如果正在放置工事，取消并退还物品
                CancelFortPlacement();
                FlushModeFPendingUtilityRewards(true);

                DevLog("[ModeF] 退出 Mode F 模式");

                modeFActive = false;
                modeFState.IsActive = false;
                InvalidateModeFSession();

                // 清理变异词条（覆盖正常通关 / 玩家死亡 / 手动退出）
                ClearMutatorsForMode("ModeF");

                EndModeFBloodfireOverload(false);

                // 撤销本局最大生命成长后，当前生命也必须回落到恢复后的真实上限。
                CleanupModeFPlayerMaxHealthGrowth();
                modeFBossRetargetTimer = 0f;
                modeFBossIntegrityTimer = 0f;
                modeFPlayerDeathHandled = false;
                modeFPendingRespawnCount = 0;
                modeFRespawnInFlightCount = 0;
                modeFHandledBossDeathIds.Clear();
                ResetModeFPlayerBountyKillLatch();
                CleanupModeFPlayerNameTag();
                CleanupModeFBountyRadarUI();
                CleanupModeFExtractionMapMarker();
                ResetModeFUiCaches();
                ClearModeFBossMoveSpeedModifiers();
                modeFState.ExtractionResolved = true;

                try
                {
                    if (modeFState.ActiveExtractionArea != null)
                    {
                        EvacuationCountdownUI.Release(modeFState.ActiveExtractionArea);
                    }
                }
                catch { }

                try
                {
                    if (modeFState.ActiveExtractionArea != null && modeFState.ActiveExtractionArea.gameObject != null)
                    {
                        UnityEngine.Object.Destroy(modeFState.ActiveExtractionArea.gameObject);
                    }
                }
                catch { }

                RestoreOriginalExtractionPoints();

                // 清理所有存活 Boss
                RunScopedRegistry.ForEachReverse(
                    modeFState.ActiveBosses,
                    boss =>
                    {
                        Teams? bossTeam = null;
                        if (!(boss == null))
                        {
                            try { bossTeam = boss.Team; } catch { }
                        }

                        CleanupModeFBossRuntimeState(boss, bossTeam);
                        RetireModeFActiveBossAtExit(boss);
                    },
                    (e, boss) => DevLog("[ModeF] [WARNING] Cleanup active boss failed: " + e.Message));

                // H2: 清理 Boss 成长 Modifier 缓存
                modeFBossModifiers.Clear();
                modeFBossDeathHandlers.Clear();
                modeFBossLootHandlers.Clear();
                modeFBossForcedTargets.Clear();
                modeFBossAppliedSpeedBonuses.Clear();
                modeFBossAiControllers.Clear();
                modeFBountyLeaderDirty = false;
                modeFBountyLeaderPreferred = null;
                ClearModeFLeaderChangeContext();
                modeFHasActiveFortificationHighlight = false;
                ResetModeESharedRuntimeAfterModeF();

                // 清理工事
                CleanupAllModeFortifications();

                // 清理商人和快递员
                CleanupModeEMerchant();
                DestroyCourierNPC();

                // 清理龙息Buff处理器
                DragonBreathBuffHandler.Cleanup();

                // 重置状态
                modeFState.Reset();
                modeFActiveBossSet.Clear();
                ClearModeFBossRegenCache();
                ClearEnemyRecoveryMonitorState();
                ClearAllModeFBossPlunderLootState();


                if (showEndMessage)
                {
                    ShowMessage(L10n.T(
                        "血猎追击模式已结束！",
                        "Bloodhunt mode ended!"
                    ));
                }
            }
            catch (Exception e)
            {
                DevLog("[ModeF] [ERROR] ExitModeF 失败: " + e.Message);
            }
        }

        private void RetireModeFActiveBossAtExit(CharacterMainControl boss)
        {
            if (object.ReferenceEquals(boss, null))
            {
                return;
            }

            try
            {
                AICharacterController ai = null;
                try { ai = GetModeFBossAIController(boss); } catch { }
                if (ai != null)
                {
                    try { ai.searchedEnemy = null; } catch { }
                    try { ai.noticed = false; } catch { }
                    try { ai.forceTracePlayerDistance = 0f; } catch { }
                    try { ai.enabled = false; } catch { }
                }

                try { boss.SetRunInput(false); } catch { }
                try { boss.dropBoxOnDead = false; } catch { }

                GameObject bossObject = null;
                try { bossObject = boss.gameObject; } catch { }
                if (bossObject == null)
                {
                    return;
                }

                try
                {
                    if (bossObject.activeSelf)
                    {
                        bossObject.SetActive(false);
                    }
                }
                catch { }

                StartCoroutine(DestroyModeFExitBossDeferred(bossObject));
            }
            catch (Exception e)
            {
                DevLog("[ModeF] [WARNING] RetireModeFActiveBossAtExit failed: " + e.Message);
            }
        }

        private System.Collections.IEnumerator DestroyModeFExitBossDeferred(GameObject bossObject)
        {
            if (bossObject == null)
            {
                yield break;
            }

            yield return new WaitForSeconds(0.25f);

            if (bossObject != null)
            {
                UnityEngine.Object.Destroy(bossObject);
            }
        }

        /// <summary>
        /// 获取阶段中文名
        /// </summary>
        private string GetModeFPhaseName(ModeFPhase phase)
        {
            switch (phase)
            {
                case ModeFPhase.Preparation: return L10n.T("准备阶段", "Preparation");
                case ModeFPhase.Bounty: return L10n.T("悬赏阶段", "Bounty");
                case ModeFPhase.HuntStorm: return L10n.T("猎潮阶段", "Hunt Storm");
                case ModeFPhase.Extraction: return L10n.T("撤离阶段", "Extraction");
                default: return "";
            }
        }

        private void ApplyModeFPhasePressure()
        {
            for (int i = 0; i < modeFState.ActiveBosses.Count; i++)
            {
                ApplyModeFPressureToBoss(modeFState.ActiveBosses[i]);
            }
        }

        private void RefreshModeFBossTargets()
        {
            ApplyModeFPhasePressure();
        }

        private AICharacterController GetModeFBossAIController(CharacterMainControl boss)
        {
            if (boss == null)
            {
                return null;
            }

            AICharacterController ai = null;
            if (modeFBossAiControllers.TryGetValue(boss, out ai) && ai != null)
            {
                return ai;
            }

            ai = boss.GetComponentInChildren<AICharacterController>(true);
            if (ai != null)
            {
                modeFBossAiControllers[boss] = ai;
            }
            else
            {
                modeFBossAiControllers.Remove(boss);
            }

            return ai;
        }

        private bool TryApplyModeFBossTarget(CharacterMainControl boss, AICharacterController ai, CharacterMainControl target)
        {
            if (boss == null || ai == null || target == null || target.mainDamageReceiver == null)
            {
                return false;
            }

            ai.forceTracePlayerDistance = Mathf.Max(ai.forceTracePlayerDistance, MODEF_FORCED_TRACE_DISTANCE);
            ai.traceTargetChance = 1f;
            ai.noticed = true;
            boss.SetRunInput(true);

            CharacterMainControl lastTarget = null;
            bool sameTarget = modeFBossForcedTargets.TryGetValue(boss, out lastTarget) &&
                              lastTarget == target &&
                              ai.searchedEnemy == target.mainDamageReceiver;
            if (sameTarget)
            {
                return true;
            }

            ai.searchedEnemy = target.mainDamageReceiver;
            try { ai.SetTarget(target.mainDamageReceiver.transform); } catch { }
            try { ai.SetNoticedToTarget(target.mainDamageReceiver); } catch { }
            try { ai.MoveToPos(target.transform.position); } catch { }
            modeFBossForcedTargets[boss] = target;
            return true;
        }

        private void ApplyModeFPressureToBoss(CharacterMainControl boss)
        {
            try
            {
                if (boss == null || boss.CharacterItem == null)
                {
                    return;
                }

                int marks = 0;
                modeFState.BountyMarksByCharacterId.TryGetValue(boss.GetInstanceID(), out marks);

                float speedBonus = 0f;
                bool forcePlayerTarget = false;
                switch (modeFState.CurrentPhase)
                {
                    case ModeFPhase.HuntStorm:
                        if (marks <= 0)
                        {
                            speedBonus += MODEF_HUNTSTORM_UNMARKED_SPEED_BONUS;
                        }
                        forcePlayerTarget = true;
                        break;
                    case ModeFPhase.Extraction:
                        speedBonus += MODEF_EXTRACTION_ALL_SPEED_BONUS;
                        if (marks <= 0)
                        {
                            speedBonus += MODEF_HUNTSTORM_UNMARKED_SPEED_BONUS;
                        }
                        forcePlayerTarget = true;
                        break;
                }

                ApplyModeFBossMoveSpeedModifier(boss, speedBonus);

                CharacterMainControl preferredTarget = GetModeFPreferredTargetForBoss(boss);
                if (preferredTarget != null && preferredTarget.mainDamageReceiver != null)
                {
                    AICharacterController preferredAi = GetModeFBossAIController(boss);
                    if (preferredAi != null && TryApplyModeFBossTarget(boss, preferredAi, preferredTarget))
                    {
                        return;
                    }
                }

                if (!forcePlayerTarget)
                {
                    modeFBossForcedTargets.Remove(boss);
                    return;
                }

                CharacterMainControl player = CharacterMainControl.Main;
                if (player == null || player.mainDamageReceiver == null)
                {
                    return;
                }

                AICharacterController ai = GetModeFBossAIController(boss);
                if (ai == null)
                {
                    return;
                }

                TryApplyModeFBossTarget(boss, ai, player);
            }
            catch (Exception e)
            {
                DevLog("[ModeF] [WARNING] ApplyModeFPressureToBoss 失败: " + e.Message);
            }
        }

        private CharacterMainControl GetModeFPreferredTargetForBoss(CharacterMainControl boss)
        {
            if (boss == null)
            {
                return null;
            }

            switch (modeFState.CurrentPhase)
            {
                case ModeFPhase.Bounty:
                    return FindModeFBountyPriorityTarget(boss);
                case ModeFPhase.HuntStorm:
                case ModeFPhase.Extraction:
                    CharacterMainControl player = CharacterMainControl.Main;
                    if (IsModeFValidCombatTarget(boss, player))
                    {
                        return player;
                    }
                    return FindModeFBountyPriorityTarget(boss);
                default:
                    return null;
            }
        }

        private CharacterMainControl FindModeFBountyPriorityTarget(CharacterMainControl boss)
        {
            CharacterMainControl player = CharacterMainControl.Main;
            int playerMarks = modeFState.PlayerBountyMarks;

            if (!modeFBountyLeaderDirty)
            {
                CharacterMainControl currentLeader = modeFState.CurrentBountyLeader;
                int currentLeaderMarks = modeFState.CurrentBountyLeaderMarks;

                if (currentLeader != null &&
                    currentLeaderMarks > 0 &&
                    currentLeaderMarks >= playerMarks &&
                    IsModeFValidCombatTarget(boss, currentLeader))
                {
                    return currentLeader;
                }

                if (playerMarks > currentLeaderMarks && IsModeFValidCombatTarget(boss, player))
                {
                    return player;
                }
            }

            CharacterMainControl bestTarget = null;
            int bestMarks = 0;

            if (IsModeFValidCombatTarget(boss, player) && playerMarks > 0)
            {
                bestTarget = player;
                bestMarks = playerMarks;
            }

            for (int i = 0; i < modeFState.ActiveBosses.Count; i++)
            {
                CharacterMainControl candidate = modeFState.ActiveBosses[i];
                if (!IsModeFValidCombatTarget(boss, candidate))
                {
                    continue;
                }

                int candidateMarks = 0;
                modeFState.BountyMarksByCharacterId.TryGetValue(candidate.GetInstanceID(), out candidateMarks);
                if (candidateMarks <= 0)
                {
                    continue;
                }

                if (candidateMarks > bestMarks)
                {
                    bestTarget = candidate;
                    bestMarks = candidateMarks;
                    continue;
                }

                if (candidateMarks == bestMarks && candidate == modeFState.CurrentBountyLeader)
                {
                    bestTarget = candidate;
                }
            }

            return bestTarget;
        }

        private bool IsModeFValidCombatTarget(CharacterMainControl boss, CharacterMainControl candidate)
        {
            if (boss == null || candidate == null || candidate == boss)
            {
                return false;
            }

            if (candidate.gameObject == null || candidate.Health == null || candidate.Health.IsDead || candidate.mainDamageReceiver == null)
            {
                return false;
            }

            if (!candidate.IsMainCharacter &&
                candidate.Team == boss.Team &&
                candidate.Team != Teams.all &&
                boss.Team != Teams.all)
            {
                return false;
            }

            return true;
        }

        private void ApplyModeFBossMoveSpeedModifier(CharacterMainControl boss, float speedBonus)
        {
            try
            {
                if (object.ReferenceEquals(boss, null))
                {
                    return;
                }

                if (boss == null || boss.CharacterItem == null)
                {
                    if (speedBonus <= 0f)
                    {
                        modeFBossMoveSpeedModifiers.Remove(boss);
                        modeFBossAppliedSpeedBonuses.Remove(boss);
                    }
                    return;
                }

                float appliedBonus = 0f;
                if (modeFBossAppliedSpeedBonuses.TryGetValue(boss, out appliedBonus) &&
                    Mathf.Abs(appliedBonus - speedBonus) < 0.001f)
                {
                    return;
                }

                // 先摘旧的，再按新数值挂。RemoveAll 会顺带清空列表，可直接复用。
                List<ZombieModeAttributeModifierRecord> records;
                if (!modeFBossMoveSpeedModifiers.TryGetValue(boss, out records) || records == null)
                {
                    records = new List<ZombieModeAttributeModifierRecord>();
                    modeFBossMoveSpeedModifiers[boss] = records;
                }
                RuntimeStatModifierTracker.RemoveAll(records, "ModeF BossMoveSpeed");

                if (speedBonus <= 0f)
                {
                    modeFBossMoveSpeedModifiers.Remove(boss);
                    modeFBossAppliedSpeedBonuses.Remove(boss);
                    return;
                }

                // 官方移动只读 WalkSpeed / RunSpeed / Moveability 三个 stat。
                // 这里两条都挂：Boss 追击时走 RunSpeed，巡逻时走 WalkSpeed，
                // 只挂一条会让加速在另一半时间里看不出来。
                bool walkAdded = RuntimeStatModifierTracker.TryAdd(
                    boss, ZombieModeStatNames.WalkSpeed, speedBonus, this, records,
                    "ModeF BossMoveSpeed");
                bool runAdded = RuntimeStatModifierTracker.TryAdd(
                    boss, ZombieModeStatNames.RunSpeed, speedBonus, this, records,
                    "ModeF BossMoveSpeed");

                if (!walkAdded && !runAdded)
                {
                    // 两个 stat 都取不到就别登记，免得 AppliedSpeedBonuses 谎报已生效
                    modeFBossMoveSpeedModifiers.Remove(boss);
                    modeFBossAppliedSpeedBonuses.Remove(boss);
                    return;
                }

                modeFBossAppliedSpeedBonuses[boss] = speedBonus;
            }
            catch (Exception e)
            {
                DevLog("[ModeF] [WARNING] ApplyModeFBossMoveSpeedModifier 失败: " + e.Message);
            }
        }

        private void ClearModeFBossMoveSpeedModifiers()
        {
            foreach (var kvp in modeFBossMoveSpeedModifiers)
            {
                // RemoveAll 内部逐条 try/catch 并容忍已销毁的 Stat，
                // 且记录的是 Stat 引用而不是 stat 名，Boss 已销毁时也不会误摘别人的。
                RuntimeStatModifierTracker.RemoveAll(kvp.Value, "ModeF BossMoveSpeed");
            }

            modeFBossMoveSpeedModifiers.Clear();
            modeFBossAppliedSpeedBonuses.Clear();
            modeFBossForcedTargets.Clear();
            modeFBossAiControllers.Clear();
        }

        #endregion
    }
}
