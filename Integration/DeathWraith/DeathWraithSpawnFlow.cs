// ============================================================================
// DeathWraithSystem partial - extracted from DeathWraithSystem.cs
// ============================================================================

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;
using Cysharp.Threading.Tasks;
using Duckov;
using Duckov.Economy;
using Duckov.Scenes;
using Duckov.Utilities;
using ItemStatsSystem;
using ItemStatsSystem.Data;
using ItemStatsSystem.Items;
using Saves;

namespace BossRush
{
    public partial class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        #region 亡魂系统 — 生成

        internal void NotifyOriginalDeadBodySpawnRequested_DeathWraith(DeadBodyManager.DeathInfo info)
        {
            if (!IsDeathWraithSystemEnabled() || info == null || !info.valid)
            {
                return;
            }

            RemovePendingDeadBodySpawnContextByRaidId_DeathWraith(info.raidID);
            pendingDeadBodySpawnContexts.Add(new DeadBodySpawnContext_DeathWraith
            {
                raidID = info.raidID,
                subSceneID = info.subSceneID ?? string.Empty,
                worldPosition = info.worldPosition
            });
        }

        internal void NotifyOriginalDeadBodyLootboxCreated_DeathWraith(
            InteractableLootbox lootbox,
            Item item,
            Vector3 position,
            InteractableLootbox prefab)
        {
            if (!IsDeathWraithSystemEnabled() || lootbox == null)
            {
                return;
            }

            InteractableLootbox tombPrefab = null;
            try
            {
                if (GameplayDataSettings.Prefabs != null)
                {
                    tombPrefab = GameplayDataSettings.Prefabs.LootBoxPrefab_Tomb;
                }
            }
            catch { }

            InteractableLootbox actualPrefab = prefab != null ? prefab : InteractableLootbox.Prefab;
            if (tombPrefab == null || actualPrefab != tombPrefab)
            {
                return;
            }

            DeadBodySpawnContext_DeathWraith context =
                TryConsumePendingDeadBodySpawnContext_DeathWraith(position, GetActiveSubSceneId_DeathWraith());
            if (context == null)
            {
                return;
            }

            TrySpawnStoredDeathWraithForRaid_DeathWraith(context.raidID, lootbox.transform.position);
        }

        internal void NotifyOriginalDeadBodyTouched_DeathWraith(DeadBodyManager.DeathInfo info)
        {
            if (info == null)
            {
                return;
            }

            RemovePendingDeadBodySpawnContextByRaidId_DeathWraith(info.raidID);
            RemoveStoredDeathWraithInfoByRaidId_DeathWraith(info.raidID, "原版遗失物被开启");
        }

        private DeadBodySpawnContext_DeathWraith TryConsumePendingDeadBodySpawnContext_DeathWraith(
            Vector3 worldPosition,
            string subSceneID)
        {
            const float positionTolerance = 0.05f;
            for (int i = 0; i < pendingDeadBodySpawnContexts.Count; i++)
            {
                DeadBodySpawnContext_DeathWraith context = pendingDeadBodySpawnContexts[i];
                if (context == null)
                {
                    continue;
                }

                if (!string.Equals(context.subSceneID ?? string.Empty, subSceneID ?? string.Empty, StringComparison.Ordinal))
                {
                    continue;
                }

                if ((context.worldPosition - worldPosition).sqrMagnitude > positionTolerance * positionTolerance)
                {
                    continue;
                }

                pendingDeadBodySpawnContexts.RemoveAt(i);
                return context;
            }

            return null;
        }

        private void RemovePendingDeadBodySpawnContextByRaidId_DeathWraith(uint raidID)
        {
            for (int i = pendingDeadBodySpawnContexts.Count - 1; i >= 0; i--)
            {
                DeadBodySpawnContext_DeathWraith context = pendingDeadBodySpawnContexts[i];
                if (context != null && context.raidID == raidID)
                {
                    pendingDeadBodySpawnContexts.RemoveAt(i);
                }
            }
        }

