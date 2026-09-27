// Mode E merchant runtime: ModeEPetSpawner.cs
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
using Cysharp.Threading.Tasks;
using Duckov.Economy;
using Duckov.Economy.UI;
using Duckov.ItemUsage;
using Duckov.Scenes;
using Duckov.UI;
using Duckov.Utilities;
using ItemStatsSystem;
using ItemStatsSystem.Data;
using TMPro;
using HarmonyLib;
using SodaCraft.StringUtilities;

namespace BossRush
{
    // ========================================================================
    // ModeEPetSpawner — 召唤煤球辅助类
    // ========================================================================

    /// <summary>
    /// Mode E 召唤煤球辅助类。
    /// 提供静态方法用于生成煤球宠物NPC。
    /// </summary>
    public static class ModeEPetSpawner
    {
        /// <summary>缓存的煤球预设（避免重复查找）</summary>
        private static CharacterRandomPreset cachedCoalballPreset = null;

        /// <summary>
        /// 异步生成煤球宠物（供 Harmony patch 调用）
        /// </summary>
        public static void SpawnPet()
        {
            var inst = ModBehaviour.Instance;
            int modeFSessionToken = inst != null ? inst.CurrentModeFSessionToken : 0;
            int modeESessionToken = inst != null ? inst.CurrentModeESessionToken : 0;
            int relatedScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex;
            SpawnPetAsync(modeFSessionToken, relatedScene, modeESessionToken, relatedScene).Forget();
        }

        /// <summary>
        /// 清理缓存（Mode E 结束时调用）
        /// </summary>
        public static void ClearCache()
        {
            cachedCoalballPreset = null;
        }

