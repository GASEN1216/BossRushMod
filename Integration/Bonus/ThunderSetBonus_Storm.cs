// ============================================================================
// ThunderSetBonus_Storm.cs - 雷霆套装「引雷术」与环境电弧
// ============================================================================
// 模块说明：
//   引雷术：主角亲手击杀敌人时，以尸体为中心向 6 米内最多 3 个存活敌人放出连锁闪电（35 电伤）。
//   若这一跳打死了人，就从**其中一具**尸体继续下一跳，最多 3 跳、每跳伤害 ×0.75。
//
//   为什么下一跳由本协程自己接、而不是靠 Hurt 同步派发的 OnDead 再进来一次：
//   后者会让一跳里死掉的每个目标各起一条链（3 目标 × 3 跳 = 最坏 39 次结算与 39 道电弧，
//   全挤在 0.24 秒内），既与「最多 3 跳」的说明不符，也会在密集波次里抖帧。
//   现在整条链是一条线：每跳至多 3 次伤害、全程至多 9 次，且同一敌人在一条链里只吃一次
//   （thunderChainHits 去重）。链自身的伤害带 isFromBuffOrEffect，加上「在飞」标志双保险，
//   绝不会反过来再起新链。
//
//   重入安全：Health.OnDead 回调里只做过滤与调度，实际扫描/结算延后到协程；结算时把当前跳数
//   写进 thunderChainDepth 供嵌套 OnDead 读取，try/finally 保证归零。
//   订阅由 ThunderSetBonus.cs 的 OnThunderSetAnyDead 统一分派，本文件不订阅任何事件。
// ============================================================================

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace BossRush
{
    public partial class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        #region 引雷术配置

        private const float THUNDER_CHAIN_RADIUS = 6f;               // 连锁搜索半径（米）
        private const int THUNDER_CHAIN_MAX_TARGETS = 3;             // 每跳最多目标数
        private const float THUNDER_CHAIN_DAMAGE = 35f;              // 首跳电伤
        private const int THUNDER_CHAIN_MAX_DEPTH = 3;               // 最多跳数
        private const float THUNDER_CHAIN_DAMAGE_DECAY = 0.75f;      // 每跳伤害衰减
        private const float THUNDER_CHAIN_FIRST_HOP_COOLDOWN = 0.4f; // 两条链之间的最小间隔（秒）
        private const float THUNDER_CHAIN_HOP_DELAY = 0.08f;         // 每跳延后（给电弧动画留时间，也脱离原调用栈）
        private const float THUNDER_AMBIENT_ARC_INTERVAL = 3f;       // 环境电弧平均间隔（秒）
        private const float THUNDER_AMBIENT_ARC_JITTER = 1f;         // 环境电弧间隔抖动（秒）

        private static readonly WaitForSeconds thunderChainHopWait = new WaitForSeconds(THUNDER_CHAIN_HOP_DELAY);

        // >0 表示当前正处于第 N 跳的伤害循环内（供嵌套 OnDead 判定是否再起一跳）
        private int thunderChainDepth = 0;
        // 一条链从调度到最后一跳收尾期间为 true：跨跳的间隙也不许第二条链插进来抢 thunderChainDepth
        private bool thunderChainInFlight = false;
        // 本条链已命中过的目标，跨跳去重：同一敌人不该被同一条链电两次
        private readonly List<Health> thunderChainHits = new List<Health>(THUNDER_CHAIN_MAX_TARGETS * THUNDER_CHAIN_MAX_DEPTH);
        private float lastThunderChainTime = -999f;
        private Coroutine thunderAmbientArcCoroutine = null;

        #endregion

        #region 引雷术

        private void ResetThunderChainState()
        {
            thunderChainDepth = 0;
            thunderChainInFlight = false;
            thunderChainHits.Clear();
            lastThunderChainTime = -999f;
        }

        /// <summary>
        /// Health.OnDead 分派入口：过滤 + 调度，不在回调里结算。
        /// 只认玩家亲手的直接击杀（武器/手雷）：DoT 与套装自身伤害都带 isFromBuffOrEffect，不起链。
        /// </summary>
        private void TryScheduleThunderChain(Health target, DamageInfo damageInfo)
        {
            try
            {
                if (!thunderSetActive) return;
                // 链自身的伤害不再起新链：isFromBuffOrEffect 是主锁，「在飞」是双保险，
                // 两条一起挡住「链打死人 → 又起一条链」的指数分叉。
                if (thunderChainInFlight || thunderChainDepth != 0) return;
                if (damageInfo.isFromBuffOrEffect) return;
                if (Time.time - lastThunderChainTime < THUNDER_CHAIN_FIRST_HOP_COOLDOWN) return;

                CharacterMainControl victim;
                Vector3 position;
                if (!TryResolveSetBonusKillVictim(target, damageInfo, out victim, out position)) return;

                lastThunderChainTime = Time.time;
                thunderChainInFlight = true;
                thunderChainHits.Clear();
                StartCoroutine(ThunderChainStep(position, victim, 1, setBonusGeneration));
            }
            catch (Exception e)
            {
                thunderChainInFlight = false;
                DevLog("[ThunderSet] TryScheduleThunderChain 出错: " + e.Message);
            }
        }

        /// <summary>
        /// 第 hop 跳结算：扫描（排除本链已命中）→ 电弧/爆发/音效 → 逐目标 Hurt →
        /// 若打死了人且未到跳数上限，从其中一具尸体接下一跳。
        /// </summary>
        private IEnumerator ThunderChainStep(Vector3 origin, CharacterMainControl corpse, int hop, int generation)
        {
            yield return thunderChainHopWait;

            bool continued = false;
            try
            {
                if (!thunderSetActive || generation != setBonusGeneration) yield break;
                CharacterMainControl player = CharacterMainControl.Main;
                if (player == null) yield break;

                int count = 0;
                try
                {
                    count = ScanSetBonusEnemies(origin, THUNDER_CHAIN_RADIUS, corpse,
                        THUNDER_CHAIN_MAX_TARGETS, thunderChainHits);
                }
                catch (Exception e)
                {
                    DevLog("[ThunderSet] 引雷术扫描出错: " + e.Message);
                    count = 0;
                }
                if (count <= 0) yield break;

                float damage = THUNDER_CHAIN_DAMAGE * Mathf.Pow(THUNDER_CHAIN_DAMAGE_DECAY, hop - 1);
                Vector3 from = origin + Vector3.up * 1f;

                // 先出表现再结算：Hurt 会同步派发 OnDead，尸体位置随后就要用来接下一跳
                for (int i = 0; i < count; i++)
                {
                    Health h = setBonusScanResults[i];
                    if (h == null) continue;
                    SpawnSetArc(from, h.transform.position + Vector3.up * 1f, THUNDER_SET_ARC_COLOR, 0.1f, 0.25f);
                }
                SpawnSetBurst(origin, THUNDER_SET_BURST_COLOR, 1.5f, 0.3f, 0);
                PlaySoundEffect(SetBonusSfx.ThunderChain);

                bool hasNext = false;
                CharacterMainControl nextCorpse = null;
                Vector3 nextOrigin = origin;

                thunderChainDepth = hop;
                try
                {
                    for (int i = 0; i < count; i++)
                    {
                        Health h = setBonusScanResults[i];
                        if (h == null || h.IsDead) continue;

                        // 本链去重登记必须在 Hurt 之前：Hurt 会同步走完死亡链路，
                        // 之后 h 可能已被销毁、拿不到引用再补登记。
                        thunderChainHits.Add(h);
                        // 位置也先取：目标可能在 Hurt 里被销毁，事后读 transform 会抛
                        Vector3 targetPosition = h.transform.position;

                        DamageInfo dmg = new DamageInfo(player);
                        dmg.damageValue = damage;
                        dmg.damageType = DamageTypes.normal;
                        dmg.isFromBuffOrEffect = true;
                        dmg.fromWeaponItemID = 0;
                        dmg.damagePoint = targetPosition;
                        dmg.AddElementFactor(ElementTypes.electricity, 1f);
                        h.Hurt(dmg);

                        // 取第一具被本跳电死的尸体作为下一跳起点：一跳只接一条，链是线性的。
                        // h 已销毁（== null）同样算「被电死」，此时只有位置可用、没有尸体引用。
                        if (!hasNext && (h == null || h.IsDead))
                        {
                            hasNext = true;
                            nextOrigin = targetPosition;
                            nextCorpse = h != null ? h.TryGetCharacter() : null;
                        }
                    }
                }
                catch (Exception e)
                {
                    DevLog("[ThunderSet] 引雷术结算出错: " + e.Message);
                }
                finally
                {
                    thunderChainDepth = 0;
                }

                DevLog("[ThunderSet] 引雷术第 " + hop + " 跳命中 " + count + " 个目标，伤害 " + damage.ToString("F0"));

                if (hasNext && hop < THUNDER_CHAIN_MAX_DEPTH)
                {
                    continued = true;
                    StartCoroutine(ThunderChainStep(nextOrigin, nextCorpse, hop + 1, generation));
                }
            }
            finally
            {
                // 旧激活也会经 yield break 进入 finally，不能清掉重穿后新链的 owner 与去重表。
                if (!continued && generation == setBonusGeneration)
                {
                    thunderChainInFlight = false;
                    thunderChainHits.Clear();
                }
            }
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
