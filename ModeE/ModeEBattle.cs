// ============================================================================
// ModeEBattle.cs - Boss 生成与动态难度缩放
// ============================================================================
// 模块说明：
//   负责 Mode E 的 Boss 一次性生成、敌人死亡追踪和按阵营动态难度缩放。
//   敌人生成复用 SpawnEnemyCore 通用方法（支持龙裔遗族和龙王）。
//
// 动态缩放规则：
//   - 每个敌人在出生时记录阵营死亡基线 deathBaseline
//   - 个人层数 = 当前阵营死亡计数 - deathBaseline（最小为0）
//   - 每层生命/枪伤/近战伤害 +5%，玩家层数按最终击杀独立累计
// ============================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using UnityEngine;
using UnityEngine.Events;
using ItemStatsSystem;
using ItemStatsSystem.Stats;
using Cysharp.Threading.Tasks;
using Duckov.UI.DialogueBubbles;

namespace BossRush
{
    /// <summary>
    /// Mode E Boss 生成与动态难度缩放模块
    /// </summary>
    internal sealed partial class ModeERuntimeModule
    {
        #region Mode E registration and shell rewards

        private ModeEShellRewardKind ClassifyFinalModeEShellRewardKind(
            EnemyPresetInfo finalPreset,
            bool isBoss,
            bool isModeFRun)
        {
            if (isModeFRun || !isBoss || finalPreset == null)
            {
                return ModeEShellRewardKind.None;
            }

            spawnRuntime.BuildModeEFactionPresetCaches();
            if (spawnRuntime.ContainsBossPreset(finalPreset))
            {
                return ModeEShellRewardKind.StandardBoss;
            }
            if (spawnRuntime.ContainsMinionPreset(finalPreset))
            {
                return ModeEShellRewardKind.PromotedBoss;
            }

            ModBehaviour.DevLog("[ModeE/Shell] final preset reward classification unavailable: " +
                (finalPreset.name ?? "<unnamed>"));
            return ModeEShellRewardKind.None;
        }

