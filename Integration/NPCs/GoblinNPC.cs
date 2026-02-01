// ============================================================================
// GoblinNPC.cs - 哥布林NPC系统
// ============================================================================
// 模块说明：
//   管理 BossRush 模组的哥布林 NPC，包括：
//   - 从 AssetBundle 加载哥布林模型
//   - 动画状态管理（Walking、Running、Stop）
//   - 玩家召唤后跑向玩家并急停
//   - 使用 A* Pathfinding Seeker 进行寻路（与游戏原生系统一致）
// ============================================================================

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Pathfinding;
using Duckov.UI.DialogueBubbles;

namespace BossRush
{
    /// <summary>
    /// 哥布林NPC系统 - ModBehaviour 的 partial class
    /// </summary>
    public partial class ModBehaviour
    {
        // ============================================================================
        // 哥布林实例和资源
        // ============================================================================
        
        // 哥布林实例
        private GameObject goblinNPCInstance = null;
        private GoblinNPCController goblinController = null;
        
        // AssetBundle 缓存
        private static AssetBundle goblinAssetBundle = null;
        private static GameObject goblinPrefab = null;
        
        /// <summary>
        /// 加载哥布林 AssetBundle
        /// </summary>
        private bool LoadGoblinAssetBundle()
        {
            if (goblinPrefab != null)
            {
                DevLog("[GoblinNPC] 预制体已缓存，跳过加载");
                return true;
            }
            
            try
            {
                string assemblyLocation = typeof(ModBehaviour).Assembly.Location;
                string modDir = System.IO.Path.GetDirectoryName(assemblyLocation);
                string bundlePath = System.IO.Path.Combine(modDir, "Assets", "npcs", "goblinnpc");
                
                DevLog("[GoblinNPC] 尝试加载 AssetBundle: " + bundlePath);
                
                if (!File.Exists(bundlePath))
                {
                    DevLog("[GoblinNPC] 错误：未找到 goblinnpc AssetBundle 文件: " + bundlePath);
                    return false;
                }
                
                // 如果之前加载过但预制体为空，先卸载
                if (goblinAssetBundle != null)
                {
                    goblinAssetBundle.Unload(false);
                    goblinAssetBundle = null;
                }
                
                // 直接使用 AssetBundle API 加载
                goblinAssetBundle = AssetBundle.LoadFromFile(bundlePath);
                if (goblinAssetBundle == null)
                {
                    DevLog("[GoblinNPC] 错误：加载 AssetBundle 失败（可能已被加载或文件损坏）: " + bundlePath);
                    return false;
                }
                
                DevLog("[GoblinNPC] AssetBundle 加载成功，开始查找预制体...");
                
                // 列出所有资源名称用于调试
                string[] assetNames = goblinAssetBundle.GetAllAssetNames();
                DevLog("[GoblinNPC] AssetBundle 包含 " + assetNames.Length + " 个资源:");
                foreach (string name in assetNames)
                {
                    DevLog("[GoblinNPC]   - " + name);
                }
                
                // 尝试加载名为 GoblinNPC 的预制体
                goblinPrefab = goblinAssetBundle.LoadAsset<GameObject>("GoblinNPC");
                
                // 如果没找到，尝试其他常见名称
                if (goblinPrefab == null)
                {
                    DevLog("[GoblinNPC] 未找到 'GoblinNPC'，尝试其他名称...");
                    goblinPrefab = goblinAssetBundle.LoadAsset<GameObject>("goblinnpc");
                }
                
                // 如果还是没找到，加载第一个 GameObject
                if (goblinPrefab == null)
                {
                    DevLog("[GoblinNPC] 尝试加载所有 GameObject...");
                    GameObject[] allPrefabs = goblinAssetBundle.LoadAllAssets<GameObject>();
                    if (allPrefabs != null && allPrefabs.Length > 0)
                    {
                        goblinPrefab = allPrefabs[0];
                        DevLog("[GoblinNPC] 使用第一个 GameObject: " + goblinPrefab.name);
                    }
                }
                
                if (goblinPrefab == null)
                {
                    DevLog("[GoblinNPC] 错误：AssetBundle 中未找到任何 GameObject 预制体");
                    return false;
                }
                
                // 检查预制体的组件
                DevLog("[GoblinNPC] 成功加载哥布林预制体: " + goblinPrefab.name);
                Animator animator = goblinPrefab.GetComponentInChildren<Animator>();
                if (animator != null)
                {
                    DevLog("[GoblinNPC] 预制体包含 Animator 组件");
                    if (animator.runtimeAnimatorController != null)
                    {
                        DevLog("[GoblinNPC] Animator Controller: " + animator.runtimeAnimatorController.name);
                    }
                    else
                    {
                        DevLog("[GoblinNPC] 警告：Animator 没有 Controller！动画可能无法播放");
                    }
                }
                else
                {
                    DevLog("[GoblinNPC] 警告：预制体没有 Animator 组件！");
                }
                
                return true;
            }
            catch (Exception e)
            {
                DevLog("[GoblinNPC] 加载 AssetBundle 出错: " + e.Message + "\n" + e.StackTrace);
                return false;
            }
        }
        
        // ============================================================================
        // 刷新位置辅助方法
        // ============================================================================
        
