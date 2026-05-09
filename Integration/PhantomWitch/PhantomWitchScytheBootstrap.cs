// ============================================================================
// PhantomWitchScytheBootstrap.cs - 幽灵女巫大镰右键技能系统启动集成
// ============================================================================
// 模块说明：
//   将幽灵女巫大镰「诅咒领域」系统集成到 ModBehaviour 中。
//   负责右键能力管理器的初始化、场景切换后的重绑定、清理。
//
//   与 FrostmourneBootstrap 保持一致的生命周期范式。
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
    /// 幽灵女巫大镰系统启动模块 - 使用 partial class 扩展 ModBehaviour
    /// </summary>
    public partial class ModBehaviour
    {
        private static bool phantomWitchScytheSharedAssetReferenceHeld = false;

        // ========== 初始化 ==========

        /// <summary>
        /// 初始化幽灵女巫大镰系统（在 Start_Integration 中调用）
        /// </summary>
        private void InitializePhantomWitchScytheSystem()
        {
            try
            {
                if (!phantomWitchScytheSharedAssetReferenceHeld)
                {
                    // 噬魂挽歌玩家侧挥击/诅咒特效与 Boss 共用同一批缓存资源，
                    // 不能只依赖 Boss 生命周期持有引用。
                    PhantomWitchAssetManager.AddReference();
                    phantomWitchScytheSharedAssetReferenceHeld = true;
                }

                EnsurePhantomWitchScytheSharedAssetReference();

                if (PhantomWitchScytheAbilityManager.Instance == null)
                {
                    GameObject mgrObj = new GameObject("PhantomWitchScytheAbilityManager");
                    DontDestroyOnLoad(mgrObj);
                    mgrObj.AddComponent<PhantomWitchScytheAbilityManager>();
                    DevLog("[PhantomWitchScythe] 右键能力管理器已创建");
                }

                PhantomWitchCurseSweatVfx.RegisterGlobalHook();
                DevLog("[PhantomWitchScythe] 系统初始化完成");
            }
            catch (Exception e)
            {
                DevLog("[PhantomWitchScythe] 系统初始化失败: " + e.Message);
            }
        }

        // ========== 场景管理 ==========

        /// <summary>
        /// 场景加载后设置幽灵女巫大镰系统
        /// </summary>
        private void SetupPhantomWitchScytheForScene(Scene scene)
        {
            try
            {
                EnsurePhantomWitchScytheSharedAssetReference();

                var abilityMgr = PhantomWitchScytheAbilityManager.Instance;
                if (abilityMgr != null)
                {
                    abilityMgr.OnSceneChanged();
                }

                if (IsGameplaySceneName(scene.name))
                {
                    StartCoroutine(DelayedSetupPhantomWitchScytheAbility());
                }
            }
            catch (Exception e)
            {
                DevLog("[PhantomWitchScythe] 场景设置失败: " + e.Message);
            }
        }

        /// <summary>
        /// 延迟把右键能力重新绑定到玩家角色
        /// </summary>
        private IEnumerator DelayedSetupPhantomWitchScytheAbility()
        {
            float waitTime = 0f;
            while (CharacterMainControl.Main == null && waitTime < 15f)
            {
                yield return new WaitForSeconds(0.5f);
                waitTime += 0.5f;
            }

            CharacterMainControl player = CharacterMainControl.Main;
            if (player == null)
            {
                DevLog("[PhantomWitchScythe] 当前场景无玩家角色，跳过能力注册");
                yield break;
            }

            PhantomWitchScytheWeaponConfig.RefreshExistingCurseBindings();

            var mgr = PhantomWitchScytheAbilityManager.Instance;
            if (mgr == null)
            {
                DevLog("[PhantomWitchScythe] [WARNING] 右键能力管理器不存在");
                yield break;
            }

            if (!mgr.IsAbilityEnabled)
            {
                mgr.RegisterAbility(player);
                DevLog("[PhantomWitchScythe] 右键能力已注册到玩家");
            }
            else
            {
                if (mgr.TargetCharacter != player)
                {
                    mgr.RebindToCharacter(player);
                    DevLog("[PhantomWitchScythe] 右键能力已重新绑定到新玩家实例");
                }
                else
                {
                    DevLog("[PhantomWitchScythe] 右键能力已由 OnSceneChanged 重建，跳过重复绑定");
                }
            }
        }

        // ========== 清理 ==========

        /// <summary>
        /// 清理幽灵女巫大镰系统
        /// </summary>
        private void CleanupPhantomWitchScytheSystem()
        {
            try
            {
                PhantomWitchCurseSweatVfx.UnregisterGlobalHook();
                PhantomWitchScytheAbilityManager.CleanupStatic();
                PhantomWitchScytheBossDropHandler.Cleanup();
                PhantomWitchCurseRealmVisual.ClearCache();
                if (phantomWitchScytheSharedAssetReferenceHeld)
                {
                    PhantomWitchAssetManager.ClearCache();
                    phantomWitchScytheSharedAssetReferenceHeld = false;
                }
                DevLog("[PhantomWitchScythe] 系统清理完成");
            }
            catch (Exception e)
            {
                DevLog("[PhantomWitchScythe] 系统清理失败: " + e.Message);
            }
        }

        private void EnsurePhantomWitchScytheSharedAssetReference()
        {
            if (phantomWitchScytheSharedAssetReferenceHeld && PhantomWitchAssetManager.HasActiveReferences)
            {
                return;
            }

            // 场景切换时 ClearPhantomWitchStaticCache() 会先 ForceCleanup Boss 侧缓存；
            // 大镰系统本体仍会保留，因此这里要在重绑路径里把共享引用补回来。
            PhantomWitchAssetManager.AddReference();
            phantomWitchScytheSharedAssetReferenceHeld = true;
        }
    }

    internal static class PhantomWitchScytheBossDropHandler
    {
        private const float ExtraDropChance = 0.5f;
        private static readonly HashSet<CharacterMainControl> pendingBossRushLootboxDrops
            = new HashSet<CharacterMainControl>();

        internal static void Cleanup()
        {
            pendingBossRushLootboxDrops.Clear();
        }

        internal static void TryHandlePhantomWitchDeath(CharacterMainControl boss)
        {
            try
            {
                PruneInvalidHooks();

                if (!IsPhantomWitchBoss(boss))
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

                TryAddScytheToInventory(inventory, null);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PhantomWitchScythe] 幽灵女巫额外掉落处理失败: " + e.Message);
            }
        }

        private static bool IsPhantomWitchBoss(CharacterMainControl character)
        {
            if (character == null || character.characterPreset == null)
            {
                return false;
            }

            return string.Equals(
                character.characterPreset.nameKey,
                PhantomWitchConfig.BossNameKey,
                StringComparison.Ordinal);
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

            TryAddScytheToInventory(inventory, "[PhantomWitchScythe] 幽灵女巫 BossRush 奖励箱额外掉落");
        }

        internal static void CancelPendingBossRushLootboxDrop(CharacterMainControl boss)
        {
            if (object.ReferenceEquals(boss, null))
            {
                return;
            }

            pendingBossRushLootboxDrops.Remove(boss);
        }

        private static bool ShouldDeferToBossRushLootbox(CharacterMainControl boss)
        {
            ModBehaviour instance = ModBehaviour.Instance;
            return instance != null && instance.ShouldDeferBlueBossExtraDropToBossRushLootbox(boss);
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

        private static bool EnsureScytheRewardPrefabLoaded()
        {
            try
            {
                if (ItemAssetsCollection.GetPrefab(PhantomWitchScytheIds.WeaponTypeId) != null)
                {
                    return true;
                }

                EquipmentFactory.LoadBundle("phantom_scythe");

                if (ItemAssetsCollection.GetPrefab(PhantomWitchScytheIds.WeaponTypeId) != null)
                {
                    return true;
                }

                ModBehaviour.DevLog("[PhantomWitchScythe] [WARNING] phantom_scythe bundle 加载后仍未找到噬魂挽歌 prefab (TypeID=" + PhantomWitchScytheIds.WeaponTypeId + ")");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PhantomWitchScythe] [WARNING] 兜底加载噬魂挽歌奖励 prefab 失败: " + e.Message);
            }

            return false;
        }

        private static bool TryAddScytheToInventory(Inventory inventory, string logPrefix)
        {
            if (inventory == null)
            {
                return false;
            }

            EnsureExtraInventoryCapacity(inventory);

            if (!EnsureScytheRewardPrefabLoaded())
            {
                return false;
            }

            Item rewardItem = null;
            try
            {
                rewardItem = ItemAssetsCollection.InstantiateSync(PhantomWitchScytheIds.WeaponTypeId);
                if (rewardItem == null)
                {
                    if (!string.IsNullOrEmpty(logPrefix))
                    {
                        ModBehaviour.DevLog(logPrefix + "失败：无法实例化噬魂挽歌");
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
            if (pendingBossRushLootboxDrops.Count <= 0)
            {
                return;
            }

            List<CharacterMainControl> pendingKeys = new List<CharacterMainControl>(pendingBossRushLootboxDrops);
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
