using ItemStatsSystem;
using Duckov.UI;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BossRush
{
    public partial class ModBehaviour
    {
        internal void InitializeDebugToolsRuntime()
        {
            RegisterInteractDebugListener();
            RegisterShootDebugListener();
        }

        internal void TickDebugTools(float deltaTime, float unscaledDeltaTime)
        {
            UpdateFpsCounter();
            UpdateMapClickDebug();
            CheckBossPoolWindowHotkey();
            CheckItemSpawnerHotkey();
            CheckF3DebugCheatMenuHotkey();
            TickF3DebugCheatMenu();
        }

        internal void LateUpdateDebugTools()
        {
            BossPoolLateUpdate();
            NPCTeleportUILateUpdate();
            F3DebugCheatMenuLateUpdate();
        }

        internal void TickDebugToolsAfterModalGate()
        {
            // 调试快捷键 F4：清空所有成就数据（DevMode专用测试功能）
            if (DevModeEnabled && UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.F4))
            {
                try
                {
                    DevLog("[BossRush] F4 按下，清空所有成就数据");
                    BossRushAchievementManager.DebugResetAll();
                    AchievementEntryUI.ClearIconCache();
                    SteamAchievementPopup.ClearIconCache();

                    // 如果成就页面打开则刷新
                    if (AchievementView.Instance != null && AchievementView.Instance.IsOpen)
                    {
                        AchievementView.Instance.RefreshAll();
                    }

                    NotificationText.Push(AchievementUIStrings.IsChinese()
                        ? "[调试] 已清空所有成就数据"
                        : "[Debug] All achievement data cleared");
                    DevLog("[BossRush] 成就数据已清空");
                }
                catch (System.Exception e)
                {
                    DevLog("[BossRush] F4 清空成就失败: " + e.Message);
                }
            }

            if (DevModeEnabled && UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.F5))
            {
                try
                {
                    CharacterMainControl main = CharacterMainControl.Main;
                    if (main == null)
                    {
                        DevLog("[BossRush] F5 调试：未找到玩家 CharacterMainControl");
                    }
                    else
                    {
                        Vector3 playerPos = main.transform.position;
                        string sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;

                        // 基地场景输出建筑物信息，其他场景输出最近的 GameObject
                        if (sceneName.Contains("Base_Scene"))
                        {
                            LogNearbyBuildingInfo(playerPos, 15f);
                        }
                        else
                        {
                            // 战斗场景输出最近的 30 个 GameObject
                            LogNearbyGameObjects(playerPos, 10f, 30);
                        }
                    }
                }
                catch (System.Exception e)
                {
                    DevLog("[BossRush] F5 调试失败: " + e.Message);
                }
            }

            // 调试快捷键 F6：切换放置模式
            if (DevModeEnabled && UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.F6))
            {
                try
                {
                    TogglePlacementMode();
                }
                catch (System.Exception e)
                {
                    DevLog("[BossRush] F6 放置模式切换失败: " + e.Message);
                }
            }

            // 放置模式更新（每帧调用）
            if (DevModeEnabled && IsPlacementModeActive())
            {
                try
                {
                    UpdatePlacementMode();
                }
                catch (System.Exception e)
                {
                    DevLog("[BossRush] 放置模式更新失败: " + e.Message);
                }
            }

            // 调试快捷键 F9：打开传送地图UI
            if (DevModeEnabled && UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.F9))
            {
                DevLog("[BossRush] F9按下，打开传送地图UI");

                // 给玩家背包发送一张 BossRush 船票
                try
                {
                    int ticketTypeId = bossRushTicketTypeId > 0 ? bossRushTicketTypeId : BossRushItemIds.BossRushTicket;
                    Item ticket = ItemAssetsCollection.InstantiateSync(ticketTypeId);
                    if (ticket != null)
                    {
                        ItemUtilities.SendToPlayerCharacterInventory(ticket, false);
                        DevLog("[BossRush] 已发送BossRush船票到玩家背包");
                    }
                }
                catch (System.Exception e)
                {
                    DevLog("[BossRush] 发送船票失败: " + e.Message);
                }

                BossRushMapSelectionHelper.ShowBossRushMapSelection();
            }

            // 调试快捷键 F8：输出场景中除玩家外所有角色信息
            if (DevModeEnabled && UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.F8))
            {
                try
                {
                    CharacterMainControl main = CharacterMainControl.Main;
                    if (main == null)
                    {
                        DevLog("[BossRush] F8 调试：未找到玩家 CharacterMainControl");
                    }
                    else
                    {
                        Vector3 playerPos = main.transform.position;
                        CharacterMainControl[] characters = UnityEngine.Object.FindObjectsOfType<CharacterMainControl>();
                        if (characters == null || characters.Length == 0)
                        {
                            DevLog("[BossRush] F8 调试：场景中未找到任何 CharacterMainControl");
                        }
                        else
                        {
                            DevLog("[BossRush] F8 调试：玩家位置=" + playerPos + "，开始列出除玩家外的所有角色");

                            foreach (var c in characters)
                            {
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
                                catch {}

                                if (isMain)
                                {
                                    continue;
                                }

                                Vector3 pos = c.transform.position;
                                float dist = (pos - playerPos).magnitude;

                                string presetKey = "";
                                Teams team = Teams.scav;
                                try
                                {
                                    if (c.characterPreset != null)
                                    {
                                        presetKey = c.characterPreset.nameKey;
                                        team = c.characterPreset.team;
                                    }
                                }
                                catch {}

                                float maxHealth = -1f;
                                try
                                {
                                    if (c.Health != null)
                                    {
                                        maxHealth = c.Health.MaxHealth;
                                    }
                                }
                                catch {}

                                DevLog("[BossRush] F8 角色：goName=" + c.gameObject.name +
                                       ", presetKey=" + presetKey +
                                       ", team=" + team +
                                       ", MaxHP=" + maxHealth +
                                       ", pos=" + pos +
                                       ", dist=" + dist.ToString("F1"));
                            }
                        }
                    }
                }
                catch (System.Exception e)
                {
                    DevLog("[BossRush] F8 调试失败: " + e.Message);
                }
            }

            // 调试快捷键 F7：输出玩家附近最近交互点的信息，辅助定位BossRush入口
            if (DevModeEnabled && UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.F7))
            {
                try
                {
                    CharacterMainControl main = CharacterMainControl.Main;
                    if (main == null)
                    {
                        DevLog("[BossRush] F7 调试：未找到玩家 CharacterMainControl");
                    }
                    else
                    {
                        Vector3 playerPos = main.transform.position;
                        var allInteractables = UnityEngine.Object.FindObjectsOfType<InteractableBase>(true);
                        InteractableBase nearest = null;
                        float bestDistSq = float.MaxValue;

                        if (allInteractables != null)
                        {
                            foreach (var it in allInteractables)
                            {
                                if (it == null || it.gameObject == null) continue;

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
                            float dist = UnityEngine.Mathf.Sqrt(bestDistSq);
                            string sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
                            string name = nearest.gameObject.name;
                            string interactName = "";
                            try { interactName = nearest.InteractName; } catch { }

                            // 组成员数量
                            int groupCount = 0;
                            try
                            {
                                var list = nearest.GetInteractableList();
                                groupCount = (list != null) ? list.Count : 0;
                            }
                            catch { }

                            DevLog("[BossRush] F7 调试：当前场景=" + sceneName +
                                      ", 玩家位置=" + playerPos +
                                      ", 最近交互点 name=" + name +
                                      ", InteractName=" + interactName +
                                      ", 位置=" + nearest.transform.position +
                                      ", 距离=" + dist +
                                      ", 组内成员数量=" + groupCount);
                        }
                        else
                        {
                            DevLog("[BossRush] F7 调试：场景中未找到任何 InteractableBase");
                        }
                    }
                }
                catch (System.Exception e)
                {
                    DevLog("[BossRush] F7 调试失败: " + e.Message);
                }
            }

            if (DevModeEnabled && IsActive && UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.F10))
            {
                try
                {
                    DevLog("[BossRush] F10 调试：直接清场并触发通关流程");
                    try
                    {
                        // [Bug修复] F10调试时强制清理所有敌人，忽略范围限制
                        ForceKillAllEnemies();
                    }
                    catch {}
                    OnAllEnemiesDefeated();
                }
                catch (System.Exception e)
                {
                    DevLog("[BossRush] F10 调试触发通关失败: " + e.Message);
                }
            }

            // F11 已分配给 InventoryInspector（背包物品详细信息查看）

            // 调试快捷键 F12：打开NPC传送UI
            if (DevModeEnabled && UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.F12))
            {
                try
                {
                    ToggleNPCTeleportUI();
                }
                catch (System.Exception e)
                {
                    DevLog("[BossRush] F12 打开NPC传送UI失败: " + e.Message);
                }
            }

            // ESC键关闭NPC传送UI
            if (npcTeleportUIVisible && UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.Escape))
            {
                HideNPCTeleportUI();
            }
        }

        internal void CleanupDebugToolsOnDestroy()
        {
            OnDestroy_F3DebugCheatMenu();
            UnregisterInteractDebugListener();
            UnregisterShootDebugListener();
        }

        internal void OnSceneLoadedDebugToolsRuntime(Scene scene, LoadSceneMode mode)
        {
            OnSceneLoaded_F3DebugCheatMenu(scene, mode);
        }
    }
}
