using System;
using System.Collections.Generic;
using UnityEngine;
using ItemStatsSystem;
using ItemStatsSystem.Stats;
using Duckov.Utilities;

namespace BossRush
{
    /// <summary>
    /// Mode F 命火过载：生命成长顶到上限后，溢出成长转为命火充能，满值进入一段
    /// “强化换风险”的过载窗口——火力与移速提升，代价是双倍失血与官方烧伤。
    /// </summary>
    public partial class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        #region 命火过载常量

        private const float MODEF_BLOODFIRE_MAX_CHARGE = 100f;
        private const float MODEF_BLOODFIRE_OVERLOAD_DURATION = 15f;
        private const float MODEF_BLOODFIRE_BOUNTY_EXTENSION = 3f;
        private const float MODEF_BLOODFIRE_OVERLOAD_MAX_REMAINING = 24f;
        private const float MODEF_BLOODFIRE_AFTERGLOW_CHARGE = 25f;
        private const float MODEF_BLOODFIRE_GUN_DAMAGE_BONUS = 0.40f;
        private const float MODEF_BLOODFIRE_MELEE_DAMAGE_BONUS = 0.40f;
        private const float MODEF_BLOODFIRE_MOVE_SPEED_BONUS = 0.15f;
        private const float MODEF_BLOODFIRE_BLEED_MULTIPLIER = 2f;

        #endregion

        #region 命火过载状态

        private readonly List<ZombieModeAttributeModifierRecord> modeFBloodfireModifiers =
            new List<ZombieModeAttributeModifierRecord>();

        #endregion

        private float ApplyModeFBloodfireKillReward(
            CharacterMainControl player,
            float overflowGrowth,
            float growthCap,
            bool isBountyBoss,
            out bool overloadStarted,
            out float overloadExtension)
        {
            overloadStarted = false;
            overloadExtension = 0f;

            if (modeFState.BloodfireOverloadActive)
            {
                if (isBountyBoss)
                {
                    float previousRemaining = modeFState.BloodfireOverloadRemaining;
                    modeFState.BloodfireOverloadRemaining = Mathf.Min(
                        MODEF_BLOODFIRE_OVERLOAD_MAX_REMAINING,
                        previousRemaining + MODEF_BLOODFIRE_BOUNTY_EXTENSION);
                    overloadExtension = modeFState.BloodfireOverloadRemaining - previousRemaining;
                }
                return 0f;
            }

            if (overflowGrowth <= 0.001f || growthCap <= 0.001f)
            {
                return 0f;
            }

            float previousCharge = modeFState.BloodfireCharge;
            float chargeGain = overflowGrowth / growthCap * MODEF_BLOODFIRE_MAX_CHARGE;
            modeFState.BloodfireCharge = Mathf.Min(
                MODEF_BLOODFIRE_MAX_CHARGE,
                previousCharge + chargeGain);
            float actualChargeGain = modeFState.BloodfireCharge - previousCharge;

            if (modeFState.BloodfireCharge >= MODEF_BLOODFIRE_MAX_CHARGE - 0.01f)
            {
                overloadStarted = StartModeFBloodfireOverload(player);
            }

            return actualChargeGain;
        }

        private bool StartModeFBloodfireOverload(CharacterMainControl player)
        {
            if (modeFState.BloodfireOverloadActive || player == null || player.CharacterItem == null)
            {
                return false;
            }

            modeFState.BloodfireCharge = MODEF_BLOODFIRE_MAX_CHARGE;
            modeFState.BloodfireOverloadActive = true;
            modeFState.BloodfireOverloadRemaining = MODEF_BLOODFIRE_OVERLOAD_DURATION;

            ClearModeFBloodfireOverloadModifiers();
            AddModeFBloodfireOverloadModifier(player, "GunDamageMultiplier", MODEF_BLOODFIRE_GUN_DAMAGE_BONUS);
            AddModeFBloodfireOverloadModifier(player, "MeleeDamageMultiplier", MODEF_BLOODFIRE_MELEE_DAMAGE_BONUS);
            // 官方角色只有 WalkSpeed / RunSpeed / Moveability 三个移动 stat
            // （CharacterMainControl 的 walkSpeedHash / runSpeedHash / moveabilityHash）。
            // "MoveSpeed" 只是 Animator 参数名，不是 stat key——曾挂在这里，
            // 每次都被 RuntimeStatModifierTracker 当作缺失 stat 静默丢弃。
            AddModeFBloodfireOverloadModifier(player, "WalkSpeed", MODEF_BLOODFIRE_MOVE_SPEED_BONUS);
            AddModeFBloodfireOverloadModifier(player, "RunSpeed", MODEF_BLOODFIRE_MOVE_SPEED_BONUS);

            try
            {
                Duckov.Buffs.Buff burnBuff = GameplayDataSettings.Buffs != null
                    ? GameplayDataSettings.Buffs.Burn
                    : null;
                if (burnBuff != null)
                {
                    player.AddBuff(burnBuff, player, 0);
                }
                else
                {
                    DevLog("[ModeF] [WARNING] 命火过载无法获取官方 Burn Buff");
                }
            }
            catch (Exception e)
            {
                DevLog("[ModeF] [WARNING] 命火过载施加烧伤失败: " + e.Message);
            }

            DevLog("[ModeF] 命火过载开始: duration=" + MODEF_BLOODFIRE_OVERLOAD_DURATION
                + ", gun/melee=+40%, move=+15%, bleed=x2");
            return true;
        }

