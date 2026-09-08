// ============================================================================
// FrostSetBonus.cs - 冰霜套装效果（寒冰之护）
// ============================================================================
// 模块说明：
//   冰霜套装（霜冠 500053 + 寒冰铠甲 500054）的 2 件套效果：
//   - 被动：冰抗提升（ElementFactor_Ice -0.5，即减少 50% 冰伤）
//   - 被动：受到的冰系伤害按 50% 回补为治疗（算法照龙套装的火焰转治疗）
//   - 常驻：淡蓝双眼慢呼吸 + 脚下霜雾（FrostMistEffect）
//   - 击杀触发「冰葬」：尸体处霜爆，范围内敌人受冰伤并冻结（见 FrostSetBonus_Nova.cs）
//   - 受击触发：被 5 米内的攻击者命中时 30% 概率冻结攻击者（减速 80%，持续 2 秒；5 秒冷却），附爆发环与音效
//
// 实现方式：
//   通过 Health.OnHurt / Health.OnDead 静态事件（命名方法、成对订阅、私有 bool 幂等）监听主角受击与全局死亡，
//   使用 IsMainCharacterHealth 过滤非玩家伤害。
//   冻结优先使用自定义 FrostSet Buff（如 AssetBundle 提供）；
//   AssetBundle 缺失时回退到原版 Buffs.Cold（与冰霜长矛同款冻结），
//   仍失败时再走纯 Modifier 减速兜底。三级回退统一在 TryApplyFrostFreeze，反击与冰葬共用。
//   两件装备的 ColdProtection +1、售价等静态属性在 FrostThunderSetConfig。
// ============================================================================

using System;
using UnityEngine;
using ItemStatsSystem;
using ItemStatsSystem.Stats;
using ItemStatsSystem.Items;
using Duckov.Buffs;

namespace BossRush
{
    /// <summary>
    /// 冰霜套装效果 - 冰伤转治疗 + 受击冻结反制（冰葬在 FrostSetBonus_Nova.cs）
    /// </summary>
    public partial class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        #region 冰霜套配置

        // 冰霜套数值配置
        private const float FROST_SET_FREEZE_CHANCE = 0.3f;       // 30% 冻结概率
        private const float FROST_SET_ICE_RESIST_BONUS = 0.5f;    // 冰抗 +50%（减少50%冰伤）
        private const float FROST_SET_ICE_HEAL_RATIO = 0.5f;      // 受到的冰系伤害 50% 回补为治疗
        private const float FROST_SET_COOLDOWN = 5f;              // 冻结冷却时间（秒）
        private const float FROST_SET_CLOSE_RANGE = 5f;           // 只冻结近身攻击者
        private const float FROST_SET_EYE_INTENSITY = 5f;

        private static readonly Color FROST_SET_BURST_COLOR = new Color(0.7f, 0.9f, 1f, 0.8f);
        private static readonly Color FROST_SET_EYE_COLOR = new Color(0.55f, 0.85f, 1f);

        // 冰霜套状态
        private bool frostSetActive = false;
        private bool frostSetHurtRegistered = false;
        private Modifier frostSetIceResistModifier = null;
        private Stat frostSetIceResistStat = null;
        private float lastFrostTriggerTime = -999f;
        private SetEyeLightState frostSetEyeLights = null;
        private FrostMistEffect frostMistEffect = null;

        private sealed class FrostFallbackSlowState
        {
            public Coroutine Coroutine;
            public Stat WalkSpeedStat;
            public Modifier WalkSpeedModifier;
            public Stat RunSpeedStat;
            public Modifier RunSpeedModifier;
        }

        // fallback 减速状态跟踪：卸下套装时一并停止协程并移除已挂载的 Modifier。
        private readonly System.Collections.Generic.List<FrostFallbackSlowState> frostFallbackSlowStates
            = new System.Collections.Generic.List<FrostFallbackSlowState>();

        #endregion

        #region 冰霜套激活/停用