        /// <summary>
        /// Mode E 敌人生成成功后的回调：设置阵营、命名、AI配置、死亡注册
        /// 阵营来自 Boss 预设的原始 team，通过 SetTeam 确保运行时一致性
        /// </summary>
        /// <param name="promotedToBoss">是否为小怪被提升为 Boss（需要克隆预设设 showName=true）</param>
        internal bool OnModeEEnemySpawned(EnemySpawnContext ctx, Teams faction, bool promotedToBoss = false)
        {
            CharacterMainControl character = null;
            try
            {
                if (ctx == null)
                {
                    return false;
                }

                character = ctx.character;
                bool isModeFRun = modeEHost.IsModeFActive && !modeEActive;
                if (character == null)
                {
                    return false;
                }

                Teams runtimeFaction = isModeFRun
                    ? modeEHost.ResolveModeFBossCombatTeam(faction, ctx.preset, ctx.position)
                    : faction;
                Teams trackedFaction = runtimeFaction;

                // 克隆 characterPreset 副本，避免修改原版 ScriptableObject
                // 1) 统一设置 aiCombatFactor=1，使 AI 互相攻击时伤害不被缩放
                // 2) 小怪提升为 Boss，或被 Mode F 复用时，额外设置 showName/showHealthBar
                if (character.characterPreset != null)
                {
                    CharacterRandomPreset customPreset = UnityEngine.Object.Instantiate(character.characterPreset);
                    // AI打AI伤害系数统一为1，确保各阵营Boss互殴时伤害公平
                    customPreset.aiCombatFactor = 1f;
                    if (promotedToBoss || isModeFRun)
                    {
                        customPreset.showName = true;
                        customPreset.showHealthBar = true;
                    }
                    character.characterPreset = customPreset;
                    ModeECharacterPresetLease presetLease =
                        character.gameObject.GetComponent<ModeECharacterPresetLease>();
                    if (presetLease == null)
                    {
                        presetLease = character.gameObject.AddComponent<ModeECharacterPresetLease>();
                    }
                    presetLease.Assign(customPreset);
                    ModBehaviour.DevLog("[ModeE] 已克隆预设并设置 aiCombatFactor=1"
                        + ((promotedToBoss || isModeFRun) ? ", showName=true" : "")
                        + ": " + ctx.preset.displayName);
                }

                if (isModeFRun && ctx.preset != null)
                {
                    modeEHost.SetModeFBossDisplayName(character, ctx.preset.displayName, runtimeFaction);
                }

                // 命名
                character.gameObject.name = (isModeFRun ? "ModeF_" : "ModeE_") + runtimeFaction + "_" + ctx.preset.displayName;

                // 设置阵营（确保运行时 team 与预设原始阵营一致）
                // 对于龙裔/龙王：CreateCharacterAsync 使用的是 Cname_Boss_Red 基础预设（team=scav），
                // 但 EnemyPresetInfo.team 记录的是 wolf，所以 SetTeam 是必要的
                character.SetTeam(runtimeFaction);
                ModBehaviour.DevLog("[ModeE] 敌人阵营已设置: " + ctx.preset.displayName + " -> " + runtimeFaction + " (预设team=" + ctx.preset.team + ")");

                // Mode E AI：不主动设置目标，让 AI 自然感知范围内的敌人后再开打
                // 不设置 forceTracePlayerDistance，不设置初始 searchedEnemy
                // 【修复】强制清零 forceTracePlayerDistance，阻止原版AI中硬编码的 Teams.player 追踪逻辑
                var ai = character.GetComponentInChildren<AICharacterController>();
                if (ai != null)
                {
                    bool bloodhoundActive = MutatorManager.HasActiveMutator("enemy_bloodhound")
                        && (isModeFRun || runtimeFaction != modeEPlayerFaction);
                    ai.forceTracePlayerDistance = bloodhoundActive ? 99999f : 0f;
                    if (bloodhoundActive)
                    {
                        ai.noticed = true;
                    }
                }

                if (!isModeFRun)
                {
                    // Mode E 基础血量提升：所有敌人血量 × 1.5（跳过玩家所属阵营）
                    ApplyModeEBaseHealthBoost(character);

                    // BEAR阵营属性提升：血量和伤害提升150%（× 2.5），补偿小怪基础数值偏低
                    if (faction == Teams.bear)
                    {
                        ApplyBearFactionStatBoost(character);
                    }
                }

                if (ctx.isBoss && faction != modeEPlayerFaction && !isModeFRun)
                {
                    ApplyModeDStyleLootToModeESpecialEnemy(character, ctx, faction, promotedToBoss);
                }

                // 先清理该实例可能残留的旧注册，再以当前 Mode E 生命周期重新登记。
                CleanupModeEEnemyRuntimeState(character);
                ModeEEnemyScalingState scalingState = new ModeEEnemyScalingState();
                scalingState.deathBaseline = GetModeEFactionDeathCount(trackedFaction);
                scalingState.birthMaxHealth = GetModeEMaxHealthValue(character);
                if (scalingState.birthMaxHealth <= 0f && character.Health != null)
                {
                    scalingState.birthMaxHealth = character.Health.MaxHealth;
                }
                scalingState.rewardKind = ClassifyFinalModeEShellRewardKind(
                    ctx.preset,
                    ctx.isBoss,
                    isModeFRun);
                scalingState.registeredFaction = trackedFaction;
                scalingState.rewardSessionToken = modeEShellSessionToken;
                scalingState.rewardSceneBuildIndex = modeEShellSessionScene;
                scalingState.rewardSessionGeneration = modeEShellSessionGeneration;
                scalingState.rewardStateComplete = !isModeFRun &&
                    scalingState.birthMaxHealth > 0f &&
                    scalingState.rewardKind != ModeEShellRewardKind.None &&
                    scalingState.rewardSessionToken > 0 &&
                    scalingState.rewardSceneBuildIndex >= 0 &&
                    scalingState.rewardSessionGeneration > 0L;
                modeEEnemyScalingStates[character] = scalingState;
                TrackModeEAliveEnemy(character, trackedFaction);
                modeEHost.RegisterModeDEnemyRecoveryAnchor(character, ctx.position);

                // 注册到虚拟 CharacterSpawnerRoot，使 BossLiveMapMod 能检测到
                RegisterModeEEnemyToSpawnerRoot(character);

                // 注册死亡事件
                RegisterModeEEnemyDeath(character);
                RegisterModeEEnemyLootHandler(character, trackedFaction);
                if (!isModeFRun)
                {
                    RegisterModeEBossHireOffer(character, trackedFaction, ctx.isBoss);
                }
                if (isModeFRun)
                {
                    modeEHost.RegisterModeFBoss(character);
                }

                // 更新生成计数
                spawnRuntime.ResolveModeESpawnAttempt();
                MarkModeEStartupBossSpawned();
                ModBehaviour.DevLog("[ModeE] 生成结案: resolved=" + spawnRuntime.SpawnResolved + "/" + spawnRuntime.TotalSpawnExpected);
                return true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeE] [ERROR] OnModeEEnemySpawned 失败: " + e.Message);
                try
                {
                    if (character != null)
                    {
                        CleanupModeEEnemyRuntimeState(character);
                    }
                }
                catch (Exception cleanupEx)
                {
                    ModBehaviour.DevLog("[ModeE] [WARNING] OnModeEEnemySpawned 失败后清理异常: " + cleanupEx.Message);
                }

                return false;
            }
        }

