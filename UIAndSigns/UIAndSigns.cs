// ============================================================================
// UIAndSigns.cs - UI 和路牌系统
// ============================================================================
// 模块说明：
//   管理 BossRush 模组的 UI 显示和路牌交互，包括：
//   - 消息提示显示
//   - 大横幅通知
//   - 路牌创建和管理
//   - 敌人方位提示
//   
// 主要功能：
//   - ShowMessage: 显示普通消息提示
//   - ShowBigBanner: 显示大横幅通知
//   - TryCreateArenaDifficultyEntryPoint: 创建竞技场难度入口路牌
//   - ShowEnemyBanner: 显示敌人生成横幅（含方位信息）
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
    /// UI 和路牌系统模块
    /// </summary>
    public partial class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        #region UI 状态字段
        
        /// <summary>当前状态消息</summary>
        private string statusMessage = "";
        
        /// <summary>消息显示计时器</summary>
        private float messageTimer = 0f;
        
        #endregion
        
        #region 路牌辅助方法
        
        /// <summary>
        /// 移除 GameObject 上的刚体并将所有 Collider 设置为 Trigger，允许玩家穿过
        /// </summary>
        /// <param name="obj">目标 GameObject</param>
        private void RemoveRigidbodyAndSetTrigger(GameObject obj)
        {
            if (obj == null) return;
            
            try
            {
                // 移除所有刚体组件
                Rigidbody[] rigidbodies = obj.GetComponentsInChildren<Rigidbody>(true);
                foreach (var rb in rigidbodies)
                {
                    if (rb != null)
                    {
                        UnityEngine.Object.Destroy(rb);
                    }
                }
                
                // 将所有 Collider 设置为 Trigger 模式
                Collider[] colliders = obj.GetComponentsInChildren<Collider>(true);
                foreach (var col in colliders)
                {
                    if (col != null)
                    {
                        col.isTrigger = true;
                    }
                }
                
                DevLog("[BossRush] RemoveRigidbodyAndSetTrigger: 已移除 " + rigidbodies.Length + " 个刚体，设置 " + colliders.Length + " 个 Collider 为 Trigger");
            }
            catch (Exception e)
            {
                DevLog("[BossRush] [WARNING] RemoveRigidbodyAndSetTrigger 异常: " + e.Message);
            }
        }
        
        /// <summary>
        /// 查找距离指定位置最近的 BoxCollider
        /// </summary>
        /// <param name="position">参考位置</param>
        /// <param name="maxDistance">最大搜索距离，默认 50 米</param>
        /// <returns>最近的 BoxCollider，未找到则返回 null</returns>
        private BoxCollider FindNearestBoxCollider(Vector3 position, float maxDistance = 50f)
        {
            try
            {
                // 使用 ObjectCache 获取缓存的 BoxCollider 数组
                BoxCollider[] allBoxColliders = ObjectCache.GetBoxColliders();
                BoxCollider nearest = null;
                float nearestDistance = maxDistance;
                
                foreach (var boxCol in allBoxColliders)
                {
                    if (boxCol == null || boxCol.gameObject == null) continue;
                    
                    // 排除已禁用的、Trigger 类型的、以及我们自己创建的对象
                    if (!boxCol.enabled) continue;
                    if (boxCol.isTrigger) continue;
                    if (boxCol.gameObject.name.StartsWith("BossRush_")) continue;
                    
                    float distance = Vector3.Distance(position, boxCol.transform.position);
                    if (distance < nearestDistance)
                    {
                        nearestDistance = distance;
                        nearest = boxCol;
                    }
                }
                
                return nearest;
            }
            catch (Exception e)
            {
                DevLog("[BossRush] [WARNING] FindNearestBoxCollider 异常: " + e.Message);
                return null;
            }
        }
        
        #endregion
        
        #region 路牌引用
        
        /// <summary>BossRush 路牌交互组件引用</summary>
        private BossRushSignInteractable bossRushSignInteract = null;
        
        /// <summary>路牌原生 InteractableBase 引用（复用原有组件时使用）</summary>
        private InteractableBase _signInteractBase = null;
        // 路牌 GameObject 引用（用于添加下一波选项）
        private GameObject _bossRushSignGameObject = null;
        
        /// <summary>垃圾桶 GameObject 引用</summary>
        private GameObject _bossRushTrashCanGameObject = null;

        private static bool notificationDurationTweaked = false;
        
        #endregion
        
        #region 垃圾桶创建
        
        /// <summary>
        /// 在路牌旁边创建垃圾桶
        /// </summary>
        /// <param name="signpostPosition">路牌位置（地面位置，未经 Y 轴偏移）</param>
        /// <param name="signpostRotation">路牌旋转</param>
        /// <param name="placeOnRight">true=放在右边，false=放在前方（仅 DEMO 挑战地图使用前方）</param>
        private void CreateTrashCanNextToSignpost(Vector3 signpostPosition, Quaternion signpostRotation, bool placeOnRight = false)
        {
            try
            {
                string trashCanName = "BossRush_TrashCan";
                
                // 检查是否已存在
                GameObject existingTrashCan = GameObject.Find(trashCanName);
                if (existingTrashCan != null)
                {
                    DevLog("[BossRush] CreateTrashCanNextToSignpost: 垃圾桶已存在，跳过创建");
                    _bossRushTrashCanGameObject = existingTrashCan;
                    return;
                }
                
                // 计算偏移方向
                const float OFFSET_DISTANCE = 1.5f;
                Vector3 trashCanPos;
                if (placeOnRight)
                {
                    // 零度挑战等非 DEMO 地图：使用固定偏移方向 (1.30, 0, 0.65)，归一化后乘以距离
                    // 原始偏移向量：(226.55 - 225.25, 0, 287.40 - 286.75) = (1.30, 0, 0.65)
                    Vector3 rightOffset = new Vector3(1.30f, 0f, 0.65f).normalized;
                    trashCanPos = signpostPosition + rightOffset * OFFSET_DISTANCE;
                }
                else
                {
                    // DEMO 挑战地图：前方方向（考虑旋转）
                    Vector3 forwardDirection = signpostRotation * Vector3.forward;
                    trashCanPos = signpostPosition + forwardDirection * OFFSET_DISTANCE;
                }
                
                // 使用 EntityModelFactory 创建垃圾桶模型（旋转与路牌一致）
                GameObject trashCan = EntityModelFactory.CreateTrashCan(trashCanPos, signpostRotation);
                trashCan.name = trashCanName;
                
                // 检查是否为后备对象
                bool isFallback = trashCan.name.Contains("_Fallback") || trashCan.GetComponentInChildren<MeshRenderer>() == null;
                
                if (!isFallback)
                {
                    // 移除刚体，确保玩家可以穿过垃圾桶
                    RemoveRigidbodyAndSetTrigger(trashCan);
                }
                
                // 添加 BoxCollider 作为交互触发器
                BoxCollider col = trashCan.GetComponent<BoxCollider>();
                if (col == null)
                {
                    col = trashCan.AddComponent<BoxCollider>();
                }
                col.isTrigger = true;
                col.size = new Vector3(0.8f, 1.2f, 0.8f);
                
                // 添加 TrashCanInteractable 作为主交互
                var trashCanInteract = trashCan.AddComponent<TrashCanInteractable>();
                trashCanInteract.interactCollider = col;
                // 交互标记放在垃圾桶中部
                trashCanInteract.interactMarkerOffset = new Vector3(0f, 0f, 0f);
                
                // 保存引用
                _bossRushTrashCanGameObject = trashCan;
                
                string positionDesc = placeOnRight ? "右边" : "前方";
                DevLog("[BossRush] CreateTrashCanNextToSignpost: 成功创建垃圾桶（" + positionDesc + "），位置=" + trashCanPos);
            }
            catch (Exception e)
            {
                DevLog("[BossRush] [ERROR] 创建垃圾桶失败: " + e.Message);
            }
        }
        
        #endregion

        private void UpdateMessage_UIAndSigns()
        {
            if (messageTimer > 0)
            {
                messageTimer -= Time.deltaTime;
                // 这里应该调用游戏的UI显示消息，例如 NotificationText.ShowNext(statusMessage)
                // 由于未引用UI库，暂时只打印日志
            }
        }

        private void CreateRescueTeleportBubble_UIAndSigns()
        {
            try
            {
                string name = "BossRush_RescueTeleportBubble";
                GameObject existing = GameObject.Find(name);
                if (existing != null)
                {
                    return;
                }

                Vector3 pos = new Vector3(459.67f, -5.19f, 104.85f);
                // 仅在救援传送点附近创建一个路牌，并在路牌中部提供传送交互
                TryCreateRescueRoadsign_UIAndSigns(pos);
            }
            catch (Exception e)
            {
                DevLog("[BossRush] [ERROR] 创建救援传送气泡失败: " + e.Message);
            }
        }

        private void TryCreateRescueRoadsign_UIAndSigns(Vector3 position)
        {
            try
            {
                string signName = "BossRush_RescueRoadsign";
                GameObject existingSign = GameObject.Find(signName);
                if (existingSign != null)
                {
                    return;
                }

                // 直接使用传入的位置（自定义模型不需要偏移）
                Vector3 signPos = position;

                // 使用 EntityModelFactory 创建路牌模型（自定义模型不需要旋转）
                GameObject sign = EntityModelFactory.CreateSignpost(signPos);
                sign.name = signName;

                // 检查是否为后备对象
                bool isFallback = sign.name.Contains("_Fallback") || sign.GetComponentInChildren<MeshRenderer>() == null;
                
                if (!isFallback)
                {
                    // 移除刚体，确保玩家可以穿过路牌
                    RemoveRigidbodyAndSetTrigger(sign);
                }

                // 添加 BoxCollider 作为交互触发器
                BoxCollider col = sign.GetComponent<BoxCollider>();
                if (col == null)
                {
                    col = sign.AddComponent<BoxCollider>();
                }
                col.isTrigger = true;
                col.size = new Vector3(1f, 2f, 1f);

                // 在路牌上挂载传送交互
                var teleport = sign.AddComponent<BossRushTeleportBubble>();
                teleport.interactCollider = col;
                // 交互标记放在路牌中部（模型已向上偏移1米，所以这里用0）
                teleport.interactMarkerOffset = new Vector3(0f, 0f, 0f);

                DevLog("[BossRush] TryCreateRescueRoadsign: 成功创建传送路牌, 位置=" + signPos);
            }
            catch (Exception e)
            {
                DevLog("[BossRush] [ERROR] 创建传送路牌失败: " + e.Message);
            }
        }

        /// <summary>
        /// 创建初始传送气泡，用于在玩家被错误传送时提供返回目标点的功能
        /// 注：由于路牌上已有完整的交互选项，此气泡作为备用
        /// </summary>
        private void CreateInitialTeleportBubble_UIAndSigns()
        {
            // 注意：初始传送气泡功能已整合到路牌交互中，不再单独创建
            // 保留此方法以便将来扩展
            DevLog("[BossRush] CreateInitialTeleportBubble: 初始传送功能已整合到路牌交互中");
        }

        /// <summary>
        /// 备用方法：使用游戏内的 SimpleTeleport 模板克隆创建传送气泡
        /// </summary>
        private void CreateTeleportBubbleFromTemplate_UIAndSigns(Vector3 pos, string name)
        {
            try
            {
                GameObject existing = GameObject.Find(name);
                if (existing != null)
                {
                    DevLog("[BossRush] " + name + " 已存在，跳过创建");
                    return;
                }

                // 查找游戏内的 SimpleTeleport 模板
                GameObject template = null;
                try
                {
                    // 方法1：通过层级路径查找
                    GameObject teleportsGo = GameObject.Find("Teleports");
                    if (teleportsGo != null)
                    {
                        Transform bubblePair = teleportsGo.transform.Find("TeleporterBubblePair");
                        if (bubblePair != null)
                        {
                            Transform simpleTeleport = bubblePair.Find("SimpleTeleport");
                            if (simpleTeleport != null)
                            {
                                template = simpleTeleport.gameObject;
                                DevLog("[BossRush] 找到 SimpleTeleport 模板（层级路径）");
                            }
                        }
                    }

                    // 方法2：直接搜索 SimpleTeleporter 组件
                    if (template == null)
                    {
                        var teleporters = FindObjectsOfType<SimpleTeleporter>();
                        if (teleporters != null && teleporters.Length > 0)
                        {
                            template = teleporters[0].gameObject;
                            DevLog("[BossRush] 找到 SimpleTeleport 模板（组件搜索）");
                        }
                    }
                }
                catch {}

                if (template != null)
                {
                    // 克隆模板，只保留原有的传送功能
                    GameObject bubble = UnityEngine.Object.Instantiate(template);
                    bubble.name = name;
                    bubble.transform.position = pos;
                    bubble.transform.rotation = Quaternion.identity;

                    DevLog("[BossRush] 已使用 SimpleTeleport 模板创建传送气泡，位置=" + pos);
                }
                else
                {
                    // 兜底：使用 BossRushTeleportBubble 提供简单传送功能
                    GameObject bubble = new GameObject(name);
                    bubble.transform.position = pos;
                    bubble.transform.rotation = Quaternion.identity;

                    BoxCollider col = bubble.AddComponent<BoxCollider>();
                    col.isTrigger = true;

                    bubble.AddComponent<BossRushTeleportBubble>();

                    DevLog("[BossRush] 未找到 SimpleTeleport 模板，使用 BossRushTeleportBubble 作为兜底传送，位置=" + pos);
                }
            }
            catch (Exception e)
            {
                DevLog("[BossRush] [ERROR] 创建传送气泡失败: " + e.Message);
            }
        }

        private void ShowMessage_UIAndSigns(string msg)
        {
            statusMessage = msg;
            messageTimer = 3f;
            DevLog("[BossRush] UI提示: " + msg);
            
            // 使用缓存的反射调用 NotificationText.ShowNext
            try
            {
                if (ReflectionCache.NotificationText_ShowNext != null)
                {
                    ReflectionCache.NotificationText_ShowNext.Invoke(null, new object[] { msg });
                }
            }
            catch {}
        }
        
        /// <summary>
        /// 显示敌人生成横幅
        /// 单Boss模式：显示名字 + 方位
        /// 多Boss模式（同一波多个Boss同时刷新）：显示“已将你包围”提示，不显示方向
        /// </summary>
        private void ShowEnemyBanner_UIAndSigns(string enemyName, Vector3 enemyPos, Vector3 playerPos, int currentEnemyIndexParam, int totalEnemiesParam, bool infiniteHellModeParam, int infiniteHellWaveIndexParam, int bossesPerWaveParam)
        {
            try
            {
                // 使用当前索引和总数构造波次信息
                int waveIndex;
                string waveTotalText;

                if (infiniteHellModeParam)
                {
                    // 无间炼狱/无限波次：使用无限波次显示，第 x/∞ 波
                    waveIndex = Mathf.Max(1, infiniteHellWaveIndexParam + 1);
                    waveTotalText = "∞";
                }
                else
                {
                    // 普通模式：当 totalEnemiesParam<=0 时也视为无限，避免出现 1/0 这样的显示
                    int total = totalEnemiesParam;
                    if (total <= 0)
                    {
                        waveIndex = Mathf.Max(1, currentEnemyIndexParam + 1);
                        waveTotalText = "∞";
                    }
                    else
                    {
                        waveIndex = Mathf.Clamp(currentEnemyIndexParam + 1, 1, total);
                        waveTotalText = total.ToString();
                    }
                }

                string bannerText;

                if (bossesPerWaveParam > 1)
                {
                    // 多Boss模式：不显示方向，改为“xxx已将你包围”，其中名字和“包围”用红色
                    bannerText = L10n.T(
                        "第 " + waveIndex + "/" + waveTotalText + " 波: <color=red>" + enemyName + "</color> 已将你<color=red>包围</color>",
                        "Wave " + waveIndex + "/" + waveTotalText + ": <color=red>" + enemyName + "</color> has <color=red>surrounded</color> you"
                    );
                }
                else
                {
                    // 单Boss模式：保留原有方位提示
                    string direction = GetDirectionFromPlayer(enemyPos, playerPos);
                    string localizedDirection = L10n.Direction(direction);
                    bannerText = L10n.T(
                        "第 " + waveIndex + "/" + waveTotalText + " 波: <color=red>" + enemyName + "</color> 在 <color=yellow>" + direction + "</color> 方向",
                        "Wave " + waveIndex + "/" + waveTotalText + ": <color=red>" + enemyName + "</color> at <color=yellow>" + localizedDirection + "</color>"
                    );
                }

                // 使用游戏的UI系统显示大横幅
                ShowBigBanner(bannerText);
            }
            catch (Exception e)
            {
                DevLog("[BossRush] [ERROR] ShowEnemyBanner错误: " + e.Message);
            }
        }
        
        /// <summary>
        /// 确保通知横幅的显示时间不少于2秒
        /// </summary>
        private void EnsureNotificationDurationAtLeastTwoSeconds()
        {
            if (notificationDurationTweaked)
            {
                return;
            }

            notificationDurationTweaked = true;

            try
            {
                // 使用 ObjectCache 获取缓存的 NotificationText 实例
                NotificationText[] instances = ObjectCache.GetNotificationTexts();
                if (instances != null && instances.Length > 0)
                {
                    // 使用 ReflectionCache 获取缓存的 FieldInfo
                    FieldInfo durationField = ReflectionCache.NotificationText_Duration;
                    FieldInfo durationPendingField = ReflectionCache.NotificationText_DurationIfPending;

                    for (int i = 0; i < instances.Length; i++)
                    {
                        NotificationText inst = instances[i];
                        if (inst == null)
                        {
                            continue;
                        }

                        try
                        {
                            if (durationField != null)
                            {
                                float current = (float)durationField.GetValue(inst);
                                if (current < 2f)
                                {
                                    durationField.SetValue(inst, 2f);
                                }
                            }

                            if (durationPendingField != null)
                            {
                                float current2 = (float)durationPendingField.GetValue(inst);
                                if (current2 < 2f)
                                {
                                    durationPendingField.SetValue(inst, 2f);
                                }
                            }
                        }
                        catch
                        {
                        }
                    }
                }
            }
            catch (Exception e)
            {
                DevLog("[BossRush] [WARNING] 调整 NotificationText 持续时间失败: " + e.Message);
            }
        }

        /// <summary>
        /// 显示大横幅（使用游戏通知系统）
        /// </summary>
        private void ShowBigBanner_UIAndSigns(string text)
        {
            try
            {
                EnsureNotificationDurationAtLeastTwoSeconds();
                // 使用游戏的通知系统显示横幅
                NotificationText.Push(text);
                DevLog("[BossRush] 显示横幅: " + text);
            }
            catch (Exception e)
            {
                DevLog("[BossRush] [ERROR] ShowBigBanner错误: " + e.Message);
            }
        }

        /// <summary>
        /// 在默认位置（DEMO挑战场景）创建路牌
        /// </summary>
        private void TryCreateArenaDifficultyEntryPoint_UIAndSigns()
        {
            TryCreateArenaDifficultyEntryPoint_UIAndSigns(null);
        }
        
        /// <summary>
        /// 在指定位置创建 BossRush 难度选择路牌
        /// 使用 EntityModelFactory 加载自定义模型，不再依赖场景模板
        /// </summary>
        /// <param name="customPosition">自定义位置，为 null 时使用默认位置（DEMO挑战场景）</param>
        private void TryCreateArenaDifficultyEntryPoint_UIAndSigns(Vector3? customPosition)
        {
            try
            {
                string currentScene = SceneManager.GetActiveScene().name;
                bool isChallengeSnow = (currentScene == "Level_ChallengeSnow");
                
                // Level_ChallengeSnow 场景：不创建路牌模型，但要创建隐形交互点和垃圾桶
                if (isChallengeSnow)
                {
                    DevLog("[BossRush] TryCreateArenaDifficultyEntryPoint: Level_ChallengeSnow 场景，创建隐形交互点和垃圾桶");
                    CreateInvisibleEntryPointForChallengeSnow(customPosition);
                    return;
                }
                
                // 从配置系统获取默认路牌位置
                Vector3 position;
                if (customPosition.HasValue)
                {
                    position = customPosition.Value;
                }
                else
                {
                    BossRushMapConfig currentConfig = GetCurrentMapConfig();
                    if (currentConfig != null && currentConfig.defaultSignPos.HasValue)
                    {
                        position = currentConfig.defaultSignPos.Value;
                    }
                    else
                    {
                        // 兜底：从配置系统获取当前地图的默认位置
                        position = GetCurrentSceneDefaultPosition();
                    }
                }
                string signName = "BossRush_Roadsign";

                // 检查是否已存在
                GameObject existingSign = GameObject.Find(signName);
                if (existingSign != null)
                {
                    DevLog("[BossRush] TryCreateArenaDifficultyEntryPoint: 路牌已存在，跳过创建");
                    // 尝试获取已有的交互组件
                    if (bossRushSignInteract == null)
                    {
                        bossRushSignInteract = existingSign.GetComponent<BossRushSignInteractable>();
                    }
                    return;
                }

                // 保存路牌的 InteractableBase 引用，供后续下一波注入使用
                bossRushSignInteract = null;

                // 直接使用配置的位置
                Vector3 signPos = position;

                // 根据场景决定路牌朝向
                // Demo 挑战地图 (Level_DemoChallenge_1) 使用默认朝向
                // 其他地图旋转使路牌面向玩家
                Quaternion signRotation = Quaternion.identity;
                if (currentScene != "Level_DemoChallenge_1")
                {
                    // 非 Demo 地图：Y 轴旋转 40 度，使路牌面向玩家
                    signRotation = Quaternion.Euler(0f, 60f, 0f);
                }

                // 使用 EntityModelFactory 创建路牌模型
                GameObject sign = EntityModelFactory.CreateSignpost(signPos, signRotation);
                sign.name = signName;

                // 检查是否为后备对象（名称包含 _Fallback）
                bool isFallback = sign.name.Contains("_Fallback") || sign.GetComponentInChildren<MeshRenderer>() == null;
                
                if (isFallback)
                {
                    DevLog("[BossRush] TryCreateArenaDifficultyEntryPoint: 使用后备交互点（无自定义模型）");
                }
                else
                {
                    DevLog("[BossRush] TryCreateArenaDifficultyEntryPoint: 使用自定义路牌模型");
                    // 移除刚体，确保玩家可以穿过路牌
                    RemoveRigidbodyAndSetTrigger(sign);
                }

                // 添加 BoxCollider 作为交互触发器
                BoxCollider col = sign.GetComponent<BoxCollider>();
                if (col == null)
                {
                    col = sign.AddComponent<BoxCollider>();
                }
                col.isTrigger = true;
                col.size = new Vector3(1f, 2f, 1f);

                // 添加 BossRushSignInteractable 作为主交互
                var signInteract = sign.AddComponent<BossRushSignInteractable>();
                signInteract.interactCollider = col;
                // 交互标记放在路牌中部（模型已向上偏移1米，所以这里用0）
                signInteract.interactMarkerOffset = new Vector3(0f, 0f, 0f);
                
                // 保存引用
                bossRushSignInteract = signInteract;
                _signInteractBase = signInteract;
                _bossRushSignGameObject = sign;
                
                // [性能优化] 设置竞技场中心位置（用于限制清理和禁用范围）
                _arenaCenter = signPos;
                _arenaCenterSet = true;

                // 创建垃圾桶：DEMO 挑战地图放前方，其他地图放右边
                bool placeTrashCanOnRight = (currentScene != "Level_DemoChallenge_1");
                CreateTrashCanNextToSignpost(signPos, signRotation, placeTrashCanOnRight);

                DevLog("[BossRush] TryCreateArenaDifficultyEntryPoint: 成功创建路牌交互，位置=" + signPos);
                DevLog("[BossRush] 已在 DEMO 挑战场景创建 BossRush 难度入口");
            }
            catch (Exception e)
            {
                DevLog("[BossRush] [ERROR] 创建 BossRush 难度入口失败: " + e.Message);
            }
        }

        /// <summary>
        /// 为零度挑战地图创建隐形交互点和垃圾桶
        /// 该场景不需要路牌模型，但需要交互点用于选择难度
        /// </summary>
        /// <param name="customPosition">自定义位置，为 null 时使用配置的默认位置</param>
        private void CreateInvisibleEntryPointForChallengeSnow(Vector3? customPosition)
        {
            try
            {
                // 获取交互点位置
                Vector3 position;
                if (customPosition.HasValue)
                {
                    position = customPosition.Value;
                }
                else
                {
                    BossRushMapConfig currentConfig = GetCurrentMapConfig();
                    if (currentConfig != null && currentConfig.defaultSignPos.HasValue)
                    {
                        position = currentConfig.defaultSignPos.Value;
                    }
                    else
                    {
                        // 兜底：使用默认位置
                        position = new Vector3(225.32f, 0.01f, 285.64f);
                    }
                }
                
                string signName = "BossRush_Roadsign";
                
                // 检查是否已存在
                GameObject existingSign = GameObject.Find(signName);
                if (existingSign != null)
                {
                    DevLog("[BossRush] CreateInvisibleEntryPointForChallengeSnow: 交互点已存在，跳过创建");
                    if (bossRushSignInteract == null)
                    {
                        bossRushSignInteract = existingSign.GetComponent<BossRushSignInteractable>();
                    }
                    return;
                }
                
                // 重置引用
                bossRushSignInteract = null;
                
                // 零度挑战地图：垃圾桶需要旋转90度面向玩家
                Quaternion signRotation = Quaternion.Euler(0f, 90f, 0f);
                
                // 创建隐形交互点（空 GameObject）
                GameObject invisibleSign = new GameObject(signName);
                invisibleSign.transform.position = position;
                invisibleSign.transform.rotation = signRotation;
                
                // 添加 BoxCollider 作为交互触发器（零度挑战地图使用更大的范围）
                BoxCollider col = invisibleSign.AddComponent<BoxCollider>();
                col.isTrigger = true;
                col.size = new Vector3(2f, 2f, 2f);  // 加大交互范围，方便玩家触发
                
                // 添加 BossRushSignInteractable 作为主交互
                var signInteract = invisibleSign.AddComponent<BossRushSignInteractable>();
                signInteract.interactCollider = col;
                signInteract.interactMarkerOffset = new Vector3(0f, 1f, 0f);  // 交互标记稍微抬高
                
                // 保存引用
                bossRushSignInteract = signInteract;
                _signInteractBase = signInteract;
                _bossRushSignGameObject = invisibleSign;
                
                // [性能优化] 设置竞技场中心位置（用于限制清理和禁用范围）
                _arenaCenter = position;
                _arenaCenterSet = true;
                
                DevLog("[BossRush] CreateInvisibleEntryPointForChallengeSnow: 成功创建隐形交互点，位置=" + position);
                
                // 在交互点右边创建垃圾桶（非 DEMO 地图统一放右边）
                CreateTrashCanNextToSignpost(position, signRotation, true);;
                
                DevLog("[BossRush] CreateInvisibleEntryPointForChallengeSnow: 零度挑战地图初始化完成");
            }
            catch (Exception e)
            {
                DevLog("[BossRush] [ERROR] CreateInvisibleEntryPointForChallengeSnow 失败: " + e.Message);
            }
        }

        private void TryCreateNextWaveEntryPoint_UIAndSigns()
        {
            try
            {
                // 优先使用路牌原生 InteractableBase
                if (_signInteractBase != null)
                {
                    if (AddNextWaveToSign())
                    {
                        DevLog("[BossRush] 已在路牌上添加下一波选项（原生InteractableBase）");
                        return;
                    }
                }

                // 其次使用 BossRushSignInteractable（仅添加下一波选项，不添加清空箱子）
                if (bossRushSignInteract != null)
                {
                    bossRushSignInteract.AddNextWaveOnly();
                    DevLog("[BossRush] 已在路牌上添加下一波选项（BossRushSignInteractable，仅下一波）");
                    return;
                }

                // 如果路牌不存在，则不创建地面交互点（避免重复）
                DevLog("[BossRush] TryCreateNextWaveEntryPoint: 未找到路牌引用，跳过创建");
            }
            catch (Exception e)
            {
                DevLog("[BossRush] [ERROR] 创建下一波交互点失败: " + e.Message);
            }
        }

        private System.Collections.IEnumerator EnsureArenaEntryPointCreated_UIAndSigns()
        {
            const string name = "BossRush_Roadsign";
            const float maxDuration = 30f;
            const float interval = 0.5f;
            float elapsed = 0f;
            int attempt = 0;

            while (elapsed < maxDuration)
            {
                attempt++;

                GameObject existing = null;
                string sceneName = string.Empty;
                Vector3 playerPos = Vector3.zero;

                try
                {
                    existing = GameObject.Find(name);
                }
                catch {}

                try
                {
                    sceneName = SceneManager.GetActiveScene().name;
                }
                catch {}

                try
                {
                    var main = CharacterMainControl.Main;
                    if (main != null)
                    {
                        playerPos = main.transform.position;
                    }
                }
                catch {}

                bool exists = (existing != null);
                DevLog("[BossRush] EnsureArenaEntryPoint: 第 " + attempt + " 次检查, scene=" + sceneName + ", elapsed=" + elapsed + ", exists=" + exists + ", playerPos=" + playerPos);

                if (exists)
                {
                    try
                    {
                        DevLog("[BossRush] EnsureArenaEntryPoint: 已确认 BossRush_Roadsign 存在, 位置=" + existing.transform.position + ", 总尝试次数=" + attempt + ", elapsed=" + elapsed + " 秒");
                    }
                    catch {}
                    yield break;
                }

                TryCreateArenaDifficultyEntryPoint();

                yield return new UnityEngine.WaitForSeconds(interval);
                elapsed += interval;
            }

            DevLog("[BossRush] EnsureArenaEntryPoint: 在 " + maxDuration + " 秒内未能创建 BossRush_Roadsign，放弃重试");
        }

        /// <summary>
        /// 向路牌添加下一波选项
        /// </summary>
        private bool AddNextWaveToSign()
        {
            try
            {
                GameObject parent = _bossRushSignGameObject;
                if (parent == null)
                {
                    return false;
                }

                // 检查是否已存在
                var existing = parent.GetComponentInChildren<BossRushNextWaveInteractable>();
                if (existing != null)
                {
                    return false;
                }

                GameObject nextObj = new GameObject("BossRushOption_NextWave");
                nextObj.transform.SetParent(parent.transform);
                nextObj.transform.localPosition = Vector3.zero;
                nextObj.AddComponent<BossRushNextWaveInteractable>();

                DevLog("[BossRush] AddNextWaveToSign: 成功添加下一波选项");
                return true;
            }
            catch (Exception e)
            {
                DevLog("[BossRush] [ERROR] AddNextWaveToSign 失败: " + e.Message);
                return false;
            }
        }

        private bool InjectIntoInteractableBaseGroup_UIAndSigns(InteractableBase target)
        {
            try
            {
                // 检查是否已经注入过
                var list = GetGroupList(target);
                if (list != null)
                {
                    foreach (var item in list)
                    {
                        if (item is BossRushInteractable)
                        {
                            return false;
                        }
                    }
                }

                // 确保 interactableGroup 为 true
                target.interactableGroup = true;

                // 使用缓存的 FieldInfo 获取 otherInterablesInGroup 字段
                var field = ReflectionCache.InteractableBase_OtherInterablesInGroup;
                if (field == null) return false;

                // 3. 获取列表值，如果为null则初始化
                list = field.GetValue(target) as List<InteractableBase>;
                if (list == null)
                {
                    list = new List<InteractableBase>();
                    field.SetValue(target, list);
                }

                // 根据当前场景决定注入内容：
                // - 在 BossRush 挑战场景 (Level_DemoChallenge_1) 里，注入两个难度选项：弹指可灭 / 有点意思
                // - 其它场景只注入一个默认 BossRush 选项（显示为“Boss Rush”），作为进入挑战地图的入口
                string sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
                // 使用配置系统判断是否在有效的 BossRush 竞技场场景
                if (IsCurrentSceneValidBossRushArena())
                {
                    // 弹指可灭（每波 1 个Boss）
                    GameObject easyObj = new GameObject("BossRushOption_Easy");
                    easyObj.transform.SetParent(target.transform);
                    easyObj.transform.localPosition = Vector3.zero;
                    easyObj.transform.localRotation = Quaternion.identity;
                    easyObj.transform.localScale = Vector3.one;

                    var easyInteract = easyObj.AddComponent<BossRushInteractable>();
                    easyInteract.useCustomName = true;
                    easyInteract.customName = "弹指可灭";
                    easyInteract.bossesPerWave = 1;
                    list.Add(easyInteract);

                    // 有点意思（每波 3 个相同Boss）
                    GameObject hardObj = new GameObject("BossRushOption_Hard");
                    hardObj.transform.SetParent(target.transform);
                    hardObj.transform.localPosition = Vector3.zero;
                    hardObj.transform.localRotation = Quaternion.identity;
                    hardObj.transform.localScale = Vector3.one;

                    var hardInteract = hardObj.AddComponent<BossRushInteractable>();
                    hardInteract.useCustomName = true;
                    hardInteract.customName = "有点意思";
                    hardInteract.bossesPerWave = 3;
                    list.Add(hardInteract);

                    DevLog("[BossRush] 成功注入 BossRush 难度选项到 " + target.name + " 的列表中！");
                    ShowMessage_UIAndSigns("BossRush 弹指可灭 / 有点意思 已添加到 " + target.name + "！");
                }
                else
                {
                    // 非 BossRush 场景：只注入一个默认 BossRush 入口
                    GameObject obj = new GameObject("BossRushOption");
                    obj.transform.SetParent(target.transform);
                    obj.transform.localPosition = Vector3.zero;
                    obj.transform.localRotation = Quaternion.identity;
                    obj.transform.localScale = Vector3.one;

                    var newInteract = obj.AddComponent<BossRushInteractable>();
                    // 不再使用 requireItem 扣费，改为通过 MapSelectionView 的 Cost 系统扣费
                    // 这样可以在玩家确认后才扣除船票
                    newInteract.requireItem = false;

                    list.Add(newInteract);

                    DevLog("[BossRush] 成功注入 BossRush 选项到 " + target.name + " 的列表中！");
                    ShowMessage_UIAndSigns("BossRush 挑战已就绪！");
                }
                return true;
            }
            catch (Exception e)
            {
                DevLog("[BossRush] [ERROR] 反射注入失败: " + e.Message);
                return false;
            }
        }

        private bool InjectBossRushOptionsIntoSign_UIAndSigns(InteractableBase target)
        {
            try
            {
                if (target == null)
                {
                    return false;
                }

                // 启用交互组
                target.interactableGroup = true;

                // 使用缓存的 FieldInfo
                var field = ReflectionCache.InteractableBase_OtherInterablesInGroup;
                if (field == null)
                {
                    DevLog("[BossRush] InjectBossRushOptionsIntoSign: 未找到 otherInterablesInGroup 字段");
                    return false;
                }

                var list = field.GetValue(target) as List<InteractableBase>;
                if (list == null)
                {
                    list = new List<InteractableBase>();
                    field.SetValue(target, list);
                }

                // 检查是否已存在
                foreach (var item in list)
                {
                    if (item is BossRushInteractable)
                    {
                        DevLog("[BossRush] InjectBossRushOptionsIntoSign: 选项已存在，跳过");
                        return false;
                    }
                }

                // 注：interactableGroup 的主交互不会触发 OnTimeOut
                // 所以需要将"哎哟~你干嘛~"作为第一个子选项

                // 1. 哎哟~你干嘛~（生小鸡）
                GameObject entryObj = new GameObject("BossRushOption_Entry");
                entryObj.transform.SetParent(target.transform);
                entryObj.transform.localPosition = Vector3.zero;
                var entryInteract = entryObj.AddComponent<BossRushEntryInteractable>();
                list.Add(entryInteract);

                // 2. 弹指可灭（每波 1 个Boss）
                GameObject easyObj = new GameObject("BossRushOption_Easy");
                easyObj.transform.SetParent(target.transform);
                easyObj.transform.localPosition = Vector3.zero;
                var easyInteract = easyObj.AddComponent<BossRushInteractable>();
                easyInteract.useCustomName = true;
                easyInteract.customName = "弹指可灭";
                easyInteract.bossesPerWave = 1;
                list.Add(easyInteract);

                // 3. 有点意思（每波 3 个Boss）
                GameObject hardObj = new GameObject("BossRushOption_Hard");
                hardObj.transform.SetParent(target.transform);
                hardObj.transform.localPosition = Vector3.zero;
                var hardInteract = hardObj.AddComponent<BossRushInteractable>();
                hardInteract.useCustomName = true;
                hardInteract.customName = "有点意思";
                hardInteract.bossesPerWave = 3;
                list.Add(hardInteract);

                // 4. 无间炼狱（每波使用配置中的Boss数量）
                GameObject hellObj = new GameObject("BossRushOption_InfiniteHell");
                hellObj.transform.SetParent(target.transform);
                hellObj.transform.localPosition = Vector3.zero;
                var hellInteract = hellObj.AddComponent<BossRushInteractable>();
                hellInteract.useCustomName = true;
                hellInteract.customName = "无间炼狱";
                hellInteract.isInfiniteHell = true;
                int hellBosses = 3;
                if (BossRush.ModBehaviour.Instance != null)
                {
                    hellBosses = BossRush.ModBehaviour.Instance.GetInfiniteHellBossesPerWaveFromConfig();
                }
                if (hellBosses < 1) hellBosses = 1;
                hellInteract.bossesPerWave = hellBosses;
                list.Add(hellInteract);

                DevLog("[BossRush] InjectBossRushOptionsIntoSign: 成功注入 4 个选项（生小鸡+两档难度+无间炼狱）");
                return true;
            }
            catch (Exception e)
            {
                DevLog("[BossRush] [ERROR] InjectBossRushOptionsIntoSign 失败: " + e.Message);
                return false;
            }
        }

        private bool InjectIntoInteractableBaseGroupWithEntry_UIAndSigns(InteractableBase target)
        {
            try
            {
                if (target == null)
                {
                    return false;
                }

                // 启用交互组
                target.interactableGroup = true;

                // 使用缓存的 FieldInfo
                var field = ReflectionCache.InteractableBase_OtherInterablesInGroup;
                if (field == null)
                {
                    DevLog("[BossRush] InjectIntoInteractableBaseGroupWithEntry: 未找到 otherInterablesInGroup 字段");
                    return false;
                }

                var list = field.GetValue(target) as List<InteractableBase>;
                if (list == null)
                {
                    list = new List<InteractableBase>();
                    field.SetValue(target, list);
                }

                // 检查是否已存在
                foreach (var item in list)
                {
                    if (item is BossRushInteractable)
                    {
                        DevLog("[BossRush] InjectIntoInteractableBaseGroupWithEntry: 选项已存在，跳过");
                        return false;
                    }
                }

                // 注：interactableGroup 的主交互不会触发 OnTimeOut
                // 所以需要将"哎哟~你干嘛~"作为第一个子选项

                // 1. 哎哟~你干嘛~（生小鸡）
                GameObject entryObj = new GameObject("BossRushOption_Entry");
                entryObj.transform.SetParent(target.transform);
                entryObj.transform.localPosition = Vector3.zero;
                entryObj.transform.localRotation = Quaternion.identity;
                entryObj.transform.localScale = Vector3.one;

                var entryInteract = entryObj.AddComponent<BossRushEntryInteractable>();
                list.Add(entryInteract);

                // 2. 弹指可灭（每波 1 个Boss）
                GameObject easyObj = new GameObject("BossRushOption_Easy");
                easyObj.transform.SetParent(target.transform);
                easyObj.transform.localPosition = Vector3.zero;
                easyObj.transform.localRotation = Quaternion.identity;
                easyObj.transform.localScale = Vector3.one;

                var easyInteract = easyObj.AddComponent<BossRushInteractable>();
                easyInteract.useCustomName = true;
                easyInteract.customName = "弹指可灭";
                easyInteract.bossesPerWave = 1;
                list.Add(easyInteract);

                // 3. 有点意思（每波 3 个相同Boss）
                GameObject hardObj = new GameObject("BossRushOption_Hard");
                hardObj.transform.SetParent(target.transform);
                hardObj.transform.localPosition = Vector3.zero;
                hardObj.transform.localRotation = Quaternion.identity;
                hardObj.transform.localScale = Vector3.one;

                var hardInteract = hardObj.AddComponent<BossRushInteractable>();
                hardInteract.useCustomName = true;
                hardInteract.customName = "有点意思";
                hardInteract.bossesPerWave = 3;
                list.Add(hardInteract);

                // 4. 无间炼狱（每波使用配置中的Boss数量）
                GameObject hellObj = new GameObject("BossRushOption_InfiniteHell");
                hellObj.transform.SetParent(target.transform);
                hellObj.transform.localPosition = Vector3.zero;
                hellObj.transform.localRotation = Quaternion.identity;
                hellObj.transform.localScale = Vector3.one;

                var hellInteract = hellObj.AddComponent<BossRushInteractable>();
                hellInteract.useCustomName = true;
                hellInteract.customName = "无间炼狱";
                hellInteract.isInfiniteHell = true;
                int hellBosses = 3;
                if (BossRush.ModBehaviour.Instance != null)
                {
                    hellBosses = BossRush.ModBehaviour.Instance.GetInfiniteHellBossesPerWaveFromConfig();
                }
                if (hellBosses < 1) hellBosses = 1;
                hellInteract.bossesPerWave = hellBosses;
                list.Add(hellInteract);

                DevLog("[BossRush] InjectIntoInteractableBaseGroupWithEntry: 成功注入 4 个选项（生小鸡+两档难度+无间炼狱）");
                return true;
            }
            catch (Exception e)
            {
                DevLog("[BossRush] [ERROR] InjectIntoInteractableBaseGroupWithEntry 失败: " + e.Message);
                return false;
            }
        }

        private bool InjectIntoMultiInteraction_UIAndSigns(MultiInteraction multi)
        {
            try
            {
                if (multi == null)
                {
                    return false;
                }

                // 使用缓存的 FieldInfo 获取私有字段 interactables
                var field = ReflectionCache.MultiInteraction_Interactables;
                if (field == null)
                {
                    return false;
                }

                var list = field.GetValue(multi) as List<InteractableBase>;
                if (list == null)
                {
                    return false;
                }

                // 检查是否已存在 BossRush 选项
                foreach (var item in list)
                {
                    if (item is BossRushInteractable)
                    {
                        return false;
                    }
                }

                // 根据当前场景决定注入内容：
                // - 在 BossRush 挑战场景 (Level_DemoChallenge_1) 里，注入三个难度选项：弹指可灭 / 有点意思 / 无间炼狱
                // - 其它场景只注入一个默认 BossRush 选项（显示为“Boss Rush”），作为进入挑战地图的入口
                string sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
                // 使用配置系统判断是否在有效的 BossRush 竞技场场景
                if (IsCurrentSceneValidBossRushArena())
                {
                    // 弹指可灭（每波 1 个Boss）
                    GameObject easyObj = new GameObject("BossRushOption_Easy");
                    easyObj.transform.SetParent(multi.transform);
                    easyObj.transform.localPosition = Vector3.zero;

                    var easyInteract = easyObj.AddComponent<BossRushInteractable>();
                    easyInteract.useCustomName = true;
                    easyInteract.customName = "弹指可灭";
                    easyInteract.bossesPerWave = 1;
                    list.Add(easyInteract);

                    // 有点意思（每波 3 个相同Boss）
                    GameObject hardObj = new GameObject("BossRushOption_Hard");
                    hardObj.transform.SetParent(multi.transform);
                    hardObj.transform.localPosition = Vector3.zero;

                    var hardInteract = hardObj.AddComponent<BossRushInteractable>();
                    hardInteract.useCustomName = true;
                    hardInteract.customName = "有点意思";
                    hardInteract.bossesPerWave = 3;
                    list.Add(hardInteract);

                    // 无间炼狱（每波使用配置中的Boss数量）
                    GameObject hellObj = new GameObject("BossRushOption_InfiniteHell");
                    hellObj.transform.SetParent(multi.transform);
                    hellObj.transform.localPosition = Vector3.zero;

                    var hellInteract = hellObj.AddComponent<BossRushInteractable>();
                    hellInteract.useCustomName = true;
                    hellInteract.customName = "无间炼狱";
                    hellInteract.isInfiniteHell = true;
                    int hellBosses = 3;
                    if (BossRush.ModBehaviour.Instance != null)
                    {
                        hellBosses = BossRush.ModBehaviour.Instance.GetInfiniteHellBossesPerWaveFromConfig();
                    }
                    if (hellBosses < 1) hellBosses = 1;
                    hellInteract.bossesPerWave = hellBosses;
                    list.Add(hellInteract);

                    DevLog("[BossRush] 成功向 MultiInteraction 注入 BossRush 难度选项: " + multi.name);
                }
                else
                {
                    // 非 BossRush 场景：只注入一个默认 BossRush 入口
                    GameObject obj = new GameObject("BossRushOption");
                    obj.transform.SetParent(multi.transform);
                    obj.transform.localPosition = Vector3.zero;

                    var newInteract = obj.AddComponent<BossRushInteractable>();
                    // 不再使用 requireItem 扣费，改为通过 MapSelectionView 的 Cost 系统扣费
                    // 这样可以在玩家确认后才扣除船票
                    newInteract.requireItem = false;

                    list.Add(newInteract);

                    DevLog("[BossRush] 成功注入 BossRush 选项到 MultiInteraction: " + multi.name);
                    ShowMessage_UIAndSigns("BossRush 选项已添加到 " + multi.name);
                }

                return true;
            }
            catch (Exception e)
            {
                DevLog("[BossRush] [ERROR] 注入 MultiInteraction 失败: " + e.Message);
                return false;
            }
        }
    }
}
