// ============================================================================
// ThunderSetBonus.cs - 雷霆套装效果
// ============================================================================
// 模块说明：
//   雷霆套装（雷神之角 + 雷霆战甲）的套装效果：
//   - 被动：电抗提升（ElementFactor_Electricity -0.5，即减少50%电伤）
//   - 受击触发：25% 概率以玩家为中心释放电击 AOE（4米范围，25伤害）
//   - 冷却时间：3秒
//
// 实现方式：
//   通过 Health.OnHurt 静态事件监听玩家受击，
//   使用 ExplosionManager.CreateExplosion 实现 AOE 电击。
// ============================================================================

using System;
using UnityEngine;
using ItemStatsSystem;
using ItemStatsSystem.Stats;
using ItemStatsSystem.Items;

namespace BossRush
{
    /// <summary>
    /// 雷霆套装效果 - 受击电击反制
    /// </summary>
    public partial class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        #region 雷霆套配置

        // 雷霆套数值配置
        private const float THUNDER_SET_COUNTER_CHANCE = 0.25f;       // 25% 反击概率
        private const float THUNDER_SET_COUNTER_DAMAGE = 25f;         // 电击伤害
        private const float THUNDER_SET_COUNTER_RADIUS = 4f;          // 电击范围（米）
        private const float THUNDER_SET_COOLDOWN = 3f;                // 电击冷却时间（秒）
        private const float THUNDER_SET_ELEC_RESIST_BONUS = 0.5f;     // 电抗 +50%
        // 触发距离：与冰霜套保持一致，远程攻击不触发反制（设计意图是"近身反制"）
        private const float THUNDER_SET_CLOSE_RANGE = 6f;

        // 雷霆套状态
        private bool thunderSetActive = false;
        private bool thunderSetHurtRegistered = false;
        private Modifier thunderSetElecResistModifier = null;
        private Stat thunderSetElecResistStat = null;
        private float lastThunderTriggerTime = -999f;

        #endregion

        #region 雷霆套激活/停用

        /// <summary>
        /// 激活雷霆套装效果
        /// </summary>
        private void ActivateThunderSetBonus(CharacterMainControl player)
        {
            try
            {
                thunderSetActive = true;
                DevLog("[ThunderSet] 雷霆套装效果激活！");

                // 1. 添加电抗 Modifier
                // 注意：用 player.CharacterItem 属性（CharacterMainControl.cs:326）
                Item playerItem = player.CharacterItem;
                if (playerItem != null)
                {
                    Stat elecFactorStat = playerItem.GetStat("ElementFactor_Electricity");
                    if (elecFactorStat != null)
                    {
                        thunderSetElecResistModifier = new Modifier(
                            ModifierType.Add,
                            -THUNDER_SET_ELEC_RESIST_BONUS,
                            this
                        );
                        elecFactorStat.AddModifier(thunderSetElecResistModifier);
                        thunderSetElecResistStat = elecFactorStat;
                        DevLog("[ThunderSet] 电抗 Modifier 已添加 (-" + THUNDER_SET_ELEC_RESIST_BONUS + ")");
                    }
                    else
                    {
                        DevLog("[ThunderSet] 未找到 ElementFactor_Electricity Stat，电抗加成跳过");
                    }
                }

                // 2. 注册受击事件
                RegisterThunderSetHurtEvent();

                // 3. 显示激活提示
                ShowMessage(L10n.T(
                    "<color=#FFD700>【雷霆之怒】</color> 套装效果激活！\n受击时有概率释放电击反制",
                    "<color=#FFD700>[Thunder's Wrath]</color> Set bonus activated!\nChance to counter with lightning when hit"
                ));
            }
            catch (Exception e)
            {
                DevLog("[ThunderSet] ActivateThunderSetBonus 出错: " + e.Message);
            }
        }

        /// <summary>
        /// 停用雷霆套装效果
        /// </summary>
        private void DeactivateThunderSetBonus()
        {
            if (!thunderSetActive) return;

            try
            {
                thunderSetActive = false;
                DevLog("[ThunderSet] 雷霆套装效果停用");

                // 1. 移除电抗 Modifier
                if (thunderSetElecResistModifier != null)
                {
                    if (thunderSetElecResistStat != null)
                    {
                        thunderSetElecResistStat.RemoveModifier(thunderSetElecResistModifier);
                        DevLog("[ThunderSet] 电抗 Modifier 已移除");
                    }
                    thunderSetElecResistModifier = null;
                    thunderSetElecResistStat = null;
                }

                // 2. 取消受击事件
                UnregisterThunderSetHurtEvent();
            }
            catch (Exception e)
            {
                DevLog("[ThunderSet] DeactivateThunderSetBonus 出错: " + e.Message);
            }
        }