        private void ApplyModeDStyleLootToModeESpecialEnemy(CharacterMainControl character, EnemySpawnContext ctx, Teams faction, bool promotedToBoss)
        {
            try
            {
                if (character == null || ctx == null || ctx.preset == null)
                {
                    return;
                }

                bool isDragonDescendant = modeEHost.IsDragonDescendantPreset(ctx.preset);
                bool isBearPromotedMinion = faction == Teams.bear && promotedToBoss;
                if (!isDragonDescendant && !isBearPromotedMinion)
                {
                    return;
                }

                InitializeModeDItemPools();
                equipment.EnsureModeDGlobalItemPool();

                float lootHealth = 100f;
                try
                {
                    if (character.Health != null)
                    {
                        lootHealth = character.Health.MaxHealth;
                    }
                    else if (ctx.preset.baseHealth > 0f)
                    {
                        lootHealth = ctx.preset.baseHealth;
                    }
                }
                catch {}

                int virtualWave = promotedToBoss ? 5 : 10;
                bool preserveBossArmor = !promotedToBoss;
                equipment.EquipEnemyForModeD(character, virtualWave, lootHealth, preserveBossArmor);

                ModBehaviour.DevLog("[ModeE] 已应用白手起家式随机掉落: " + character.gameObject.name
                    + " (dragonDescendant=" + isDragonDescendant
                    + ", bearPromotedMinion=" + isBearPromotedMinion
                    + ", virtualWave=" + virtualWave + ")");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeE] [ERROR] 应用白手起家式随机掉落失败: " + e.Message);
            }
        }


        #endregion
    }

    /// <summary>
    /// Mode E/F 运行时克隆预设的对象级租约。预设必须活到角色本体完成销毁，
    /// 否则 Health、血条或 OnDestroy 链仍可能读取已经失效的 characterPreset。
    /// </summary>
    internal sealed class ModeECharacterPresetLease : MonoBehaviour
    {
        private CharacterRandomPreset _ownedPreset;

        internal void Assign(CharacterRandomPreset preset)
        {
            if (_ownedPreset != null && _ownedPreset != preset)
            {
                UnityEngine.Object.Destroy(_ownedPreset, 0.05f);
            }
            _ownedPreset = preset;
        }

        private void OnDestroy()
        {
            CharacterRandomPreset preset = _ownedPreset;
            _ownedPreset = null;
            if (preset != null)
            {
                // 延迟到角色所有 OnDestroy 回调结束后再释放 ScriptableObject。
                UnityEngine.Object.Destroy(preset, 0.05f);
            }
        }
    }
}