        private void AddModeFBloodfireOverloadModifier(
            CharacterMainControl player, string statName, float percent)
        {
            if (!RuntimeStatModifierTracker.TryAdd(
                player, statName, percent, this, modeFBloodfireModifiers, "ModeF Bloodfire"))
            {
                DevLog("[ModeF] [WARNING] 命火过载缺少 Stat 或 Modifier 施加失败: " + statName);
            }
        }

        private void TickModeFBloodfireOverload(float deltaTime)
        {
            if (!modeFState.BloodfireOverloadActive)
            {
                return;
            }

            modeFState.BloodfireOverloadRemaining = Mathf.Max(
                0f,
                modeFState.BloodfireOverloadRemaining - deltaTime);
            if (modeFState.BloodfireOverloadRemaining <= 0f)
            {
                EndModeFBloodfireOverload(true);
            }
        }

        /// <summary>
        /// 结束命火过载。三个 Stat Modifier 在此对称撤销；开场施加的官方 Burn 有意不撤销——
        /// 烧伤要自己烧完，这是过载“强化换风险”的组成部分，不是漏清理。
        /// </summary>
        private void EndModeFBloodfireOverload(bool keepAfterglowCharge)
        {
            bool wasActive = modeFState.BloodfireOverloadActive;
            modeFState.BloodfireOverloadActive = false;
            modeFState.BloodfireOverloadRemaining = 0f;
            modeFState.BloodfireCharge = keepAfterglowCharge
                ? MODEF_BLOODFIRE_AFTERGLOW_CHARGE
                : 0f;
            ClearModeFBloodfireOverloadModifiers();

            if (wasActive)
            {
                DevLog("[ModeF] 命火过载结束，命火=" + modeFState.BloodfireCharge.ToString("F0"));
            }
        }

        private void ClearModeFBloodfireOverloadModifiers()
        {
            RuntimeStatModifierTracker.RemoveAll(modeFBloodfireModifiers, "ModeF Bloodfire");
        }

        /// <summary>Dev 验收专用：实挂三项速度 Modifier 并验证结束后精确恢复。</summary>
        internal bool DebugValidateModeFBloodfire(out string metrics)
        {
            metrics = string.Empty;
            if (!DevModeEnabled) return false;
            CharacterMainControl player = CharacterMainControl.Main;
            if (player == null || player.CharacterItem == null) return false;

            // 只核对官方真实存在的两个移动 stat；"MoveSpeed" 不是 stat key，
            // 旧口径把它算进 speedModifierCount 期望值，导致本用例恒 FAIL 且 metrics 为空。
            Stat walk = player.CharacterItem.GetStat("WalkSpeed");
            Stat run = player.CharacterItem.GetStat("RunSpeed");
            if (walk == null || run == null)
            {
                metrics = "missing_stat=" + (walk == null ? "WalkSpeed " : string.Empty)
                    + (run == null ? "RunSpeed" : string.Empty);
                return false;
            }

            float walkBefore = walk.Value;
            float runBefore = run.Value;
            bool started = StartModeFBloodfireOverload(player);
            float walkDuring = walk.Value;
            float runDuring = run.Value;

            int speedModifierCount = 0;
            for (int i = 0; i < modeFBloodfireModifiers.Count; i++)
            {
                ZombieModeAttributeModifierRecord record = modeFBloodfireModifiers[i];
                if (record == null || record.Modifier == null) continue;
                if (record.StatName == "WalkSpeed" || record.StatName == "RunSpeed")
                {
                    if (Mathf.Approximately(record.Modifier.Value, MODEF_BLOODFIRE_MOVE_SPEED_BONUS))
                    {
                        speedModifierCount++;
                    }
                }
            }
            bool modifierValues = speedModifierCount == 2;
            EndModeFBloodfireOverload(false);

            bool restored = Mathf.Approximately(walk.Value, walkBefore)
                && Mathf.Approximately(run.Value, runBefore);
            bool increased = walkDuring > walkBefore && runDuring > runBefore;
            metrics = "before=" + walkBefore.ToString("F3") + "/" + runBefore.ToString("F3")
                + ", during=" + walkDuring.ToString("F3") + "/" + runDuring.ToString("F3")
                + ", speed_modifiers=" + speedModifierCount
                + ", bonus=" + MODEF_BLOODFIRE_MOVE_SPEED_BONUS.ToString("F2")
                + ", restored=" + restored;
            return started && modifierValues && increased && restored;
        }
    }
}
