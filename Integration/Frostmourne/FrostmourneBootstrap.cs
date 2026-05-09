// ============================================================================
// FrostmourneBootstrap.cs - 霜之哀伤系统启动集成
// ============================================================================
// 模块说明：
//   将霜之哀伤系统集成到 ModBehaviour 中
//   负责初始化、场景切换处理和清理
// ============================================================================

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using ItemStatsSystem;

namespace BossRush
{
    /// <summary>
    /// 霜之哀伤系统启动模块 - 使用 partial class 扩展 ModBehaviour
    /// </summary>
    public partial class ModBehaviour
    {
        // ========== 初始化 ==========

        /// <summary>
        /// 初始化霜之哀伤系统（在 Start_Integration 中调用）
        /// </summary>
        private void InitializeFrostmourneSystem()
        {
            try
            {
                // 创建右键能力管理器（MonoBehaviour 单例）
                if (FrostmourneAbilityManager.Instance == null)
                {
                    GameObject mgrObj = new GameObject("FrostmourneAbilityManager");
                    DontDestroyOnLoad(mgrObj);
                    mgrObj.AddComponent<FrostmourneAbilityManager>();
                    DevLog("[Frostmourne] 右键能力管理器已创建");
                }

                DevLog("[Frostmourne] 系统初始化完成");
            }
            catch (Exception e)
            {
                DevLog("[Frostmourne] 系统初始化失败: " + e.Message);
            }
        }

        // ========== 场景管理 ==========

        /// <summary>
        /// 场景加载后设置霜之哀伤系统
        /// </summary>
        private void SetupFrostmourneForScene(Scene scene)
        {
            try
            {
                // 通知右键能力管理器场景已切换
                var abilityMgr = FrostmourneAbilityManager.Instance;
                if (abilityMgr != null)
                {
                    abilityMgr.OnSceneChanged();
                }

                // 延迟注册/重新绑定能力到玩家角色
                if (IsGameplaySceneName(scene.name))
                {
                    StartCoroutine(DelayedSetupFrostmourneAbility());
                }
            }
            catch (Exception e)
            {
                DevLog("[Frostmourne] 场景设置失败: " + e.Message);
            }
        }

        /// <summary>
        /// 延迟设置霜之哀伤能力
        /// </summary>
        private IEnumerator DelayedSetupFrostmourneAbility()
        {
            // 等待玩家角色可用
            float waitTime = 0f;
            while (CharacterMainControl.Main == null && waitTime < 15f)
            {
                yield return new WaitForSeconds(0.5f);
                waitTime += 0.5f;
            }

            CharacterMainControl player = CharacterMainControl.Main;
            if (player == null)
            {
                DevLog("[Frostmourne] 当前场景无玩家角色，跳过能力注册");
                yield break;
            }

            var mgr = FrostmourneAbilityManager.Instance;
            if (mgr == null)
            {
                DevLog("[Frostmourne] [WARNING] 右键能力管理器不存在");
                yield break;
            }

            if (!mgr.IsAbilityEnabled)
            {
                mgr.RegisterAbility(player);
                DevLog("[Frostmourne] 右键能力已注册到玩家");
            }
            else
            {
                if (mgr.TargetCharacter != player)
                {
                    mgr.RebindToCharacter(player);
                    DevLog("[Frostmourne] 右键能力已重新绑定到新玩家实例");
                }
                else
                {
                    DevLog("[Frostmourne] 右键能力已由 OnSceneChanged 重建，跳过重复绑定");
                }
            }
        }

        // ========== 清理 ==========

        /// <summary>
        /// 清理霜之哀伤系统
        /// </summary>
        private void CleanupFrostmourneSystem()
        {
            try
            {
                // 清理右键能力管理器
                FrostmourneAbilityManager.CleanupStatic();

                // 清理所有召唤的僵尸
                FrostmourneAction.CleanupAllSummonedZombies();

                // 清理 Blue Boss 额外掉落订阅
                FrostmourneBlueBossDropHandler.Cleanup();

                DevLog("[Frostmourne] 系统清理完成");
            }
            catch (Exception e)
            {
                DevLog("[Frostmourne] 系统清理失败: " + e.Message);
            }
        }
    }

    internal static class FrostmourneBlueBossDropHandler
    {
        private const string BlueBossNameKey = "Cname_Boss_Blue";
        private const float ExtraDropChance = 0.5f;
        private static readonly HashSet<CharacterMainControl> pendingBossRushLootboxDrops
            = new HashSet<CharacterMainControl>();

