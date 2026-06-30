// ============================================================================
// WavesArenaEntryAndTeleport.cs - BossRush arena entry and teleport flow
// ============================================================================

using System;
using UnityEngine;
using Duckov.Scenes;

namespace BossRush
{
    public partial class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        /// <summary>
        /// 开始 BossRush 模式（WavesArena 分部实现，由 ModBehaviour.StartBossRush 转发调用）
        /// </summary>
        private void StartBossRush_WavesArena(BossRushInteractable interactionSource = null)
        {
            if (IsActive)
            {
                ShowMessage(L10n.T("BossRush已经在进行中！", "BossRush is already in progress!"));
                return;
            }

            BossRushMapSelectionHelper.MarkEntryFlowFromDirectTeleport();

            // 没有从交互点传入时，使用默认难度（每波1个Boss）
            if (interactionSource == null)
            {
                bossesPerWave = 1;
            }

            // 标记：后续进入 DEMO 挑战地图应由 BossRush 控制
            bossRushArenaPlanned = true;

            // [性能优化] 预热角色预设缓存：InitializeEnemyPresets 在进竞技场那一帧会同步
            // Resources.FindObjectsOfTypeAll<CharacterRandomPreset> 全内存扫描（最贵的单项）。
            // 这里趁玩家刚发起进入、场景尚未切换（loading 期）先把它扫好缓存起来，
            // _cachedCharacterPresets 不随场景失效，进图那一帧即可命中缓存而非现场扫描。
            try
            {
                ObjectCache.GetCharacterPresets();
            }
            catch { }

            // 设置 pending 地图索引为 DEMO 挑战地图（索引 0），确保中间场景检查能正确识别目标场景
            BossRushMapSelectionHelper.SetPendingMapEntryIndex(0);
            DevLog("[BossRush] F9 快捷启动：设置 pending 地图索引为 0 (DEMO挑战)");

            // 1. 尝试使用 CharacterMainControl.Main (如果是静态单例)
            // 由于不确定是否存在静态 Main 属性，我们使用 FindObjectOfType
            CharacterMainControl main = null;
            try
            {
                main = CharacterMainControl.Main;
            }
            catch { }

            if (main != null)
            {
                playerCharacter = main;
                try
                {
                    DevLog("[BossRush] StartBossRush: 使用 CharacterMainControl.Main 作为玩家角色: " + main.name + " (scene=" + main.gameObject.scene.name + ") pos=" + main.transform.position);
                }
                catch { }
            }
            else
            {
                try
                {
                    var candidate = FindObjectOfType<CharacterMainControl>();
                    if (candidate != null)
                    {
                        bool isMain = false;
                        try
                        {
                            isMain = CharacterMainControlExtensions.IsMainCharacter(candidate);
                        }
                        catch { }
                        try
                        {
                            DevLog("[BossRush] StartBossRush: FindObjectOfType 得到候选角色: " + candidate.name + " (scene=" + candidate.gameObject.scene.name + ") pos=" + candidate.transform.position + ", IsMainCharacter=" + isMain);
                        }
                        catch { }
                        if (isMain)
                        {
                            playerCharacter = candidate;
                        }
                    }
                }
                catch { }
            }

            // 2. 如果没找到，尝试查找所有 CharacterMainControl 并检查 IsMainCharacter 属性
            if (playerCharacter == null)
            {
                try
                {
                    var allCharacters = FindObjectsOfType<CharacterMainControl>();
                    foreach (var character in allCharacters)
                    {
                        bool isMain = false;
                        try
                        {
                            isMain = CharacterMainControlExtensions.IsMainCharacter(character);
                        }
                        catch { }
                        try
                        {
                            DevLog("[BossRush] StartBossRush: 扫描角色: " + character.name + " (scene=" + character.gameObject.scene.name + ") pos=" + character.transform.position + ", IsMainCharacter=" + isMain);
                        }
                        catch { }
                        if (isMain)
                        {
                            playerCharacter = character;
                            break;
                        }
                    }
                }
                catch { }
            }

            // 3. 如果还是没找到，尝试通过 Tag 查找
            if (playerCharacter == null)
            {
                try
                {
                    var playerObj = GameObject.FindGameObjectWithTag("Player");
                    if (playerObj != null)
                    {
                        var candidate = playerObj.GetComponent<CharacterMainControl>();
                        if (candidate != null)
                        {
                            bool isMain = false;
                            try
                            {
                                isMain = CharacterMainControlExtensions.IsMainCharacter(candidate);
                            }
                            catch { }
                            try
                            {
                                DevLog("[BossRush] StartBossRush: Tag=Player 得到角色: " + candidate.name + " (scene=" + candidate.gameObject.scene.name + ") pos=" + candidate.transform.position + ", IsMainCharacter=" + isMain);
                            }
                            catch { }
                            playerCharacter = candidate;
                        }
                        else
                        {
                            DevLog("[BossRush] [WARNING] StartBossRush: Tag=Player 对象上没有 CharacterMainControl 组件: " + playerObj.name);
                        }
                    }
                    else
                    {
                        DevLog("[BossRush] [WARNING] StartBossRush: 未找到 Tag=Player 对象");
                    }
                }
                catch { }
            }

            if (playerCharacter == null)
            {
                ShowMessage(L10n.T("无法找到玩家角色！请确保在游戏中！", "Player not found! Make sure you are in game!"));
                DevLog("[BossRush] [ERROR] 无法找到玩家角色！");
                return;
            }
            else
            {
                try
                {
                    var finalMain = playerCharacter as CharacterMainControl;
                    if (finalMain != null)
                    {
                        DevLog("[BossRush] StartBossRush: 最终锁定玩家角色: " + finalMain.name + " (scene=" + finalMain.gameObject.scene.name + ") pos=" + finalMain.transform.position);
                    }
                    else
                    {
                        DevLog("[BossRush] [WARNING] StartBossRush: 最终 playerCharacter 不是 CharacterMainControl 类型: " + playerCharacter.name);
                    }
                }
                catch { }
            }

            ShowMessage(L10n.T("开始BossRush模式，正在前往竞技场...", "Starting BossRush, heading to arena..."));
            DevLog("[BossRush] 开始BossRush模式，正在前往竞技场...");

            try
            {
                if (MultiSceneCore.Instance != null)
                {
                    CharacterMainControl finalMainForTeleport = null;
                    try
                    {
                        finalMainForTeleport = playerCharacter as CharacterMainControl;
                    }
                    catch {}
                    if (finalMainForTeleport == null)
                    {
                        try
                        {
                            finalMainForTeleport = CharacterMainControl.Main;
                        }
                        catch {}
                    }

                    if (finalMainForTeleport != null)
                    {
                        TeleportToBossRushAsync();
                    }
                    else
                    {
                        DevLog("[BossRush] [WARNING] StartBossRush: 未找到用于传送的玩家角色 CharacterMainControl");
                    }
                }
                else
                {
                    DevLog("[BossRush] [WARNING] StartBossRush: MultiSceneCore.Instance 为 null，尝试使用 SceneLoader 方案");
                    TeleportToBossRushAsync();
                }
            }
            catch (Exception e)
            {
                DevLog("[BossRush] [ERROR] StartBossRush: 启动传送任务时出错: " + e.Message);
            }

            // 直接开始第一波Boss（会在短暂延迟后在玩家附近生成）
        }