        /// <summary>
        /// 重置宠物NPC的雇佣交互点状态，防止位置哈希导致的状态复用
        /// 原版游戏使用位置哈希作为 requireItemUsed 的存储键，
        /// 相同位置生成的NPC会共享状态，导致第一次雇佣后后续不再需要消耗物品
        /// </summary>
        private static void ResetPetHireInteractable(GameObject petGo)
        {
            try
            {
                if (petGo == null) return;

                // 查找宠物NPC上的所有 InteractableBase 组件
                var interactables = petGo.GetComponentsInChildren<InteractableBase>(true);
                if (interactables == null || interactables.Length == 0)
                {
                    ModBehaviour.DevLog("[ModeE] 煤球NPC上未找到 InteractableBase");
                    return;
                }

                foreach (var interact in interactables)
                {
                    if (interact == null) continue;

                    // 通过反射重置 requireItem 和 requireItemUsed 状态
                    try
                    {
                        // 获取 requireItem 字段
                        var requireItemField = typeof(InteractableBase).GetField("requireItem",
                            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                        // 获取 requireItemUsed 字段
                        var requireItemUsedField = typeof(InteractableBase).GetField("requireItemUsed",
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        // 获取 requireItemId 字段（用于判断是否是雇佣交互）
                        var requireItemIdField = typeof(InteractableBase).GetField("requireItemId",
                            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

                        if (requireItemField != null && requireItemIdField != null)
                        {
                            int itemId = (int)requireItemIdField.GetValue(interact);
                            // 只重置需要 ID=388 物品的交互点（雇佣交互）
                            if (itemId == 388)
                            {
                                requireItemField.SetValue(interact, true);
                                if (requireItemUsedField != null)
                                {
                                    requireItemUsedField.SetValue(interact, false);
                                }
                                ModBehaviour.DevLog("[ModeE] 已重置煤球雇佣交互点状态 (requireItemId=388)");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        ModBehaviour.DevLog("[ModeE] [WARNING] 重置交互点状态失败: " + ex.Message);
                    }
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeE] [ERROR] ResetPetHireInteractable 失败: " + e.Message);
            }
        }

        /// <summary>
        /// 异步生成煤球NPC
        /// </summary>
        private static async UniTaskVoid SpawnPetAsync(
            int modeFSessionToken,
            int modeFRelatedScene,
            int modeESessionToken,
            int modeESessionRelatedScene)
        {
            try
            {
                // 获取玩家位置
                CharacterMainControl player = CharacterMainControl.Main;
                if (player == null)
                {
                    ModBehaviour.DevLog("[ModeE] [WARNING] 召唤煤球：玩家为空");
                    return;
                }

                // 查找煤球预设（优先使用缓存）
                CharacterRandomPreset coalballPreset = cachedCoalballPreset;

                if (coalballPreset == null)
                {
                    // 优先从 ModBehaviour 的缓存字典查找
                    var inst = ModBehaviour.Instance;
                    if (inst != null)
                    {
                        // 通过反射获取 cachedCharacterPresets（如果可访问）
                        // 回退到 FindObjectsOfTypeAll
                        try
                        {
                            var allPresets = ObjectCache.GetCharacterPresets();
                            foreach (var preset in allPresets)
                            {
                                if (preset == null) continue;
                                try
                                {
                                    string nameKey = preset.nameKey;
                                    if (!string.IsNullOrEmpty(nameKey) && nameKey.Contains("SnowPMC"))
                                    {
                                        coalballPreset = preset;
                                        cachedCoalballPreset = preset; // 缓存以供后续使用
                                        ModBehaviour.DevLog("[ModeE] 找到煤球预设: " + nameKey);
                                        break;
                                    }
                                }
                                catch { }
                            }
                        }
                        catch { }
                    }
                }

                if (coalballPreset == null)
                {
                    ModBehaviour.DevLog("[ModeE] [WARNING] 未找到煤球预设 (Character_SnowPMC)，无法召唤");
                    NotificationText.Push(L10n.T("未找到煤球预设", "Coalball preset not found"));
                    return;
                }

                // 在玩家前方生成煤球
                Vector3 spawnPos = player.transform.position + player.transform.forward * 1.5f;
                Vector3 dir = -player.transform.forward;
                var coalballCharacter = await coalballPreset.CreateCharacterAsync(spawnPos, dir, modeFSessionToken > 0 ? modeFRelatedScene : modeESessionRelatedScene, null, false);
                if (coalballCharacter == null)
                {
                    ModBehaviour.DevLog("[ModeE] [WARNING] 煤球生成失败");
                    return;
                }

                // 设置煤球为玩家阵营
                var inst2 = ModBehaviour.Instance;
                if (inst2 == null ||
                    !inst2.IsModeEOrModeFSpawnSessionStillValid(
                        modeFSessionToken,
                        modeFRelatedScene,
                        modeESessionToken,
                        modeESessionRelatedScene))
                {
                    try
                    {
                        if (coalballCharacter.gameObject != null)
                        {
                            UnityEngine.Object.Destroy(coalballCharacter.gameObject);
                        }
                    }
                    catch { }

                    ModBehaviour.DevLog("[ModeE] 煤球生成完成时模式已结束或场景已切换，已放弃该实例");
                    return;
                }

                if (inst2 != null)
                {
                    coalballCharacter.SetTeam(inst2.ModeEPlayerFaction);
                }
                else
                {
                    coalballCharacter.SetTeam(Teams.player);
                }

                // [修复] 重置煤球NPC的雇佣交互点状态，防止位置哈希导致的状态复用
                // 原版游戏使用位置哈希作为 requireItemUsed 的存储键，
                // 相同位置生成的NPC会共享状态，导致第一次雇佣后后续不再需要消耗物品
                ResetPetHireInteractable(coalballCharacter.gameObject);

                ModBehaviour.DevLog("[ModeE] 煤球召唤成功");
                NotificationText.Push(L10n.T("煤球已召唤！", "Coalball summoned!"));
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeE] [ERROR] SpawnPetAsync 失败: " + e.Message);
            }
        }
    }
}