        /// <summary>
        /// 获取哥布林刷新位置（避开快递员位置）
        /// 使用 NPCSpawnConfig 中的配置
        /// </summary>
        private Vector3 GetGoblinSpawnPosition(string sceneName)
        {
            Vector3 courierPosition = Vector3.zero;
            
            // 获取快递员位置（如果存在）
            try
            {
                if (courierNPCInstance != null)
                {
                    courierPosition = courierNPCInstance.transform.position;
                    DevLog("[GoblinNPC] 检测到快递员位置: " + courierPosition + "，将避开此位置");
                }
            }
            catch (Exception e)
            {
                DevLog("[GoblinNPC] 获取快递员位置失败: " + e.Message);
            }
            
            // 从配置中查询哥布林刷新位置（传入快递员位置以避免重复）
            if (NPCSpawnConfig.TryGetGoblinSpawnPosition(sceneName, out Vector3 position, courierPosition, 10f))
            {
                if (courierPosition != Vector3.zero)
                {
                    float distance = Vector3.Distance(position, courierPosition);
                    DevLog("[GoblinNPC] 刷新位置与快递员距离: " + distance.ToString("F1") + "米");
                }
                return position;
            }
            
            // 未配置的场景使用随机刷新点
            Vector3[] spawnPoints = GetCurrentSceneSpawnPoints();
            if (spawnPoints != null && spawnPoints.Length > 0)
            {
                int randomIndex = UnityEngine.Random.Range(0, spawnPoints.Length);
                DevLog("[GoblinNPC] 随机刷新点 [" + randomIndex + "/" + spawnPoints.Length + "]");
                return spawnPoints[randomIndex];
            }
            return Vector3.zero;
        }
        
        /// <summary>
        /// 检查当前场景是否应该生成哥布林
        /// </summary>
        private bool ShouldSpawnGoblin(string sceneName)
        {
            return NPCSpawnConfig.HasGoblinConfig(sceneName);
        }
        
        /// <summary>
        /// 生成哥布林 NPC
        /// </summary>
        public void SpawnGoblinNPC()
        {
            DevLog("[GoblinNPC] 开始生成哥布林...");
            
            // 如果已经存在，不重复生成
            if (goblinNPCInstance != null)
            {
                DevLog("[GoblinNPC] 哥布林已存在，跳过生成");
                return;
            }
            
            // 加载 AssetBundle
            if (!LoadGoblinAssetBundle())
            {
                DevLog("[GoblinNPC] 无法加载哥布林资源，跳过生成");
                return;
            }
            
            // 获取生成位置
            string currentSceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            
            // 检查场景是否配置了哥布林刷新点
            if (!ShouldSpawnGoblin(currentSceneName))
            {
                DevLog("[GoblinNPC] 场景 " + currentSceneName + " 未配置哥布林刷新点，跳过生成");
                return;
            }
            
            Vector3 spawnPos = GetGoblinSpawnPosition(currentSceneName);
            DevLog("[GoblinNPC] 场景: " + currentSceneName + ", 位置: " + spawnPos);
            
            // 检查是否获取到有效位置
            if (spawnPos == Vector3.zero)
            {
                DevLog("[GoblinNPC] 无法获取刷新点，跳过生成");
                return;
            }
            
            // 使用 Raycast 修正落点到地面
            RaycastHit hit;
            if (Physics.Raycast(spawnPos + Vector3.up * 5f, Vector3.down, out hit, 20f))
            {
                spawnPos = hit.point + new Vector3(0f, 0.1f, 0f);
                DevLog("[GoblinNPC] Raycast修正后位置: " + spawnPos);
            }
            else
            {
                DevLog("[GoblinNPC] Raycast修正失败，使用原始坐标: " + spawnPos);
            }
            
            try
            {
                // 实例化预制体
                goblinNPCInstance = UnityEngine.Object.Instantiate(goblinPrefab, spawnPos, Quaternion.identity);
                goblinNPCInstance.name = "GoblinNPC_BossRush";
                DevLog("[GoblinNPC] 预制体实例化成功");
                
                // 确保所有子对象都激活
                goblinNPCInstance.SetActive(true);
                foreach (Transform child in goblinNPCInstance.GetComponentsInChildren<Transform>(true))
                {
                    child.gameObject.SetActive(true);
                }
                
                // 修复 Shader（从 Standard 替换为游戏使用的 Shader）
                FixGoblinShaders(goblinNPCInstance);
                
                // 设置 Layer（确保渲染正确）
                SetGoblinLayerRecursively(goblinNPCInstance, LayerMask.NameToLayer("Default"));
                
                // 添加控制器组件
                goblinController = goblinNPCInstance.AddComponent<GoblinNPCController>();
                DevLog("[GoblinNPC] 控制器组件添加成功");
                
                // 添加移动控制组件
                GoblinMovement movement = goblinNPCInstance.AddComponent<GoblinMovement>();
                movement.SetSceneName(currentSceneName);
                DevLog("[GoblinNPC] 移动组件添加成功");
                
                // 添加交互组件（重铸服务）
                GoblinInteractable interactable = goblinNPCInstance.AddComponent<GoblinInteractable>();
                DevLog("[GoblinNPC] 交互组件添加成功");
                
                DevLog("[GoblinNPC] 哥布林生成成功，位置: " + spawnPos);
            }
            catch (Exception e)
            {
                DevLog("[GoblinNPC] 生成哥布林出错: " + e.Message + "\n" + e.StackTrace);
            }
        }
        