        private async void TeleportToBossRushAsync_WavesArena()
        {
            bool usedTicketTeleport = false;

            // 直接使用 SceneLoader 方案加载 BossRush 场景（与原版挑战船票流程一致）
            try
            {
                if (SceneLoader.Instance != null)
                {
                    DevLog("[BossRush] TeleportToBossRushAsync: 使用 SceneLoader.LoadScene 加载 BossRush 场景, SceneID=" + BossRushArenaSceneID);
                    try
                    {
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
                        usedTicketTeleport = true;
                    }
                    catch (Exception ex)
                    {
                        DevLog("[BossRush] [ERROR] TeleportToBossRushAsync: SceneLoader.LoadScene 调用失败: " + ex.Message + "\n" + ex.StackTrace);
                    }
                }
                else
                {
                    DevLog("[BossRush] [ERROR] TeleportToBossRushAsync: SceneLoader.Instance 为 null，无法加载 BossRush 场景");
                }
            }
            catch (Exception ex)
            {
                DevLog("[BossRush] [ERROR] TeleportToBossRushAsync: 处理 SceneLoader 方案时出错: " + ex.Message);
            }

            if (!usedTicketTeleport)
            {
                ShowMessage(L10n.T("进入BossRush场景失败，请查看日志", "Failed to enter BossRush scene, check logs"));
            }
        }




        private void TryCreateReturnInteractable_WavesArena()
        {
            try
            {
                if (GameObject.Find("BossRushReturnButton_DemoChallenge") != null)
                {
                    return;
                }

                CharacterMainControl main = null;
                try
                {
                    main = CharacterMainControl.Main;
                }
                catch {}

                if (main == null)
                {
                    try
                    {
                        main = playerCharacter as CharacterMainControl;
                    }
                    catch {}
                }

                if (main == null)
                {
                    DevLog("[BossRush] [WARNING] TryCreateReturnInteractable: 无法找到玩家角色");
                    return;
                }

                Vector3 pos = main.transform.position + main.transform.forward * 2f;
                pos.y += 0.5f;

                GameObject returnButton = GameObject.CreatePrimitive(PrimitiveType.Cube);
                returnButton.name = "BossRushReturnButton_DemoChallenge";
                returnButton.transform.position = pos;

                var renderer = returnButton.GetComponent<Renderer>();
                if (renderer != null)
                {
                    renderer.material.color = Color.green;
                }

                var col = returnButton.GetComponent<Collider>();
                if (col != null)
                {
                    col.isTrigger = true;
                }

                returnButton.AddComponent<BossRushReturnInteractable>();

                DevLog("[BossRush] 已创建 BossRush 返回出生点交互点");
            }
            catch (Exception e)
            {
                DevLog("[BossRush] [ERROR] TryCreateReturnInteractable 出错: " + e.Message);
            }
        }



    }
}