        private async void TrySpawnStoredDeathWraithForRaid_DeathWraith(uint raidID, Vector3 spawnPos)
        {
            CharacterMainControl spawnedWraith = null;
            bool registered = false;
            try
            {
                if (!IsDeathWraithSystemEnabled())
                {
                    return;
                }

                if (spawningWraithRaidIds.Contains(raidID) || activeWraithsByRaidId.ContainsKey(raidID))
                {
                    return;
                }

                WraithInfo info = FindStoredDeathWraithInfoByRaidId_DeathWraith(raidID);
                if (info == null || !info.valid)
                {
                    return;
                }

                spawningWraithRaidIds.Add(raidID);
                DevLog("[DeathWraith] 开始生成与原版遗失物绑定的亡魂: raidID=" + raidID);

                CharacterMainControl wraith = null;
                try
                {
                    wraith = await CreateWraithCharacterFromPlayerSnapshot_DeathWraith(info, spawnPos);
                    spawnedWraith = wraith;
                }
                catch (Exception e)
                {
                    DevLog("[DeathWraith] 创建亡魂宿主异常: " + e.Message + "\n" + e.StackTrace);
                    return;
                }

                if (wraith == null)
                {
                    DevLog("[DeathWraith] 角色生成失败");
                    return;
                }

                await UniTask.Yield();

                NormalizeDamageMultiplier(wraith);
                RestoreWraithMaxHealthSnapshot_DeathWraith(wraith, info.playerMaxHealth);
                ApplyBossStatMultiplier(wraith);

                wraith.dropBoxOnDead = false;
                wraith.SetTeam(Teams.scav);
                InitializeWraithAI_DeathWraith(wraith, spawnPos, info);

                WraithTier tier = ClassifyWraithTier_DeathWraith(info.droppedItemsValue, info.playerTotalWealth);
                string displayName = GetWraithDisplayName_DeathWraith(info.playerName, tier);
                string displayNameKey = CreateWraithDisplayNameKey_DeathWraith(displayName);
                ApplyWraithRuntimePreset_DeathWraith(wraith, info, displayNameKey, displayName);
                await PrepareWraithCombatLoadout_DeathWraith(wraith);

                try
                {
                    if (wraith.Health != null)
                    {
                        wraith.Health.showHealthBar = true;
                    }
                }
                catch { }

                ApplyWraithTierStats_DeathWraith(wraith, tier);

                try
                {
                    if (wraith.Health != null)
                    {
                        wraith.Health.SetHealth(wraith.Health.MaxHealth);
                    }
                }
                catch { }

                wraith.gameObject.name = "BossRush_DeathWraith_" + raidID;
                wraith.gameObject.SetActive(true);

                try
                {
                    if (wraith.Health != null)
                    {
                        wraith.Health.RequestHealthBar();
                    }
                }
                catch { }

                RegisterActiveWraith_DeathWraith(raidID, wraith);
                registered = true;
                spawnedWraith = null;

                DevLog("[DeathWraith] 亡魂生成成功: " + displayName + " tier=" + tier + " raidID=" + raidID);
            }
            catch (Exception e)
            {
                DevLog("[DeathWraith] TrySpawnStoredDeathWraithForRaid 异常: " + e.Message + "\n" + e.StackTrace);
            }
            finally
            {
                if (spawnedWraith != null)
                {
                    DestroyWraithInstance_DeathWraith(spawnedWraith, "生成流程异常中断");
                }

                if (!registered)
                {
                    activeWraithsByRaidId.Remove(raidID);
                }
                spawningWraithRaidIds.Remove(raidID);
            }
        }

        /// <summary>
        /// 主角本体由 CharacterCreator + LevelManager.characterModel 创建。
        /// 亡魂应沿用同一条链创建宿主角色，再切换成敌对 AI。
        /// </summary>
        private async UniTask<CharacterMainControl> CreateWraithCharacterFromPlayerSnapshot_DeathWraith(
            WraithInfo info,
            Vector3 spawnPos)
        {
            if (info == null)
            {
                return null;
            }

            LevelManager level = LevelManager.Instance;
            if (level == null || level.CharacterCreator == null)
            {
                DevLog("[DeathWraith] LevelManager.CharacterCreator 不可用，无法创建亡魂");
                return null;
            }

            CharacterModel mainCharacterModelPrefab = GetMainCharacterModelPrefab_DeathWraith(level);
            if (mainCharacterModelPrefab == null)
            {
                DevLog("[DeathWraith] 未找到主角 CharacterModel 预设，无法创建亡魂");
                return null;
            }

            Item characterItem = null;
            try
            {
                if (info.itemTreeData != null)
                {
                    characterItem = await ItemTreeData.InstantiateAsync(info.itemTreeData);
                    if (characterItem != null)
                    {
                        RestoreWraithItemRuntimeStateRecursive_DeathWraith(
                            characterItem,
                            "DeathWraith.CreateCharacter");
                    }
                }

                if (characterItem == null)
                {
                    characterItem = await level.CharacterCreator.LoadOrCreateCharacterItemInstance(
                        GameplayDataSettings.ItemAssets.DefaultCharacterItemTypeID);
                    DevLog("[DeathWraith] 未取到死亡时装备树，使用默认主角物品容器创建亡魂宿主");
                }
            }
            catch (Exception e)
            {
                DevLog("[DeathWraith] 创建亡魂物品树异常: " + e.Message);
                return null;
            }

            if (characterItem == null)
            {
                DevLog("[DeathWraith] 亡魂宿主物品树为空，放弃生成");
                return null;
            }

            CharacterMainControl wraith = null;
            try
            {
                wraith = await level.CharacterCreator.CreateCharacter(
                    characterItem,
                    mainCharacterModelPrefab,
                    spawnPos,
                    Quaternion.identity);
            }
            catch (Exception e)
            {
                DevLog("[DeathWraith] CharacterCreator.CreateCharacter 异常: " + e.Message);
                return null;
            }

            if (wraith == null)
            {
                return null;
            }

            ApplyStoredWraithFaceData_DeathWraith(wraith, info);
            await EnsureStoredBoundMeleeEquipped_DeathWraith(wraith, info);
            return wraith;
        }

