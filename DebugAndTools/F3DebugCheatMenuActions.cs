// ============================================================================
// F3DebugCheatMenu partial - extracted from F3DebugCheatMenu.cs
// ============================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using Cysharp.Threading.Tasks;
using Duckov.Economy;
using Duckov.Scenes;
using Duckov.UI;
using ItemStatsSystem;
using ItemStatsSystem.Stats;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace BossRush
{
    public partial class ModBehaviour
    {
        private void HealPlayerToFull()
        {
            CharacterMainControl player;
            if (!TryGetMainCharacter(out player) || player.Health == null)
            {
                SetF3DebugCheatStatus(L10n.T("玩家未就绪，无法满血", "Player not ready. Cannot heal"), true);
                return;
            }

            player.Health.SetHealth(player.Health.MaxHealth);
            SetF3DebugCheatStatus(L10n.T("已恢复至满血", "Healed to full"), false);
        }

        private void TeleportToCurrentSceneDefaultPoint()
        {
            CharacterMainControl player;
            if (!TryGetMainCharacter(out player))
            {
                SetF3DebugCheatStatus(L10n.T("未找到玩家，无法传送", "Player not found. Cannot teleport"), true);
                return;
            }

            Vector3 targetPosition = GetCurrentSceneDefaultPosition();
            try
            {
                player.SetPosition(targetPosition);
            }
            catch
            {
                player.transform.position = targetPosition;
            }

            SetF3DebugCheatStatus(L10n.T("已传送到当前场景默认点", "Teleported to the current scene default point"), false);
        }

        private async void TeleportToBossRushStartPointFromF3()
        {
            string currentSceneName = SceneManager.GetActiveScene().name;
            bool alreadyInBossRushScene = currentSceneName == BossRushArenaSceneName || currentSceneName == BossRushArenaSceneID;
            if (!alreadyInBossRushScene)
            {
                if (SceneLoader.Instance == null)
                {
                    SetF3DebugCheatStatus(L10n.T("SceneLoader 未就绪，无法前往 BossRush 起始点", "SceneLoader not ready. Cannot go to the BossRush start point"), true);
                    return;
                }

                try
                {
                    HideF3DebugCheatMenu();
                    ShowMessage(L10n.T("正在前往 BossRush 起始点...", "Traveling to the BossRush start point..."));
                    await SceneLoader.Instance.LoadScene(
                        BossRushArenaSceneID,
                        null,
                        false,
                        false,
                        true,
                        false,
                        default(MultiSceneLocation),
                        true,
                        false
                    );
                    ShowMessage(L10n.T("已进入 BossRush 场地", "Entered the BossRush arena"));
                }
                catch (Exception e)
                {
                    DevLog("[BossRush] 前往 BossRush 起始点失败: " + e.Message + "\n" + e.StackTrace);
                    SetF3DebugCheatStatus(L10n.T("前往 BossRush 起始点失败", "Failed to go to the BossRush start point"), true);
                }
                return;
            }

            CharacterMainControl player;
            if (!TryGetMainCharacter(out player))
            {
                SetF3DebugCheatStatus(L10n.T("未找到玩家，无法传送到 BossRush 起始点", "Player not found. Cannot teleport to the BossRush start point"), true);
                return;
            }

            Vector3 targetPosition = GetDefaultPositionForScene(BossRushArenaSceneName);
            if (targetPosition == Vector3.zero)
            {
                targetPosition = GetCurrentSceneDefaultPosition();
            }
            try
            {
                player.SetPosition(targetPosition);
            }
            catch
            {
                player.transform.position = targetPosition;
            }

            SetF3DebugCheatStatus(L10n.T("已传送到 BossRush 起始点", "Teleported to the BossRush start point"), false);
        }

        private async void TeleportPlayerHomeToBaseScene()
        {
            if (SceneLoader.Instance == null)
            {
                SetF3DebugCheatStatus(L10n.T("SceneLoader 未就绪，无法回基地", "SceneLoader not ready. Cannot return home"), true);
                return;
            }

            try
            {
                HideF3DebugCheatMenu();
                ShowMessage(L10n.T("正在返回基地...", "Returning to base..."));
                await SceneLoader.Instance.LoadScene(
                    BaseSceneName,
                    null,
                    false,
                    false,
                    true,
                    false,
                    default(MultiSceneLocation),
                    true,
                    false
                );
                ShowMessage(L10n.T("已返回基地", "Returned to base"));
            }
            catch (Exception e)
            {
                DevLog("[BossRush] 回基地失败: " + e.Message + "\n" + e.StackTrace);
                SetF3DebugCheatStatus(L10n.T("返回基地失败", "Failed to return to base"), true);
            }
        }

        private void SpawnItemFromF3Inputs()
        {
            int itemId;
            if (!TryReadPositiveInt(f3ItemIdInputField, out itemId))
            {
                SetF3DebugCheatStatus(L10n.T("请输入有效的物品 ID", "Please enter a valid item ID"), true);
                return;
            }

            int count;
            if (!TryReadPositiveInt(f3ItemCountInputField, out count))
            {
                count = 1;
            }

            int successCount = 0;
            try
            {
                for (int i = 0; i < count; i++)
                {
                    Item item = ItemAssetsCollection.InstantiateSync(itemId);
                    if (item == null)
                    {
                        break;
                    }

                    ItemUtilities.SendToPlayer(item);
                    successCount++;
                }

                if (successCount <= 0)
                {
                    SetF3DebugCheatStatus(L10n.T("物品创建失败或 ID 不存在", "Item spawn failed or ID does not exist"), true);
                    return;
                }

                SetF3DebugCheatStatus(L10n.T("已发放物品 x", "Spawned item x") + successCount, false);
            }
            catch (Exception e)
            {
                DevLog("[BossRush] F3 发放物品失败: " + e.Message);
                SetF3DebugCheatStatus(L10n.T("发放物品失败", "Failed to spawn item"), true);
            }
        }

        private void SpawnQuickTestItem(int itemId, int count, string successMessage)
        {
            if (itemId <= 0 || count <= 0)
            {
                SetF3DebugCheatStatus(L10n.T("快捷发物品失败：参数无效", "Quick spawn failed: invalid parameters"), true);
                return;
            }

            int successCount = 0;
            try
            {
                for (int i = 0; i < count; i++)
                {
                    Item item = ItemAssetsCollection.InstantiateSync(itemId);
                    if (item == null)
                    {
                        break;
                    }

                    ItemUtilities.SendToPlayer(item);
                    successCount++;
                }

                if (successCount <= 0)
                {
                    SetF3DebugCheatStatus(L10n.T("快捷发物品失败", "Quick spawn failed"), true);
                    return;
                }

                SetF3DebugCheatStatus(successMessage + " x" + successCount, false);
            }
            catch (Exception e)
            {
                DevLog("[BossRush] F3 快捷发物品失败: typeId=" + itemId + ", error=" + e.Message);
                SetF3DebugCheatStatus(L10n.T("快捷发物品失败", "Quick spawn failed"), true);
            }
        }

        private bool TryReadPositiveInt(InputField field, out int value)
        {
            value = 0;
            if (field == null || string.IsNullOrWhiteSpace(field.text))
            {
                return false;
            }

            return int.TryParse(field.text.Trim(), out value) && value > 0;
        }

        private void AddMoneyFromInputField()
        {
            if (f3MoneyInputField == null)
            {
                SetF3DebugCheatStatus(L10n.T("金额输入框未就绪", "Money input field not ready"), true);
                return;
            }

            long amount;
            if (!long.TryParse(f3MoneyInputField.text.Trim(), out amount) || amount <= 0)
            {
                SetF3DebugCheatStatus(L10n.T("请输入有效金额", "Please enter a valid amount"), true);
                return;
            }

            AddMoneyAndReport(amount);
        }

        private void AddMoneyAndReport(long amount)
        {
            try
            {
                if (!EconomyManager.Add(amount))
                {
                    SetF3DebugCheatStatus(L10n.T("加钱失败", "Failed to add money"), true);
                    return;
                }

                SetF3DebugCheatStatus(L10n.T("已增加金钱: ", "Added money: ") + amount.ToString("N0", CultureInfo.InvariantCulture), false);
            }
            catch (Exception e)
            {
                DevLog("[BossRush] F3 加钱失败: " + e.Message);
                SetF3DebugCheatStatus(L10n.T("加钱失败", "Failed to add money"), true);
            }
        }

        private void ClearWishRewardCooldownOnly()
        {
            try
            {
                WishFountainService.ClearWishRewardCooldownForDevMode();
                SetF3DebugCheatStatus(L10n.T("已清除星愿奖励冷却", "Wish reward cooldown cleared"), false);
            }
            catch (Exception e)
            {
                DevLog("[BossRush] 清除星愿奖励冷却失败: " + e.Message);
                SetF3DebugCheatStatus(L10n.T("清除奖励冷却失败", "Failed to clear reward cooldown"), true);
            }
        }

        private void ClearWishSendCooldownOnly()
        {
            try
            {
                WishFountainService.ClearSendCooldownForDevMode();
                SetF3DebugCheatStatus(L10n.T("已清除星愿发送冷却", "Wish send cooldown cleared"), false);
            }
            catch (Exception e)
            {
                DevLog("[BossRush] 清除星愿发送冷却失败: " + e.Message);
                SetF3DebugCheatStatus(L10n.T("清除发送冷却失败", "Failed to clear send cooldown"), true);
            }
        }

        private void ClearAllWishDevCooldowns()
        {
            try
            {
                WishFountainService.ClearWishRewardCooldownForDevMode();
                WishFountainService.ClearSendCooldownForDevMode();
                SetF3DebugCheatStatus(L10n.T("已清除星愿奖励与发送冷却", "Wish reward and send cooldowns cleared"), false);
            }
            catch (Exception e)
            {
                DevLog("[BossRush] 清除星愿冷却失败: " + e.Message);
                SetF3DebugCheatStatus(L10n.T("清除星愿冷却失败", "Failed to clear Wish Fountain cooldowns"), true);
            }
        }

        private void OpenInventoryInspectorFromF3()
        {
            try
            {
                InventoryInspector inspector = GetComponent<InventoryInspector>();
                if (inspector == null)
                {
                    inspector = gameObject.AddComponent<InventoryInspector>();
                }

                HideF3DebugCheatMenu();
                inspector.ShowAndRefresh();
            }
            catch (Exception e)
            {
                DevLog("[BossRush] 打开 InventoryInspector 失败: " + e.Message);
                SetF3DebugCheatStatus(L10n.T("打开背包检查器失败", "Failed to open inventory inspector"), true);
            }
        }

        private void ForceKillAllEnemiesFromF3()
        {
            try
            {
                ForceKillAllEnemies();
                SetF3DebugCheatStatus(L10n.T("已执行强制清场", "Force kill executed"), false);
            }
            catch (Exception e)
            {
                DevLog("[BossRush] F3 强制清场失败: " + e.Message);
                SetF3DebugCheatStatus(L10n.T("强制清场失败", "Failed to force kill enemies"), true);
            }
        }

        private void TriggerBossRushVictoryFromF3()
        {
            if (!IsActive)
            {
                SetF3DebugCheatStatus(L10n.T("当前不在 BossRush 流程中", "BossRush is not active right now"), true);
                return;
            }

            try
            {
                ForceKillAllEnemies();
            }
            catch { }

            try
            {
                OnAllEnemiesDefeated();
                SetF3DebugCheatStatus(L10n.T("已触发通关流程", "Victory flow triggered"), false);
            }
            catch (Exception e)
            {
                DevLog("[BossRush] F3 触发通关失败: " + e.Message);
                SetF3DebugCheatStatus(L10n.T("触发通关失败", "Failed to trigger victory"), true);
            }
        }

        private void GrantTicketAndOpenMapSelectionFromF3()
        {
            try
            {
                int ticketTypeId = bossRushTicketTypeId > 0 ? bossRushTicketTypeId : BossRushItemIds.BossRushTicket;
                Item ticket = ItemAssetsCollection.InstantiateSync(ticketTypeId);
                if (ticket != null)
                {
                    ItemUtilities.SendToPlayerCharacterInventory(ticket, false);
                }

                HideF3DebugCheatMenu();
                BossRushMapSelectionHelper.ShowBossRushMapSelection();
            }
            catch (Exception e)
            {
                DevLog("[BossRush] F3 发船票并打开地图失败: " + e.Message);
                SetF3DebugCheatStatus(L10n.T("打开地图失败", "Failed to open map selection"), true);
            }
        }

        private void GrantZombieInvitationAndOpenMapSelectionFromF3()
        {
            try
            {
                string failureReason;
                if (!CanStartZombieModeMapSelectionPhase1(out failureReason))
                {
                    SetF3DebugCheatStatus(string.IsNullOrEmpty(failureReason) ? L10n.T("当前无法开始尸潮模式", "Cannot start Zombie Mode now") : failureReason, true);
                    return;
                }

                ZombieTideInvitationConfig.EnsureRuntimeFallbackRegistrationShell();
                Item invitation = ItemAssetsCollection.InstantiateSync(BossRushItemIds.ZombieTideInvitation);
                if (invitation == null)
                {
                    SetF3DebugCheatStatus(L10n.T("尸潮邀请函创建失败", "Failed to create Zombie Tide Invitation"), true);
                    return;
                }

                ItemUtilities.SendToPlayerCharacterInventory(invitation, false);
                if (!ZombieModeMapSelectionHelper.ShowZombieModeMapSelection(out failureReason))
                {
                    SetF3DebugCheatStatus(string.IsNullOrEmpty(failureReason) ? L10n.T("打开尸潮地图失败", "Failed to open Zombie Mode map") : failureReason, true);
                    return;
                }

                HideF3DebugCheatMenu();
            }
            catch (Exception e)
            {
                DevLog("[BossRush] F3 打开尸潮地图失败: " + e.Message);
                SetF3DebugCheatStatus(L10n.T("打开尸潮地图失败", "Failed to open Zombie Mode map"), true);
            }
        }

        private void TriggerZombieModeExtractionFromF3()
        {
            try
            {
                if (!IsZombieModeActive)
                {
                    SetF3DebugCheatStatus(L10n.T("尸潮模式未激活", "Zombie Mode is not active"), true);
                    return;
                }

                if (TryUseZombieModeBeacon())
                {
                    SetF3DebugCheatStatus(L10n.T("已触发尸潮撤离", "Zombie extraction triggered"), false);
                }
                else
                {
                    SetF3DebugCheatStatus(L10n.T("当前无法触发尸潮撤离", "Cannot trigger Zombie extraction now"), true);
                }
            }
            catch (Exception e)
            {
                DevLog("[BossRush] F3 触发尸潮撤离失败: " + e.Message);
                SetF3DebugCheatStatus(L10n.T("触发尸潮撤离失败", "Failed to trigger Zombie extraction"), true);
            }
        }

        private void ResetZombieModeFromF3()
        {
            try
            {
                DebugResetZombieModeShell();
                SetF3DebugCheatStatus(L10n.T("已重置尸潮模式", "Zombie Mode reset"), false);
                RefreshF3DebugCheatSummary();
            }
            catch (Exception e)
            {
                DevLog("[BossRush] F3 重置尸潮模式失败: " + e.Message);
                SetF3DebugCheatStatus(L10n.T("重置尸潮模式失败", "Failed to reset Zombie Mode"), true);
            }
        }

        private void TogglePlacementModeFromF3()
        {
            try
            {
                TogglePlacementMode();
                SetF3DebugCheatStatus(L10n.T("已切换放置模式", "Placement mode toggled"), false);
            }
            catch (Exception e)
            {
                DevLog("[BossRush] F3 切换放置模式失败: " + e.Message);
                SetF3DebugCheatStatus(L10n.T("切换放置模式失败", "Failed to toggle placement mode"), true);
            }
        }

        private void ClearAchievementsFromF3()
        {
            try
            {
                BossRushAchievementManager.DebugResetAll();
                AchievementEntryUI.ClearIconCache();
                SteamAchievementPopup.ClearIconCache();

                if (AchievementView.Instance != null && AchievementView.Instance.IsOpen)
                {
                    AchievementView.Instance.RefreshAll();
                }

                SetF3DebugCheatStatus(L10n.T("已清空所有成就数据", "All achievement data cleared"), false);
            }
            catch (Exception e)
            {
                DevLog("[BossRush] F3 清空成就失败: " + e.Message);
                SetF3DebugCheatStatus(L10n.T("清空成就失败", "Failed to clear achievements"), true);
            }
        }

        private void DumpNearbyObjectsFromF3()
        {
            try
            {
                CharacterMainControl player;
                if (!TryGetMainCharacter(out player))
                {
                    SetF3DebugCheatStatus(L10n.T("未找到玩家，无法输出对象信息", "Player not found. Cannot dump objects"), true);
                    return;
                }

                Vector3 playerPos = player.transform.position;
                string sceneName = SceneManager.GetActiveScene().name;
                if (sceneName.Contains("Base_Scene"))
                {
                    LogNearbyBuildingInfo(playerPos, 15f);
                }
                else
                {
                    LogNearbyGameObjects(playerPos, 10f, 30);
                }

                SetF3DebugCheatStatus(L10n.T("已输出附近对象信息", "Nearby object info dumped"), false);
            }
            catch (Exception e)
            {
                DevLog("[BossRush] F3 输出附近对象失败: " + e.Message);
                SetF3DebugCheatStatus(L10n.T("输出附近对象失败", "Failed to dump nearby objects"), true);
            }
        }

        private void DumpNearestInteractableFromF3()
        {
            try
            {
                CharacterMainControl main;
                if (!TryGetMainCharacter(out main))
                {
                    SetF3DebugCheatStatus(L10n.T("未找到玩家，无法输出交互点", "Player not found. Cannot dump interactables"), true);
                    return;
                }

                Vector3 playerPos = main.transform.position;
                InteractableBase[] allInteractables = UnityEngine.Object.FindObjectsOfType<InteractableBase>(true);
                InteractableBase nearest = null;
                float bestDistSq = float.MaxValue;

                if (allInteractables != null)
                {
                    for (int i = 0; i < allInteractables.Length; i++)
                    {
                        InteractableBase it = allInteractables[i];
                        if (it == null || it.gameObject == null)
                        {
                            continue;
                        }

                        float distSq = (it.transform.position - playerPos).sqrMagnitude;
                        if (distSq < bestDistSq)
                        {
                            bestDistSq = distSq;
                            nearest = it;
                        }
                    }
                }

                if (nearest != null)
                {
                    float dist = Mathf.Sqrt(bestDistSq);
                    string sceneName = SceneManager.GetActiveScene().name;
                    string name = nearest.gameObject.name;
                    string interactName = string.Empty;
                    try { interactName = nearest.InteractName; } catch { }
                    int groupCount = 0;
                    try
                    {
                        var list = nearest.GetInteractableList();
                        groupCount = list != null ? list.Count : 0;
                    }
                    catch { }

                    DevLog("[BossRush] F3 场景调试：当前场景=" + sceneName +
                           ", 玩家位置=" + playerPos +
                           ", 最近交互点 name=" + name +
                           ", InteractName=" + interactName +
                           ", 位置=" + nearest.transform.position +
                           ", 距离=" + dist +
                           ", 组内成员数量=" + groupCount);
                    SetF3DebugCheatStatus(L10n.T("已输出最近交互点信息", "Nearest interactable info dumped"), false);
                }
                else
                {
                    SetF3DebugCheatStatus(L10n.T("当前场景未找到交互点", "No interactables found in the current scene"), true);
                }
            }
            catch (Exception e)
            {
                DevLog("[BossRush] F3 输出最近交互点失败: " + e.Message);
                SetF3DebugCheatStatus(L10n.T("输出最近交互点失败", "Failed to dump nearest interactable"), true);
            }
        }

        private void DumpSceneCharactersFromF3()
        {
            try
            {
                CharacterMainControl main;
                if (!TryGetMainCharacter(out main))
                {
                    SetF3DebugCheatStatus(L10n.T("未找到玩家，无法输出角色信息", "Player not found. Cannot dump characters"), true);
                    return;
                }

                Vector3 playerPos = main.transform.position;
                CharacterMainControl[] characters = UnityEngine.Object.FindObjectsOfType<CharacterMainControl>();
                if (characters == null || characters.Length == 0)
                {
                    SetF3DebugCheatStatus(L10n.T("当前场景未找到任何角色", "No characters found in the current scene"), true);
                    return;
                }

                DevLog("[BossRush] F3 场景调试：玩家位置=" + playerPos + "，开始列出除玩家外的所有角色");
                for (int i = 0; i < characters.Length; i++)
                {
                    CharacterMainControl c = characters[i];
                    if (c == null)
                    {
                        continue;
                    }

                    bool isMain = false;
                    try
                    {
                        if (c == main)
                        {
                            isMain = true;
                        }
                        else
                        {
                            isMain = CharacterMainControlExtensions.IsMainCharacter(c);
                        }
                    }
                    catch { }

                    if (isMain)
                    {
                        continue;
                    }

                    Vector3 pos = c.transform.position;
                    float dist = (pos - playerPos).magnitude;
                    string presetKey = string.Empty;
                    Teams team = Teams.scav;
                    try
                    {
                        if (c.characterPreset != null)
                        {
                            presetKey = c.characterPreset.nameKey;
                            team = c.characterPreset.team;
                        }
                    }
                    catch { }

                    float maxHealth = -1f;
                    try
                    {
                        if (c.Health != null)
                        {
                            maxHealth = c.Health.MaxHealth;
                        }
                    }
                    catch { }

                    DevLog("[BossRush] F3 角色：goName=" + c.gameObject.name +
                           ", presetKey=" + presetKey +
                           ", team=" + team +
                           ", MaxHP=" + maxHealth +
                           ", pos=" + pos +
                           ", dist=" + dist.ToString("F1", CultureInfo.InvariantCulture));
                }

                SetF3DebugCheatStatus(L10n.T("已输出场景角色信息", "Scene character info dumped"), false);
            }
            catch (Exception e)
            {
                DevLog("[BossRush] F3 输出角色信息失败: " + e.Message);
                SetF3DebugCheatStatus(L10n.T("输出角色信息失败", "Failed to dump scene characters"), true);
            }
        }
    }
}
