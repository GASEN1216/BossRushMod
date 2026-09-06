// ============================================================================
// ThunderSetBonus_Storm.cs - 雷霆套装「引雷术」与环境电弧
// ============================================================================
// 模块说明：
//   引雷术：主角亲手击杀敌人时，以尸体为中心向 6 米内最多 3 个存活敌人放出连锁闪电（35 电伤）。
//   被闪电击杀的敌人再触发下一跳，最多 3 跳，每跳伤害 ×0.75；首跳（武器/手雷击杀）有 0.4 秒间隔，
//   链内无间隔。DoT、套装自身伤害不起首跳（isFromBuffOrEffect），连锁再触发只靠 thunderChainDepth>0。
//
//   重入安全：Health.OnDead 回调里只做过滤与调度，实际扫描/结算延后到协程；结算时把当前跳数写进
//   thunderChainDepth 供嵌套 OnDead 读取（Hurt 会同步派发 OnDead），try/finally 保证归零。
//   订阅由 ThunderSetBonus.cs 的 OnThunderSetAnyDead 统一分派，本文件不订阅任何事件。
// ============================================================================

using System;
using System.Collections;
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
        private const float THUNDER_CHAIN_FIRST_HOP_COOLDOWN = 0.4f; // 首跳最小间隔（秒）
        private const float THUNDER_CHAIN_HOP_DELAY = 0.08f;         // 每跳延后（给电弧动画留时间，也脱离原调用栈）
        private const float THUNDER_AMBIENT_ARC_INTERVAL = 3f;       // 环境电弧平均间隔（秒）
        private const float THUNDER_AMBIENT_ARC_JITTER = 1f;         // 环境电弧间隔抖动（秒）

        private static readonly WaitForSeconds thunderChainHopWait = new WaitForSeconds(THUNDER_CHAIN_HOP_DELAY);

        // >0 表示当前正处于第 N 跳的伤害循环内（供嵌套 OnDead 判定是否再起一跳）
        private int thunderChainDepth = 0;
        private float lastThunderChainTime = -999f;
        private Coroutine thunderAmbientArcCoroutine = null;

        #endregion

        #region 引雷术

        private void ResetThunderChainState()
        {
            thunderChainDepth = 0;
            lastThunderChainTime = -999f;
        }

        /// <summary>
        /// Health.OnDead 分派入口：过滤 + 调度，不在回调里结算。
        /// </summary>
        private void TryScheduleThunderChain(Health target, DamageInfo damageInfo)
        {
            try
            {
                if (!thunderSetActive) return;

                int depth = thunderChainDepth;
                if (depth >= THUNDER_CHAIN_MAX_DEPTH) return;

                if (depth == 0)
                {
                    // 首跳只认玩家亲手的直接击杀（武器/手雷）；DoT 与套装自身伤害不起链
                    if (damageInfo.isFromBuffOrEffect) return;
                    if (Time.time - lastThunderChainTime < THUNDER_CHAIN_FIRST_HOP_COOLDOWN) return;
                }

                CharacterMainControl victim;
                Vector3 position;
                if (!TryResolveSetBonusKillVictim(target, damageInfo, out victim, out position)) return;

                if (depth == 0)
                {
                    lastThunderChainTime = Time.time;
                }

                StartCoroutine(ThunderChainStep(position, victim, depth + 1));
            }
            catch (Exception e)
            {
                DevLog("[ThunderSet] TryScheduleThunderChain 出错: " + e.Message);
            }
        }

        /// <summary>
        /// 第 hop 跳结算：扫描 → 电弧/爆发/音效 → 逐目标 Hurt（嵌套 OnDead 由 thunderChainDepth 门控）
        /// </summary>
        private IEnumerator ThunderChainStep(Vector3 origin, CharacterMainControl corpse, int hop)
        {
            yield return thunderChainHopWait;

            if (!thunderSetActive) yield break;
            CharacterMainControl player = CharacterMainControl.Main;
            if (player == null) yield break;

            int count = 0;
            try
            {
                count = ScanSetBonusEnemies(origin, THUNDER_CHAIN_RADIUS, corpse, THUNDER_CHAIN_MAX_TARGETS);
            }
            catch (Exception e)
            {
                DevLog("[ThunderSet] 引雷术扫描出错: " + e.Message);
                count = 0;
            }
            if (count <= 0) yield break;

            float damage = THUNDER_CHAIN_DAMAGE * Mathf.Pow(THUNDER_CHAIN_DAMAGE_DECAY, hop - 1);
            Vector3 from = origin + Vector3.up * 1f;

            // 先出表现再结算：Hurt 会同步派发 OnDead，可能立刻调度下一跳
            for (int i = 0; i < count; i++)
            {
                Health h = setBonusScanResults[i];
                if (h == null) continue;
                SpawnSetArc(from, h.transform.position + Vector3.up * 1f, THUNDER_SET_ARC_COLOR, 0.1f, 0.25f);
            }
            SpawnSetBurst(origin, THUNDER_SET_BURST_COLOR, 1.5f, 0.3f, 0);
            PlaySoundEffect(SetBonusSfx.ThunderChain);

            thunderChainDepth = hop;
            try
            {
                for (int i = 0; i < count; i++)
                {
                    Health h = setBonusScanResults[i];
                    if (h == null || h.IsDead) continue;

                    DamageInfo dmg = new DamageInfo(player);
                    dmg.damageValue = damage;
                    dmg.damageType = DamageTypes.normal;
                    dmg.isFromBuffOrEffect = true;
                    dmg.fromWeaponItemID = 0;
                    dmg.damagePoint = h.transform.position;
                    dmg.AddElementFactor(ElementTypes.electricity, 1f);
                    h.Hurt(dmg);
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
