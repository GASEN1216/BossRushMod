// ============================================================================
// WavesArena.cs - 波次与竞技场管理
// ============================================================================
// 模块说明：
//   管理 BossRush 模组的波次系统和竞技场逻辑，包括：
//   - 波次敌人生成和管理
//   - 竞技场几何体构建（地面、围墙、掩体）
//   - 玩家传送到竞技场
//   - 波次间隔倒计时
//   
// 主要功能：
//   - StartBossRush: 开始 BossRush 模式
//   - TeleportToBossRushAsync: 异步传送到竞技场
//   - BuildBossRushArenaGeometry: 构建竞技场几何体
//   - SpawnNextEnemy: 生成下一波敌人
// ============================================================================

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using UnityEngine;
using Cysharp.Threading.Tasks;
using ItemStatsSystem;
using Duckov.ItemUsage;
using Duckov.Scenes;
using Duckov.Economy;
using System.Reflection;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using Duckov.UI.DialogueBubbles;
using Duckov.UI;
using UnityEngine.AI;
using Duckov.ItemBuilders;

namespace BossRush
{
    /// <summary>
    /// 波次与竞技场管理模块
    /// </summary>
    public partial class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        #region 前期波次Boss排除

        /// <summary>
        /// 前期波次需要排除的强力 Boss 名称列表
        /// 包括：口口口口和四骑士（Cname_StormBoss1-5）
        /// </summary>
        private static readonly HashSet<string> EarlyWaveExcludedBosses = new HashSet<string>
        {
            "Cname_StormBoss1",    // 口口口口 或 四骑士
            "Cname_StormBoss2",    // 口口口口 或 四骑士
            "Cname_StormBoss3",    // 口口口口 或 四骑士
            "Cname_StormBoss4",    // 口口口口 或 四骑士
            "Cname_StormBoss5"     // 口口口口 或 四骑士
        };

        /// <summary>
        /// 检查是否是前期波次需要排除的强力Boss
        /// </summary>
        private bool IsEarlyWaveExcludedBoss(string bossName)
        {
            if (string.IsNullOrEmpty(bossName)) return false;
            return EarlyWaveExcludedBosses.Contains(bossName);
        }

        /// <summary>
        /// 预处理：确保前10波不出现强力Boss
        /// 在挑战开始时调用一次，将前10位中的强力Boss与后面的普通Boss交换
        /// </summary>
        private void EnsureEarlyWavesNoStrongBoss()
        {
            if (enemyPresets == null || enemyPresets.Count <= 10) return;
            
            int swapCount = 0;
            int nextSwapTarget = 10; // 从第10位开始找可交换的普通Boss
            
            for (int i = 0; i < 10 && i < enemyPresets.Count; i++)
            {
                if (!IsEarlyWaveExcludedBoss(enemyPresets[i].name)) continue;
                
                // 找一个第10位之后的普通Boss来交换
                while (nextSwapTarget < enemyPresets.Count && 
                       IsEarlyWaveExcludedBoss(enemyPresets[nextSwapTarget].name))
                {
                    nextSwapTarget++;
                }
                
                if (nextSwapTarget >= enemyPresets.Count) break; // 没有可交换的了
                
                // 交换
                var tmp = enemyPresets[i];
                enemyPresets[i] = enemyPresets[nextSwapTarget];
                enemyPresets[nextSwapTarget] = tmp;
                nextSwapTarget++;
                swapCount++;
            }
            
            if (swapCount > 0)
            {
                DevLog("[BossRush] 前10波强力Boss预处理完成，交换了 " + swapCount + " 个Boss");
            }
        }

        #endregion