        /// <summary>
        /// 修复哥布林模型的 Shader（从 Standard 替换为游戏 Shader）
        /// </summary>
        private void FixGoblinShaders(GameObject obj)
        {
            try
            {
                // 尝试获取游戏使用的 Shader
                Shader gameShader = Shader.Find("SodaCraft/SodaCharacter");
                if (gameShader == null)
                {
                    gameShader = Shader.Find("Standard");
                }
                
                Renderer[] renderers = obj.GetComponentsInChildren<Renderer>(true);
                foreach (Renderer renderer in renderers)
                {
                    if (renderer.materials != null)
                    {
                        foreach (Material mat in renderer.materials)
                        {
                            if (mat != null && mat.shader != null)
                            {
                                string shaderName = mat.shader.name;
                                // 如果是 Standard shader，替换为游戏 shader
                                if (shaderName == "Standard" || shaderName.Contains("Standard"))
                                {
                                    if (gameShader != null)
                                    {
                                        mat.shader = gameShader;
                                        DevLog("[GoblinNPC] 替换 Shader: " + shaderName + " -> " + gameShader.name);
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                DevLog("[GoblinNPC] 修复 Shader 出错: " + e.Message);
            }
        }
        
        /// <summary>
        /// 递归设置 Layer
        /// </summary>
        private void SetGoblinLayerRecursively(GameObject obj, int layer)
        {
            obj.layer = layer;
            foreach (Transform child in obj.transform)
            {
                SetGoblinLayerRecursively(child.gameObject, layer);
            }
        }
        
        /// <summary>
        /// 销毁哥布林 NPC
        /// </summary>
        public void DestroyGoblinNPC()
        {
            if (goblinNPCInstance != null)
            {
                UnityEngine.Object.Destroy(goblinNPCInstance);
                goblinNPCInstance = null;
                goblinController = null;
                DevLog("[GoblinNPC] 哥布林已销毁");
            }
        }
        
        /// <summary>
        /// 召唤哥布林跑向玩家
        /// 当玩家使用特定物品时调用此方法
        /// </summary>
        public void SummonGoblin()
        {
            if (goblinController != null)
            {
                goblinController.RunToPlayer();
                DevLog("[GoblinNPC] 哥布林被召唤，开始跑向玩家");
            }
            else
            {
                DevLog("[GoblinNPC] 哥布林控制器不存在，无法召唤");
            }
        }
        
        /// <summary>
        /// 获取哥布林NPC实例
        /// </summary>
        public GoblinNPCController GetGoblinController()
        {
            return goblinController;
        }
    }

    
    /// <summary>
    /// 哥布林NPC动画控制器
    /// 管理 Walking、Running、Stop 三种动画状态
    /// </summary>
    public class GoblinNPCController : MonoBehaviour
    {
        private Animator animator;
        private Transform playerTransform;
        private bool hasAnimator = false;
        
        // 名字标签
        private GameObject nameTagObject;
        private TMPro.TextMeshPro nameTagText;
        private const float NAME_TAG_HEIGHT = 2.3f;  // 名字标签高度（头顶上方，与快递员一致）
        
        // 距离阈值（米）
        private const float BRAKE_ANIMATION_DISTANCE = 3f;  // 距离玩家3米时播放急停动画
        private const float STOP_DISTANCE = 1f;  // 距离玩家1米时真正停下
        
        // 状态
        private bool isRunningToPlayer = false;
        private bool isInDialogue = false;  // 是否在对话中
        private bool isIdling = false;  // 是否在待机
        private bool isBraking = false;  // 是否正在播放急停动画（但还在移动）
        private GoblinMovement movement;
        
        // 待机时间配置
        private const float IDLE_DURATION_AFTER_STOP = 3f;  // 急停后待机时间
        
        // 动画参数哈希值（与文档定义一致）
        private static readonly int hash_IsRunning = Animator.StringToHash("IsRunning");
        private static readonly int hash_DoStop = Animator.StringToHash("DoStop");
        private static readonly int hash_MoveSpeed = Animator.StringToHash("MoveSpeed");
        private static readonly int hash_IsIdle = Animator.StringToHash("IsIdle");  // 待机动画
        
        void Awake()
        {
            animator = GetComponentInChildren<Animator>();
            hasAnimator = animator != null;
            
            if (hasAnimator)
            {
                ModBehaviour.DevLog("[GoblinNPC] Controller.Awake: 找到 Animator 组件");
                if (animator.runtimeAnimatorController != null)
                {
                    ModBehaviour.DevLog("[GoblinNPC] Animator Controller: " + animator.runtimeAnimatorController.name);
                    // 列出所有参数
                    foreach (var param in animator.parameters)
                    {
                        ModBehaviour.DevLog("[GoblinNPC]   参数: " + param.name + " (" + param.type + ")");
                    }
                }
                else
                {
                    ModBehaviour.DevLog("[GoblinNPC] [WARNING] 警告：Animator 没有 RuntimeAnimatorController！");
                }
            }
            else
            {
                ModBehaviour.DevLog("[GoblinNPC] [WARNING] Controller.Awake: 未找到 Animator 组件！");
            }
        }
        
        void Start()
        {
            // 获取玩家引用
            try
            {
                if (CharacterMainControl.Main != null)
                {
                    playerTransform = CharacterMainControl.Main.transform;
                    ModBehaviour.DevLog("[GoblinNPC] Controller.Start: 获取到玩家引用");
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[GoblinNPC] [WARNING] 获取 CharacterMainControl.Main 失败: " + e.Message);
            }
            
            if (playerTransform == null)
            {
                var player = GameObject.FindGameObjectWithTag("Player");
                if (player != null)
                {
                    playerTransform = player.transform;
                    ModBehaviour.DevLog("[GoblinNPC] Controller.Start: 通过 Tag 获取到玩家引用");
                }
            }
            
            // 获取移动组件引用
            movement = GetComponent<GoblinMovement>();
            
            // 初始化动画参数（初始为走路状态）
            SafeSetBool(hash_IsRunning, false);
            SafeSetBool(hash_IsIdle, false);
            
            // 创建名字标签
            CreateNameTag();
            
            ModBehaviour.DevLog("[GoblinNPC] Controller.Start 完成");
        }
        
        /// <summary>
        /// 创建头顶名字标签（与快递员阿稳完全一致的设置）
        /// </summary>
        private void CreateNameTag()
        {
            try
            {
                // 创建名字标签对象
                nameTagObject = new GameObject("GoblinNameTag");
                nameTagObject.transform.SetParent(transform);
                nameTagObject.transform.localPosition = new Vector3(0f, NAME_TAG_HEIGHT, 0f);
                
                // 添加 TextMeshPro 组件
                nameTagText = nameTagObject.AddComponent<TMPro.TextMeshPro>();
                // 使用本地化名称
                string goblinName = "叮当";
                try
                {
                    goblinName = BossRush.L10n.T("叮当", "Dingdang");
                }
                catch
                {
                    // 如果本地化失败，使用默认名称
                }
                nameTagText.text = goblinName;
                nameTagText.fontSize = 4f;  // 与快递员阿稳一致
                nameTagText.alignment = TMPro.TextAlignmentOptions.Center;
                
                // 强制设置颜色为白色（与快递员阿稳一致）
                // 使用 Color32 确保精确的颜色值
                nameTagText.color = new Color(1f, 1f, 1f, 1f);
                nameTagText.faceColor = new Color32(255, 255, 255, 255);
                
                // 设置文字始终面向相机
                nameTagText.enableAutoSizing = false;
                
                // 设置排序层级确保可见
                nameTagText.sortingOrder = 100;
                
                // 禁用富文本以防止颜色标签影响
                nameTagText.richText = false;
                
                ModBehaviour.DevLog("[GoblinNPC] 名字标签创建成功: " + goblinName);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[GoblinNPC] [WARNING] 创建名字标签失败: " + e.Message);
            }
        }
        
        void LateUpdate()
        {
            // 让名字标签始终面向相机
            if (nameTagObject != null && Camera.main != null)
            {
                nameTagObject.transform.rotation = Camera.main.transform.rotation;
            }
        }
        
        void Update()
        {
            // 更新玩家引用
            if (playerTransform == null)
            {
                try
                {
                    if (CharacterMainControl.Main != null)
                    {
                        playerTransform = CharacterMainControl.Main.transform;
                    }
                }
                catch { }
            }
            
            // 如果正在跑向玩家，检查距离
            if (isRunningToPlayer && playerTransform != null)
            {
                float distance = Vector3.Distance(transform.position, playerTransform.position);
                
                // 距离3米时播放急停动画（但继续移动）
                if (!isBraking && distance <= BRAKE_ANIMATION_DISTANCE)
                {
                    StartBrakeAnimation();
                }
                
                // 距离1米时真正停下来
                if (distance <= STOP_DISTANCE)
                {
                    StopAndIdle();
                }
                // 如果移动已停止但距离玩家还较远，说明玩家移动了，需要重新寻路
                else if (movement != null && !movement.IsMoving && !movement.IsWaitingForPath && !isIdling)
                {
                    ModBehaviour.DevLog("[GoblinNPC] 玩家移动了，重新寻路到玩家位置");
                    isBraking = false;  // 重置急停状态
                    movement.RunToPlayer(playerTransform.position);
                }
            }
        }
        
        // 安全设置参数的辅助方法
        private void SafeSetBool(int hash, bool value)
        {
            try { if (animator != null) animator.SetBool(hash, value); } catch { }
        }
        
        private void SafeSetTrigger(int hash)
        {
            try { if (animator != null) animator.SetTrigger(hash); } catch { }
        }
        
        private void SafeSetFloat(int hash, float value)
        {
            try { if (animator != null) animator.SetFloat(hash, value); } catch { }
        }
        
        // ========== 公共接口 ==========
        
        /// <summary>
        /// 玩家使用物品召唤哥布林 - 哥布林跑向玩家
        /// </summary>
        public void RunToPlayer()
        {
            if (playerTransform == null)
            {
                ModBehaviour.DevLog("[GoblinNPC] RunToPlayer: 玩家引用为空");
                return;
            }
            
            isRunningToPlayer = true;
            
            // 设置跑步动画
            SafeSetBool(hash_IsRunning, true);
            
            // 通知移动组件跑向玩家
            if (movement != null)
            {
                movement.RunToPlayer(playerTransform.position);
            }
            
            ModBehaviour.DevLog("[GoblinNPC] 开始跑向玩家");
        }
        
        /// <summary>
        /// 开始播放急停动画（但继续移动）
        /// </summary>
        private void StartBrakeAnimation()
        {
            isBraking = true;
            
            // 触发急停动画（DoStop trigger）
            SafeSetTrigger(hash_DoStop);
            
            ModBehaviour.DevLog("[GoblinNPC] 距离3米，播放急停动画（继续移动）");
        }
        
        /// <summary>
        /// 到达玩家1米范围内，真正停下来并进入待机
        /// </summary>
        private void StopAndIdle()
        {
            isRunningToPlayer = false;
            isBraking = false;
            
            // 停止移动
            if (movement != null)
            {
                movement.StopMove();
            }
            
            // 面向玩家
            FacePlayer();
            
            // 停止跑步动画
            SafeSetBool(hash_IsRunning, false);
            
            ModBehaviour.DevLog("[GoblinNPC] 到达玩家1米范围，停止移动");
            
            // 进入待机并显示气泡
            StartCoroutine(IdleAndShowBubble());
        }
        
        /// <summary>
        /// 待机3秒并显示气泡
        /// </summary>
        private IEnumerator IdleAndShowBubble()
        {
            // 进入待机状态
            StartIdleAnimation();
            
            // 显示裂开的心气泡（使用文字代替emoji）
            ShowBrokenHeartBubble();
            
            ModBehaviour.DevLog("[GoblinNPC] 进入待机状态，显示气泡，持续3秒");
            
            // 待机3秒
            yield return new WaitForSeconds(IDLE_DURATION_AFTER_STOP);
            
            // 如果不在对话中，恢复走路
            if (!isInDialogue)
            {
                StopIdleAnimation();
                if (movement != null)
                {
                    movement.ResumeWalking();
                }
                ModBehaviour.DevLog("[GoblinNPC] 待机结束，恢复走路");
            }
            else
            {
                ModBehaviour.DevLog("[GoblinNPC] 待机结束但在对话中，继续待机");
            }
        }
        
        /// <summary>
        /// 显示裂开的心气泡
        /// 优先使用序列帧动画，如果没有则使用文字气泡
        /// </summary>
        private void ShowBrokenHeartBubble()
        {
            try
            {
                // 尝试使用序列帧动画
                if (TryShowBrokenHeartAnimation())
                {
                    ModBehaviour.DevLog("[GoblinNPC] 显示心裂开动画");
                    return;
                }
                
                // 回退：使用文字气泡
                // 使用中文"心碎"代替emoji（游戏字体不支持emoji）
                string brokenHeart = "心碎";
                DialogueBubblesManager.Show(
                    brokenHeart, 
                    transform, 
                    NAME_TAG_HEIGHT + 0.3f,  // 在名字标签上方
                    false,  // 不需要交互
                    false,  // 不可跳过
                    -1f,    // 默认速度
                    2.5f    // 显示2.5秒
                );
                ModBehaviour.DevLog("[GoblinNPC] 显示文字气泡（回退方案）");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[GoblinNPC] [WARNING] 显示气泡失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 尝试显示心裂开的序列帧动画
        /// </summary>
        /// <returns>是否成功显示</returns>
        private bool TryShowBrokenHeartAnimation()
        {
            try
            {
                // 尝试从AssetBundle加载序列帧
                string modDir = System.IO.Path.GetDirectoryName(typeof(ModBehaviour).Assembly.Location);
                string bundlePath = System.IO.Path.Combine(modDir, "Assets", "ui", "broken_heart");
                
                if (!System.IO.File.Exists(bundlePath))
                {
                    ModBehaviour.DevLog("[GoblinNPC] 心裂开动画资源不存在: " + bundlePath);
                    return false;
                }
                
                AssetBundle bundle = AssetBundle.LoadFromFile(bundlePath);
                if (bundle == null)
                {
                    ModBehaviour.DevLog("[GoblinNPC] 加载心裂开动画Bundle失败");
                    return false;
                }
                
                // 加载所有Sprite
                Sprite[] frames = bundle.LoadAllAssets<Sprite>();
                bundle.Unload(false);
                
                if (frames == null || frames.Length == 0)
                {
                    ModBehaviour.DevLog("[GoblinNPC] 心裂开动画没有Sprite");
                    return false;
                }
                
                // 按名称排序（确保帧顺序正确）
                System.Array.Sort(frames, (a, b) => string.Compare(a.name, b.name));
                
                // 创建动画
                NPCBubbleAnimator.Create(
                    transform,
                    frames,
                    NAME_TAG_HEIGHT + 0.8f,  // 在名字标签上方，避免遮挡
                    2.5f,   // 显示2.5秒
                    false   // 不循环
                );
                
                return true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[GoblinNPC] 加载心裂开动画失败: " + e.Message);
                return false;
            }
        }
        
        /// <summary>
        /// 开始待机动画
        /// </summary>
        private void StartIdleAnimation()
        {
            isIdling = true;
            SafeSetBool(hash_IsIdle, true);
            SafeSetBool(hash_IsRunning, false);
        }
        
        /// <summary>
        /// 停止待机动画
        /// </summary>
        private void StopIdleAnimation()
        {
            isIdling = false;
            SafeSetBool(hash_IsIdle, false);
        }
        
        // ========== 对话接口 ==========
        
        /// <summary>
        /// 开始对话（玩家打开了UI）
        /// 哥布林进入待机状态
        /// </summary>
        public void StartDialogue()
        {
            isInDialogue = true;
            
            // 停止移动
            if (movement != null)
            {
                movement.StopMove();
            }
            
            // 面向玩家
            FacePlayer();
            
            // 播放待机动画
            StartIdleAnimation();
            
            ModBehaviour.DevLog("[GoblinNPC] 开始对话，进入待机状态");
        }
        
        /// <summary>
        /// 结束对话（UI关闭）
        /// 哥布林恢复走路
        /// </summary>
        public void EndDialogue()
        {
            isInDialogue = false;
            
            // 停止待机动画
            StopIdleAnimation();
            
            // 恢复走路
            if (movement != null)
            {
                movement.ResumeWalking();
            }
            
            ModBehaviour.DevLog("[GoblinNPC] 结束对话，恢复走路");
        }
        
        /// <summary>
        /// 获取是否在对话中
        /// </summary>
        public bool IsInDialogue
        {
            get { return isInDialogue; }
        }
        
        /// <summary>
        /// 获取是否在待机
        /// </summary>
        public bool IsIdling
        {
            get { return isIdling; }
        }
        
        /// <summary>
        /// 面向玩家
        /// </summary>
        private void FacePlayer()
        {
            if (playerTransform == null) return;
            
            try
            {
                Vector3 direction = playerTransform.position - transform.position;
                direction.y = 0;  // 只在水平面上旋转
                if (direction.sqrMagnitude > 0.01f)
                {
                    transform.rotation = Quaternion.LookRotation(direction);
                }
            }
            catch { }
        }
        
        /// <summary>
        /// 更新移动速度动画（由 GoblinMovement 调用）
        /// </summary>
        public void UpdateMoveSpeed(float speed)
        {
            SafeSetFloat(hash_MoveSpeed, speed);
        }
        
        /// <summary>
        /// 获取是否正在跑向玩家
        /// </summary>
        public bool IsRunningToPlayer
        {
            get { return isRunningToPlayer; }
        }
    }
    
    /// <summary>
    /// 哥布林移动控制（使用 A* Pathfinding Seeker）
    /// 复用快递员的移动逻辑，简化版
    /// </summary>
    public class GoblinMovement : MonoBehaviour
    {
        private GoblinNPCController controller;
        private Transform playerTransform;
        private Animator animator;
        
        // A* Pathfinding 组件
        public Seeker seeker;
        public Pathfinding.Path path;
        public float nextWaypointDistance = 0.5f;
        private int currentWaypoint;
        private bool reachedEndOfPath;
        public float stopDistance = 0.3f;
        private bool moving;
        private bool waitingForPathResult;
        
        // 公共属性，让 Controller 可以检查移动状态
        public bool IsMoving { get { return moving; } }
        public bool IsWaitingForPath { get { return waitingForPathResult; } }
        
        // 移动参数
        public float walkSpeed = 2f;
        public float runSpeed = 6f;  // 哥布林跑得快一点
        public float turnSpeed = 360f;
        
        // 漫步参数
        public float wanderRadius = 8f;
        
        private float wanderTimer = 0f;
        private const float WANDER_INTERVAL = 5f;
        private bool isInitialized = false;
        
        // 场景名称（用于获取刷新点）
        private string sceneName;
        
        // CharacterController 用于物理碰撞
        private CharacterController characterController;
        private float verticalVelocity = 0f;
        private float gravity = -9.8f;
        
        // 动画参数哈希值
        private static readonly int hash_MoveSpeed = Animator.StringToHash("MoveSpeed");
        
        /// <summary>
        /// 设置场景名称（用于获取刷新点）
        /// </summary>
        public void SetSceneName(string name)
        {
            sceneName = name;
        }
        
        void Start()
        {
            ModBehaviour.DevLog("[GoblinNPC] GoblinMovement.Start 开始");
            
            controller = GetComponent<GoblinNPCController>();
            animator = GetComponentInChildren<Animator>();
            
            // 获取玩家引用
            try
            {
                if (CharacterMainControl.Main != null)
                {
                    playerTransform = CharacterMainControl.Main.transform;
                    ModBehaviour.DevLog("[GoblinNPC] 获取到玩家引用");
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[GoblinNPC] [WARNING] 获取玩家引用失败: " + e.Message);
            }
            
            if (playerTransform == null)
            {
                var player = GameObject.FindGameObjectWithTag("Player");
                if (player != null)
                {
                    playerTransform = player.transform;
                    ModBehaviour.DevLog("[GoblinNPC] 通过 Tag 获取到玩家引用");
                }
            }
            
            // 延迟初始化（等待 A* 图准备好）
            StartCoroutine(InitializeDelayed());
        }
        
        /// <summary>
        /// 延迟初始化，等待 A* 图准备好
        /// </summary>
        private IEnumerator InitializeDelayed()
        {
            yield return new WaitForSeconds(0.5f);
            
            ModBehaviour.DevLog("[GoblinNPC] 开始初始化移动系统，当前位置: " + transform.position);
            
            // 1. 添加 CharacterController
            characterController = gameObject.GetComponent<CharacterController>();
            if (characterController == null)
            {
                characterController = gameObject.AddComponent<CharacterController>();
                characterController.height = 1.2f;  // 哥布林较矮
                characterController.radius = 0.25f;
                characterController.center = new Vector3(0f, 0.6f, 0f);
                characterController.slopeLimit = 45f;
                characterController.stepOffset = 0.3f;
                characterController.skinWidth = 0.08f;
                ModBehaviour.DevLog("[GoblinNPC] 添加 CharacterController 组件");
            }
            
            // 2. 添加 Seeker 组件
            seeker = gameObject.GetComponent<Seeker>();
            if (seeker == null)
            {
                seeker = gameObject.AddComponent<Seeker>();
                ModBehaviour.DevLog("[GoblinNPC] 添加 Seeker 组件");
            }
            
            // 3. 检查 A* 是否可用
            if (AstarPath.active == null)
            {
                ModBehaviour.DevLog("[GoblinNPC] [WARNING] 警告：A* Pathfinding 未激活！哥布林将无法寻路");
            }
            else
            {
                ModBehaviour.DevLog("[GoblinNPC] A* Pathfinding 已激活");
            }
            
            isInitialized = true;
            ModBehaviour.DevLog("[GoblinNPC] 移动系统初始化完成");
        }
        
        /// <summary>
        /// 移动到指定位置
        /// </summary>
        public void MoveToPos(Vector3 pos)
        {
            if (seeker == null)
            {
                ModBehaviour.DevLog("[GoblinNPC] [WARNING] MoveToPos 失败：Seeker 为空");
                return;
            }
            if (waitingForPathResult || moving)
            {
                return;
            }
            
            reachedEndOfPath = false;
            seeker.StartPath(transform.position, pos, new OnPathDelegate(OnPathComplete));
            waitingForPathResult = true;
        }
        
        /// <summary>
        /// 跑向玩家（被召唤时调用）
        /// </summary>
        public void RunToPlayer(Vector3 playerPosition)
        {
            if (seeker == null) return;
            
            // 强制停止当前移动
            path = null;
            moving = false;
            waitingForPathResult = false;
            
            // 开始寻路到玩家位置
            reachedEndOfPath = false;
            seeker.StartPath(transform.position, playerPosition, new OnPathDelegate(OnPathComplete));
            waitingForPathResult = true;
            
            ModBehaviour.DevLog("[GoblinNPC] 开始寻路到玩家位置: " + playerPosition);
        }
        
        /// <summary>
        /// 路径计算完成回调
        /// </summary>
        public void OnPathComplete(Pathfinding.Path p)
        {
            if (!p.error)
            {
                path = p;
                currentWaypoint = 0;
                moving = true;
                ModBehaviour.DevLog("[GoblinNPC] 路径计算成功，路点数: " + p.vectorPath.Count);
            }
            else
            {
                ModBehaviour.DevLog("[GoblinNPC] [WARNING] 路径计算失败: " + p.errorLog);
            }
            waitingForPathResult = false;
        }
        
        /// <summary>
        /// 停止移动
        /// </summary>
        public void StopMove()
        {
            path = null;
            moving = false;
            waitingForPathResult = false;
            UpdateMoveAnimation(0f);
        }
        
        /// <summary>
        /// 恢复走路（急停后调用）
        /// </summary>
        public void ResumeWalking()
        {
            // 继续漫步
            wanderTimer = WANDER_INTERVAL;  // 立即触发下一次漫步
        }
        
        void Update()
        {
            if (!isInitialized) return;
            
            // 如果在对话中，停止所有移动
            if (controller != null && controller.IsInDialogue)
            {
                UpdateMoveAnimation(0f);
                ApplyGravity();
                return;
            }
            
            // 更新玩家引用
            if (playerTransform == null)
            {
                try
                {
                    if (CharacterMainControl.Main != null)
                    {
                        playerTransform = CharacterMainControl.Main.transform;
                    }
                }
                catch { }
            }
            
            // 如果正在跑向玩家，不做漫步决策
            if (controller != null && controller.IsRunningToPlayer)
            {
                UpdatePathFollowing(true);  // 使用跑步速度
                ApplyGravity();
                return;
            }
            
            // 更新漫步决策
            UpdateWanderDecision();
            
            // 沿路径移动
            UpdatePathFollowing(false);  // 使用走路速度
            
            // 应用重力
            ApplyGravity();
        }
        
        /// <summary>
        /// 更新漫步决策
        /// </summary>
        private void UpdateWanderDecision()
        {
            wanderTimer += Time.deltaTime;
            
            // 如果正在移动或等待路径结果，不做新的决策
            if (moving || waitingForPathResult)
            {
                return;
            }
            
            // 定时漫步
            if (wanderTimer >= WANDER_INTERVAL)
            {
                wanderTimer = 0f;
                
                // 获取随机刷新点作为目标
                Vector3 targetPos = GetRandomSpawnPoint();
                if (targetPos != Vector3.zero)
                {
                    MoveToPos(targetPos);
                }
            }
        }
        
        /// <summary>
        /// 沿路径移动
        /// </summary>
        private void UpdatePathFollowing(bool isRunning)
        {
            if (waitingForPathResult) return;
            
            if (path == null)
            {
                moving = false;
                UpdateMoveAnimation(0f);
                return;
            }
            
            moving = true;
            reachedEndOfPath = false;
            
            // 检查是否到达当前路点
            float distanceToWaypoint;
            while (true)
            {
                Vector3 toWaypoint = path.vectorPath[currentWaypoint] - transform.position;
                toWaypoint.y = 0;
                distanceToWaypoint = toWaypoint.magnitude;
                
                if (distanceToWaypoint < nextWaypointDistance)
                {
                    if (currentWaypoint + 1 < path.vectorPath.Count)
                    {
                        currentWaypoint++;
                    }
                    else
                    {
                        reachedEndOfPath = true;
                        break;
                    }
                }
                else
                {
                    break;
                }
            }
            
            // 计算移动方向
            Vector3 direction = path.vectorPath[currentWaypoint] - transform.position;
            direction.y = 0;
            direction = direction.normalized;
            
            // 计算移动速度
            float speedMultiplier;
            if (reachedEndOfPath)
            {
                speedMultiplier = Mathf.Sqrt(distanceToWaypoint / nextWaypointDistance);
                
                if (distanceToWaypoint < stopDistance)
                {
                    path = null;
                    moving = false;
                    UpdateMoveAnimation(0f);
                    ModBehaviour.DevLog("[GoblinNPC] 到达目标点");
                    return;
                }
            }
            else
            {
                speedMultiplier = 1f;
            }
            
            // 计算实际移动速度
            float currentSpeed = (isRunning ? runSpeed : walkSpeed) * speedMultiplier;
            
            // 使用 CharacterController 移动
            Vector3 moveVector = direction * currentSpeed * Time.deltaTime;
            moveVector.y = verticalVelocity * Time.deltaTime;
            
            if (characterController != null && characterController.enabled)
            {
                characterController.Move(moveVector);
            }
            
            // 平滑转向
            Vector3 horizontalDir = direction;
            horizontalDir.y = 0;
            if (horizontalDir != Vector3.zero)
            {
                Quaternion targetRotation = Quaternion.LookRotation(horizontalDir);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, turnSpeed * Time.deltaTime);
            }
            
            // 更新动画
            UpdateMoveAnimation(currentSpeed);
        }
        
        /// <summary>
        /// 应用重力
        /// </summary>
        private void ApplyGravity()
        {
            if (characterController == null || !characterController.enabled) return;
            
            if (characterController.isGrounded)
            {
                verticalVelocity = -0.5f;
            }
            else
            {
                verticalVelocity += gravity * Time.deltaTime;
            }
        }
        
        /// <summary>
        /// 更新移动动画参数
        /// </summary>
        private void UpdateMoveAnimation(float speed)
        {
            if (animator != null)
            {
                try
                {
                    animator.SetFloat(hash_MoveSpeed, speed);
                    // 当速度为0时，设置 IsIdle 为 true，避免原地播放走路动画
                    animator.SetBool("IsIdle", speed < 0.01f);
                }
                catch { }
            }

            // 通知控制器更新动画
            if (controller != null)
            {
                controller.UpdateMoveSpeed(speed);
            }
        }
        
        /// <summary>
        /// 修正目标点的Y坐标到地面
        /// </summary>
        private Vector3 CorrectTargetHeight(Vector3 pos)
        {
            RaycastHit hit;
            Vector3 rayStart = pos + Vector3.up * 50f;
            
            RaycastHit[] hits = Physics.RaycastAll(rayStart, Vector3.down, 100f);
            if (hits != null && hits.Length > 0)
            {
                float lowestY = float.MaxValue;
                float configY = pos.y;
                float bestY = pos.y;
                
                foreach (var h in hits)
                {
                    if (Mathf.Abs(h.point.y - configY) < 1f)
                    {
                        bestY = h.point.y + 0.1f;
                        break;
                    }
                    if (h.point.y < lowestY)
                    {
                        lowestY = h.point.y;
                        bestY = h.point.y + 0.1f;
                    }
                }
                
                return new Vector3(pos.x, bestY, pos.z);
            }
            
            if (Physics.Raycast(rayStart, Vector3.down, out hit, 100f))
            {
                return new Vector3(pos.x, hit.point.y + 0.1f, pos.z);
            }
            
            return pos;
        }
        
        /// <summary>
        /// 获取随机的刷新点作为目标
        /// </summary>
        private Vector3 GetRandomSpawnPoint()
        {
            try
            {
                // 从 NPCSpawnConfig 获取哥布林刷新点
                if (!string.IsNullOrEmpty(sceneName) && 
                    NPCSpawnConfig.GoblinSpawnConfigs.TryGetValue(sceneName, out NPCSceneSpawnConfig config))
                {
                    Vector3[] spawnPoints = config.spawnPoints;
                    if (spawnPoints != null && spawnPoints.Length > 0)
                    {
                        // 随机选择一个刷新点（排除当前位置附近的点）
                        int maxAttempts = 5;
                        for (int i = 0; i < maxAttempts; i++)
                        {
                            int randomIndex = UnityEngine.Random.Range(0, spawnPoints.Length);
                            Vector3 targetPos = spawnPoints[randomIndex];
                            
                            // 修正高度
                            targetPos = CorrectTargetHeight(targetPos);
                            
                            // 确保目标点与当前位置有一定距离
                            Vector3 diff = targetPos - transform.position;
                            diff.y = 0;
                            float distance = diff.magnitude;
                            if (distance > 3f)
                            {
                                return targetPos;
                            }
                        }
                        
                        // 多次尝试都找不到合适的点，用第一个
                        return CorrectTargetHeight(spawnPoints[0]);
                    }
                }
                
                // 如果没有配置，使用 Boss 刷新点（回退逻辑）
                if (ModBehaviour.Instance != null)
                {
                    Vector3[] bossSpawnPoints = ModBehaviour.Instance.GetCurrentSceneSpawnPoints();
                    if (bossSpawnPoints != null && bossSpawnPoints.Length > 0)
                    {
                        int randomIndex = UnityEngine.Random.Range(0, bossSpawnPoints.Length);
                        return CorrectTargetHeight(bossSpawnPoints[randomIndex]);
                    }
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[GoblinNPC] [WARNING] 获取刷新点失败: " + e.Message);
            }
            
            return Vector3.zero;
        }
    }
}
