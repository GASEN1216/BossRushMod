// ============================================================================
// NewWeaponMeleeFx.cs - 三把新近战武器共享的近战特效接线
// ============================================================================
// 模块说明：
//   两件事：
//   1. slashFx / hitFx 回退：走共享的 MeleeWeaponFxPolicy，不再复制第四份
//      EnsureMeleeAttackFx 模板（tests/MeleeWeaponFxPolicyUsageGuard.py 明确禁止）。
//      毒蛇匕首 / 冰霜长矛 / 召唤法杖三把共用这一处调用。
//   2. 挥砍拖尾：Harmony 后缀挂 CA_Attack.OnStart，按手持武器 TypeID 分派颜色与扫掠角，
//      实际生成交给 NewWeaponSwingFx。
//
//   扫掠角按武器手感区分：匕首快而窄、长矛中等、法杖大开大合。
//
// 门控（AGENTS.md 4.12）：
//   拖尾只在 CA_Attack 真正起手成功、且当前手持武器就是这三把之一时生成；
//   背包里的、仓库里的、NPC 手上的同名武器都不会触发任何工作。
// ============================================================================

using System;
using HarmonyLib;
using ItemStatsSystem;
using UnityEngine;

namespace BossRush
{
    /// <summary>三把新近战武器共享的 slashFx / hitFx 回退策略</summary>
    internal static class NewWeaponMeleeFx
    {
        private static readonly MeleeWeaponFxPolicy FxPolicy =
            new MeleeWeaponFxPolicy(allowSlashFxFallback: true, allowHitFxFallback: true);

        private static GameObject cachedFallbackSlashFx;
        private static GameObject cachedFallbackHitFx;

        // 一次都没找到时的重扫节流。场上暂时没有别的近战武器可借（例如刚进图），
        // 缓存会一直是 null，于是每次挥砍都要跑一遍 Resources.FindObjectsOfTypeAll——
        // 毒蛇匕首攻速 2.1，一秒两刀、每刀两次（slash + hit），足够造成可感知的卡顿。
        // 不能直接「找不到就永久放弃」：官方武器可能晚于首次挥砍才加载进来。
        private const float FallbackRescanInterval = 5f;
        private static float lastFailedScanTime = float.NegativeInfinity;

        /// <summary>为新武器的近战 Agent 补上官方 slashFx / hitFx 回退。可重复调用。</summary>
        public static void EnsureMeleeAttackFx(ItemAgent_MeleeWeapon meleeAgent)
        {
            FxPolicy.ApplyTo(meleeAgent, GetFallbackSlashFx, GetFallbackHitFx);
        }

        public static void ResetStaticCaches()
        {
            cachedFallbackSlashFx = null;
            cachedFallbackHitFx = null;
            lastFailedScanTime = float.NegativeInfinity;
        }

        private static GameObject GetFallbackSlashFx()
        {
            if (cachedFallbackSlashFx == null && CanRescan())
            {
                cachedFallbackSlashFx = FindFallbackMeleeFx(true);
                NoteScanResult(cachedFallbackSlashFx);
            }
            return cachedFallbackSlashFx;
        }

        private static GameObject GetFallbackHitFx()
        {
            if (cachedFallbackHitFx == null && CanRescan())
            {
                cachedFallbackHitFx = FindFallbackMeleeFx(false);
                NoteScanResult(cachedFallbackHitFx);
            }
            return cachedFallbackHitFx;
        }

        /// <summary>距上次「扫了但没找到」是否已超过节流间隔。</summary>
        private static bool CanRescan()
        {
            return Time.unscaledTime - lastFailedScanTime >= FallbackRescanInterval;
        }

        /// <summary>只有扫空才记时间戳；扫到了就不再进这条路径。</summary>
        private static void NoteScanResult(GameObject found)
        {
            if (found == null)
            {
                lastFailedScanTime = Time.unscaledTime;
            }
        }