        /// <summary>
        /// 激活冰霜套装效果
        /// </summary>
        /// <param name="announce">是否弹横幅；场景重载后的静默重建传 false</param>
        private void ActivateFrostSetBonus(CharacterMainControl player, bool announce = true)
        {
            try
            {
                frostSetActive = true;
                DevLog("[FrostSet] 冰霜套装效果激活！");

                // 1. 添加冰抗 Modifier
                // 注意：用 player.CharacterItem 属性（CharacterMainControl.cs:326）
                Item playerItem = player.CharacterItem;
                if (playerItem != null)
                {
                    Stat iceFactorStat = playerItem.GetStat("ElementFactor_Ice");
                    if (iceFactorStat != null)
                    {
                        frostSetIceResistModifier = new Modifier(
                            ModifierType.Add,
                            -FROST_SET_ICE_RESIST_BONUS,
                            this
                        );
                        iceFactorStat.AddModifier(frostSetIceResistModifier);
                        frostSetIceResistStat = iceFactorStat;
                        DevLog("[FrostSet] 冰抗 Modifier 已添加 (-" + FROST_SET_ICE_RESIST_BONUS + ")");
                    }
                    else
                    {
                        DevLog("[FrostSet] 未找到 ElementFactor_Ice Stat，冰抗加成跳过");
                    }
                }

                // 2. 注册受击/死亡事件（反击 + 冰葬）
                RegisterFrostSetHurtEvent();

                // 3. 常驻表现：眼光慢呼吸 + 脚下霜雾
                frostSetEyeLights = CreateSetEyeLights(player, FROST_SET_EYE_COLOR, FROST_SET_EYE_INTENSITY, SetEyePulseMode.Breathe);
                StartFrostMist(player);

                // 4. 显示激活提示
                if (announce)
                {
                    ShowMessage(L10n.T(
                        "<color=#87CEEB>【寒冰之护】</color> 套装效果激活！\n冰伤转治疗 · 击杀冰葬霜爆 · 受击冻结攻击者",
                        "<color=#87CEEB>[Frost Ward]</color> Set bonus activated!\nIce heals you · kills unleash a frost nova · freeze attackers when hit"
                    ));
                }
            }
            catch (Exception e)
            {
                DevLog("[FrostSet] ActivateFrostSetBonus 出错: " + e.Message);
            }
        }

        /// <summary>
        /// 停用冰霜套装效果
        /// </summary>
        private void DeactivateFrostSetBonus()
        {
            if (!frostSetActive) return;

            try
            {
                frostSetActive = false;
                DevLog("[FrostSet] 冰霜套装效果停用");

                // 1. 移除冰抗 Modifier
                if (frostSetIceResistModifier != null)
                {
                    if (frostSetIceResistStat != null)
                    {
                        frostSetIceResistStat.RemoveModifier(frostSetIceResistModifier);
                        DevLog("[FrostSet] 冰抗 Modifier 已移除");
                    }
                    frostSetIceResistModifier = null;
                    frostSetIceResistStat = null;
                }

                // 2. 取消受击/死亡事件
                UnregisterFrostSetHurtEvent();

                // 3. 停止仍在飞的 fallback 减速协程，并立刻把对应 Modifier 摘掉
                StopAndClearFrostFallbackSlowCoroutines();

                // 4. 清理表现层与冰葬状态。先递增代数，让已排队的延时结算协程整条作废
                BumpSetBonusGeneration();
                DestroySetEyeLights(ref frostSetEyeLights);
                StopFrostMist();
                ResetFrostNovaState();
            }
            catch (Exception e)
            {
                DevLog("[FrostSet] DeactivateFrostSetBonus 出错: " + e.Message);
            }
        }

        private void StartFrostMist(CharacterMainControl player)
        {
            StopFrostMist();
            if (player == null) return;

            try
            {
                frostMistEffect = FrostMistEffect.Create<FrostMistEffect>(
                    player.transform,
                    player.transform.position + new Vector3(0f, -0.3f, 0f));
            }
            catch (Exception e)
            {
                DevLog("[FrostSet] 创建霜雾特效失败: " + e.Message);
                frostMistEffect = null;
            }
        }

        private void StopFrostMist()
        {
            if (frostMistEffect == null) return;

            try
            {
                frostMistEffect.StopEffect();
            }
            catch (Exception e)
            {
                DevLog("[FrostSet] 停止霜雾特效失败: " + e.Message);
            }
            frostMistEffect = null;
        }

        #endregion

        #region 冰霜套受击事件

        /// <summary>
        /// 注册冰霜套受击事件
        /// </summary>
        private void RegisterFrostSetHurtEvent()
        {
            if (frostSetHurtRegistered) return;

            try
            {
                Health.OnHurt += OnFrostSetHurt;
                Health.OnDead += OnFrostSetAnyDead;
                frostSetHurtRegistered = true;
                DevLog("[FrostSet] 已注册受击事件");
            }
            catch (Exception e)
            {
                DevLog("[FrostSet] 注册受击事件失败: " + e.Message);
            }
        }

        /// <summary>
        /// 取消注册冰霜套受击事件
        /// </summary>
        private void UnregisterFrostSetHurtEvent()
        {
            if (!frostSetHurtRegistered) return;

            try
            {
                Health.OnHurt -= OnFrostSetHurt;
                Health.OnDead -= OnFrostSetAnyDead;
                frostSetHurtRegistered = false;
                DevLog("[FrostSet] 已取消注册受击事件");
            }
            catch (Exception e)
            {
                DevLog("[FrostSet] 取消注册受击事件失败: " + e.Message);
            }
        }

