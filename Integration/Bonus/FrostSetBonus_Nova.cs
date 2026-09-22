// ============================================================================
// FrostSetBonus_Nova.cs - 冰霜套装「霜噬」（普攻附带）
// ============================================================================
// 模块说明：
//   霜噬：主角用**普通攻击**打中敌人时，为这一击追加少量冰伤并有概率冻结目标。
//   2026-09-20 owner 定：从「累计 3 次击杀后霜爆」改为普攻附带，并把单次伤害压低、
//   加内置冷却，避免高射速武器把套装增伤拉成主要 DPS 来源。
//
//   反 DPS 缩放的三道闸（缺一不可）：
//     1) FROST_BITE_COOLDOWN 内置冷却：每 N 秒最多触发一次，与射速完全脱钩；
//     2) 单次伤害是常数（不随武器伤害缩放），压到 Boss 血量的可忽略量级；
//     3) 只认玩家亲手的直接命中（isFromBuffOrEffect 的伤害不触发），套装自身伤害不自续。
//
//   重入安全：Health.OnHurt 回调里只做过滤与调度，实际结算延后到协程；
//   结算期间 frostBiteResolving 置位，套装自己打出的伤害不会再排一次。
//   订阅由 FrostSetBonus.cs 的 OnFrostSetHurt 统一分派，本文件不订阅任何事件。
// ============================================================================

using System;
using System.Collections;
using UnityEngine;

namespace BossRush
{
    public partial class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        #region 霜噬配置

        /// <summary>霜噬冰伤（常数，不随武器伤害缩放）。</summary>
        private const float FROST_BITE_DAMAGE = 5f;
        /// <summary>霜噬内置冷却（秒）。高射速武器只能按这个节奏触发。</summary>
        private const float FROST_BITE_COOLDOWN = 1.1f;
        /// <summary>霜噬冻结概率。</summary>
        private const float FROST_BITE_FREEZE_CHANCE = 0.35f;
        /// <summary>结算延后（秒），脱离官方 Hurt 的调用栈。</summary>
        private const float FROST_BITE_DELAY = 0.04f;

        private static readonly WaitForSeconds frostBiteWait = new WaitForSeconds(FROST_BITE_DELAY);

        private float lastFrostBiteTime = -999f;
        /// <summary>结算中：套装自己打出的那一下不再排新的霜噬。</summary>
        private bool frostBiteResolving = false;
        /// <summary>
        /// 已排队、尚未结算：冷却改在结算那一刻才扣（见 FrostBiteStep），
        /// 调度与结算之间的这 40 毫秒必须另有一道闸，否则高射速武器能在窗口里连排好几条。
        /// </summary>
        private bool frostBitePending = false;

        #endregion

        #region 霜噬

        private void ResetFrostNovaState()
        {
            lastFrostBiteTime = -999f;
            frostBiteResolving = false;
            frostBitePending = false;
        }

        /// <summary>
        /// Health.OnHurt 分派入口：过滤 + 调度，不在回调里结算。
        /// 过滤序按「越便宜越靠前」排布：布尔 → 浮点比较 → 结构体字段 → 组件解析。
        /// </summary>
        private void TryScheduleFrostBite(Health target, DamageInfo damageInfo)
        {
            try
            {
                if (!frostSetActive || frostBiteResolving || frostBitePending) return;
                if (Time.time - lastFrostBiteTime < FROST_BITE_COOLDOWN) return;
                if (damageInfo.isFromBuffOrEffect) return;   // 只认玩家亲手的直接命中
                if (!(damageInfo.finalDamage > 0f)) return;

                CharacterMainControl victim;
                Vector3 position;
                if (!TryResolveSetBonusEnemyTarget(target, damageInfo, out victim, out position)) return;
                if (target.IsDead) return;

                // 冷却在 FrostBiteStep 真正结算时才扣。调度即扣会让
                // 「目标在这 40 毫秒里被打死 / 中途脱下装备 / 切图」白白吃掉一整轮冷却。
                frostBitePending = true;
                if (StartCoroutine(FrostBiteStep(target, victim, position, setBonusGeneration)) == null)
                {
                    frostBitePending = false;
                }
            }
            catch (Exception e)
            {
                DevLog("[FrostSet] TryScheduleFrostBite 出错: " + e.Message);
            }
        }

        /// <summary>
        /// 霜噬结算：命中点小霜爆 → 追加冰伤 → 概率冻结。单目标，不扫描、不连锁。
        /// </summary>
        private IEnumerator FrostBiteStep(Health target, CharacterMainControl victim, Vector3 origin, int generation)
        {
            yield return frostBiteWait;

            // 代数不符 = 这条是上一次激活（多半是上一张图）排队下来的，目标已作废
            if (!frostSetActive || generation != setBonusGeneration) yield break;
            // 只有本次激活的请求能释放 pending，旧请求不能放开新请求的排队闸。
            frostBitePending = false;
            if (Time.time - lastFrostBiteTime < FROST_BITE_COOLDOWN) yield break;
            if (target == null || target.IsDead) yield break;
            CharacterMainControl player = CharacterMainControl.Main;
            if (player == null) yield break;

            // 走到这里才是真的会造成伤害的一次霜噬，此刻才扣冷却
            lastFrostBiteTime = Time.time;
            SpawnSetBurst(origin, FROST_SET_BURST_COLOR, 0.9f, 0.22f, 3);

            frostBiteResolving = true;
            try
            {
                DamageInfo dmg = new DamageInfo(player);
                dmg.damageValue = FROST_BITE_DAMAGE;
                dmg.damageType = DamageTypes.normal;
                dmg.isFromBuffOrEffect = true;
                dmg.fromWeaponItemID = 0;
                dmg.damagePoint = origin;
                dmg.AddElementFactor(ElementTypes.ice, 1f);
                target.Hurt(dmg);

                if (!target.IsDead && victim != null
                    && UnityEngine.Random.value <= FROST_BITE_FREEZE_CHANCE)
                {
                    if (TryApplyFrostFreeze(victim))
                    {
                        PlaySoundEffect(SetBonusSfx.FrostNova);
                    }
                }
            }
            catch (Exception e)
            {
                DevLog("[FrostSet] 霜噬结算出错: " + e.Message);
            }
            finally
            {
                frostBiteResolving = false;
            }
        }

        #endregion
    }
}
