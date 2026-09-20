// ============================================================================
// ThunderSetBonus_Storm.cs - 雷霆套装「雷噬」与环境电弧
// ============================================================================
// 模块说明：
//   雷噬：主角用**普通攻击**打中敌人时，从命中点向周围最多 2 个**其它**敌人各放一道
//   小电弧（常数电伤）。2026-09-20 owner 定：从「累计 3 次击杀后引雷连锁」改为普攻附带，
//   并把单次伤害压低、加内置冷却，避免高射速武器把套装增伤拉成主要 DPS 来源。
//
//   反 DPS 缩放的三道闸（缺一不可）：
//     1) THUNDER_BITE_COOLDOWN 内置冷却：每 N 秒最多触发一次，与射速完全脱钩；
//     2) 单次伤害是常数（不随武器伤害缩放），且只打命中目标**之外**的敌人，不叠在主目标上；
//     3) 只认玩家亲手的直接命中（isFromBuffOrEffect 的伤害不触发），套装自身伤害不自续。
//
//   重入安全：Health.OnHurt 回调里只做过滤与调度，实际扫描/结算延后到协程；
//   结算期间 thunderBiteResolving 置位，套装自己打出的伤害不会再排一次。
//   订阅由 ThunderSetBonus.cs 的 OnThunderSetHurt 统一分派，本文件不订阅任何事件。
// ============================================================================

using System;
using System.Collections;
using UnityEngine;

namespace BossRush
{
    public partial class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        #region 雷噬配置

        /// <summary>雷噬搜索半径（米），以命中点为中心。</summary>
        private const float THUNDER_BITE_RADIUS = 4f;
        /// <summary>雷噬每次最多波及的其它敌人数。</summary>
        private const int THUNDER_BITE_MAX_TARGETS = 2;
        /// <summary>雷噬电伤（常数，不随武器伤害缩放）。</summary>
        private const float THUNDER_BITE_DAMAGE = 7f;
        /// <summary>雷噬内置冷却（秒）。高射速武器只能按这个节奏触发。</summary>
        private const float THUNDER_BITE_COOLDOWN = 1.4f;
        /// <summary>结算延后（秒），给电弧动画留时间，也脱离官方 Hurt 的调用栈。</summary>
        private const float THUNDER_BITE_DELAY = 0.05f;

        private const float THUNDER_AMBIENT_ARC_INTERVAL = 3f;       // 环境电弧平均间隔（秒）
        private const float THUNDER_AMBIENT_ARC_JITTER = 1f;         // 环境电弧间隔抖动（秒）

        private static readonly WaitForSeconds thunderBiteWait = new WaitForSeconds(THUNDER_BITE_DELAY);

        private float lastThunderBiteTime = -999f;
        /// <summary>结算中：套装自己打出的那一下不再排新的雷噬。</summary>
        private bool thunderBiteResolving = false;
        /// <summary>
        /// 已排队、尚未结算：冷却改在扫到目标那一刻才扣（见 ThunderBiteStep），
        /// 调度与结算之间的这 50 毫秒必须另有一道闸，否则高射速武器能在窗口里连排好几条。
        /// </summary>
        private bool thunderBitePending = false;
        private Coroutine thunderAmbientArcCoroutine = null;

        #endregion

        #region 雷噬

        private void ResetThunderChainState()
        {
            lastThunderBiteTime = -999f;
            thunderBiteResolving = false;
            thunderBitePending = false;
        }

        /// <summary>
        /// Health.OnHurt 分派入口：过滤 + 调度，不在回调里结算。
        /// 过滤序按「越便宜越靠前」排布：布尔 → 浮点比较 → 结构体字段 → 组件解析。
        /// </summary>
        private void TryScheduleThunderBite(Health target, DamageInfo damageInfo)
        {
            try
            {
                if (!thunderSetActive || thunderBiteResolving || thunderBitePending) return;
                if (Time.time - lastThunderBiteTime < THUNDER_BITE_COOLDOWN) return;
                if (damageInfo.isFromBuffOrEffect) return;   // 只认玩家亲手的直接命中
                if (!(damageInfo.finalDamage > 0f)) return;

                CharacterMainControl victim;
                Vector3 position;
                if (!TryResolveSetBonusEnemyTarget(target, damageInfo, out victim, out position)) return;

                // 冷却在 ThunderBiteStep 扫到目标之后才扣。调度即扣会让
                // 「附近只有被你打的那一个敌人」「中途脱下装备 / 切图」白白吃掉一整轮冷却——
                // 单挑 Boss 时那正是最常见的情形。
                thunderBitePending = true;
                if (StartCoroutine(ThunderBiteStep(position, victim, setBonusGeneration)) == null)
                {
                    thunderBitePending = false;
                }
            }
            catch (Exception e)
            {
                DevLog("[ThunderSet] TryScheduleThunderBite 出错: " + e.Message);
            }
        }