        /// <summary>
        /// 开始 BossRush 模式（WavesArena 备份实现，不作为主入口）
        /// </summary>
        private void StartBossRush_WavesArena(BossRushInteractable interactionSource = null)
        {
            if (IsActive)
            {
                ShowMessage(L10n.T("BossRush已经在进行中！", "BossRush is already in progress!"));
                return;
            }

            // 没有从交互点传入时，使用默认难度（每波1个Boss）
            if (interactionSource == null)
            {
                bossesPerWave = 1;
            }

            // 标记：后续进入 DEMO 挑战地图应由 BossRush 控制
            bossRushArenaPlanned = true;
            
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

        private void EnsureBossRushArenaForScene_WavesArena(Scene scene)
        {
            if (arenaCreated && arenaStartPoint != null && arenaStartPoint.scene == scene)
            {
                return;
            }

            try
            {
                SceneLocationsProvider provider = null;
                System.Collections.ObjectModel.ReadOnlyCollection<SceneLocationsProvider> providers = SceneLocationsProvider.ActiveProviders;
                if (providers != null)
                {
                    foreach (SceneLocationsProvider p in providers)
                    {
                        if (p != null && p.gameObject.scene == scene)
                        {
                            provider = p;
                            break;
                        }
                    }
                }

                if (provider == null)
                {
                    DevLog("[BossRush] [WARNING] EnsureBossRushArenaForScene: 未找到 SceneLocationsProvider, scene=" + scene.name);
                    return;
                }

                Transform arenaRoot = provider.transform.Find("BossRushArena");
                if (arenaRoot == null)
                {
                    GameObject arenaRootObj = new GameObject("BossRushArena");
                    arenaRootObj.transform.SetParent(provider.transform);
                    arenaRootObj.transform.localPosition = new Vector3(0f, 150f, 0f);
                    arenaRoot = arenaRootObj.transform;
                }

                arenaStartPoint = arenaRoot.gameObject;
                arenaCreated = true;

                Transform spawn = arenaRoot.Find("SpawnPoint");
                if (spawn == null)
                {
                    GameObject sp = new GameObject("SpawnPoint");
                    sp.transform.SetParent(arenaRoot);
                    sp.transform.localPosition = new Vector3(0f, 2f, 0f);
                }

                BuildBossRushArenaGeometry(arenaRoot);
            }
            catch (Exception e)
            {
                DevLog("[BossRush] [ERROR] EnsureBossRushArenaForScene 出错: " + e.Message);
            }
        }

        private void BuildBossRushArenaGeometry_WavesArena(Transform arenaRoot)
        {
            if (arenaRoot == null)
            {
                return;
            }

            // 如果已经有开始按钮，认为竞技场已经搭建完毕
            if (arenaRoot.Find("BossRushStartButton") != null)
            {
                return;
            }

            try
            {
                // 创建地面平台
                GameObject platform = GameObject.CreatePrimitive(PrimitiveType.Cube);
                platform.name = "BossRushPlatform";
                platform.transform.SetParent(arenaRoot);
                platform.transform.localPosition = Vector3.zero;
                platform.transform.localScale = new Vector3(100, 2, 100); // 100x100的平台
                int originalLayer = platform.layer;

                // 将平台的 Layer 设置为游戏的 groundLayerMask 中的第一个有效层，确保角色与平台的交互与官方地面一致
                try
                {
                    LayerMask groundMask = Duckov.Utilities.GameplayDataSettings.Layers.groundLayerMask;
                    int maskValue = groundMask.value;
                    int groundLayer = originalLayer;
                    for (int i = 0; i < 32; i++)
                    {
                        if ((maskValue & (1 << i)) != 0)
                        {
                            groundLayer = i;
                            break;
                        }
                    }
                    platform.layer = groundLayer;
                    DevLog("[BossRush] BuildBossRushArenaGeometry: 设置平台 Layer 为 groundLayerMask 中的层: " + groundLayer);
                }
                catch {}

                // 设置材质颜色，方便视觉识别
                Renderer renderer = platform.GetComponent<Renderer>();
                MeshFilter srcMeshFilter = platform.GetComponent<MeshFilter>();
                if (renderer != null)
                {
                    renderer.material.color = new Color(0.3f, 0.3f, 0.3f, 1f);
                }
                if (renderer != null && srcMeshFilter != null)
                {
                    GameObject visual = new GameObject("BossRushPlatform_Visual");
                    visual.transform.SetParent(arenaRoot);
                    visual.transform.localPosition = platform.transform.localPosition;
                    visual.transform.localRotation = platform.transform.localRotation;
                    visual.transform.localScale = platform.transform.localScale;
                    MeshFilter visualMF = visual.AddComponent<MeshFilter>();
                    visualMF.sharedMesh = srcMeshFilter.sharedMesh;
                    MeshRenderer visualRenderer = visual.AddComponent<MeshRenderer>();
                    visualRenderer.sharedMaterial = renderer.material;
                    visual.layer = originalLayer;
                    renderer.enabled = false;
                }

                // 在平台四周创建可见矮墙，防止玩家掉出平台
                try
                {
                    float arenaSize = 100f; // 对应 plane 10x10 缩放后的尺寸
                    float halfSize = arenaSize * 0.5f;
                    float wallHeight = 4f;
                    float wallThickness = 2f;

                    // 计算墙体使用的 Layer（来自 wallLayerMask）
                    LayerMask wallMask = Duckov.Utilities.GameplayDataSettings.Layers.wallLayerMask;
                    int wallMaskValue = wallMask.value;
                    int wallLayer = platform.layer;
                    for (int i = 0; i < 32; i++)
                    {
                        if ((wallMaskValue & (1 << i)) != 0)
                        {
                            wallLayer = i;
                            break;
                        }
                    }

                    // 北墙（+Z）
                    GameObject northWall = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    northWall.name = "BossRushWall_North";
                    northWall.transform.SetParent(arenaRoot);
                    northWall.transform.localPosition = new Vector3(0f, wallHeight * 0.5f, halfSize);
                    northWall.transform.localScale = new Vector3(arenaSize, wallHeight, wallThickness);
                    northWall.layer = wallLayer;

                    // 南墙（-Z）
                    GameObject southWall = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    southWall.name = "BossRushWall_South";
                    southWall.transform.SetParent(arenaRoot);
                    southWall.transform.localPosition = new Vector3(0f, wallHeight * 0.5f, -halfSize);
                    southWall.transform.localScale = new Vector3(arenaSize, wallHeight, wallThickness);
                    southWall.layer = wallLayer;

                    // 东墙（+X）
                    GameObject eastWall = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    eastWall.name = "BossRushWall_East";
                    eastWall.transform.SetParent(arenaRoot);
                    eastWall.transform.localPosition = new Vector3(halfSize, wallHeight * 0.5f, 0f);
                    eastWall.transform.localScale = new Vector3(wallThickness, wallHeight, arenaSize);
                    eastWall.layer = wallLayer;

                    // 西墙（-X）
                    GameObject westWall = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    westWall.name = "BossRushWall_West";
                    westWall.transform.SetParent(arenaRoot);
                    westWall.transform.localPosition = new Vector3(-halfSize, wallHeight * 0.5f, 0f);
                    westWall.transform.localScale = new Vector3(wallThickness, wallHeight, arenaSize);
                    westWall.layer = wallLayer;

                    // 在场地中添加一些简单掩体
                    Vector3[] coverPositions = new Vector3[]
                    {
                        new Vector3(15f, 1.5f, 0f),
                        new Vector3(-15f, 1.5f, 0f),
                        new Vector3(0f, 1.5f, 15f),
                        new Vector3(0f, 1.5f, -15f)
                    };

                    for (int ci = 0; ci < coverPositions.Length; ci++)
                    {
                        GameObject cover = GameObject.CreatePrimitive(PrimitiveType.Cube);
                        cover.name = "BossRushCover_" + ci;
                        cover.transform.SetParent(arenaRoot);
                        cover.transform.localPosition = coverPositions[ci];
                        cover.transform.localScale = new Vector3(4f, 3f, 4f);
                        cover.layer = wallLayer;
                    }

                    // 在竞技场周围创建一圈简单的可见边界方块，模拟粒子环效果
                    try
                    {
                        GameObject fxRoot = new GameObject("BossRushBoundaryFX");
                        fxRoot.transform.SetParent(arenaRoot);
                        fxRoot.transform.localPosition = Vector3.zero;

                        float fxRadius = halfSize + 5f;
                        int fxCount = 16;
                        for (int i = 0; i < fxCount; i++)
                        {
                            float ang = (float)i / (float)fxCount * Mathf.PI * 2f;
                            GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
                            marker.name = "BoundaryFX_" + i;
                            marker.transform.SetParent(fxRoot.transform);
                            marker.transform.localPosition = new Vector3(Mathf.Cos(ang) * fxRadius, 1.5f, Mathf.Sin(ang) * fxRadius);
                            marker.transform.localScale = new Vector3(1.5f, 3f, 1.5f);
                            marker.layer = wallLayer;

                            Renderer mr = marker.GetComponent<Renderer>();
                            if (mr != null)
                            {
                                mr.material.color = new Color(0.2f, 0.8f, 1.0f, 1f);
                            }
                        }

                        // 为竞技场中心添加一点局部光源，避免场景过暗
                        GameObject lightObj = new GameObject("BossRushLight");
                        lightObj.transform.SetParent(arenaRoot);
                        lightObj.transform.localPosition = new Vector3(0f, 15f, 0f);
                        Light pointLight = lightObj.AddComponent<Light>();
                        pointLight.type = LightType.Point;
                        pointLight.range = arenaSize + 40f;
                        pointLight.intensity = 1.5f;
                        pointLight.color = new Color(0.9f, 0.9f, 1.0f, 1f);
                        pointLight.shadows = LightShadows.None;
                    }
                    catch {}

                    DevLog("[BossRush] BuildBossRushArenaGeometry: 创建围墙、掩体和边界标记完成，使用 Layer: " + wallLayer);
                }
                catch {}

                DevLog("[BossRush] BuildBossRushArenaGeometry: 竞技场创建完成");
            }
            catch (Exception e)
            {
                DevLog("[BossRush] [ERROR] BuildBossRushArenaGeometry 出错: " + e.Message);
            }
        }

        private IEnumerator TeleportPlayerToArenaDelayed_WavesArena()
        {
            // 给 MultiScene 系统和其他管理器一点时间完成初始化，然后再用老的 SetPosition 方案把玩家拉到竞技场
            yield return new UnityEngine.WaitForSeconds(0.5f);
            TeleportPlayerToArena();
        }

        private bool TryResolveTeleportLocation_WavesArena(BossRushInteractable interactionSource, CharacterMainControl main, out MultiSceneLocation location)
        {
            location = default(MultiSceneLocation);
            try
            {
                MultiSceneTeleporter teleporter = null;
                Transform sourceTransform = null;
                if (interactionSource != null)
                {
                    sourceTransform = interactionSource.transform;
                    try
                    {
                        teleporter = sourceTransform.GetComponentInParent<MultiSceneTeleporter>();
                    }
                    catch {}
                }

                Vector3 refPos = Vector3.zero;
                if (main != null)
                {
                    refPos = main.transform.position;
                }
                else if (sourceTransform != null)
                {
                    refPos = sourceTransform.position;
                }

                if (teleporter == null)
                {
                    try
                    {
                        MultiSceneTeleporter[] allTeleporters = UnityEngine.Object.FindObjectsOfType<MultiSceneTeleporter>(true);
                        float bestDist = float.MaxValue;
                        MultiSceneTeleporter best = null;
                        foreach (MultiSceneTeleporter t in allTeleporters)
                        {
                            if (t == null)
                            {
                                continue;
                            }
                            float d = (t.transform.position - refPos).sqrMagnitude;
                            if (d < bestDist)
                            {
                                bestDist = d;
                                best = t;
                            }
                        }
                        teleporter = best;
                    }
                    catch {}
                }

                if (teleporter != null)
                {
                    location = teleporter.Target;
                    try
                    {
                        DevLog("[BossRush] TryResolveTeleportLocation: 使用 MultiSceneTeleporter " + teleporter.name + " (scene=" + teleporter.gameObject.scene.name + ") Target.SceneID=" + location.SceneID + " LocationName=" + location.LocationName);
                    }
                    catch {}
                    return true;
                }
            }
            catch {}
            return false;
        }

        /// <summary>
        /// 创建竞技场（备用老实现）
        /// </summary>
        private void CreateArena_WavesArena()
        {
            // 创建竞技场起点
            if (arenaStartPoint == null)
            {
                arenaStartPoint = new GameObject("BossRushArena");
                // 将竞技场放在当前玩家位置上方一段高度，形成独立的空中平台
                Vector3 basePos = Vector3.zero;
                if (playerCharacter != null)
                {
                    basePos = playerCharacter.transform.position;
                }
                else
                {
                    basePos = new Vector3(500f, 50f, 500f);
                }
                arenaStartPoint.transform.position = basePos + new Vector3(0f, 50f, 0f);
                
                // 创建地面平台
                GameObject platform = GameObject.CreatePrimitive(PrimitiveType.Plane);
                platform.transform.SetParent(arenaStartPoint.transform);
                platform.transform.localPosition = Vector3.zero;
                platform.transform.localScale = new Vector3(10, 1, 10); // 100x100的平台

                // 将平台的 Layer 设置为游戏的 groundLayerMask 中的第一个有效层，确保角色与平台的交互与官方地面一致
                try
                {
                    LayerMask groundMask = Duckov.Utilities.GameplayDataSettings.Layers.groundLayerMask;
                    int maskValue = groundMask.value;
                    int groundLayer = platform.layer;
                    for (int i = 0; i < 32; i++)
                    {
                        if ((maskValue & (1 << i)) != 0)
                        {
                            groundLayer = i;
                            break;
                        }
                    }
                    platform.layer = groundLayer;
                    DevLog("[BossRush] CreateArena: 设置平台 Layer 为 groundLayerMask 中的层: " + groundLayer);
                }
                catch {}
                
                // 设置材质
                var renderer = platform.GetComponent<Renderer>();
                if (renderer != null)
                {
                    renderer.material.color = new Color(0.3f, 0.3f, 0.3f, 1f);
                }

                // 在平台四周创建可见矮墙，防止玩家掉出平台
                try
                {
                    float arenaSize = 100f; // 对应 plane 10x10 缩放后的尺寸
                    float halfSize = arenaSize * 0.5f;
                    float wallHeight = 4f;
                    float wallThickness = 2f;

                    // 计算墙体使用的 Layer（来自 wallLayerMask）
                    LayerMask wallMask = Duckov.Utilities.GameplayDataSettings.Layers.wallLayerMask;
                    int wallMaskValue = wallMask.value;
                    int wallLayer = platform.layer;
                    for (int i = 0; i < 32; i++)
                    {
                        if ((wallMaskValue & (1 << i)) != 0)
                        {
                            wallLayer = i;
                            break;
                        }
                    }

                    // 北墙（+Z）
                    GameObject northWall = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    northWall.name = "BossRushWall_North";
                    northWall.transform.SetParent(arenaStartPoint.transform);
                    northWall.transform.localPosition = new Vector3(0f, wallHeight * 0.5f, halfSize);
                    northWall.transform.localScale = new Vector3(arenaSize, wallHeight, wallThickness);
                    northWall.layer = wallLayer;

                    // 南墙（-Z）
                    GameObject southWall = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    southWall.name = "BossRushWall_South";
                    southWall.transform.SetParent(arenaStartPoint.transform);
                    southWall.transform.localPosition = new Vector3(0f, wallHeight * 0.5f, -halfSize);
                    southWall.transform.localScale = new Vector3(arenaSize, wallHeight, wallThickness);
                    southWall.layer = wallLayer;

                    // 东墙（+X）
                    GameObject eastWall = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    eastWall.name = "BossRushWall_East";
                    eastWall.transform.SetParent(arenaStartPoint.transform);
                    eastWall.transform.localPosition = new Vector3(halfSize, wallHeight * 0.5f, 0f);
                    eastWall.transform.localScale = new Vector3(wallThickness, wallHeight, arenaSize);
                    eastWall.layer = wallLayer;

                    // 西墙（-X）
                    GameObject westWall = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    westWall.name = "BossRushWall_West";
                    westWall.transform.SetParent(arenaStartPoint.transform);
                    westWall.transform.localPosition = new Vector3(-halfSize, wallHeight * 0.5f, 0f);
                    westWall.transform.localScale = new Vector3(wallThickness, wallHeight, arenaSize);
                    westWall.layer = wallLayer;

                    // 在场地中添加一些简单掩体
                    Vector3[] coverPositions = new Vector3[]
                    {
                        new Vector3(15f, 1.5f, 0f),
                        new Vector3(-15f, 1.5f, 0f),
                        new Vector3(0f, 1.5f, 15f),
                        new Vector3(0f, 1.5f, -15f)
                    };

                    for (int ci = 0; ci < coverPositions.Length; ci++)
                    {
                        GameObject cover = GameObject.CreatePrimitive(PrimitiveType.Cube);
                        cover.name = "BossRushCover_" + ci;
                        cover.transform.SetParent(arenaStartPoint.transform);
                        cover.transform.localPosition = coverPositions[ci];
                        cover.transform.localScale = new Vector3(4f, 3f, 4f);
                        cover.layer = wallLayer;
                    }

                    DevLog("[BossRush] CreateArena: 创建围墙和掩体完成，使用 Layer: " + wallLayer);
                }
                catch {}
                
                DevLog("[BossRush] 竞技场创建完成");
            }
        }

        /// <summary>
        /// 传送玩家到竞技场区域
        /// </summary>
        private void TeleportPlayerToArena_WavesArena()
        {
            // 确保 playerCharacter 缓存可用
            if (playerCharacter == null)
            {
                try
                {
                    playerCharacter = CharacterMainControl.Main;
                }
                catch {}

                if (playerCharacter == null)
                {
                    try
                    {
                        var candidate = UnityEngine.Object.FindObjectOfType<CharacterMainControl>();
                        if (candidate != null)
                        {
                            bool isMain = false;
                            try
                            {
                                isMain = CharacterMainControlExtensions.IsMainCharacter(candidate);
                            }
                            catch {}
                            if (isMain)
                            {
                                playerCharacter = candidate;
                            }
                        }
                    }
                    catch {}
                }
            }

            // 确保 arenaStartPoint 指向当前场景的竞技场根节点
            if (arenaStartPoint == null)
            {
                try
                {
                    EnsureBossRushArenaForScene(SceneManager.GetActiveScene());
                }
                catch {}
            }

            if (playerCharacter == null || arenaStartPoint == null)
            {
                DevLog("[BossRush] [ERROR] TeleportPlayerToArena: playerCharacter 或 arenaStartPoint 为空，无法传送");
                return;
            }

            // 尝试获取 CharacterMainControl 实例
            CharacterMainControl main = playerCharacter as CharacterMainControl;
            if (main == null)
            {
                try
                {
                    main = CharacterMainControl.Main;
                }
                catch {}
            }

            if (main == null)
            {
                DevLog("[BossRush] [ERROR] TeleportPlayerToArena: 无法获取 CharacterMainControl 实例");
                return;
            }

            bool isMainCharacter = false;
            try
            {
                isMainCharacter = CharacterMainControlExtensions.IsMainCharacter(main);
            }
            catch {}
            try
            {
                DevLog("[BossRush] TeleportPlayerToArena: 选中传送目标角色: " + main.name + " (scene=" + main.gameObject.scene.name + ") pos=" + main.transform.position + ", IsMainCharacter=" + isMainCharacter);
            }
            catch {}

            // 传送到竞技场
            Vector3 fromPos = main.transform.position;
            Vector3 arenaPosition = arenaStartPoint.transform.position + new Vector3(0f, 5f, 0f);

            // 使用官方的地面/墙体 LayerMask 尝试向下射线，修正落点到实际地面，避免无限下落
            Vector3 finalPosition = arenaPosition;
            try
            {
                LayerMask mask = Duckov.Utilities.GameplayDataSettings.Layers.wallLayerMask | Duckov.Utilities.GameplayDataSettings.Layers.groundLayerMask;
                RaycastHit hit;
                Vector3 rayOrigin = arenaPosition + new Vector3(0f, 50f, 0f);
                if (Physics.Raycast(rayOrigin, Vector3.down, out hit, 200f, mask, QueryTriggerInteraction.Ignore))
                {
                    finalPosition = hit.point + new Vector3(0f, 1f, 0f);
                    DevLog("[BossRush] TeleportPlayerToArena: 使用 Raycast 修正落点为地面: " + finalPosition + " 命中碰撞体: " + hit.collider.name);
                }
                else
                {
                    DevLog("[BossRush] TeleportPlayerToArena: 未找到地面碰撞体，使用默认落点: " + arenaPosition);
                }
            }
            catch {}

            // 获取相机并记录偏移，参照 InvisibleTeleporter 的做法
            GameCamera cam = null;
            try
            {
                cam = GameCamera.Instance;
            }
            catch {}

            Vector3 camOffset = Vector3.zero;
            if (cam != null)
            {
                camOffset = cam.transform.position - fromPos;
                try
                {
                    DevLog("[BossRush] TeleportPlayerToArena: Camera cullingMask = " + cam.renderCamera.cullingMask);
                }
                catch {}
            }

            DevLog("[BossRush] TeleportPlayerToArena: from " + fromPos + " (scene=" + main.gameObject.scene.name + ") to " + finalPosition + " (arenaScene=" + arenaStartPoint.scene.name + ")");

            try
            {
                main.SetPosition(finalPosition);
                DevLog("[BossRush] 使用 CharacterMainControl.SetPosition 传送玩家");
            }
            catch (Exception e)
            {
                DevLog("[BossRush] [ERROR] SetPosition 传送出错: " + e.Message + "，改用 transform.position");
                main.transform.position = arenaPosition;
            }

            // 同步相机位置，避免相机留在原地导致视觉错觉
            if (cam != null)
            {
                cam.transform.position = main.transform.position + camOffset;
                DevLog("[BossRush] 已同步相机位置到 " + cam.transform.position);
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

        private void ReturnToBossRushStart_WavesArena()
        {
            try
            {
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
                    DevLog("[BossRush] [WARNING] ReturnToBossRushStart: 无法找到玩家角色");
                    return;
                }

                Vector3 targetPos = demoChallengeStartPosition;
                if (targetPos == Vector3.zero)
                {
                    targetPos = main.transform.position;
                }

                try
                {
                    main.SetPosition(targetPos);
                    DevLog("[BossRush] ReturnToBossRushStart: 使用 SetPosition 将玩家传送回 BossRush 起始位置 " + targetPos);
                }
                catch (Exception e)
                {
                    DevLog("[BossRush] [ERROR] ReturnToBossRushStart: SetPosition 出错: " + e.Message + "，改用 transform.position");
                    main.transform.position = targetPos;
                }

                ShowMessage(L10n.T("已返回出生点", "Returned to spawn point"));
            }
            catch {}
        }

        /// <summary>
        /// 开始第一波Boss（在竞技场内）- 单波生成模式
        /// </summary>
        public void StartFirstWave()
        {
            // [DEBUG] 记录当前状态
            DevLog("[BossRush] StartFirstWave 调用: IsActive=" + IsActive + ", bossesPerWave=" + bossesPerWave + ", infiniteHellMode=" + infiniteHellMode);
            
            if (!IsActive)
            {
                // 记录玩家当前位置作为出生点（BossRush失败时传送回此处）
                try
                {
                    CharacterMainControl main = CharacterMainControl.Main;
                    if (main != null)
                    {
                        demoChallengeStartPosition = main.transform.position;
                        DevLog("[BossRush] 已记录玩家出生点: " + demoChallengeStartPosition);
                    }
                }
                catch (Exception e)
                {
                    DevLog("[BossRush] [WARNING] 记录玩家出生点失败: " + e.Message);
                }
                
                // 每次挑战开始时随机打乱本次要挑战的敌人顺序
                try
                {
                    if (enemyPresets != null && enemyPresets.Count > 1)
                    {
                        // Fisher-Yates 洗牌
                        for (int i = enemyPresets.Count - 1; i > 0; i--)
                        {
                            int j = UnityEngine.Random.Range(0, i + 1);
                            if (j != i)
                            {
                                var tmp = enemyPresets[i];
                                enemyPresets[i] = enemyPresets[j];
                                enemyPresets[j] = tmp;
                            }
                        }
                        
                        // 弹指可灭/有点意思模式：预处理，确保前10波不出现强力Boss
                        // 将前10位中的强力Boss与第10位之后的普通Boss交换
                        if (!infiniteHellMode)
                        {
                            EnsureEarlyWavesNoStrongBoss();
                        }
                        
                        DevLog("[BossRush] 已随机打乱本次 BossRush 的敌人出场顺序");
                    }
                }
                catch (Exception shuffleEx)
                {
                    DevLog("[BossRush] [WARNING] 打乱敌人顺序时出错: " + shuffleEx.Message);
                }

                // 清理场景中现有的敌人，准备开始BossRush
                ClearEnemiesForBossRush();
                
                ShowMessage(L10n.T("开始BossRush挑战！", "BossRush challenge started!"));
                SetBossRushRuntimeActive(true);
                currentEnemyIndex = 0;
                defeatedEnemies = 0;
                
                // 使用过滤后的 Boss 池计算总数，确保横幅显示正确的 Boss 数量
                var filteredPresetsForCount = GetFilteredEnemyPresets();
                totalEnemies = (filteredPresetsForCount != null) ? filteredPresetsForCount.Count : 0;
                
                bossesInCurrentWaveTotal = 0;
                bossesInCurrentWaveRemaining = 0;
                currentWaveBosses.Clear();
                
                // 清空掉落追踪字典
                bossSpawnTimes.Clear();
                bossOriginalLootCounts.Clear();
                countedDeadBosses.Clear();
                
                DevLog("[BossRush] 启动单波生成模式，共 " + totalEnemies + " 个敌人（已过滤）");
                
                // 订阅敌人死亡事件（只订阅一次）
                Health.OnDead -= OnEnemyDiedWithDamageInfo; // 先取消避免重复
                Health.OnDead += OnEnemyDiedWithDamageInfo;
                
                // 立即生成第一个敌人
                SpawnNextEnemy();
            }
        }
        
        /// <summary>
        /// 获取安全的Boss生成位置（只修正Y轴高度，不改变XZ坐标）
        /// </summary>
        /// <remarks>
        /// 保持原始XZ坐标不变，只通过Raycast修正Y轴高度到地面
        /// 避免NavMesh采样导致敌人刷在预设点位之外
        /// </remarks>
        private static Vector3 GetSafeBossSpawnPosition(Vector3 rawPosition)
        {
            Vector3 result = rawPosition;
            string method = "原始";
            try
            {
                // 从更高的位置向下射线检测地面高度
                Vector3 origin = rawPosition + Vector3.up * 15f;
                float maxDistance = 30f;
                LayerMask groundMask = Duckov.Utilities.GameplayDataSettings.Layers.groundLayerMask;
                RaycastHit hit;
                if (Physics.Raycast(origin, Vector3.down, out hit, maxDistance, groundMask))
                {
                    // 只修正Y轴，保持XZ不变
                    result = new Vector3(rawPosition.x, hit.point.y + 0.15f, rawPosition.z);
                    method = "Raycast(仅Y轴)";
                }
                else
                {
                    // Raycast失败，尝试用NavMesh获取高度（但不改变XZ）
                    NavMeshHit navHit;
                    if (NavMesh.SamplePosition(rawPosition, out navHit, 5f, NavMesh.AllAreas))
                    {
                        // 只取NavMesh的Y值，XZ保持原始
                        result = new Vector3(rawPosition.x, navHit.position.y + 0.15f, rawPosition.z);
                        method = "NavMesh(仅Y轴)";
                    }
                    else
                    {
                        // 都失败了，使用原始位置但抬高一点
                        result = rawPosition + Vector3.up * 0.5f;
                        method = "回退(+0.5m)";
                    }
                }
                
                DevLog($"[BossRush] 刷新点修正: 原始={rawPosition}, 结果={result}, 方法={method}");
            }
            catch (Exception e)
            {
                result = rawPosition + Vector3.up * 0.5f;
                DevLog($"[BossRush] GetSafeBossSpawnPosition 异常: {e.Message}");
            }
            return result;
        }
        
        /// <summary>
        /// 校验并修正Boss位置（生成后调用，防止Boss卡在地下）
        /// </summary>
        private void ValidateAndFixBossPosition(CharacterMainControl boss)
        {
            if (boss == null) return;
            
            try
            {
                Vector3 currentPos = boss.transform.position;
                
                // 从Boss当前位置向上发射射线，检测是否在地面以下
                Vector3 checkOrigin = currentPos + Vector3.up * 0.5f;
                LayerMask groundMask = Duckov.Utilities.GameplayDataSettings.Layers.groundLayerMask;
                RaycastHit hitUp;
                
                // 如果向上能打到地面，说明Boss在地下
                if (Physics.Raycast(checkOrigin, Vector3.up, out hitUp, 20f, groundMask))
                {
                    // Boss在地面以下，需要修正
                    Vector3 fixedPos = hitUp.point + Vector3.up * 0.5f;
                    boss.transform.position = fixedPos;
                    DevLog("[BossRush] 修正Boss位置（从地下拉出）: " + boss.name + " -> " + fixedPos);
                    return;
                }
                
                // 从高处向下检测，确认Boss脚下有地面
                Vector3 abovePos = currentPos + Vector3.up * 15f;
                RaycastHit hitDown;
                if (Physics.Raycast(abovePos, Vector3.down, out hitDown, 30f, groundMask))
                {
                    float groundY = hitDown.point.y;
                    // 如果Boss的Y坐标比地面低超过0.5米，修正位置
                    if (currentPos.y < groundY - 0.5f)
                    {
                        Vector3 fixedPos = new Vector3(currentPos.x, groundY + 0.15f, currentPos.z);
                        boss.transform.position = fixedPos;
                        DevLog("[BossRush] 修正Boss位置（Y坐标过低）: " + boss.name + " -> " + fixedPos);
                    }
                }
                else
                {
                    // Raycast失败，尝试NavMesh
                    NavMeshHit navHit;
                    if (NavMesh.SamplePosition(currentPos, out navHit, 15f, NavMesh.AllAreas))
                    {
                        if (currentPos.y < navHit.position.y - 0.5f)
                        {
                            Vector3 fixedPos = navHit.position + Vector3.up * 0.15f;
                            boss.transform.position = fixedPos;
                            DevLog("[BossRush] 修正Boss位置（NavMesh校准）: " + boss.name + " -> " + fixedPos);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                DevLog("[BossRush] ValidateAndFixBossPosition 异常: " + e.Message);
            }
        }
        
        /// <summary>
        /// 延迟校验Boss位置的协程（给地形加载留出时间）
        /// </summary>
        private IEnumerator DelayedBossPositionValidation(CharacterMainControl boss, float delay)
        {
            if (boss == null) yield break;
            
            yield return new WaitForSeconds(delay);
            
            if (boss != null && boss.gameObject != null)
            {
                ValidateAndFixBossPosition(boss);
            }
        }
        
        /// <summary>
        /// 生成下一个敌人（根据 bossesPerWave 支持单Boss或多Boss一波）
        /// </summary>
        private void SpawnNextEnemy()
        {
            // [DEBUG] 记录当前状态
            DevLog("[BossRush] SpawnNextEnemy 调用: bossesPerWave=" + bossesPerWave + ", currentEnemyIndex=" + currentEnemyIndex + ", totalEnemies=" + totalEnemies);
            
            // 通知快递员 Boss 战开始
            NotifyCourierBossFightStart();
            
            // 通知快递员有Boss了（不再是召唤间隔）
            NotifyCourierNoBoss(false);
            
            // 获取过滤后的 Boss 列表
            var filteredPresets = GetFilteredEnemyPresets();
            
            // 检查 Boss 池是否为空
            if (filteredPresets == null || filteredPresets.Count == 0)
            {
                ShowMessage(L10n.T("Boss池为空！请至少启用一个Boss。(Ctrl+F10 打开设置)", "Boss pool is empty! Enable at least one Boss. (Ctrl+F10 to open settings)"));
                DevLog("[BossRush] [WARNING] SpawnNextEnemy: Boss 池为空，无法生成敌人");
                return;
            }

            // 普通模式：跑完列表后直接通关
            if (!infiniteHellMode)
            {
                if (currentEnemyIndex >= filteredPresets.Count)
                {
                    // 所有敌人已击败，显示完成对话
                    OnAllEnemiesDefeated();
                    return;
                }
            }

            EnemyPresetInfo preset = null;
            if (infiniteHellMode)
            {
                // 无间炼狱：每一波按权重随机选择Boss，不再依赖 currentEnemyIndex 作为索引
                preset = PickRandomEnemyForInfiniteHell();
                if (preset == null)
                {
                    DevLog("[BossRush] [ERROR] SpawnNextEnemy: InfiniteHell 模式下未找到可用敌人预设");
                    return;
                }
            }
            else
            {
                // 弹指可灭/有点意思模式：按顺序选取Boss
                // 强力Boss已在挑战开始时预处理，前10波不会出现
                preset = filteredPresets[currentEnemyIndex];
            }

            DevLog("[BossRush] 生成第 " + (currentEnemyIndex + 1) + "/" + totalEnemies + " 波: " + preset.displayName);

            try
            {
                // 获取玩家
                CharacterMainControl playerMain = CharacterMainControl.Main;
                if (playerMain == null)
                {
                    DevLog("[BossRush] [ERROR] 玩家未找到，无法生成敌人");
                    return;
                }

                // 使用当前地图的刷新点（根据场景动态选择）
                Vector3[] spawnPoints = GetCurrentSpawnPoints();
                if (spawnPoints == null || spawnPoints.Length == 0)
                {
                    DevLog("[BossRush] [ERROR] 当前地图刷新点为空，无法生成敌人");
                    return;
                }

                if (bossesPerWave <= 1)
                {
                    // 单Boss模式：每波只生成一个Boss，同样维护波次计数，便于自检逻辑使用
                    bossesInCurrentWaveTotal = 1;
                    bossesInCurrentWaveRemaining = 1;
                    currentWaveBosses.Clear();

                    int index = UnityEngine.Random.Range(0, spawnPoints.Length);
                    Vector3 spawnPos = GetSafeBossSpawnPosition(spawnPoints[index]);

                    // 显示敌人生成横幅（在生成前显示）
                    ShowEnemyBanner(preset.displayName, spawnPos, playerMain.transform.position);

                    // 使用带验证的异步生成方法
                    SpawnBossWithVerificationAsync(preset, spawnPos, spawnPoints).Forget();
                }
                else
                {
                    // 多Boss模式：同一波生成 bossesPerWave 个相同Boss
                    bossesInCurrentWaveTotal = bossesPerWave;
                    bossesInCurrentWaveRemaining = bossesPerWave;
                    currentWaveBosses.Clear();

                    // 收集本波需要生成的所有Boss预设信息（位置由生成方法内部分配，确保不重复）
                    var bossSpawnInfos = new List<(EnemyPresetInfo preset, Vector3 position)>();
                    
                    for (int i = 0; i < bossesPerWave; i++)
                    {
                        EnemyPresetInfo wavePreset = preset;
                        if (infiniteHellMode)
                        {
                            var altPreset = PickRandomEnemyForInfiniteHell();
                            if (altPreset != null)
                            {
                                wavePreset = altPreset;
                            }
                        }

                        // 位置占位，实际位置由 SpawnMultipleBossesWithVerificationAsync 内部分配
                        bossSpawnInfos.Add((wavePreset, Vector3.zero));
                    }
                    
                    // 显示第一个Boss的横幅（使用第一个刷怪点作为参考）
                    if (bossSpawnInfos.Count > 0)
                    {
                        Vector3 bannerPos = GetSafeBossSpawnPosition(spawnPoints[0]);
                        ShowEnemyBanner(bossSpawnInfos[0].preset.displayName, bannerPos, playerMain.transform.position);
                    }
                    
                    // 使用带验证和重试的批量生成方法
                    SpawnMultipleBossesWithVerificationAsync(bossSpawnInfos, spawnPoints).Forget();
                }
            }
            catch (Exception e)
            {
                DevLog("[BossRush] [ERROR] 生成敌人失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 单Boss模式：带验证的异步生成（包含重试机制）
        /// </summary>
        private async UniTaskVoid SpawnBossWithVerificationAsync(EnemyPresetInfo preset, Vector3 position, Vector3[] spawnPoints)
        {
            const int maxRetries = 3;
            int attempt = 0;
            CharacterMainControl spawnedBoss = null;
            
            while (attempt < maxRetries && spawnedBoss == null)
            {
                attempt++;
                
                if (attempt > 1)
                {
                    DevLog("[BossRush] 单Boss生成重试 #" + attempt + ": " + preset.displayName);
                    // 重试时使用新的随机位置
                    int newIndex = UnityEngine.Random.Range(0, spawnPoints.Length);
                    position = GetSafeBossSpawnPosition(spawnPoints[newIndex]);
                }
                
                spawnedBoss = await SpawnEnemyAtPositionAsync(preset, position);
                
                if (spawnedBoss == null && attempt < maxRetries)
                {
                    // 等待一小段时间后重试
                    await UniTask.Delay(200);
                }
            }
            
            if (spawnedBoss == null)
            {
                DevLog("[BossRush] [ERROR] 单Boss生成失败，已重试 " + maxRetries + " 次: " + preset.displayName);
                OnBossSpawnFailed(preset);
            }
            else
            {
                DevLog("[BossRush] 单Boss生成成功: " + preset.displayName + " (尝试次数: " + attempt + ")");
            }
        }
        
        /// <summary>
        /// 多Boss模式：带验证和重试的批量生成
        /// 确保每个Boss使用不同的刷怪点，避免位置冲突
        /// </summary>
        private async UniTaskVoid SpawnMultipleBossesWithVerificationAsync(
            List<(EnemyPresetInfo preset, Vector3 position)> bossSpawnInfos, 
            Vector3[] spawnPoints)
        {
            const int maxRetries = 3;
            int expectedCount = bossSpawnInfos.Count;
            
            DevLog("[BossRush] 开始批量生成 " + expectedCount + " 个Boss");
            
            // 重新分配刷怪点，确保每个Boss使用不同的位置
            var assignedPositions = AssignUniqueSpawnPositions(expectedCount, spawnPoints);
            for (int i = 0; i < bossSpawnInfos.Count && i < assignedPositions.Count; i++)
            {
                bossSpawnInfos[i] = (bossSpawnInfos[i].preset, assignedPositions[i]);
            }
            
            // 记录已使用的刷怪点索引，用于重试时避免冲突
            var usedSpawnIndices = new HashSet<int>();
            for (int i = 0; i < Mathf.Min(expectedCount, spawnPoints.Length); i++)
            {
                usedSpawnIndices.Add(i);
            }
            
            // 第一轮：串行生成所有Boss（避免并行时的潜在冲突）
            var results = new List<CharacterMainControl>();
            var failedInfos = new List<(EnemyPresetInfo preset, int originalIndex)>();
            
            for (int i = 0; i < bossSpawnInfos.Count; i++)
            {
                var info = bossSpawnInfos[i];
                CharacterMainControl spawned = null;
                
                try
                {
                    spawned = await SpawnEnemyAtPositionAsync(info.preset, info.position);
                }
                catch (Exception e)
                {
                    DevLog("[BossRush] Boss生成异常 #" + i + ": " + e.Message);
                }
                
                results.Add(spawned);
                
                if (spawned == null && !IsDragonDescendantPreset(info.preset))
                {
                    failedInfos.Add((info.preset, i));
                }
                
                // 每个Boss生成后短暂等待，确保游戏状态稳定
                if (i < bossSpawnInfos.Count - 1)
                {
                    await UniTask.Delay(50);
                }
            }
            
            // 统计成功生成的数量
            int successCount = 0;
            for (int i = 0; i < results.Count; i++)
            {
                if (results[i] != null)
                {
                    successCount++;
                }
                else if (IsDragonDescendantPreset(bossSpawnInfos[i].preset))
                {
                    // 龙裔遗族返回 null 但不视为失败
                    successCount++;
                }
            }
            
            DevLog("[BossRush] 首轮生成完成: 成功=" + successCount + ", 失败=" + failedInfos.Count);
            
            // 重试失败的Boss
            int retryAttempt = 0;
            while (failedInfos.Count > 0 && retryAttempt < maxRetries)
            {
                retryAttempt++;
                DevLog("[BossRush] 开始重试失败的Boss (第 " + retryAttempt + " 轮), 剩余: " + failedInfos.Count);
                
                // 等待一小段时间后重试
                await UniTask.Delay(300);
                
                var stillFailed = new List<(EnemyPresetInfo preset, int originalIndex)>();
                
                foreach (var failedInfo in failedInfos)
                {
                    // 重试时选择一个未使用过的刷怪点
                    Vector3 newPos = GetUnusedSpawnPosition(spawnPoints, usedSpawnIndices);
                    
                    CharacterMainControl retryResult = null;
                    try
                    {
                        retryResult = await SpawnEnemyAtPositionAsync(failedInfo.preset, newPos);
                    }
                    catch (Exception e)
                    {
                        DevLog("[BossRush] Boss重试生成异常: " + e.Message);
                    }
                    
                    if (retryResult != null)
                    {
                        successCount++;
                        DevLog("[BossRush] 重试成功: " + failedInfo.preset.displayName);
                    }
                    else if (!IsDragonDescendantPreset(failedInfo.preset))
                    {
                        stillFailed.Add(failedInfo);
                    }
                    else
                    {
                        successCount++;
                    }
                    
                    // 每次重试后短暂等待
                    await UniTask.Delay(100);
                }
                
                failedInfos = stillFailed;
                DevLog("[BossRush] 重试轮 " + retryAttempt + " 完成: 当前成功总数=" + successCount + ", 仍失败=" + failedInfos.Count);
            }
            
            // 最终验证
            int finalFailCount = expectedCount - successCount;
            if (finalFailCount > 0)
            {
                DevLog("[BossRush] [WARNING] 最终有 " + finalFailCount + " 个Boss生成失败，修正波次计数");
                
                // 修正波次计数，避免卡住
                bossesInCurrentWaveTotal = successCount;
                bossesInCurrentWaveRemaining = successCount;
                
                // 如果全部失败，直接推进下一波
                if (successCount <= 0)
                {
                    DevLog("[BossRush] [ERROR] 本波所有Boss生成失败，跳过本波");
                    ProceedAfterWaveFinished();
                }
            }
            else
            {
                DevLog("[BossRush] 批量生成完成: 全部 " + expectedCount + " 个Boss成功生成");
            }
        }
        
        /// <summary>
        /// 为多个Boss分配不重复的刷怪点位置
        /// </summary>
        private List<Vector3> AssignUniqueSpawnPositions(int count, Vector3[] spawnPoints)
        {
            var positions = new List<Vector3>();
            
            if (spawnPoints == null || spawnPoints.Length == 0)
            {
                DevLog("[BossRush] [WARNING] AssignUniqueSpawnPositions: 刷怪点数组为空");
                return positions;
            }
            
            // 打乱刷怪点顺序，然后依次分配
            var shuffledIndices = new List<int>();
            for (int i = 0; i < spawnPoints.Length; i++)
            {
                shuffledIndices.Add(i);
            }
            
            // Fisher-Yates 洗牌算法
            for (int i = shuffledIndices.Count - 1; i > 0; i--)
            {
                int j = UnityEngine.Random.Range(0, i + 1);
                int temp = shuffledIndices[i];
                shuffledIndices[i] = shuffledIndices[j];
                shuffledIndices[j] = temp;
            }
            
            // 分配位置（每个Boss使用不同的刷怪点）
            for (int i = 0; i < count; i++)
            {
                int spawnIndex = shuffledIndices[i % shuffledIndices.Count];
                Vector3 basePos = spawnPoints[spawnIndex];
                positions.Add(GetSafeBossSpawnPosition(basePos));
            }
            
            DevLog("[BossRush] 分配了 " + positions.Count + " 个不重复的刷怪位置");
            return positions;
        }
        
        /// <summary>
        /// 获取一个未使用过的刷怪点位置（用于重试）
        /// </summary>
        private Vector3 GetUnusedSpawnPosition(Vector3[] spawnPoints, HashSet<int> usedIndices)
        {
            if (spawnPoints == null || spawnPoints.Length == 0)
            {
                return Vector3.zero;
            }
            
            // 优先选择未使用过的刷怪点
            for (int i = 0; i < spawnPoints.Length; i++)
            {
                if (!usedIndices.Contains(i))
                {
                    usedIndices.Add(i);
                    return GetSafeBossSpawnPosition(spawnPoints[i]);
                }
            }
            
            // 所有刷怪点都用过了，随机选一个
            int randomIndex = UnityEngine.Random.Range(0, spawnPoints.Length);
            return GetSafeBossSpawnPosition(spawnPoints[randomIndex]);
        }
        
        /// <summary>
        /// 获取当前地图的刷新点数组（使用 BossRushMapConfig 配置系统）
        /// </summary>
        private Vector3[] GetCurrentSpawnPoints()
        {
            // 使用配置系统获取当前场景的刷新点
            return GetCurrentSceneSpawnPoints();
        }
        
        public void StartNextWaveCountdown()
        {
            float interval = GetWaveIntervalSeconds();

            if (!infiniteHellMode)
            {
                try
                {
                    nextWaveBossName = null;
                    // 使用过滤后的 Boss 列表，确保预告的 Boss 与实际生成的一致
                    var filteredPresets = GetFilteredEnemyPresets();
                    int presetCount = (filteredPresets != null) ? filteredPresets.Count : 0;
                    if (currentEnemyIndex >= 0 && currentEnemyIndex < presetCount)
                    {
                        EnemyPresetInfo nextPreset = filteredPresets[currentEnemyIndex];
                        if (nextPreset != null)
                        {
                            nextWaveBossName = nextPreset.displayName;
                        }
                    }
                }
                catch
                {
                    nextWaveBossName = null;
                }
            }
            else
            {
                nextWaveBossName = null;
            }
            if (interval <= 0f)
            {
                waitingForNextWave = false;
                lastWaveCountdownSeconds = -1;
                SpawnNextEnemy();
                return;
            }

            // 重置上一轮倒计时状态
            waitingForNextWave = true;
            waveCountdown = interval;
            lastWaveCountdownSeconds = -1;

            if (interval <= 5f)
            {
                int secondsInt = Mathf.RoundToInt(interval);
                if (secondsInt < 1)
                {
                    secondsInt = 1;
                }

                if (!infiniteHellMode && !string.IsNullOrEmpty(nextWaveBossName))
                {
                    ShowBigBanner(L10n.T(
                        "<color=red>" + nextWaveBossName + "</color> 将在 <color=yellow>" + secondsInt + "</color> 秒后抵达战场...",
                        "<color=red>" + nextWaveBossName + "</color> arriving in <color=yellow>" + secondsInt + "</color> seconds..."
                    ));
                }
                else
                {
                    ShowBigBanner(L10n.T(
                        "下一波将在 <color=yellow>" + secondsInt + "</color> 秒后开始...",
                        "Next wave in <color=yellow>" + secondsInt + "</color> seconds..."
                    ));
                }
            }
        }

        /// <summary>
        /// 敌人死亡事件处理（带DamageInfo参数）
        /// <para>仅用于普通模式（弹指可灭/有点意思/无间炼狱），Mode D 有独立的死亡处理逻辑</para>
        /// </summary>
        private void OnEnemyDiedWithDamageInfo(Health deadHealth, DamageInfo damageInfo)
        {
            try
            {
                // Mode D 有独立的敌人死亡处理（RegisterModeDEnemyDeath），不走普通模式逻辑
                // 避免 Mode D 打死敌人时误触发普通模式的通关判定
                if (modeDActive)
                {
                    return;
                }
                
                if (!IsActive || deadHealth == null)
                {
                    return;
                }

                CharacterMainControl deadCharacter = null;
                try
                {
                    deadCharacter = deadHealth.TryGetCharacter();
                }
                catch {}

                // 多Boss模式：检查是否是当前波的其中一名Boss
                if (bossesPerWave > 1 && currentWaveBosses != null && currentWaveBosses.Count > 0)
                {
                    MonoBehaviour matchedBoss = null;
                    for (int i = 0; i < currentWaveBosses.Count; i++)
                    {
                        MonoBehaviour boss = currentWaveBosses[i];
                        if (boss == null) continue;

                        bool isDeadBoss = false;

                        try
                        {
                            CharacterMainControl bossCharacter = boss as CharacterMainControl;
                            if (bossCharacter != null && deadCharacter != null)
                            {
                                isDeadBoss = (bossCharacter == deadCharacter);
                            }
                        }
                        catch {}

                        if (!isDeadBoss)
                        {
                            try
                            {
                                Health bossHealth = boss.GetComponent<Health>();
                                if (bossHealth == deadHealth || boss.gameObject == deadHealth.gameObject)
                                {
                                    isDeadBoss = true;
                                }
                            }
                            catch {}
                        }

                        if (isDeadBoss)
                        {
                            matchedBoss = boss;
                            break;
                        }
                    }

                    if (matchedBoss != null)
                    {
                        DevLog("[BossRush] 当前波有一名Boss被击败");

                        // 处理Boss掉落随机化
                        CharacterMainControl bossMainControl = matchedBoss as CharacterMainControl;
                        if (bossMainControl != null)
                        {
                            HandleBossDeath(bossMainControl, damageInfo);
                        }
                    }
                }
                else
                {
                    // 单Boss模式：保持原有逻辑
                    bool isCurrentBossDead = false;

                    if (currentBoss != null)
                    {
                        try
                        {
                            CharacterMainControl currentBossCharacter = currentBoss as CharacterMainControl;
                            if (currentBossCharacter != null && deadCharacter != null)
                            {
                                isCurrentBossDead = (currentBossCharacter == deadCharacter);
                            }
                        }
                        catch {}

                        if (!isCurrentBossDead)
                        {
                            try
                            {
                                isCurrentBossDead = (deadHealth.gameObject == ((MonoBehaviour)currentBoss).gameObject);
                            }
                            catch {}
                        }
                    }

                    if (currentBoss != null && isCurrentBossDead)
                    {
                        DevLog("[BossRush] 当前敌人已击败");
                        CharacterMainControl bossMainControl = currentBoss as CharacterMainControl;
                        if (bossMainControl != null)
                        {
                            HandleBossDeath(bossMainControl, damageInfo);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                DevLog("[BossRush] [ERROR] OnEnemyDied 错误: " + e.Message);
            }
        }

        private void HandleBossDeath(CharacterMainControl bossMain, DamageInfo damageInfo)
        {
            try
            {
                if (!IsActive || bossMain == null)
                {
                    return;
                }

                if (countedDeadBosses.Contains(bossMain))
                {
                    return;
                }

                countedDeadBosses.Add(bossMain);

                // 无间炼狱：先累加现金池
                if (infiniteHellMode)
                {
                    try
                    {
                        float maxHp = 0f;
                        if (bossMain.Health != null)
                        {
                            maxHp = bossMain.Health.MaxHealth;
                        }
                        if (maxHp < 0f) maxHp = 0f;
                        long reward = (long)Mathf.Round(maxHp * 10f);
                        if (reward < 0L) reward = 0L;
                        infiniteHellCashPool += reward;
                        infiniteHellWaveCashThisWave += reward;
                    }
                    catch {}
                }

                if (bossesPerWave > 1 && currentWaveBosses != null && currentWaveBosses.Count > 0)
                {
                    for (int i = 0; i < currentWaveBosses.Count; i++)
                    {
                        MonoBehaviour boss = currentWaveBosses[i];
                        if (boss == null)
                        {
                            continue;
                        }

                        CharacterMainControl bossCharacter = null;
                        try
                        {
                            bossCharacter = boss as CharacterMainControl;
                        }
                        catch {}

                        if (bossCharacter == bossMain)
                        {
                            currentWaveBosses.RemoveAt(i);
                            break;
                        }
                    }
                }

                defeatedEnemies++;

                if (bossesPerWave > 1)
                {
                    bossesInCurrentWaveRemaining = Mathf.Max(0, bossesInCurrentWaveRemaining - 1);

                    if (bossesInCurrentWaveRemaining <= 0)
                    {
                        ProceedAfterWaveFinished();
                        return;
                    }
                }
                else
                {
                    // 单Boss模式：击杀后直接推进到下一波
                    ProceedAfterWaveFinished();
                    return;
                }
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// 当当前波所有Boss被击杀或因生成失败/异常被跳过时，推进到下一波或结束挑战
        /// </summary>
        private void ProceedAfterWaveFinished()
        {
            try
            {
                // 通知快递员 Boss 战结束
                NotifyCourierBossFightEnd();
                
                // 通知快递员当前没有Boss（召唤间隔期间）
                NotifyCourierNoBoss(true);
                
                currentEnemyIndex++;
                currentBoss = null;

                if (infiniteHellMode)
                {
                    // 无间炼狱：统一走专用逻辑
                    OnInfiniteHellWaveCompleted();
                    return;
                }

                // 使用过滤后的 Boss 列表判断是否还有下一波
                var filteredPresets = GetFilteredEnemyPresets();
                int presetCount = (filteredPresets != null) ? filteredPresets.Count : 0;
                if (currentEnemyIndex < presetCount)
                {
                    // 注意：清空箱子选项已移至垃圾桶交互物，不再在路牌上添加
                    // try
                    // {
                    //     if (bossRushSignInteract != null)
                    //     {
                    //         bossRushSignInteract.AddClearLootboxOptions();
                    //     }
                    // }
                    // catch {}

                    if (config != null && config.useInteractBetweenWaves)
                    {
                        try
                        {
                            if (bossRushSignInteract != null)
                            {
                                bossRushSignInteract.SetNextWaveMode();
                            }
                        }
                        catch {}
                    }
                    else
                    {
                        StartNextWaveCountdown();
                    }
                }
                else
                {
                    OnAllEnemiesDefeated();
                }
            }
            catch {}
        }

        /// <summary>
        /// Boss 在生成阶段失败时的统一处理：修正当前波计数并在必要时推进波次
        /// </summary>
        private void OnBossSpawnFailed(EnemyPresetInfo preset)
        {
            try
            {
                // 记录日志方便排查
                try
                {
                    string name = (preset != null ? preset.displayName : "<null>");
                    DevLog("[BossRush] OnBossSpawnFailed: Boss 生成失败, preset=" + name);
                }
                catch {}

                // 递增已击败敌人数，保持总数一致
                defeatedEnemies++;

                if (bossesPerWave > 1)
                {
                    // 多Boss模式：减少当前波剩余Boss数量
                    bossesInCurrentWaveRemaining = Mathf.Max(0, bossesInCurrentWaveRemaining - 1);

                    if (bossesInCurrentWaveRemaining <= 0)
                    {
                        ProceedAfterWaveFinished();
                    }
                }
                else
                {
                    // 单Boss模式：视为跳过该敌人，直接进入下一波
                    ProceedAfterWaveFinished();
                }
            }
            catch {}
        }

        /// <summary>
        /// 初始化敌人预设列表 - 动态识别所有显示名字的敌人
        /// [性能优化] 添加初始化标记，避免每次传送都重复扫描
        /// </summary>
        private void InitializeEnemyPresets()
        {
            // [性能优化] 如果已经初始化过，跳过重复扫描
            if (_enemyPresetsInitialized && enemyPresets != null && enemyPresets.Count > 0)
            {
                DevLog("[BossRush] 敌人预设已初始化，跳过重复扫描 (共 " + enemyPresets.Count + " 个)");
                return;
            }
            
            enemyPresets.Clear();;
            
            // 获取所有可能的敌人类型
            var enemyTypes = new List<EnemyPresetInfo>();
            
            // 仅通过游戏内的角色预设动态发现敌人类型
            TryDiscoverAdditionalEnemies(enemyTypes);
            
            // 按团队类型和基础生命值排序，使用排除法过滤（排除玩家和中立阵营）
            // 这样可以兼容其他mod添加的自定义敌对阵营
            enemyPresets = enemyTypes
                .Where(e =>
                    e.team != (int)Teams.player    // 排除玩家阵营
                    && e.team != (int)Teams.middle // 排除中立阵营
                    && e.baseHealth > 100f)
                .OrderBy(e => e.team)
                .ThenBy(e => e.baseHealth)
                .ToList();
            
            // 注册龙裔遗族Boss
            RegisterDragonDescendantPreset();

            // 计算 Boss 池基础血量范围
            try
            {
                if (enemyPresets != null && enemyPresets.Count > 0)
                {
                    float minH = float.MaxValue;
                    float maxH = 0f;
                    for (int i = 0; i < enemyPresets.Count; i++)
                    {
                        float h = enemyPresets[i].baseHealth;
                        if (h <= 0f)
                        {
                            continue;
                        }
                        if (h < minH)
                        {
                            minH = h;
                        }
                        if (h > maxH)
                        {
                            maxH = h;
                        }
                    }

                    if (minH < float.MaxValue && maxH > 0f && maxH >= minH)
                    {
                        minBossBaseHealth = minH;
                        maxBossBaseHealth = maxH;
                        DevLog("[BossRush] Boss池基础血量范围: " + minBossBaseHealth + " ~ " + maxBossBaseHealth);
                    }
                }
            }
            catch {}

            DevLog("[BossRush] 初始化完成，共发现 " + enemyPresets.Count + " 个敌人类型");
            
            // [性能优化] 标记初始化完成，后续传送不再重复扫描
            _enemyPresetsInitialized = true;
        }

        /// <summary>
        /// 无间炼狱模式下按权重随机选取一个敌人预设
        /// 权重根据基础血量与波次线性放大，高血量Boss在后期权重更高
        /// </summary>
        private EnemyPresetInfo PickRandomEnemyForInfiniteHell()
        {
            // 使用过滤后的 Boss 列表
            var filteredPresets = GetFilteredEnemyPresets();
            if (filteredPresets == null || filteredPresets.Count == 0)
            {
                return null;
            }

            float refMin = minBossBaseHealth;
            float refMax = maxBossBaseHealth;

            // 如果没有有效范围，退化为等概率随机
            if (!(refMax > refMin && refMin > 0f))
            {
                int idx = UnityEngine.Random.Range(0, filteredPresets.Count);
                return filteredPresets[idx];
            }

            // 计算每个Boss的权重
            float totalWeight = 0f;
            float[] weights = new float[filteredPresets.Count];
            // 基础系数：t * baseK + (wave/50)*t，t 为基础血量归一化
            const float baseK = 4f;
            float waveTerm = (float)infiniteHellWaveIndex / 50f;

            for (int i = 0; i < filteredPresets.Count; i++)
            {
                float h = filteredPresets[i].baseHealth;
                if (h <= 0f)
                {
                    h = refMin;
                }

                float t = Mathf.Clamp01((h - refMin) / (refMax - refMin));
                float w = 1f + t * baseK + waveTerm * t;
                if (w < 0.01f)
                {
                    w = 0.01f;
                }

                weights[i] = w;
                totalWeight += w;
            }

            if (totalWeight <= 0f)
            {
                int idx = UnityEngine.Random.Range(0, filteredPresets.Count);
                return filteredPresets[idx];
            }

            // 按累计权重抽样
            float r = UnityEngine.Random.value * totalWeight;
            float acc = 0f;
            for (int i = 0; i < filteredPresets.Count; i++)
            {
                acc += weights[i];
                if (r <= acc)
                {
                    return filteredPresets[i];
                }
            }

            // 理论上不会到这里，兜底返回最后一个
            return filteredPresets[filteredPresets.Count - 1];
        }

        private void AddEnemyType(List<EnemyPresetInfo> list, string name, string displayName, int team, float health, float damage)
        {
            list.Add(new EnemyPresetInfo 
            { 
                name = name, 
                displayName = displayName, 
                team = team, 
                baseHealth = health, 
                baseDamage = damage 
            });
        }
        
        /// <summary>
        /// 尝试发现额外的敌人类型
        /// </summary>
        private void TryDiscoverAdditionalEnemies(List<EnemyPresetInfo> enemyList)
        {
            try
            {
                var allPresets = Resources.FindObjectsOfTypeAll<CharacterRandomPreset>();
                if (allPresets != null && allPresets.Length > 0)
                {
                    foreach (var preset in allPresets)
                    {
                        if (preset == null)
                        {
                            continue;
                        }

                        string nameKey = preset.nameKey;
                        if (string.IsNullOrEmpty(nameKey))
                        {
                            continue;
                        }

                        string displayName = GetLocalizedCharacterName(nameKey);
                        bool isSpecialUnknownBoss = nameKey == "Cname_Boss_Red";

                        if (!preset.showName && !isSpecialUnknownBoss)
                        {
                            continue;
                        }

                        if (enemyList.Any(e => e.name == nameKey))
                        {
                            continue;
                        }

                        int team = (int)preset.team;
                        float health = (preset.health > 0f) ? preset.health : 100f;
                        float damage = preset.damageMultiplier;

                        var newEnemy = new EnemyPresetInfo
                        {
                            name = nameKey,
                            displayName = displayName,
                            team = team,
                            baseHealth = health,
                            baseDamage = damage
                        };

                        enemyList.Add(newEnemy);
                        DevLog("[BossRush] 发现额外敌人类型: " + nameKey + " (team=" + team + ", health=" + health + ")");
                    }
                }
            }
            catch (Exception e)
            {
                DevLog("[BossRush] 动态发现敌人时出现异常: " + e.Message);
            }
        }

        private string GetLocalizedCharacterName(string nameKey)
        {
            if (string.IsNullOrEmpty(nameKey))
            {
                return nameKey;
            }

            try
            {
                string[] types = new string[]
                {
                    "SodaCraft.Localizations.LocalizationManager, SodaLocalization",
                    "SodaCraft.Localizations.LocalizationManager, TeamSoda.Duckov.Core",
                    "LocalizationManager, Assembly-CSharp"
                };

                Type locType = null;
                for (int i = 0; i < types.Length; i++)
                {
                    locType = Type.GetType(types[i]);
                    if (locType != null)
                    {
                        break;
                    }
                }

                if (locType != null)
                {
                    var method = locType.GetMethod("ToPlainText", BindingFlags.Static | BindingFlags.Public);
                    if (method != null)
                    {
                        object result = method.Invoke(null, new object[] { nameKey });
                        string str = result as string;
                        if (!string.IsNullOrEmpty(str))
                        {
                            return str;
                        }
                    }
                }
            }
            catch
            {
            }

            return nameKey;
        }

    }
}
