using System;
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

        private Modifier modeFBloodfireGunDamageModifier = null;
        private Modifier modeFBloodfireMeleeDamageModifier = null;
        private Modifier modeFBloodfireMoveSpeedModifier = null;

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
            modeFBloodfireGunDamageModifier = AddModeFBloodfireOverloadModifier(
                player.CharacterItem, "GunDamageMultiplier", MODEF_BLOODFIRE_GUN_DAMAGE_BONUS);
            modeFBloodfireMeleeDamageModifier = AddModeFBloodfireOverloadModifier(
                player.CharacterItem, "MeleeDamageMultiplier", MODEF_BLOODFIRE_MELEE_DAMAGE_BONUS);
            modeFBloodfireMoveSpeedModifier = AddModeFBloodfireOverloadModifier(
                player.CharacterItem, "MoveSpeed", MODEF_BLOODFIRE_MOVE_SPEED_BONUS);

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

        private Modifier AddModeFBloodfireOverloadModifier(Item characterItem, string statName, float percent)
        {
            try
            {
                Stat stat = characterItem != null ? characterItem.GetStat(statName) : null;
                if (stat == null)
                {
                    DevLog("[ModeF] [WARNING] 命火过载缺少 Stat: " + statName);
                    return null;
                }

                Modifier modifier = new Modifier(ModifierType.PercentageAdd, percent, this);
                stat.AddModifier(modifier);
                return modifier;
            }
            catch (Exception e)
            {
                DevLog("[ModeF] [WARNING] 命火过载 Modifier 失败: " + statName + ", " + e.Message);
                return null;
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
            RemoveModeFBloodfireOverloadModifier(ref modeFBloodfireGunDamageModifier);
            RemoveModeFBloodfireOverloadModifier(ref modeFBloodfireMeleeDamageModifier);
            RemoveModeFBloodfireOverloadModifier(ref modeFBloodfireMoveSpeedModifier);
        }

        private void RemoveModeFBloodfireOverloadModifier(ref Modifier modifier)
        {
            Modifier target = modifier;
            modifier = null;
            if (target == null)
            {
                return;
            }

            try
            {
                target.RemoveFromTarget();
            }
            catch (Exception e)
            {
                DevLog("[ModeF] [WARNING] 命火过载 Modifier 清理失败: " + e.Message);
            }
        }
    }
}
