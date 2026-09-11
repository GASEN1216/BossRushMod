// ============================================================================
// ThunderSetBonus.cs - 雷霆套装效果（雷霆之怒）
// ============================================================================
// 模块说明：
//   雷霆套装（雷神之角 500055 + 雷霆战甲 500056）的 2 件套效果：
//   - 被动：电抗提升（ElementFactor_Electricity -0.5，即减少 50% 电伤）
//   - 被动：受到的电系伤害按 50% 回补为治疗（算法照龙套装的火焰转治疗）
//   - 常驻：青白色双眼电闪 + 肩部环境电弧（见 ThunderSetBonus_Storm.cs）
//   - 击杀触发「引雷术」：连锁闪电（见 ThunderSetBonus_Storm.cs）
//   - 受击触发「雷霆反震」：被 6 米内的攻击者命中时 25% 概率以玩家为中心释放电击 AOE
//     （4 米 / 30 电伤 / 3 秒冷却 / canHurtSelf=false 不伤自己与友军），附电弧与爆发环
//
// 实现方式：
//   通过 Health.OnHurt / Health.OnDead 静态事件（命名方法、成对订阅、私有 bool 幂等）监听主角受击与全局死亡；
//   反震与引雷术的结算都延后一帧到协程：OnHurt 可能正处在敌方爆炸的 ExplosionManager 循环里，
//   嵌套 CreateExplosion 会覆写其共享缓冲；OnDead 内同步再 Hurt 也会让死亡派发顺序倒置。
//   两件装备的 StormProtection +1（风暴天气免疫）、售价等静态属性在 FrostThunderSetConfig。
// ============================================================================

using System;
using System.Collections;
using UnityEngine;
using ItemStatsSystem;
using ItemStatsSystem.Stats;
using ItemStatsSystem.Items;

namespace BossRush
{
    /// <summary>
    /// 雷霆套装效果 - 电伤转治疗 + 受击雷霆反震（引雷术在 ThunderSetBonus_Storm.cs）
    /// </summary>
    public partial class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        #region 雷霆套配置

        // 雷霆套数值配置
        private const float THUNDER_SET_COUNTER_CHANCE = 0.25f;       // 25% 反击概率
        private const float THUNDER_SET_COUNTER_DAMAGE = 30f;         // 电击伤害
        private const float THUNDER_SET_COUNTER_RADIUS = 4f;          // 电击范围（米）
        private const float THUNDER_SET_COOLDOWN = 3f;                // 电击冷却时间（秒）
        private const float THUNDER_SET_ELEC_RESIST_BONUS = 0.5f;     // 电抗 +50%
        private const float THUNDER_SET_ELEC_HEAL_RATIO = 0.5f;       // 受到的电系伤害 50% 回补为治疗
        // 触发距离：与冰霜套保持一致，远程攻击不触发反制（设计意图是"近身反制"）
        private const float THUNDER_SET_CLOSE_RANGE = 6f;
        private const float THUNDER_SET_EYE_INTENSITY = 6f;

        private static readonly Color THUNDER_SET_ARC_COLOR = new Color(0.65f, 0.9f, 1f, 0.95f);
        private static readonly Color THUNDER_SET_BURST_COLOR = new Color(0.55f, 0.8f, 1f, 0.75f);
        private static readonly Color THUNDER_SET_EYE_COLOR = new Color(0.6f, 0.9f, 1f);

        // 雷霆套状态
        private bool thunderSetActive = false;
        private bool thunderSetHurtRegistered = false;
        private Modifier thunderSetElecResistModifier = null;
        private Stat thunderSetElecResistStat = null;
        private float lastThunderTriggerTime = -999f;
        private SetEyeLightState thunderSetEyeLights = null;

        #endregion

        #region 雷霆套激活/停用

        /// <summary>
        /// 激活雷霆套装效果
        /// </summary>
        /// <param name="announce">是否弹横幅；场景重载后的静默重建传 false</param>
        private void ActivateThunderSetBonus(CharacterMainControl player, bool announce = true)
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

                // 2. 注册受击/死亡事件（反震 + 引雷术）
                RegisterThunderSetHurtEvent();

                // 3. 常驻表现：眼光电闪 + 肩部环境电弧
                thunderSetEyeLights = CreateSetEyeLights(player, THUNDER_SET_EYE_COLOR, THUNDER_SET_EYE_INTENSITY, SetEyePulseMode.Flicker);
                StartThunderAmbientArcLoop(player);

                // 4. 显示激活提示
                if (announce)
                {
                    ShowMessage(L10n.T(
                        "<color=#FFD700>【雷霆之怒】</color> 套装效果激活！\n电伤转治疗 · 击杀引雷连锁 · 受击雷霆反震",
                        "<color=#FFD700>[Thunder's Wrath]</color> Set bonus activated!\nShock heals you · kills chain lightning · counter-shock when hit"
                    ));
                }
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

                // 2. 取消受击/死亡事件
                UnregisterThunderSetHurtEvent();