        #endregion

        #region 雷霆套受击事件

        /// <summary>
        /// 注册雷霆套受击事件
        /// </summary>
        private void RegisterThunderSetHurtEvent()
        {
            if (thunderSetHurtRegistered) return;

            try
            {
                Health.OnHurt += OnThunderSetHurt;
                Health.OnDead += OnThunderSetMainCharacterDead;
                thunderSetHurtRegistered = true;
                DevLog("[ThunderSet] 已注册受击事件");
            }
            catch (Exception e)
            {
                DevLog("[ThunderSet] 注册受击事件失败: " + e.Message);
            }
        }

        /// <summary>
        /// 取消注册雷霆套受击事件
        /// </summary>
        private void UnregisterThunderSetHurtEvent()
        {
            if (!thunderSetHurtRegistered) return;

            try
            {
                Health.OnHurt -= OnThunderSetHurt;
                Health.OnDead -= OnThunderSetMainCharacterDead;
                thunderSetHurtRegistered = false;
                DevLog("[ThunderSet] 已取消注册受击事件");
            }
            catch (Exception e)
            {
                DevLog("[ThunderSet] 取消注册受击事件失败: " + e.Message);
            }
        }

        /// <summary>
        /// 玩家死亡时重置冷却。
        /// Mode E/F 等模式支持局内复活，避免复活瞬间被打就触发反制（虽然技术上不算 bug，
        /// 但与"挨打才反弹"的设计语义一致——重新计算冷却让节奏更可预期）。
        /// </summary>
        private void OnThunderSetMainCharacterDead(Health target, DamageInfo damageInfo)
        {
            if (target == null) return;
            if (!target.IsMainCharacterHealth) return;
            lastThunderTriggerTime = -999f;
        }

        /// <summary>
        /// 雷霆套受击回调 - 概率释放电击 AOE
        /// </summary>
        private void OnThunderSetHurt(Health health, DamageInfo damageInfo)
        {
            try
            {
                // 只处理主角受击
                if (!thunderSetActive || health == null || !health.IsMainCharacterHealth) return;

                // 冷却检测
                if (Time.time - lastThunderTriggerTime < THUNDER_SET_COOLDOWN) return;

                // 只对真实攻击来源反制，避免环境/自伤触发 AOE
                if (damageInfo.fromCharacter == null) return;

                // 以玩家为中心释放电击 AOE
                CharacterMainControl player = CharacterMainControl.Main;
                if (player == null) return;
                if (object.ReferenceEquals(damageInfo.fromCharacter, player)) return;

                // 近身距离判定：远程攻击不触发反制（避免狙击手隔半张地图被电）
                Vector3 delta = damageInfo.fromCharacter.transform.position - player.transform.position;
                delta.y = 0f;
                if (delta.sqrMagnitude > THUNDER_SET_CLOSE_RANGE * THUNDER_SET_CLOSE_RANGE) return;

                // 概率判定
                if (UnityEngine.Random.value > THUNDER_SET_COUNTER_CHANCE) return;

                lastThunderTriggerTime = Time.time;

                // 安全检查 LevelManager
                if (LevelManager.Instance == null || LevelManager.Instance.ExplosionManager == null) return;

                // 构建伤害信息
                DamageInfo dmg = new DamageInfo(player);
                dmg.damageValue = THUNDER_SET_COUNTER_DAMAGE;
                dmg.isExplosion = true;
                dmg.AddElementFactor(ElementTypes.electricity, 1.0f);

                // 使用 ExplosionManager 创建爆炸（已有 API，无需新系统）
                LevelManager.Instance.ExplosionManager.CreateExplosion(
                    player.transform.position,
                    THUNDER_SET_COUNTER_RADIUS,
                    dmg,
                    ExplosionFxTypes.normal,
                    0.3f
                );

                DevLog("[ThunderSet] 电击反制触发！范围: " + THUNDER_SET_COUNTER_RADIUS + "m");
            }
            catch (Exception e)
            {
                DevLog("[ThunderSet] OnThunderSetHurt 出错: " + e.Message);
            }
        }

        #endregion
    }
}