        /// <summary>从场上任意一把非本批的近战武器身上借 slashFx / hitFx 引用（照霜之哀伤同款做法）。</summary>
        private static GameObject FindFallbackMeleeFx(bool slashFx)
        {
            try
            {
                ItemAgent_MeleeWeapon[] meleeAgents = Resources.FindObjectsOfTypeAll<ItemAgent_MeleeWeapon>();
                for (int i = 0; i < meleeAgents.Length; i++)
                {
                    ItemAgent_MeleeWeapon candidate = meleeAgents[i];
                    if (candidate == null) continue;

                    Item candidateItem = candidate.Item;
                    if (candidateItem != null && IsNewMeleeWeapon(candidateItem.TypeID))
                    {
                        // 不拿本批武器当回退源，否则五把互相借空引用
                        continue;
                    }

                    GameObject fx = slashFx ? candidate.slashFx : candidate.hitFx;
                    if (fx == null) continue;

                    if (cachedFallbackSlashFx == null && candidate.slashFx != null)
                    {
                        cachedFallbackSlashFx = candidate.slashFx;
                    }
                    if (cachedFallbackHitFx == null && candidate.hitFx != null)
                    {
                        cachedFallbackHitFx = candidate.hitFx;
                    }

                    return fx;
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[NewWeaponMeleeFx] 搜索近战特效引用失败: " + e.Message);
            }

            return null;
        }

        internal static bool IsNewMeleeWeapon(int typeId)
        {
            return typeId == NewWeaponIds.ViperDaggerTypeId
                || typeId == NewWeaponIds.FrostSpearTypeId
                || typeId == NewWeaponIds.SummonStaffTypeId;
        }
    }

    /// <summary>
    /// 挥砍拖尾触发点。参照 FrostmourneAttackFxPatch：CA_Attack.OnStart 返回 true 才算真正起手。
    /// </summary>
    [HarmonyPatch(typeof(CA_Attack), "OnStart")]
    public static class NewWeaponAttackFxPatch
    {
        // 匕首窄而快、长矛居中、法杖大开大合
        private const float ViperSweep = 95f;
        private const float FrostSpearSweep = 120f;
        private const float SummonStaffSweep = 140f;

        [HarmonyPostfix]
        [HarmonyPriority(Priority.First)]
        public static void Postfix(CA_Attack __instance, bool __result)
        {
            if (!__result) return;

            try
            {
                CharacterMainControl character = __instance.characterController;
                if (character == null) return;

                ItemAgent_MeleeWeapon melee = character.GetMeleeWeapon();
                if (melee == null || melee.Item == null) return;

                int typeId = melee.Item.TypeID;
                Color core;
                Color fade;
                float sweep;
                float baseRange;

                if (typeId == NewWeaponIds.ViperDaggerTypeId)
                {
                    core = NewWeaponPalette.VenomCore;
                    fade = NewWeaponPalette.VenomFade;
                    sweep = ViperSweep;
                    baseRange = ViperDaggerConfig.AttackRange;
                }
                else if (typeId == NewWeaponIds.FrostSpearTypeId)
                {
                    core = NewWeaponPalette.FrostCore;
                    fade = NewWeaponPalette.FrostFade;
                    sweep = FrostSpearSweep;
                    baseRange = FrostSpearConfig.AttackRange;
                }
                else if (typeId == NewWeaponIds.SummonStaffTypeId)
                {
                    core = NewWeaponPalette.SoulCore;
                    fade = NewWeaponPalette.SoulFade;
                    sweep = SummonStaffSweep;
                    baseRange = SummonStaffConfig.AttackRange;
                }
                else
                {
                    return;
                }

                NewWeaponMeleeFx.EnsureMeleeAttackFx(melee);
                SpawnSwing(character, melee, core, fade, sweep, baseRange);
            }
            catch { /* 特效失败不能影响攻击本身 */ }
        }

        private static void SpawnSwing(
            CharacterMainControl character,
            ItemAgent_MeleeWeapon melee,
            Color core,
            Color fade,
            float sweep,
            float baseRange)
        {
            // 玩家重铸/词缀改过攻击距离时按比例放大拖尾，保持「打到哪、划到哪」
            float rangeScale = 1f;
            if (melee != null && baseRange > 0.01f)
            {
                rangeScale = Mathf.Max(0.2f, melee.AttackRange / baseRange);
            }

            Vector3 forward = GetFlatAimDirection(character);
            Vector3 spawnPos = character.transform.position + Vector3.up * 1.1f + forward * 0.15f;
            NewWeaponSwingFx.PlayAt(spawnPos, Quaternion.LookRotation(forward), core, fade, sweep, rangeScale);
        }

        private static Vector3 GetFlatAimDirection(CharacterMainControl character)
        {
            Vector3 aimDirection = character.CurrentAimDirection;
            aimDirection.y = 0f;

            if (aimDirection.sqrMagnitude < 0.0001f)
            {
                aimDirection = character.transform.forward;
                aimDirection.y = 0f;
            }

            if (aimDirection.sqrMagnitude < 0.0001f)
            {
                return Vector3.forward;
            }

            return aimDirection.normalized;
        }
    }
}
