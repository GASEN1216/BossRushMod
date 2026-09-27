// ============================================================================
// Utilities.cs - 工具方法
// ============================================================================
// 模块说明：
//   提供 BossRush 模组的通用工具方法，包括：
//   - 加油站创建和管理
//   - 其他辅助功能
//   
// 加油站：
//   在无间炼狱模式下提供弹药购买功能，包含常见弹药类型。
// ============================================================================

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Duckov.Economy;
using ItemStatsSystem;

namespace BossRush
{
    /// <summary>
    /// 工具方法模块
    /// </summary>
    public partial class ModBehaviour
    {
        // ========== 性能优化：共享 WaitForSeconds 缓存（供所有 partial class 使用） ==========

        private readonly ZombieSpawnSanitizer zombieSpawnSanitizer =
            new ZombieSpawnSanitizer(ZombieModeRuntimeModule.ShouldKeepBossRushZombieSelfDestructionSkill);

        internal void SanitizeBossRushZombieSpawn(CharacterMainControl character, string spawnOwner)
        { zombieSpawnSanitizer.SanitizeBossRushZombieSpawn(character, spawnOwner); }

        internal static string TryGetSteamPersonaName() { return SteamPlatformInfo.TryGetSteamPersonaName(); }

        private static WaitForSeconds sharedWait01s { get { return BossRushWaitCache.sharedWait01s; } }
        private static WaitForSeconds sharedWait05s { get { return BossRushWaitCache.sharedWait05s; } }
        private static WaitForSeconds sharedWait1s { get { return BossRushWaitCache.sharedWait1s; } }

        /// <summary>
        /// 确保加油站已创建
        /// <para>在无间炼狱模式下提供弹药购买功能</para>
        /// </summary>
        private void EnsureAmmoShop_Utilities()
        { bossRushIntegrationRuntime.EnsureAmmoShop_Utilities(); }

        // ============================================================================
        // Boss 数值倍率统一方法
        // ============================================================================

        /// <summary>
        /// 应用全局 Boss 数值倍率（统一方法，供所有模式复用）
        /// <para>影响：生命值、枪械伤害、近战伤害、反应速度</para>
        /// </summary>
        /// <param name="character">目标角色</param>
        /// <param name="multiplier">倍率值（默认从 config.bossStatMultiplier 读取）</param>
        private void ApplyBossStatMultiplier(CharacterMainControl character, float? multiplier = null)
        { BossStatScaling.ApplyBossStatMultiplier(character, multiplier, config != null ? config.bossStatMultiplier : 1f); }
    }
}