        private CharacterModel GetMainCharacterModelPrefab_DeathWraith(LevelManager level)
        {
            if (level == null)
            {
                return null;
            }

            try
            {
                if (LevelManager_CharacterModelField == null)
                {
                    DevLog("[DeathWraith] 反射不到 LevelManager.characterModel");
                    return null;
                }

                CharacterModel modelPrefab = LevelManager_CharacterModelField.GetValue(level) as CharacterModel;
                return modelPrefab;
            }
            catch (Exception e)
            {
                DevLog("[DeathWraith] 获取主角 CharacterModel 预设失败: " + e.Message);
                return null;
            }
        }

        private void ApplyStoredWraithFaceData_DeathWraith(CharacterMainControl wraith, WraithInfo info)
        {
            if (wraith == null || info == null || !info.hasPlayerFaceData)
            {
                return;
            }

            try
            {
                if (wraith.characterModel != null)
                {
                    wraith.characterModel.SetFaceFromData(info.playerFaceData);
                }
            }
            catch (Exception e)
            {
                DevLog("[DeathWraith] 应用亡魂捏脸数据失败: " + e.Message);
            }
        }

        private async UniTask EnsureStoredBoundMeleeEquipped_DeathWraith(
            CharacterMainControl wraith,
            WraithInfo info)
        {
            if (wraith == null || info == null || !info.hasBoundMeleeSnapshot ||
                info.boundMeleeItemTreeData == null)
            {
                return;
            }

            try
            {
                Slot meleeSlot = wraith.MeleeWeaponSlot();
                if (meleeSlot != null && meleeSlot.Content != null &&
                    meleeSlot.Content.TypeID == info.boundMeleeTypeId)
                {
                    return;
                }

                Item meleeItem = await ItemTreeData.InstantiateAsync(info.boundMeleeItemTreeData);
                if (meleeItem == null)
                {
                    DevLog("[DeathWraith] [WARNING] 绑定近战实例化失败");
                    return;
                }

                RestoreWraithItemRuntimeStateRecursive_DeathWraith(
                    meleeItem,
                            "DeathWraith.BoundMelee");

                bool equipped = false;
                Item pluggedOut = null;
                try
                {
                    if (meleeSlot != null)
                    {
                        meleeSlot.Plug(meleeItem, out pluggedOut);
                        equipped = meleeSlot.Content == meleeItem;
                    }
                }
                catch { }

                if (!equipped)
                {
                    try
                    {
                        equipped = wraith.CharacterItem != null &&
                            wraith.CharacterItem.TryPlug(meleeItem, true, null, 0);
                    }
                    catch { }
                }

                if (pluggedOut != null && pluggedOut != meleeItem)
                {
                    Inventory inventory = wraith.CharacterItem != null ? wraith.CharacterItem.Inventory : null;
                    if (!TryAddItemToInventory_DeathWraith(inventory, pluggedOut))
                    {
                        DestroyDetachedItem_DeathWraith(pluggedOut, "绑定近战替换后的旧物品无法回收");
                    }
                }

                if (!equipped)
                {
                    Inventory inventory = wraith.CharacterItem != null ? wraith.CharacterItem.Inventory : null;
                    if (!TryAddItemToInventory_DeathWraith(inventory, meleeItem))
                    {
                        DestroyDetachedItem_DeathWraith(meleeItem, "绑定近战回填失败");
                        return;
                    }
                }

                DevLog("[DeathWraith] 已回填绑定近战武器: "
                    + (string.IsNullOrEmpty(info.boundMeleeDisplayName) ? "<unknown>" : info.boundMeleeDisplayName)
                    + " (TypeID=" + info.boundMeleeTypeId + ")");
            }
            catch (Exception e)
            {
                DevLog("[DeathWraith] 回填绑定近战武器失败: " + e.Message);
            }
        }


        #endregion
    }
}