        /// <summary>
        /// 全局死亡分派器（唯一的 Health.OnDead 订阅点，保证 += / -= 同文件配对）：
        /// - 主角死亡：重置冷却 + 停止仍在飞的减速协程。Mode E/F 等模式支持局内复活，
        ///   避免复活后冷却仍在挂、敌人仍被减速的体感残留。
        /// - 其他角色死亡：交给冰葬判定（FrostSetBonus_Nova.cs），只在套装激活时有效。
        /// </summary>
        private void OnFrostSetAnyDead(Health target, DamageInfo damageInfo)
        {
            if (target == null) return;
            if (target.IsMainCharacterHealth)
            {
                BumpSetBonusGeneration(); // 死亡作废仍在等待的冰葬，局内复活不继承旧结算。
                lastFrostTriggerTime = -999f;
                ResetFrostNovaState();
                StopAndClearFrostFallbackSlowCoroutines();
                return;
            }

            TryScheduleFrostNova(target, damageInfo);
        }

        /// <summary>
        /// 冰霜套受击回调 - 冰伤转治疗 + 概率冻结攻击者
        /// </summary>
        private void OnFrostSetHurt(Health health, DamageInfo damageInfo)
        {
            try
            {
                // 只处理主角受击
                if (!frostSetActive || health == null || !health.IsMainCharacterHealth) return;

                // 1) 冰伤转治疗（OnHurt 在扣血之后派发，只做回补，下一帧生效）
                float iceDamage = GetSetBonusElementDamagePortion(health, damageInfo, ElementTypes.ice);
                if (iceDamage > 0f)
                {
                    float heal = iceDamage * FROST_SET_ICE_HEAL_RATIO;
                    if (heal > 0f)
                    {
                        StartCoroutine(DelayedHeal(health, heal));
                    }
                }

                // 2) 反击冻结
                // 冷却检测
                if (Time.time - lastFrostTriggerTime < FROST_SET_COOLDOWN) return;

                // 需要有攻击来源
                if (damageInfo.fromCharacter == null) return;

                CharacterMainControl player = CharacterMainControl.Main;
                if (player == null) return;
                if (object.ReferenceEquals(damageInfo.fromCharacter, player)) return;

                Vector3 delta = damageInfo.fromCharacter.transform.position - player.transform.position;
                delta.y = 0f;
                if (delta.sqrMagnitude > FROST_SET_CLOSE_RANGE * FROST_SET_CLOSE_RANGE) return;

                // 概率判定
                if (UnityEngine.Random.value > FROST_SET_FREEZE_CHANCE) return;

                // 攻击者存活检测
                Health attackerHealth = damageInfo.fromCharacter.Health;
                if (attackerHealth == null || attackerHealth.IsDead) return;

                lastFrostTriggerTime = Time.time;

                if (TryApplyFrostFreeze(damageInfo.fromCharacter))
                {
                    Vector3 attackerPosition = damageInfo.fromCharacter.transform.position;
                    SpawnSetBurst(attackerPosition, FROST_SET_BURST_COLOR, 1.2f, 0.3f, 4);
                    PlaySoundEffect(SetBonusSfx.FrostCounter);
                }
            }
            catch (Exception e)
            {
                DevLog("[FrostSet] OnFrostSetHurt 出错: " + e.Message);
            }
        }

        /// <summary>
        /// 冻结目标：自定义 FrostSet Buff → 原版 Cold（真冻结）→ 临时减速 Modifier 三级回退。
        /// 反击与冰葬共用；目标为空或已死亡返回 false。
        /// </summary>
        private bool TryApplyFrostFreeze(CharacterMainControl target)
        {
            if (target == null) return false;

            Health targetHealth = target.Health;
            if (targetHealth == null || targetHealth.IsDead) return false;

            CharacterMainControl player = CharacterMainControl.Main;

            // 优先使用自定义 FrostSet Buff（资源 Bundle 提供时），否则回退到原版 Cold（真冻结）
            Buff freezeBuff = EquipmentFactory.GetLoadedBuff("FrostSet");
            if (freezeBuff == null)
            {
                try
                {
                    freezeBuff = Duckov.Utilities.GameplayDataSettings.Buffs != null
                        ? Duckov.Utilities.GameplayDataSettings.Buffs.Cold
                        : null;
                }
                catch { freezeBuff = null; }
            }

            if (freezeBuff != null)
            {
                target.AddBuff(freezeBuff, player, 0);
                DevLog("[FrostSet] 冰冻触发！目标: " + target.name + " (Buff=" + freezeBuff.name + ")");
                return true;
            }

            // 极端兜底：原版 Cold 也拿不到时，用临时减速 Modifier（信息性兜底，不应触达）
            ApplyFrostSetFallbackSlow(target);
            return true;
        }