                // 3. 清理表现层与引雷术状态。先递增代数，让已排队的延时结算协程整条作废
                BumpSetBonusGeneration();
                StopThunderAmbientArcLoop();
                DestroySetEyeLights(ref thunderSetEyeLights);
                DestroySetArcPool();
                ResetThunderChainState();
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
                Health.OnDead += OnThunderSetAnyDead;
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
                Health.OnDead -= OnThunderSetAnyDead;
                thunderSetHurtRegistered = false;
                DevLog("[ThunderSet] 已取消注册受击事件");
            }
            catch (Exception e)
            {
                DevLog("[ThunderSet] 取消注册受击事件失败: " + e.Message);
            }
        }

        /// <summary>
        /// 全局死亡分派器（唯一的 Health.OnDead 订阅点，保证 += / -= 同文件配对）：
        /// - 主角死亡：重置冷却。Mode E/F 等模式支持局内复活，避免复活瞬间被打就触发反制。
        /// - 其他角色死亡：交给引雷术判定（ThunderSetBonus_Storm.cs），只在套装激活时有效。
        /// </summary>
        private void OnThunderSetAnyDead(Health target, DamageInfo damageInfo)
        {
            if (target == null) return;
            if (target.IsMainCharacterHealth)
            {
                BumpSetBonusGeneration(); // 死亡作废仍在等待的连锁与反震，局内复活不继承旧结算。
                lastThunderTriggerTime = -999f;
                ResetThunderChainState();
                return;
            }

            TryScheduleThunderChain(target, damageInfo);
        }

        /// <summary>
        /// 雷霆套受击回调 - 电伤转治疗 + 概率雷霆反震
        /// </summary>
        private void OnThunderSetHurt(Health health, DamageInfo damageInfo)
        {
            try
            {
                // 只处理主角受击
                if (!thunderSetActive || health == null || health.IsDead || !health.IsMainCharacterHealth) return;

                // 1) 电伤转治疗（OnHurt 在扣血之后派发，只做回补，下一帧生效）
                float electricDamage = GetSetBonusElementDamagePortion(health, damageInfo, ElementTypes.electricity);
                if (electricDamage > 0f)
                {
                    float heal = electricDamage * THUNDER_SET_ELEC_HEAL_RATIO;
                    if (heal > 0f)
                    {
                        StartCoroutine(DelayedHeal(health, heal));
                    }
                }

                // 2) 雷霆反震
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

                // 延后一帧结算：OnHurt 可能正处在敌方爆炸的 ExplosionManager 循环内，
                // 嵌套 CreateExplosion 会覆写其共享 colliders/damagedHealth 缓冲。
                StartCoroutine(ThunderCounterStep(player, damageInfo.fromCharacter, setBonusGeneration));
            }
            catch (Exception e)
            {
                DevLog("[ThunderSet] OnThunderSetHurt 出错: " + e.Message);
            }
        }

        /// <summary>
        /// 雷霆反震结算：电击 AOE（不伤自己与友军）+ 玩家→攻击者电弧 + 爆发环 + 音效
        /// </summary>
        private IEnumerator ThunderCounterStep(CharacterMainControl player, CharacterMainControl attacker, int generation)
        {
            yield return null;

            if (!thunderSetActive || generation != setBonusGeneration || player == null) yield break;
            if (LevelManager.Instance == null || LevelManager.Instance.ExplosionManager == null) yield break;

            try
            {
                Vector3 origin = player.transform.position;

                // 构建伤害信息：buff/effect 通道（Mode G 矩阵登记为不计分）、无武器 TypeID
                DamageInfo dmg = new DamageInfo(player);
                dmg.damageValue = THUNDER_SET_COUNTER_DAMAGE;
                dmg.isExplosion = true;
                dmg.isFromBuffOrEffect = true;
                dmg.fromWeaponItemID = 0;
                dmg.AddElementFactor(ElementTypes.electricity, 1.0f);

                // canHurtSelf=false：官方默认 true 时 selfTeam=Teams.all，爆炸中心的玩家自己必吃这一下
                LevelManager.Instance.ExplosionManager.CreateExplosion(
                    origin,
                    THUNDER_SET_COUNTER_RADIUS,
                    dmg,
                    ExplosionFxTypes.normal,
                    0.3f,
                    false
                );

                SpawnSetBurst(origin, THUNDER_SET_BURST_COLOR, THUNDER_SET_COUNTER_RADIUS, 0.35f, 0);
                if (attacker != null && attacker.transform != null)
                {
                    SpawnSetArc(origin + Vector3.up * 1f, attacker.transform.position + Vector3.up * 1f,
                        THUNDER_SET_ARC_COLOR, 0.08f, 0.2f);
                }
                PlaySoundEffect(SetBonusSfx.ThunderCounter);

                DevLog("[ThunderSet] 雷霆反震触发！范围: " + THUNDER_SET_COUNTER_RADIUS + "m");
            }
            catch (Exception e)
            {
                DevLog("[ThunderSet] ThunderCounterStep 出错: " + e.Message);
            }
        }

        #endregion
    }
}
