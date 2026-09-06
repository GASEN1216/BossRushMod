// ============================================================================
// FrostSetBonus_Nova.cs - 冰霜套装「冰葬」
// ============================================================================
// 模块说明：
//   冰葬：主角亲手击杀敌人时，以尸体为中心 4.5 米霜爆——范围内存活敌人受 20 冰伤并被冻结
//   （复用 FrostSetBonus.TryApplyFrostFreeze 的三级回退），1.5 秒冷却，不连锁：
//   霜爆结算期间的击杀不再起新的霜爆（frostNovaResolving），DoT 与套装自身伤害的击杀也不起
//   （isFromBuffOrEffect）。
//
//   重入安全：Health.OnDead 回调里只做过滤与调度，实际扫描/结算延后到协程；
//   订阅由 FrostSetBonus.cs 的 OnFrostSetAnyDead 统一分派，本文件不订阅任何事件。
// ============================================================================

using System;
using System.Collections;
using UnityEngine;

namespace BossRush
{
    public partial class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        #region 冰葬配置

        private const float FROST_NOVA_RADIUS = 4.5f;      // 霜爆半径（米）
        private const float FROST_NOVA_DAMAGE = 20f;       // 冰伤
        private const float FROST_NOVA_COOLDOWN = 1.5f;    // 冷却（秒）
        private const int FROST_NOVA_MAX_TARGETS = 6;      // 每次最多结算目标数
        private const float FROST_NOVA_DELAY = 0.06f;      // 延后（秒），脱离死亡派发调用栈

        private static readonly WaitForSeconds frostNovaWait = new WaitForSeconds(FROST_NOVA_DELAY);

        private float lastFrostNovaTime = -999f;
        private bool frostNovaResolving = false;   // 结算中：其间的击杀不再起新的霜爆（不连锁）

        #endregion

        #region 冰葬

        private void ResetFrostNovaState()
        {
            lastFrostNovaTime = -999f;
            frostNovaResolving = false;
        }

        /// <summary>
        /// Health.OnDead 分派入口：过滤 + 调度，不在回调里结算。
        /// </summary>
        private void TryScheduleFrostNova(Health target, DamageInfo damageInfo)
        {
            try
            {
                if (!frostSetActive || frostNovaResolving) return;
                if (damageInfo.isFromBuffOrEffect) return;   // 只认玩家亲手的直接击杀
                if (Time.time - lastFrostNovaTime < FROST_NOVA_COOLDOWN) return;

                CharacterMainControl victim;
                Vector3 position;
                if (!TryResolveSetBonusKillVictim(target, damageInfo, out victim, out position)) return;

                lastFrostNovaTime = Time.time;
                StartCoroutine(FrostNovaStep(position, victim));
            }
            catch (Exception e)
            {
                DevLog("[FrostSet] TryScheduleFrostNova 出错: " + e.Message);
            }
        }

        /// <summary>
        /// 霜爆结算：爆发环 + 碎片 + 音效 → 扫描 → 逐目标冰伤 + 冻结
        /// </summary>
        private IEnumerator FrostNovaStep(Vector3 origin, CharacterMainControl corpse)
        {
            yield return frostNovaWait;

            if (!frostSetActive) yield break;
            CharacterMainControl player = CharacterMainControl.Main;
            if (player == null) yield break;

            SpawnSetBurst(origin, FROST_SET_BURST_COLOR, FROST_NOVA_RADIUS, 0.45f, 6);
            PlaySoundEffect(SetBonusSfx.FrostNova);

            int count = 0;
            try
            {
                count = ScanSetBonusEnemies(origin, FROST_NOVA_RADIUS, corpse, FROST_NOVA_MAX_TARGETS);
            }
            catch (Exception e)
            {
                DevLog("[FrostSet] 冰葬扫描出错: " + e.Message);
                count = 0;
            }
            if (count <= 0) yield break;

            frostNovaResolving = true;
            try
            {
                for (int i = 0; i < count; i++)
                {
                    Health h = setBonusScanResults[i];
                    if (h == null || h.IsDead) continue;

                    DamageInfo dmg = new DamageInfo(player);
                    dmg.damageValue = FROST_NOVA_DAMAGE;
                    dmg.damageType = DamageTypes.normal;
                    dmg.isFromBuffOrEffect = true;
                    dmg.fromWeaponItemID = 0;
                    dmg.damagePoint = h.transform.position;
                    dmg.AddElementFactor(ElementTypes.ice, 1f);
                    h.Hurt(dmg);

                    if (!h.IsDead)
                    {
                        CharacterMainControl enemy = h.TryGetCharacter();
                        if (enemy != null)
                        {
                            TryApplyFrostFreeze(enemy);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                DevLog("[FrostSet] 冰葬结算出错: " + e.Message);
            }
            finally
            {
                frostNovaResolving = false;
            }

            DevLog("[FrostSet] 冰葬霜爆命中 " + count + " 个目标");
        }

        #endregion
    }
}