        /// <summary>
        /// 冰冻后备方案 - 当 Buff prefab 未加载时，通过临时 Modifier 减速
        /// </summary>
        private void ApplyFrostSetFallbackSlow(CharacterMainControl target)
        {
            try
            {
                Item targetItem = target.CharacterItem;
                if (targetItem == null) return;

                Stat walkSpeedStat = targetItem.GetStat("WalkSpeed");
                Stat runSpeedStat = targetItem.GetStat("RunSpeed");

                // 创建临时减速 Modifier（-80%）
                Modifier slowWalk = new Modifier(ModifierType.PercentageMultiply, -0.8f, this);
                Modifier slowRun = new Modifier(ModifierType.PercentageMultiply, -0.8f, this);

                if (walkSpeedStat != null)
                {
                    walkSpeedStat.AddModifier(slowWalk);
                }
                if (runSpeedStat != null)
                {
                    runSpeedStat.AddModifier(slowRun);
                }

                // 2秒后移除（协程入跟踪列表，卸装时统一停掉）
                var state = new FrostFallbackSlowState
                {
                    WalkSpeedStat = walkSpeedStat,
                    WalkSpeedModifier = slowWalk,
                    RunSpeedStat = runSpeedStat,
                    RunSpeedModifier = slowRun
                };
                state.Coroutine = StartCoroutine(RemoveFrostFallbackSlow(state, 2f));
                if (state.Coroutine != null)
                {
                    frostFallbackSlowStates.Add(state);
                }

                DevLog("[FrostSet] 后备减速已应用");
            }
            catch (Exception e)
            {
                DevLog("[FrostSet] ApplyFrostSetFallbackSlow 出错: " + e.Message);
            }
        }

        /// <summary>
        /// 延迟移除后备减速 Modifier
        /// </summary>
        private System.Collections.IEnumerator RemoveFrostFallbackSlow(FrostFallbackSlowState state, float duration)
        {
            yield return new WaitForSeconds(duration);

            RemoveFrostFallbackSlowModifiers(state);

            // 协程结束后从跟踪列表移除（避免列表无限膨胀）
            // 注意：StopAndClearFrostFallbackSlowCoroutines 在卸装时会清空列表，这里只在自然结束时清自己。
            // 由于 List.Remove 走线性扫描，开销极低（同时活跃的减速协程通常 <= 1）。
            try { frostFallbackSlowStates.Remove(state); } catch  { /* best-effort fallback intentionally ignored */ }
        }

        /// <summary>
        /// 卸下套装时停止仍在飞的 fallback 减速协程，并立即把对应 Modifier 摘除
        /// </summary>
        private void StopAndClearFrostFallbackSlowCoroutines()
        {
            try
            {
                for (int i = frostFallbackSlowStates.Count - 1; i >= 0; i--)
                {
                    FrostFallbackSlowState state = frostFallbackSlowStates[i];
                    if (state == null)
                    {
                        continue;
                    }
                    if (state.Coroutine != null)
                    {
                        StopCoroutine(state.Coroutine);
                    }
                    RemoveFrostFallbackSlowModifiers(state);
                }
                frostFallbackSlowStates.Clear();
            }
            catch (Exception e)
            {
                DevLog("[FrostSet] StopAndClearFrostFallbackSlowCoroutines 出错: " + e.Message);
            }
        }

        private void RemoveFrostFallbackSlowModifiers(FrostFallbackSlowState state)
        {
            if (state == null) return;

            try
            {
                if (state.WalkSpeedStat != null && state.WalkSpeedModifier != null)
                {
                    state.WalkSpeedStat.RemoveModifier(state.WalkSpeedModifier);
                }
            }
            catch (Exception e)
            {
                DevLog("[FrostSet] 移除后备行走减速失败: " + e.Message);
            }

            try
            {
                if (state.RunSpeedStat != null && state.RunSpeedModifier != null)
                {
                    state.RunSpeedStat.RemoveModifier(state.RunSpeedModifier);
                }
            }
            catch (Exception e)
            {
                DevLog("[FrostSet] 移除后备奔跑减速失败: " + e.Message);
            }

            state.WalkSpeedStat = null;
            state.WalkSpeedModifier = null;
            state.RunSpeedStat = null;
            state.RunSpeedModifier = null;
            state.Coroutine = null;
        }

        #endregion
    }
}
