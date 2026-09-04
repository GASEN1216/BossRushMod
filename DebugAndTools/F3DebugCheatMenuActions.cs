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
        private const int DragonKingBossGunDebugAmmoStackCount = 120;

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

        private void SpawnDragonKingBossGunDebugKitFromF3()
        {
            int weaponSuccess = 0;
            int ammoSuccess = 0;
            int failedCount = 0;

            try
            {
                Item weapon = ItemAssetsCollection.InstantiateSync(DragonKingBossGunConfig.WeaponTypeId);
                if (weapon != null)
                {
                    ItemUtilities.SendToPlayer(weapon);
                    weaponSuccess = 1;
                }
                else
                {
                    failedCount++;
                    DevLog("[BossRush] F3 焚天龙铳测试套装缺少武器 typeId=" + DragonKingBossGunConfig.WeaponTypeId);
                }

                foreach (int ammoTypeId in DragonKingBossGunProfiles.SupportedTypeIds)
                {
                    Item ammo = ItemAssetsCollection.InstantiateSync(ammoTypeId);
                    if (ammo == null)
                    {
                        failedCount++;
                        DevLog("[BossRush] F3 焚天龙铳测试套装缺少弹药 typeId=" + ammoTypeId);
                        continue;
                    }

                    ammo.StackCount = ResolveDragonKingBossGunDebugAmmoStackCount(ammo);
                    ItemUtilities.SendToPlayer(ammo);
                    ammoSuccess++;
                }

                if (weaponSuccess <= 0 || ammoSuccess < DragonKingBossGunProfiles.SupportedTypeIds.Count)
                {
                    SetF3DebugCheatStatus(
                        L10n.T("焚天龙铳测试套装发放不完整：武器 x", "Dragon Gun debug kit incomplete: weapon x") + weaponSuccess +
                        L10n.T("，弹药种类 x", ", ammo types x") + ammoSuccess +
                        L10n.T("，失败 x", ", failed x") + failedCount,
                        true);
                    return;
                }

                SetF3DebugCheatStatus(
                    L10n.T("已发放焚天龙铳测试套装：武器 x1，弹药种类 x", "Granted Dragon Gun debug kit: weapon x1, ammo types x") + ammoSuccess,
                    false);
            }
            catch (Exception e)
            {
                DevLog("[BossRush] F3 焚天龙铳测试套装发放失败: " + e.Message);
                SetF3DebugCheatStatus(L10n.T("焚天龙铳测试套装发放失败", "Failed to grant Dragon Gun debug kit"), true);
            }
        }

        private int ResolveDragonKingBossGunDebugAmmoStackCount(Item ammo)
        {
            int maxStack = ammo != null && ammo.MaxStackCount > 0 ? ammo.MaxStackCount : DragonKingBossGunDebugAmmoStackCount;
            return Math.Max(1, Math.Min(DragonKingBossGunDebugAmmoStackCount, maxStack));
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
                BossRushMapSelectionHelper.ShowBossRushMapSelection(IsModeGEntryIntentNow());
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

        // ====================================================================
        // 遗种巢 PoC 闸门（实施计划 步骤 0）
        // 只服务于闸门实机验证：召唤幼体 / 回收幼体 / 输出探针报告。
        // 闸门通过后本组按钮保留为调试工具，不参与正式玩法链路。
        // ====================================================================

        private void SpawnPetNestProbeCompanionFromF3()
        {
            PetNestDebugProbe.SpawnProbeCompanion(this, null);
            SetF3DebugCheatStatus(PetNestDebugProbe.LastStatus, false);
        }

        private void DespawnPetNestProbeCompanionFromF3()
        {
            PetNestDebugProbe.DespawnProbeCompanion();
            SetF3DebugCheatStatus(PetNestDebugProbe.LastStatus, false);
        }

        private void DumpPetNestProbeReportFromF3()
        {
            string report = PetNestDebugProbe.BuildProbeReport();
            DevLog(report);
            SetF3DebugCheatStatus(L10n.T("已输出遗种巢 PoC 探针报告（见日志）",
                "PetNest PoC probe report dumped (see log)"), false);
        }

        // ====================================================================
        // 鸭科夫日报
        // ====================================================================

        /// <summary>
        /// 快进一个完整游戏日并立刻结算。
        /// 一个游戏日是 86300 游戏秒（≈24 现实分钟），冒烟测试不可能真等，
        /// 没有这个按钮就没法验证跨天/断签/发奖。
        /// </summary>
        private void AdvanceDailyReportOneDayFromF3()
        {
            DailyReportService.DebugAdvanceGameSeconds(DailyReportTuning.GameSecondsPerDay);
            SetF3DebugCheatStatus(L10n.T(
                "日报已快进一天，当前第 " + DailyReportService.Data.DayIndex + " 天",
                "Daily advanced one day, now day " + DailyReportService.Data.DayIndex), false);
        }

        private void OpenDailyReportFromF3()
        {
            OpenDailyReportUI();
            SetF3DebugCheatStatus(L10n.T("已打开日报面板", "Daily report panel opened"), false);
        }

        private void DumpDailyReportStateFromF3()
        {
            DailyReportData data = DailyReportService.Data;
            if (data == null)
            {
                SetF3DebugCheatStatus(L10n.T("日报数据不可用", "Daily report data unavailable"), true);
                return;
            }

            // 旁边的「悬赏进度」用的是 GetActiveBountyProgress()（**今日**在售题），
            // 题目也必须取今日在售题。存档里的 data.BountyKindId 是 SettleBounty 写入的
            // **昨日**已结算题，新档首个 rollover 前恒为空 → 显示「<无>」而实际有在售题，
            // 正好干扰这个 dump 本身要服务的排查。
            DailyReportBountyDef activeBounty = DailyReportService.GetActiveBounty();

            string report = DailyReportTuning.LogPrefix
                + "第 " + data.DayIndex + " 天"
                + " | 当日进度 " + Mathf.RoundToInt(DailyReportService.DayProgress01 * 100f) + "%"
                + " | 第 " + data.PeriodIndex + " 期 " + data.PeriodSignedCount + "/"
                    + DailyReportTuning.DaysPerPeriod
                + " | 连签 " + data.Streak + " 累计 " + data.TotalSignedDays
                + " | 领取掩码 0x" + data.PeriodClaimedMask.ToString("X")
                + " | 今日击杀 " + data.Today.Kills + "（Boss " + data.Today.BossKills + "）"
                + " 出击 " + data.Today.Raids + " 撤离 " + data.Today.Extractions
                + " 阵亡 " + data.Today.Deaths
                + " | 悬赏进度 " + DailyReportService.GetActiveBountyProgress()
                + " | 本进程跨天 " + DailyReportService.RolloverCount + " 次"
                // 撤离类悬赏（成功撤离 N 次 / 出击且零阵亡）完全依赖官方 raid 事件。
                // 若竞技场地图未标记 isRaidMap，这两类题目在纯竞技场玩法下永远无法达成，
                // 抽到即废题。下面三项用于在实机里定位这一点：进竞技场打一场再 dump，
                // 看 Raids/Extractions 是否有增长。
                + " | 悬赏题目 " + (activeBounty == null || string.IsNullOrEmpty(activeBounty.Id)
                    ? "<无>" : activeBounty.Id)
                + " | 当前场景 " + UnityEngine.SceneManagement.SceneManager.GetActiveScene().name
                + " | isRaidMap " + DescribeCurrentRaidMapFlag();

            DevLog(report);
            SetF3DebugCheatStatus(L10n.T("已输出日报状态（见日志）",
                "Daily report state dumped (see log)"), false);
        }

        // ====================================================================
        // 捏脸 NPC 工具（数据采集 + 探针）
        // ====================================================================
        // 这一组按钮服务的是「未来用原版捏脸造新 NPC」这条路线的前置验证，
        // 与现有哥布林/护士/快递员三个 AssetBundle NPC 完全无关，不触碰它们。
        // 报告落盘在 Application.persistentDataPath/BossRushTestReports/。

        private void DumpDuckNpcInventoryFromF3()
        {
            DuckNpcDebugProbe.DumpInventoryReport();
            SetF3DebugCheatStatus(DuckNpcDebugProbe.LastStatus, false);
        }

        private void ExportPlayerFaceFromF3()
        {
            DuckNpcDebugProbe.ExportPlayerFace();
            SetF3DebugCheatStatus(DuckNpcDebugProbe.LastStatus, false);
        }

        private void SpawnDuckNpcProbeWithPlayerFaceFromF3()
        {
            DuckNpcDebugProbe.SpawnProbe(DuckNpcProbeFaceSource.PlayerFace, false);
            SetF3DebugCheatStatus(DuckNpcDebugProbe.LastStatus, false);
        }

        private void SpawnDuckNpcProbeWithBaselineFaceFromF3()
        {
            DuckNpcDebugProbe.SpawnProbe(DuckNpcProbeFaceSource.OfficialBaseline, false);
            SetF3DebugCheatStatus(DuckNpcDebugProbe.LastStatus, false);
        }

        /// <summary>
        /// 随机夸张脸。用来肉眼确认"生成出来的确实不一样" ——
        /// 参数被故意推到官方区间两端，连点几次差异会非常明显。
        /// </summary>
        private void SpawnDuckNpcProbeWithRandomFaceFromF3()
        {
            DuckNpcDebugProbe.SpawnProbe(DuckNpcProbeFaceSource.RandomExaggerated, false);
            SetF3DebugCheatStatus(DuckNpcDebugProbe.LastStatus, false);
        }

        /// <summary>随机夸张脸 + 随机官方装备（头盔/护甲/耳机/面罩/背包/主武器）。</summary>
        private void SpawnDuckNpcProbeFullRandomFromF3()
        {
            DuckNpcDebugProbe.SpawnProbe(DuckNpcProbeFaceSource.RandomExaggerated, true);
            SetF3DebugCheatStatus(DuckNpcDebugProbe.LastStatus, false);
        }

        /// <summary>就地把当前探针换成一张新的随机夸张脸，不重新生成角色。</summary>
        private void RerollDuckNpcProbeFaceFromF3()
        {
            DuckNpcDebugProbe.RerollProbeFace();
            SetF3DebugCheatStatus(DuckNpcDebugProbe.LastStatus, false);
        }

        /// <summary>就地给当前探针重掷一套随机装备，不重新生成角色。</summary>
        private void RerollDuckNpcProbeEquipmentFromF3()
        {
            DuckNpcDebugProbe.RerollProbeEquipment();
            SetF3DebugCheatStatus(DuckNpcDebugProbe.LastStatus, false);
        }

        /// <summary>
        /// 把当前探针的脸 + 装备保存成可直接粘进 Assets/Data/DuckNpcs.json 的蓝图。
        /// 这是「摇到满意的长相 → 固化成永久 NPC」这条作者流程的落点。
        /// </summary>
        private void SaveDuckNpcProbeAsPermanentFromF3()
        {
            DuckNpcDebugProbe.SaveProbeAsPermanentNpc();
            SetF3DebugCheatStatus(DuckNpcDebugProbe.LastStatus, false);
        }

        // ====================================================================
        // 永久捏脸 NPC（模式 B）测试
        // ====================================================================
        // 永久 NPC 正常靠蓝图的 scenes 白名单自动生成；在还没决定它住哪张图之前，
        // 这一组按钮提供「当场生成来验证」的路子。
        // 现有婚姻/好感度调试 UI 全部写死叮当和护士的 NPC_ID，新 NPC 用不上，
        // 所以这里自带好感度拉满/清零。

        private void SpawnPermanentDuckNpcFromF3()
        {
            PermanentDuckNpcDebug.SpawnHere();
            SetF3DebugCheatStatus(PermanentDuckNpcDebug.LastStatus, false);
        }

        private void DespawnPermanentDuckNpcFromF3()
        {
            PermanentDuckNpcDebug.Despawn();
            SetF3DebugCheatStatus(PermanentDuckNpcDebug.LastStatus, false);
        }

        private void MaxPermanentDuckNpcAffinityFromF3()
        {
            PermanentDuckNpcDebug.MaxAffinity();
            SetF3DebugCheatStatus(PermanentDuckNpcDebug.LastStatus, false);
        }

        private void ResetPermanentDuckNpcAffinityFromF3()
        {
            PermanentDuckNpcDebug.ResetAffinity();
            SetF3DebugCheatStatus(PermanentDuckNpcDebug.LastStatus, false);
        }

        private void DumpPermanentDuckNpcStateFromF3()
        {
            PermanentDuckNpcDebug.DumpState();
            SetF3DebugCheatStatus(PermanentDuckNpcDebug.LastStatus, false);
        }

        private void DespawnDuckNpcProbeFromF3()
        {
            DuckNpcDebugProbe.DespawnProbe();
            SetF3DebugCheatStatus(DuckNpcDebugProbe.LastStatus, false);
        }

        private void DumpDuckNpcProbeStateFromF3()
        {
            DuckNpcDebugProbe.DumpProbeState();
            SetF3DebugCheatStatus(DuckNpcDebugProbe.LastStatus, false);
        }

        /// <summary>
        /// 当前地图是否被官方标记为 raid map。出击/撤离两个计数完全由官方
        /// RaidUtilities 的 raid 事件驱动，非 raid map 不会触发。
        /// </summary>
        private string DescribeCurrentRaidMapFlag()
        {
            try
            {
                return LevelConfig.IsRaidMap ? "true" : "false";
            }
            catch (Exception)
            {
                return "<unavailable>";
            }
        }
    }
}