        /// <summary>
        /// 雷噬结算：扫描命中点周围（排除被打的那个）→ 电弧 + 爆发 + 音效 → 逐目标电伤。
        /// 只有一跳，不接链：被电死的目标不会再起第二段。
        /// </summary>
        private IEnumerator ThunderBiteStep(Vector3 origin, CharacterMainControl struckTarget, int generation)
        {
            yield return thunderBiteWait;
            thunderBitePending = false;

            // 代数不符 = 这条是上一次激活（多半是上一张图）排队下来的，坐标已作废
            if (!thunderSetActive || generation != setBonusGeneration) yield break;
            CharacterMainControl player = CharacterMainControl.Main;
            if (player == null) yield break;

            int count = 0;
            try
            {
                count = ScanSetBonusEnemies(origin, THUNDER_BITE_RADIUS, struckTarget, THUNDER_BITE_MAX_TARGETS);
            }
            catch (Exception e)
            {
                DevLog("[ThunderSet] 雷噬扫描出错: " + e.Message);
                count = 0;
            }
            // 一个都没扫到 = 这次雷噬不会造成任何伤害，冷却不扣，下一发普攻还能再试
            if (count <= 0) yield break;

            // 走到这里才是真的会放电弧的一次雷噬，此刻才扣冷却
            lastThunderBiteTime = Time.time;
            Vector3 from = origin + Vector3.up * 1f;

            // 先出表现再结算：Hurt 会同步派发死亡链路，之后 transform 可能已销毁
            for (int i = 0; i < count; i++)
            {
                Health h = setBonusScanResults[i];
                if (h == null) continue;
                SpawnSetArc(from, h.transform.position + Vector3.up * 1f, THUNDER_SET_ARC_COLOR, 0.1f, 0.22f);
            }
            SpawnSetBurst(origin, THUNDER_SET_BURST_COLOR, 1.2f, 0.25f, 0);
            PlaySoundEffect(SetBonusSfx.ThunderChain);

            thunderBiteResolving = true;
            try
            {
                for (int i = 0; i < count; i++)
                {
                    Health h = setBonusScanResults[i];
                    if (h == null || h.IsDead) continue;

                    // 位置先取：目标可能在 Hurt 里被销毁，事后读 transform 会抛
                    Vector3 targetPosition = h.transform.position;

                    DamageInfo dmg = new DamageInfo(player);
                    dmg.damageValue = THUNDER_BITE_DAMAGE;
                    dmg.damageType = DamageTypes.normal;
                    dmg.isFromBuffOrEffect = true;
                    dmg.fromWeaponItemID = 0;
                    dmg.damagePoint = targetPosition;
                    dmg.AddElementFactor(ElementTypes.electricity, 1f);
                    h.Hurt(dmg);
                }
            }
            catch (Exception e)
            {
                DevLog("[ThunderSet] 雷噬结算出错: " + e.Message);
            }
            finally
            {
                thunderBiteResolving = false;
            }

            DevLog("[ThunderSet] 雷噬波及 " + count + " 个目标");
        }

        #endregion

        #region 环境电弧

        private void StartThunderAmbientArcLoop(CharacterMainControl player)
        {
            StopThunderAmbientArcLoop();
            if (player == null) return;
            thunderAmbientArcCoroutine = StartCoroutine(ThunderAmbientArcLoop(player));
        }

        private void StopThunderAmbientArcLoop()
        {
            if (thunderAmbientArcCoroutine != null)
            {
                StopCoroutine(thunderAmbientArcCoroutine);
                thunderAmbientArcCoroutine = null;
            }
        }

        /// <summary>
        /// 每 3±1 秒在主角肩宽处闪一道小电弧。角色随场景销毁时循环自然结束，
        /// 场景重载后由 SetBonusManager 的停用→重查重新启动。
        /// </summary>
        private IEnumerator ThunderAmbientArcLoop(CharacterMainControl player)
        {
            while (thunderSetActive && player != null)
            {
                yield return new WaitForSeconds(THUNDER_AMBIENT_ARC_INTERVAL
                    + UnityEngine.Random.Range(-THUNDER_AMBIENT_ARC_JITTER, THUNDER_AMBIENT_ARC_JITTER));

                if (!thunderSetActive || player == null) break;

                Transform t = player.transform;
                Vector3 center = t.position + Vector3.up * 1.2f;
                Vector3 side = t.right * 0.45f;
                Vector3 jitter = new Vector3(
                    UnityEngine.Random.Range(-0.15f, 0.15f),
                    UnityEngine.Random.Range(-0.25f, 0.25f),
                    UnityEngine.Random.Range(-0.15f, 0.15f));
                SpawnSetArc(center - side + jitter, center + side - jitter, THUNDER_SET_ARC_COLOR, 0.05f, 0.12f);
            }

            thunderAmbientArcCoroutine = null;
        }

        #endregion
    }
}
