// ============================================================================
// CampaignObjectiveCollector.cs - 契约目标的全局击杀/受伤采集
// ============================================================================
// 由 Utilities/PlayerLifecycleRuntimeHooks.cs 转发官方 Health.OnDead / OnHurt。
// 用**命名方法**而不是 lambda（AGENTS.md 4.6）：lambda 退订不掉。
//
// 【热路径纪律】
//   OnHurt 是全 Mod 最热的事件（日报采集器的注释明示「不允许出现任何分配」）。
//   本文件同样遵守：未武装时第一行就早返，判定只有判空 + bool + 整数比较；
//   武器 family 查询带缓存，绝不在每次击杀时走一遍反射或字符串拼接。
//
// 【近战判定为什么本地复制而不提升为共享工具】
//   ModeG 的 ModeGCombatTelemetry 有同款逻辑，但那是 ModeG 的局内遥测，
//   带着它自己的降级状态机。按 AGENTS.md 4.9「不要因『未来可能复用』提前提升」，
//   这里只复制那 15 行判定，不去动 ModeG。
// ============================================================================

using System;
using System.Collections.Generic;

namespace BossRush
{
    /// <summary>契约目标采集器。静态命名 handler，热路径零分配。</summary>
    internal static class CampaignObjectiveCollector
    {
        #region 武器 family 缓存

        /// <summary>weaponTypeId -> 是否近战。避免每次击杀都查一遍元数据。</summary>
        private static readonly Dictionary<int, bool> _meleeWeaponCache = new Dictionary<int, bool>();

        #endregion

        #region Health 事件（由 PlayerLifecycleRuntimeHooks 转发）

        /// <summary>
        /// 全局死亡事件。只统计玩家自己打死的，避免把 NPC 互殴、环境伤害算进契约。
        /// 热路径：未武装时第一行早返。
        /// </summary>
        internal static void OnGlobalDead(Health target, DamageInfo info)
        {
            if (!CampaignObjectiveTracker.IsArmed) return;
            try
            {
                if (target == null) return;
                if (target.IsMainCharacterHealth) return;

                if (info.fromCharacter == null || !info.fromCharacter.IsMainCharacter) return;

                CharacterMainControl victim = target.TryGetCharacter();
                bool isBoss = victim != null && victim.isBossCharacter;
                bool isMelee = IsMeleeWeapon(info.fromWeaponItemID);
                bool hasBounty = isBoss && HasBountyMark(victim);

                CampaignObjectiveTracker.ReportPlayerKill(isMelee, isBoss, hasBounty);
            }
            catch (Exception)
            {
                // 采集不是关键路径：热事件里绝不抛，也不打日志（会刷屏）
            }
        }

        /// <summary>
        /// 全局受伤事件。只关心「玩家挨打」，用于无伤目标判失败。
        /// 这是全 Mod 最热的事件，实现里不允许出现任何分配。
        /// </summary>
        internal static void OnGlobalHurt(Health target, DamageInfo info)
        {
            if (!CampaignObjectiveTracker.IsArmed) return;
            try
            {
                if (target == null || !target.IsMainCharacterHealth) return;

                ModBehaviour owner = ModBehaviour.Instance;
                if (owner == null) return;

                CampaignObjectiveTracker.ReportPlayerDamaged(owner.GetCampaignCurrentWave());
            }
            catch (Exception)
            {
                // 全 Mod 最热事件：每次挨打都会走，记日志会刷屏且产生分配
            }
        }

        #endregion

        #region 判定

        /// <summary>
        /// 该武器是否近战。判定口径与 ModeGCombatTelemetry 一致：
        /// 物品 tag 里只有 MeleeWeapon/Melee 而没有 Gun 才算近战，两者都有或都没有时不算。
        /// </summary>
        private static bool IsMeleeWeapon(int weaponTypeId)
        {
            if (weaponTypeId <= 0) return false;

            bool cached;
            if (_meleeWeaponCache.TryGetValue(weaponTypeId, out cached)) return cached;

            bool melee = false;
            try
            {
                ItemStatsSystem.ItemMetaData metaData =
                    ItemStatsSystem.ItemAssetsCollection.GetMetaData(weaponTypeId);
                bool gun = false;
                bool meleeTag = false;
                if (metaData.id > 0 && metaData.tags != null)
                {
                    for (int i = 0; i < metaData.tags.Length; i++)
                    {
                        Duckov.Utilities.Tag tag = metaData.tags[i];
                        if (tag == null) continue;
                        if (string.Equals(tag.name, "Gun", StringComparison.Ordinal)) gun = true;
                        else if (string.Equals(tag.name, "MeleeWeapon", StringComparison.Ordinal)
                            || string.Equals(tag.name, "Melee", StringComparison.Ordinal)) meleeTag = true;
                    }
                }
                melee = meleeTag && !gun;
            }
            catch (Exception)
            {
                melee = false;
            }

            _meleeWeaponCache[weaponTypeId] = melee;
            return melee;
        }

        /// <summary>该 Boss 是否带血猎悬赏印记。非 Mode F 时恒为 false。</summary>
        private static bool HasBountyMark(CharacterMainControl victim)
        {
            try
            {
                ModBehaviour owner = ModBehaviour.Instance;
                if (owner == null) return false;
                return owner.HasCampaignBountyMark(victim);
            }
            catch (Exception)
            {
                return false;
            }
        }

        #endregion

        #region 清理

        internal static void ResetStaticCaches()
        {
            _meleeWeaponCache.Clear();
        }

        #endregion
    }
}