        internal static void Cleanup()
        {
            pendingBossRushLootboxDrops.Clear();
        }

        internal static void TryHandleBlueBossDeath(CharacterMainControl boss)
        {
            try
            {
                PruneInvalidHooks();

                if (!IsBlueBoss(boss))
                {
                    return;
                }

                if (UnityEngine.Random.value >= ExtraDropChance)
                {
                    return;
                }

                if (ShouldDeferToBossRushLootbox(boss))
                {
                    pendingBossRushLootboxDrops.Add(boss);
                    return;
                }

                Item bossCharacterItem = boss.CharacterItem;
                Inventory inventory = bossCharacterItem != null ? bossCharacterItem.Inventory : null;
                if (inventory == null)
                {
                    return;
                }

                TryAddFrostmourneToInventory(inventory, null);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[Frostmourne] Blue Boss 额外掉落处理失败: " + e.Message);
            }
        }

        private static bool IsBlueBoss(CharacterMainControl character)
        {
            if (character == null || character.characterPreset == null)
            {
                return false;
            }

            return string.Equals(character.characterPreset.nameKey, BlueBossNameKey, StringComparison.Ordinal);
        }

        internal static void TryConsumePendingBossRushLootboxDrop(CharacterMainControl boss, Inventory inventory)
        {
            PruneInvalidHooks();

            if (boss == null || inventory == null)
            {
                return;
            }

            if (!pendingBossRushLootboxDrops.Remove(boss))
            {
                return;
            }

            TryAddFrostmourneToInventory(inventory, "[Frostmourne] Blue Boss BossRush 奖励箱额外掉落");
        }

        internal static void CancelPendingBossRushLootboxDrop(CharacterMainControl boss)
        {
            if (object.ReferenceEquals(boss, null))
            {
                return;
            }

            pendingBossRushLootboxDrops.Remove(boss);
        }

        private static void EnsureExtraInventoryCapacity(Inventory inventory)
        {
            if (inventory == null)
            {
                return;
            }

            try
            {
                int contentCount = inventory.Content != null ? inventory.Content.Count : 0;
                int requiredCapacity = Mathf.Max(inventory.Capacity, contentCount + 1);
                if (requiredCapacity > inventory.Capacity)
                {
                    inventory.SetCapacity(requiredCapacity);
                }
            }
            catch
            {
            }
        }

        private static bool ShouldDeferToBossRushLootbox(CharacterMainControl boss)
        {
            ModBehaviour instance = ModBehaviour.Instance;
            return instance != null && instance.ShouldDeferBlueBossExtraDropToBossRushLootbox(boss);
        }

        private static bool TryAddFrostmourneToInventory(Inventory inventory, string logPrefix)
        {
            if (inventory == null)
            {
                return false;
            }

            EnsureExtraInventoryCapacity(inventory);

            Item rewardItem = null;
            try
            {
                rewardItem = ItemAssetsCollection.InstantiateSync(FrostmourneIds.WeaponTypeId);
                if (rewardItem == null)
                {
                    if (!string.IsNullOrEmpty(logPrefix))
                    {
                        ModBehaviour.DevLog(logPrefix + "失败：无法实例化霜之哀伤");
                    }
                    return false;
                }

                if (!inventory.AddAndMerge(rewardItem, 0))
                {
                    if (!string.IsNullOrEmpty(logPrefix))
                    {
                        ModBehaviour.DevLog(logPrefix + "失败：无法加入库存");
                    }
                    return false;
                }

                rewardItem = null;
                return true;
            }
            catch (Exception e)
            {
                if (!string.IsNullOrEmpty(logPrefix))
                {
                    ModBehaviour.DevLog(logPrefix + "失败: " + e.Message);
                }
                return false;
            }
            finally
            {
                try
                {
                    if (rewardItem != null)
                    {
                        rewardItem.DestroyTree();
                    }
                }
                catch
                {
                }
            }
        }

        private static void PruneInvalidHooks()
        {
            if (pendingBossRushLootboxDrops.Count > 0)
            {
                var pendingKeys = new System.Collections.Generic.List<CharacterMainControl>(pendingBossRushLootboxDrops);
                for (int i = 0; i < pendingKeys.Count; i++)
                {
                    CharacterMainControl pendingKey = pendingKeys[i];
                    if (pendingKey == null)
                    {
                        pendingBossRushLootboxDrops.Remove(pendingKey);
                    }
                }
            }
        }
    }
}
