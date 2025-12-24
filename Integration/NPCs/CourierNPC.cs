// ============================================================================
// CourierNPC.cs - 快递员NPC系统
// ============================================================================
// 模块说明：
//   管理 BossRush 模组的快递员 NPC，包括：
//   - 从 AssetBundle 加载快递员模型
//   - 动画状态管理（Walking、Idle、Dancing、Cheer、Running）
//   - 与玩家的距离检测和行为逻辑
//   - 快递服务交互选项
//   - 使用 A* Pathfinding Seeker 进行寻路（与游戏原生系统一致）
// ============================================================================

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;
using Duckov.Utilities;
using Pathfinding;

namespace BossRush
{
    /// <summary>
    /// 快递员NPC系统
    /// </summary>
    public partial class ModBehaviour
    {
        // 快递员实例
        private GameObject courierNPCInstance = null;
        private CourierNPCController courierController = null;
        
        // AssetBundle 缓存
        private static AssetBundle courierAssetBundle = null;
        private static GameObject courierPrefab = null;
        
        /// <summary>
        /// 加载快递员 AssetBundle
        /// </summary>
        private bool LoadCourierAssetBundle()
        {
            if (courierPrefab != null)
            {
                DevLog("[CourierNPC] 预制体已缓存，跳过加载");
                return true;
            }
            
            try
            {
                string assemblyLocation = typeof(ModBehaviour).Assembly.Location;
                string modDir = System.IO.Path.GetDirectoryName(assemblyLocation);
                string bundlePath = System.IO.Path.Combine(modDir, "Assets", "npcs", "couriernpc");
                
                DevLog("[CourierNPC] 尝试加载 AssetBundle: " + bundlePath);
                
                if (!File.Exists(bundlePath))
                {
                    DevLog("[CourierNPC] 错误：未找到 couriernpc AssetBundle 文件: " + bundlePath);
                    return false;
                }
                
                // 如果之前加载过但预制体为空，先卸载
                if (courierAssetBundle != null)
                {
                    courierAssetBundle.Unload(false);
                    courierAssetBundle = null;
                }
                
                // 直接使用 AssetBundle API 加载
                courierAssetBundle = AssetBundle.LoadFromFile(bundlePath);
                if (courierAssetBundle == null)
                {
                    DevLog("[CourierNPC] 错误：加载 AssetBundle 失败（可能已被加载或文件损坏）: " + bundlePath);
                    return false;
                }
                
                DevLog("[CourierNPC] AssetBundle 加载成功，开始查找预制体...");
                
                // 列出所有资源名称用于调试
                string[] assetNames = courierAssetBundle.GetAllAssetNames();
                DevLog("[CourierNPC] AssetBundle 包含 " + assetNames.Length + " 个资源:");
                foreach (string name in assetNames)
                {
                    DevLog("[CourierNPC]   - " + name);
                }
                
                // 尝试加载名为 CourierNPC 的预制体
                courierPrefab = courierAssetBundle.LoadAsset<GameObject>("CourierNPC");
                
                // 如果没找到，尝试其他常见名称
                if (courierPrefab == null)
                {
                    DevLog("[CourierNPC] 未找到 'CourierNPC'，尝试其他名称...");
                    courierPrefab = courierAssetBundle.LoadAsset<GameObject>("couriernpc");
                }
                
                // 如果还是没找到，加载第一个 GameObject
                if (courierPrefab == null)
                {
                    DevLog("[CourierNPC] 尝试加载所有 GameObject...");
                    GameObject[] allPrefabs = courierAssetBundle.LoadAllAssets<GameObject>();
                    if (allPrefabs != null && allPrefabs.Length > 0)
                    {
                        courierPrefab = allPrefabs[0];
                        DevLog("[CourierNPC] 使用第一个 GameObject: " + courierPrefab.name);
                    }
                }
                
                if (courierPrefab == null)
                {
                    DevLog("[CourierNPC] 错误：AssetBundle 中未找到任何 GameObject 预制体");
                    return false;
                }
                
                // 检查预制体的组件
                DevLog("[CourierNPC] 成功加载快递员预制体: " + courierPrefab.name);
                Animator animator = courierPrefab.GetComponentInChildren<Animator>();
                if (animator != null)
                {
                    DevLog("[CourierNPC] 预制体包含 Animator 组件");
                    if (animator.runtimeAnimatorController != null)
                    {
                        DevLog("[CourierNPC] Animator Controller: " + animator.runtimeAnimatorController.name);
                    }
                    else
                    {
                        DevLog("[CourierNPC] 警告：Animator 没有 Controller！动画可能无法播放");
                    }
                }
                else
                {
                    DevLog("[CourierNPC] 警告：预制体没有 Animator 组件！");
                }
                
                return true;
            }
            catch (Exception e)
            {
                DevLog("[CourierNPC] 加载 AssetBundle 出错: " + e.Message + "\n" + e.StackTrace);
                return false;
            }
        }
        
        /// <summary>
        /// 在 BossRush 竞技场生成快递员 NPC
        /// </summary>
        public void SpawnCourierNPC()
        {
            DevLog("[CourierNPC] 开始生成快递员...");
            
            // 如果已经存在，不重复生成
            if (courierNPCInstance != null)
            {
                DevLog("[CourierNPC] 快递员已存在，跳过生成");
                return;
            }
            
            // 加载 AssetBundle
            if (!LoadCourierAssetBundle())
            {
                DevLog("[CourierNPC] 无法加载快递员资源，跳过生成");
                return;
            }
            
            // 获取生成位置（随机选择一个 Boss 刷新点）
            Vector3[] spawnPoints = GetCurrentSceneSpawnPoints();
            if (spawnPoints == null || spawnPoints.Length == 0)
            {
                DevLog("[CourierNPC] 无法获取刷新点，跳过生成");
                return;
            }
            
            // 随机选择一个刷新点
            int randomIndex = UnityEngine.Random.Range(0, spawnPoints.Length);
            Vector3 spawnPos = spawnPoints[randomIndex];
            DevLog("[CourierNPC] 随机选择刷新点 [" + randomIndex + "/" + spawnPoints.Length + "], 位置: " + spawnPos);
            
            // 使用 Raycast 修正落点到地面
            RaycastHit hit;
            if (Physics.Raycast(spawnPos + Vector3.up * 5f, Vector3.down, out hit, 20f))
            {
                spawnPos = hit.point + new Vector3(0f, 0.1f, 0f);
                DevLog("[CourierNPC] 修正后位置: " + spawnPos);
            }
            
            try
            {
                // 实例化预制体
                courierNPCInstance = UnityEngine.Object.Instantiate(courierPrefab, spawnPos, Quaternion.identity);
                courierNPCInstance.name = "CourierNPC_BossRush";
                DevLog("[CourierNPC] 预制体实例化成功");
                
                // 确保所有子对象都激活
                courierNPCInstance.SetActive(true);
                foreach (Transform child in courierNPCInstance.GetComponentsInChildren<Transform>(true))
                {
                    child.gameObject.SetActive(true);
                }
                
                // 修复 Shader（从 Standard 替换为游戏使用的 Shader）
                FixCourierShaders(courierNPCInstance);
                
                // 设置 Layer（确保渲染正确）
                SetLayerRecursively(courierNPCInstance, LayerMask.NameToLayer("Default"));
                
                // 添加控制器组件
                courierController = courierNPCInstance.AddComponent<CourierNPCController>();
                DevLog("[CourierNPC] 控制器组件添加成功");
                
                // 添加移动控制组件（内部会延迟初始化 NavMeshAgent）
                CourierMovement movement = courierNPCInstance.AddComponent<CourierMovement>();
                DevLog("[CourierNPC] 移动组件添加成功");
                
                // 添加交互组件
                AddCourierInteraction(courierNPCInstance);
                
                DevLog("[CourierNPC] 快递员生成成功，位置: " + spawnPos);
            }
            catch (Exception e)
            {
                DevLog("[CourierNPC] 生成快递员出错: " + e.Message + "\n" + e.StackTrace);
            }
        }
        
        /// <summary>
        /// 修复快递员模型的 Shader（从 Standard 替换为游戏 Shader）
        /// </summary>
        private void FixCourierShaders(GameObject obj)
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
                                        DevLog("[CourierNPC] 替换 Shader: " + shaderName + " -> " + gameShader.name);
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                DevLog("[CourierNPC] 修复 Shader 出错: " + e.Message);
            }
        }
        
        /// <summary>
        /// 递归设置 Layer
        /// </summary>
        private void SetLayerRecursively(GameObject obj, int layer)
        {
            obj.layer = layer;
            foreach (Transform child in obj.transform)
            {
                SetLayerRecursively(child.gameObject, layer);
            }
        }
        
        /// <summary>
        /// 为快递员添加交互选项
        /// </summary>
        private void AddCourierInteraction(GameObject courier)
        {
            try
            {
                DevLog("[CourierNPC] 开始添加交互组件...");
                
                // 添加交互组件（CourierInteractable.Awake 会自动处理 Collider 和 Layer）
                CourierInteractable interactable = courier.AddComponent<CourierInteractable>();
                
                DevLog("[CourierNPC] 交互组件添加成功");
            }
            catch (Exception e)
            {
                DevLog("[CourierNPC] 添加交互组件出错: " + e.Message + "\n" + e.StackTrace);
            }
        }
        
        /// <summary>
        /// 销毁快递员 NPC
        /// </summary>
        public void DestroyCourierNPC()
        {
            if (courierNPCInstance != null)
            {
                UnityEngine.Object.Destroy(courierNPCInstance);
                courierNPCInstance = null;
                courierController = null;
                DevLog("[CourierNPC] 快递员已销毁");
            }
        }
        
        /// <summary>
        /// 通知快递员 Boss 战开始
        /// </summary>
        public void NotifyCourierBossFightStart()
        {
            if (courierController != null)
            {
                courierController.SetBossFight(true);
            }
            
            // 同时通知移动组件
            if (courierNPCInstance != null)
            {
                CourierMovement movement = courierNPCInstance.GetComponent<CourierMovement>();
                if (movement != null)
                {
                    movement.SetBossFight(true);
                }
            }
        }
        
        /// <summary>
        /// 通知快递员 Boss 战结束
        /// </summary>
        public void NotifyCourierBossFightEnd()
        {
            if (courierController != null)
            {
                courierController.SetBossFight(false);
            }
            
            if (courierNPCInstance != null)
            {
                CourierMovement movement = courierNPCInstance.GetComponent<CourierMovement>();
                if (movement != null)
                {
                    movement.SetBossFight(false);
                }
            }
        }
        
        /// <summary>
        /// 通知快递员当前没有Boss（召唤间隔期间）
        /// </summary>
        public void NotifyCourierNoBoss(bool noBoss)
        {
            if (courierController != null)
            {
                courierController.SetNoBoss(noBoss);
            }
            
            if (courierNPCInstance != null)
            {
                CourierMovement movement = courierNPCInstance.GetComponent<CourierMovement>();
                if (movement != null)
                {
                    movement.SetNoBoss(noBoss);
                }
            }
        }
        
        /// <summary>
        /// 通知快递员 BossRush 通关
        /// </summary>
        public void NotifyCourierBossRushCompleted()
        {
            if (courierController != null)
            {
                courierController.SetCompleted();
            }
            
            // 同时通知移动组件停止移动
            if (courierNPCInstance != null)
            {
                CourierMovement movement = courierNPCInstance.GetComponent<CourierMovement>();
                if (movement != null)
                {
                    movement.SetCompleted(true);
                }
            }
        }
    }

    
    /// <summary>
    /// 快递员NPC动画控制器
    /// 管理 Walking、Idle、Dancing、Cheer、Running 五种动画状态
    /// </summary>
    public class CourierNPCController : MonoBehaviour
    {
        private Animator animator;
        private Transform playerTransform;
        private bool hasAnimator = false;
        
        // 名字标签
        private GameObject nameTagObject;
        private TMPro.TextMeshPro nameTagText;
        private const float NAME_TAG_HEIGHT = 2.3f;  // 名字标签高度（头顶上方）
        
        // 距离阈值（米）
        private const float NEAR_DISTANCE = 5f;
        
        // 游戏原生动画参数哈希值（与 CharacterAnimationControl 一致）
        private static readonly int hash_MoveSpeed = Animator.StringToHash("MoveSpeed");
        private static readonly int hash_MoveDirX = Animator.StringToHash("MoveDirX");
        private static readonly int hash_MoveDirY = Animator.StringToHash("MoveDirY");
        private static readonly int hash_RightHandOut = Animator.StringToHash("RightHandOut");
        private static readonly int hash_HandState = Animator.StringToHash("HandState");
        private static readonly int hash_Dashing = Animator.StringToHash("Dashing");
        private static readonly int hash_Attack = Animator.StringToHash("Attack");
        
        // 快递员专用动画参数哈希值
        private static readonly int hash_IsTalking = Animator.StringToHash("IsTalking");
        private static readonly int hash_IsBossFight = Animator.StringToHash("IsBossFight");
        private static readonly int hash_IsCompleted = Animator.StringToHash("IsCompleted");
        private static readonly int hash_IsNearPlayer = Animator.StringToHash("IsNearPlayer");
        private static readonly int hash_IsArrived = Animator.StringToHash("IsArrived");
        private static readonly int hash_NoBoss = Animator.StringToHash("NoBoss");
        
        void Awake()
        {
            animator = GetComponentInChildren<Animator>();
            hasAnimator = animator != null;
            
            if (hasAnimator)
            {
                Debug.Log("[CourierNPC] Controller.Awake: 找到 Animator 组件");
                if (animator.runtimeAnimatorController != null)
                {
                    Debug.Log("[CourierNPC] Animator Controller: " + animator.runtimeAnimatorController.name);
                    // 列出所有参数
                    foreach (var param in animator.parameters)
                    {
                        Debug.Log("[CourierNPC]   参数: " + param.name + " (" + param.type + ")");
                    }
                }
                else
                {
                    Debug.LogWarning("[CourierNPC] 警告：Animator 没有 RuntimeAnimatorController！");
                }
            }
            else
            {
                Debug.LogWarning("[CourierNPC] Controller.Awake: 未找到 Animator 组件！");
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
                    Debug.Log("[CourierNPC] Controller.Start: 获取到玩家引用");
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[CourierNPC] 获取 CharacterMainControl.Main 失败: " + e.Message);
            }
            
            if (playerTransform == null)
            {
                var player = GameObject.FindGameObjectWithTag("Player");
                if (player != null)
                {
                    playerTransform = player.transform;
                    Debug.Log("[CourierNPC] Controller.Start: 通过 Tag 获取到玩家引用");
                }
            }
            
            // 初始化游戏原生参数默认值
            InitializeDefaultAnimatorParams();
            
            // 创建名字标签
            CreateNameTag();
        }
        
        /// <summary>
        /// 创建头顶名字标签
        /// </summary>
        private void CreateNameTag()
        {
            try
            {
                // 创建名字标签对象
                nameTagObject = new GameObject("CourierNameTag");
                nameTagObject.transform.SetParent(transform);
                nameTagObject.transform.localPosition = new Vector3(0f, NAME_TAG_HEIGHT, 0f);
                
                // 添加 TextMeshPro 组件
                nameTagText = nameTagObject.AddComponent<TMPro.TextMeshPro>();
                // 使用本地化键获取快递员名称
                string courierName = LocalizationHelper.GetLocalizedText("BossRush_CourierName");
                if (string.IsNullOrEmpty(courierName) || courierName == "BossRush_CourierName")
                {
                    courierName = L10n.T("阿稳", "Awen");  // 回退到硬编码
                }
                nameTagText.text = courierName;
                nameTagText.fontSize = 4f;  // 字号大一点
                nameTagText.alignment = TMPro.TextAlignmentOptions.Center;
                nameTagText.color = Color.white;  // 白色
                
                // 设置文字始终面向相机
                nameTagText.enableAutoSizing = false;
                
                // 设置排序层级确保可见
                nameTagText.sortingOrder = 100;
                
                Debug.Log("[CourierNPC] 名字标签创建成功: " + courierName);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[CourierNPC] 创建名字标签失败: " + e.Message);
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
        
        /// <summary>
        /// 初始化游戏原生动画参数的默认值
        /// </summary>
        private void InitializeDefaultAnimatorParams()
        {
            if (!hasAnimator || animator == null) return;
            
            try
            {
                // 安全地设置参数（检查参数是否存在）
                SafeSetFloat(hash_MoveSpeed, 0f);
                SafeSetFloat(hash_MoveDirX, 0f);
                SafeSetFloat(hash_MoveDirY, 0f);
                SafeSetBool(hash_RightHandOut, false);
                SafeSetInteger(hash_HandState, 0);
                SafeSetBool(hash_Dashing, false);
                
                // 初始化自定义参数
                SafeSetBool(hash_IsTalking, false);
                SafeSetBool(hash_IsBossFight, false);
                SafeSetBool(hash_IsCompleted, false);
                SafeSetBool(hash_IsNearPlayer, false);
                SafeSetBool(hash_IsArrived, true);  // 初始状态为已到达（静止）
                SafeSetBool(hash_NoBoss, true);  // 初始状态为没有Boss
                
                Debug.Log("[CourierNPC] 动画参数初始化完成");
            }
            catch (Exception e)
            {
                Debug.LogWarning("[CourierNPC] 初始化动画参数出错: " + e.Message);
            }
        }
        
        // 安全设置参数的辅助方法
        private void SafeSetFloat(int hash, float value)
        {
            try { animator.SetFloat(hash, value); } catch { }
        }
        
        private void SafeSetBool(int hash, bool value)
        {
            try { animator.SetBool(hash, value); } catch { }
        }
        
        private void SafeSetInteger(int hash, int value)
        {
            try { animator.SetInteger(hash, value); } catch { }
        }
        
        void Update()
        {
            // 实时更新与玩家的距离状态
            UpdateDistanceState();
        }
        
        /// <summary>
        /// 更新与玩家的距离状态
        /// </summary>
        private void UpdateDistanceState()
        {
            if (playerTransform == null)
            {
                // 尝试重新获取玩家引用
                try
                {
                    if (CharacterMainControl.Main != null)
                    {
                        playerTransform = CharacterMainControl.Main.transform;
                    }
                }
                catch { }
            }
            
            if (playerTransform == null || !hasAnimator) return;
            
            float distance = Vector3.Distance(transform.position, playerTransform.position);
            bool isNear = distance <= NEAR_DISTANCE;
            
            SafeSetBool(hash_IsNearPlayer, isNear);
        }
        
        /// <summary>
        /// 开始与玩家交互/对话
        /// </summary>
        public void StartTalking()
        {
            if (hasAnimator)
            {
                SafeSetBool(hash_IsTalking, true);
                Debug.Log("[CourierNPC] 开始对话动画");
            }
            
            // 显示随机对话气泡
            ShowRandomDialogue();
        }
        
        /// <summary>
        /// 显示随机对话气泡（使用原版 DialogueBubblesManager）
        /// </summary>
        private void ShowRandomDialogue()
        {
            try
            {
                // 随机选择一句对话（使用本地化键）
                int dialogueCount = LocalizationInjector.GetCourierDialogueCount();
                int index = UnityEngine.Random.Range(0, dialogueCount);
                string dialogueKey = "BossRush_CourierDialogue_" + index;
                string dialogue = LocalizationHelper.GetLocalizedText(dialogueKey);
                
                // 如果本地化失败，使用回退文本
                if (string.IsNullOrEmpty(dialogue) || dialogue == dialogueKey)
                {
                    dialogue = L10n.T("你好，有什么需要帮忙的吗？", "Hello, can I help you?");
                }
                
                // 使用原版气泡系统显示对话（speed=-1表示一次性显示全部文字）
                // yOffset 设置为名字标签高度附近（1.5f，比名字标签稍低）
                float yOffset = 1.5f;
                Cysharp.Threading.Tasks.UniTaskExtensions.Forget(
                    Duckov.UI.DialogueBubbles.DialogueBubblesManager.Show(dialogue, transform, yOffset, false, false, -1f, 3f)
                );
                
                Debug.Log("[CourierNPC] 显示对话: " + dialogue);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[CourierNPC] 显示对话气泡失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 结束交互，返回之前的状态
        /// </summary>
        public void StopTalking()
        {
            if (hasAnimator)
            {
                SafeSetBool(hash_IsTalking, false);
                Debug.Log("[CourierNPC] 结束对话动画");
            }
        }
        
        /// <summary>
        /// 设置是否已到达目标点（控制加油动画）
        /// </summary>
        public void SetArrived(bool arrived)
        {
            if (hasAnimator)
            {
                SafeSetBool(hash_IsArrived, arrived);
                Debug.Log("[CourierNPC] 设置 IsArrived: " + arrived);
            }
        }
        
        /// <summary>
        /// 设置是否正在打Boss
        /// </summary>
        public void SetBossFight(bool isFighting)
        {
            if (hasAnimator)
            {
                SafeSetBool(hash_IsBossFight, isFighting);
                Debug.Log("[CourierNPC] 设置 BossFight: " + isFighting);
            }
        }
        
        /// <summary>
        /// 设置是否没有Boss（召唤间隔期间）
        /// </summary>
        public void SetNoBoss(bool noBoss)
        {
            if (hasAnimator)
            {
                SafeSetBool(hash_NoBoss, noBoss);
                Debug.Log("[CourierNPC] 设置 NoBoss: " + noBoss);
            }
        }
        
        /// <summary>
        /// 设置BossRush已通关（开始庆祝）
        /// </summary>
        public void SetCompleted()
        {
            if (hasAnimator)
            {
                SafeSetBool(hash_IsCompleted, true);
                SafeSetBool(hash_IsBossFight, false);
                Debug.Log("[CourierNPC] 设置通关状态");
            }
        }
        
        /// <summary>
        /// 重置状态（新一轮BossRush）
        /// </summary>
        public void ResetState()
        {
            if (hasAnimator)
            {
                SafeSetBool(hash_IsTalking, false);
                SafeSetBool(hash_IsBossFight, false);
                SafeSetBool(hash_IsCompleted, false);
                Debug.Log("[CourierNPC] 重置状态");
            }
        }
    }
    
    /// <summary>
    /// 快递员移动控制（使用 A* Pathfinding Seeker）
    /// 严格模仿游戏原生的 AI_PathControl 实现
    /// </summary>
    public class CourierMovement : MonoBehaviour
    {
        private CourierNPCController controller;
        private Transform playerTransform;
        private Animator animator;
        
        // A* Pathfinding 组件（与原版 AI_PathControl 完全一致）
        public Seeker seeker;
        public Pathfinding.Path path;
        public float nextWaypointDistance = 0.5f;  // 减小到0.5米，避免过早跳过路点
        private int currentWaypoint;
        private bool reachedEndOfPath;
        public float stopDistance = 0.3f;  // 停止距离
        private bool moving;
        private bool waitingForPathResult;
        
        // 移动参数
        public float walkSpeed = 2f;
        public float runSpeed = 5f;
        public float turnSpeed = 360f;
        
        // 漫步参数
        public float wanderRadius = 10f;
        public float safeDistance = 5f;  // 安全距离5米，玩家靠近时触发逃跑
        
        private bool isBossFight = false;
        private bool isCompleted = false;
        private float wanderTimer = 0f;
        private const float WANDER_INTERVAL = 4f;
        private bool isInitialized = false;
        
        // 待机状态（到达目标后播放待机动画）
        private bool isIdling = false;
        private float idleTimer = 0f;
        private const float IDLE_DURATION = 2f;  // 待机2秒
        
        // 气泡显示计时器
        private float cheerBubbleTimer = 0f;
        private float victoryBubbleTimer = 0f;
        private const float CHEER_BUBBLE_INTERVAL = 5f;  // 加油气泡间隔5秒
        private const float VICTORY_BUBBLE_INTERVAL = 5f;  // 胜利气泡间隔5秒
        
        // CharacterController 用于物理碰撞（因为快递员没有 CharacterMainControl）
        private CharacterController characterController;
        private float verticalVelocity = 0f;
        private float gravity = -9.8f;
        
        // 动画参数哈希值
        private static readonly int hash_MoveSpeed = Animator.StringToHash("MoveSpeed");
        
        // 属性（与原版 AI_PathControl 一致）
        public bool ReachedEndOfPath { get { return reachedEndOfPath; } }
        public bool Moving { get { return moving; } }
        public bool WaitingForPathResult { get { return waitingForPathResult; } }
        
        void Start()
        {
            Debug.Log("[CourierNPC] CourierMovement.Start 开始");
            
            controller = GetComponent<CourierNPCController>();
            animator = GetComponentInChildren<Animator>();
            
            // 获取玩家引用
            try
            {
                if (CharacterMainControl.Main != null)
                {
                    playerTransform = CharacterMainControl.Main.transform;
                    Debug.Log("[CourierNPC] 获取到玩家引用");
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[CourierNPC] 获取玩家引用失败: " + e.Message);
            }
            
            if (playerTransform == null)
            {
                var player = GameObject.FindGameObjectWithTag("Player");
                if (player != null)
                {
                    playerTransform = player.transform;
                    Debug.Log("[CourierNPC] 通过 Tag 获取到玩家引用");
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
            
            Debug.Log("[CourierNPC] 开始初始化移动系统，当前位置: " + transform.position);
            
            // 1. 添加 CharacterController（用于物理碰撞，因为快递员没有 CharacterMainControl）
            characterController = gameObject.GetComponent<CharacterController>();
            if (characterController == null)
            {
                characterController = gameObject.AddComponent<CharacterController>();
                characterController.height = 2f;
                characterController.radius = 0.3f;
                characterController.center = new Vector3(0f, 1f, 0f);
                characterController.slopeLimit = 45f;
                characterController.stepOffset = 0.3f;
                characterController.skinWidth = 0.08f;
                Debug.Log("[CourierNPC] 添加 CharacterController 组件");
            }
            
            // 2. 添加 Seeker 组件（A* Pathfinding 核心组件，与原版 AI_PathControl 一致）
            seeker = gameObject.GetComponent<Seeker>();
            if (seeker == null)
            {
                seeker = gameObject.AddComponent<Seeker>();
                Debug.Log("[CourierNPC] 添加 Seeker 组件");
            }
            else
            {
                Debug.Log("[CourierNPC] Seeker 组件已存在");
            }
            
            // 3. 检查 A* 是否可用
            if (AstarPath.active == null)
            {
                Debug.LogWarning("[CourierNPC] 警告：A* Pathfinding 未激活！快递员将无法寻路");
            }
            else
            {
                Debug.Log("[CourierNPC] A* Pathfinding 已激活，图数量: " + AstarPath.active.graphs.Length);
                
                // 列出所有图的类型
                for (int i = 0; i < AstarPath.active.graphs.Length; i++)
                {
                    var graph = AstarPath.active.graphs[i];
                    if (graph != null)
                    {
                        Debug.Log("[CourierNPC]   图[" + i + "]: " + graph.GetType().Name);
                    }
                }
            }
            
            isInitialized = true;
            Debug.Log("[CourierNPC] 移动系统初始化完成（使用 A* Seeker）");
            
            // 立即尝试一次寻路测试
            yield return new WaitForSeconds(0.5f);
            if (seeker != null && AstarPath.active != null)
            {
                Vector3 testTarget = transform.position + new Vector3(2f, 0f, 2f);
                Debug.Log("[CourierNPC] 测试寻路到: " + testTarget);
                MoveToPos(testTarget);
            }
        }
        
        /// <summary>
        /// 移动到指定位置（与原版 AI_PathControl.MoveToPos 完全一致）
        /// </summary>
        public void MoveToPos(Vector3 pos)
        {
            if (seeker == null)
            {
                Debug.LogWarning("[CourierNPC] MoveToPos 失败：Seeker 为空");
                return;
            }
            if (waitingForPathResult || moving)
            {
                return;  // 正在等待路径结果或正在移动，不重复请求
            }
            
            reachedEndOfPath = false;
            // 开始移动时，设置 IsArrived 为 false
            if (controller != null)
            {
                controller.SetArrived(false);
            }
            // 注意：不要在这里清空 path，否则会导致 UpdatePathFollowing 立即将 moving 设为 false
            seeker.StartPath(transform.position, pos, new OnPathDelegate(OnPathComplete));
            waitingForPathResult = true;
            Debug.Log("[CourierNPC] 开始寻路到: " + pos);
        }
        
        /// <summary>
        /// 路径计算完成回调（与原版 AI_PathControl.OnPathComplete 完全一致）
        /// </summary>
        public void OnPathComplete(Pathfinding.Path p)
        {
            if (!p.error)
            {
                path = p;
                currentWaypoint = 0;
                moving = true;
                Debug.Log("[CourierNPC] 路径计算成功，路点数: " + p.vectorPath.Count);
            }
            else
            {
                Debug.LogWarning("[CourierNPC] 路径计算失败: " + p.errorLog);
            }
            waitingForPathResult = false;
        }
        
        /// <summary>
        /// 停止移动（与原版 AI_PathControl.StopMove 一致）
        /// </summary>
        public void StopMove()
        {
            path = null;
            moving = false;
            waitingForPathResult = false;
            UpdateMoveAnimation(0f);
        }
        
        void Update()
        {
            if (!isInitialized) return;
            
            // 通关后显示胜利气泡（每5秒一次）
            if (isCompleted)
            {
                StopMove();
                victoryBubbleTimer += Time.deltaTime;
                if (victoryBubbleTimer >= VICTORY_BUBBLE_INTERVAL)
                {
                    victoryBubbleTimer = 0f;
                    FacePlayer();  // 面向玩家
                    ShowBubble(LocalizationHelper.GetLocalizedText("BossRush_CourierVictory"), 3f);
                }
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
            
            // 更新移动决策（决定去哪里）
            UpdateMovementDecision();
            
            // 沿路径移动（与原版 AI_PathControl.Update 核心逻辑一致）
            UpdatePathFollowing();
            
            // 应用重力
            ApplyGravity();
        }
        
        /// <summary>
        /// 更新移动决策（决定去哪里）
        /// </summary>
        private void UpdateMovementDecision()
        {
            // 如果正在待机，更新待机计时器
            if (isIdling)
            {
                idleTimer += Time.deltaTime;
                if (idleTimer >= IDLE_DURATION)
                {
                    // 待机结束，停止待机动画
                    isIdling = false;
                    idleTimer = 0f;
                    if (controller != null)
                    {
                        controller.StopTalking();
                    }
                    Debug.Log("[CourierNPC] 待机结束，准备继续移动");
                }
                return;  // 待机期间不做移动决策
            }
            
            wanderTimer += Time.deltaTime;
            
            // 如果正在移动或等待路径结果，不做新的决策
            if (moving || waitingForPathResult)
            {
                return;
            }
            
            if (isBossFight && playerTransform != null)
            {
                // 打Boss时：保持安全距离，如果玩家太近则跑到远离玩家的 Boss 刷新点
                float distance = Vector3.Distance(transform.position, playerTransform.position);
                if (distance < safeDistance)
                {
                    // 触发逃跑，显示逃跑气泡
                    ShowBubble(LocalizationHelper.GetLocalizedText("BossRush_CourierFlee"), 3f);
                    
                    // 从 Boss 刷新点中选择一个远离玩家的点
                    Vector3 targetPos = GetSpawnPointAwayFromPlayer();
                    if (targetPos != Vector3.zero)
                    {
                        MoveToPos(targetPos);
                    }
                }
                else
                {
                    // 玩家距离大于安全距离，显示加油气泡（每5秒一次）
                    cheerBubbleTimer += Time.deltaTime;
                    if (cheerBubbleTimer >= CHEER_BUBBLE_INTERVAL)
                    {
                        cheerBubbleTimer = 0f;
                        FacePlayer();  // 面向玩家
                        ShowBubble(LocalizationHelper.GetLocalizedText("BossRush_CourierCheer"), 3f);
                    }
                }
            }
            else
            {
                // 没打Boss时：在Boss刷新点之间随机走动
                bool needNewTarget = wanderTimer >= WANDER_INTERVAL;
                bool reachedTarget = path == null || reachedEndOfPath;
                
                if (needNewTarget || reachedTarget)
                {
                    wanderTimer = 0f;
                    
                    // 从 ModBehaviour 获取 Boss 刷新点
                    Vector3 targetPos = GetRandomSpawnPoint();
                    if (targetPos != Vector3.zero)
                    {
                        MoveToPos(targetPos);
                    }
                }
            }
        }
        
        /// <summary>
        /// 沿路径移动（与原版 AI_PathControl.Update 核心逻辑完全一致）
        /// 原版使用 controller.SetMoveInput()，我们使用 CharacterController.Move()
        /// </summary>
        private void UpdatePathFollowing()
        {
            // 如果正在等待路径结果，保持当前状态
            if (waitingForPathResult)
            {
                return;
            }
            
            if (path == null)
            {
                moving = false;
                UpdateMoveAnimation(0f);
                return;
            }
            
            moving = true;
            reachedEndOfPath = false;
            
            // 检查是否到达当前路点（使用水平距离，忽略Y轴差异）
            float distanceToWaypoint;
            while (true)
            {
                // 使用水平距离，避免因高度差导致卡住
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
            
            // 计算移动方向（只在水平面上移动，忽略Y轴）
            Vector3 direction = path.vectorPath[currentWaypoint] - transform.position;
            direction.y = 0;
            direction = direction.normalized;
            
            // 计算移动输入（与原版逻辑一致：接近终点时减速）
            float speedMultiplier;
            if (reachedEndOfPath)
            {
                speedMultiplier = Mathf.Sqrt(distanceToWaypoint / nextWaypointDistance);
                
                // 到达停止距离时完全停止（与原版一致）
                if (distanceToWaypoint < stopDistance)
                {
                    path = null;
                    moving = false;
                    UpdateMoveAnimation(0f);
                    
                    // 到达目标点，设置 IsArrived 为 true
                    if (controller != null)
                    {
                        controller.SetArrived(true);
                    }
                    
                    // 注意：不再调用 SnapToGround()，因为目标点已在 GetRandomSpawnPoint 中预先修正高度
                    
                    // 开始待机动画
                    isIdling = true;
                    idleTimer = 0f;
                    if (controller != null)
                    {
                        controller.StartTalking();  // 使用 IsTalking 触发待机动画
                    }
                    Debug.Log("[CourierNPC] 到达目标点，开始待机动画");
                    return;
                }
            }
            else
            {
                speedMultiplier = 1f;
            }
            
            // 计算实际移动速度
            float currentSpeed = (isBossFight ? runSpeed : walkSpeed) * speedMultiplier;
            
            // 使用 CharacterController 移动（因为快递员没有 CharacterMainControl）
            Vector3 moveVector = direction * currentSpeed * Time.deltaTime;
            moveVector.y = verticalVelocity * Time.deltaTime;
            
            if (characterController != null && characterController.enabled)
            {
                CollisionFlags flags = characterController.Move(moveVector);
                // 调试：每秒输出一次位置
                if (Time.frameCount % 60 == 0)
                {
                    Debug.Log("[CourierNPC] 移动中: 位置=" + transform.position + ", 速度=" + currentSpeed + ", 方向=" + direction + ", 路点=" + currentWaypoint + "/" + path.vectorPath.Count);
                }
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
            
            // 待机期间不应用重力，避免下沉
            if (isIdling) return;
            
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
        /// 将快递员位置修正到地面（只在到达目标点时调用一次）
        /// </summary>
        private void SnapToGround()
        {
            // 从当前位置向下发射射线，找到地面
            RaycastHit hit;
            Vector3 rayStart = transform.position + Vector3.up * 2f;
            if (Physics.Raycast(rayStart, Vector3.down, out hit, 10f))
            {
                // 临时禁用 CharacterController 以直接设置位置
                if (characterController != null)
                {
                    characterController.enabled = false;
                }
                
                // 设置位置到地面上方一点（CharacterController 的中心在 1 米高）
                Vector3 newPos = hit.point + new Vector3(0f, 0.1f, 0f);
                transform.position = newPos;
                
                // 重新启用 CharacterController
                if (characterController != null)
                {
                    characterController.enabled = true;
                }
                
                // 重置垂直速度
                verticalVelocity = 0f;
                
                Debug.Log("[CourierNPC] 位置修正到地面: " + newPos);
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
                }
                catch { }
            }
        }
        
        public void SetBossFight(bool fighting)
        {
            isBossFight = fighting;
            if (fighting)
            {
                wanderTimer = WANDER_INTERVAL;  // 立即触发移动决策
            }
            Debug.Log("[CourierNPC] SetBossFight: " + fighting);
        }
        
        public void SetCompleted(bool completed)
        {
            isCompleted = completed;
            if (completed)
            {
                StopMove();
            }
            Debug.Log("[CourierNPC] SetCompleted: " + completed);
        }
        
        /// <summary>
        /// 设置是否没有Boss（召唤间隔期间）
        /// </summary>
        public void SetNoBoss(bool noBoss)
        {
            // CourierMovement 不需要存储这个状态，只需要通知 Controller
            if (controller != null)
            {
                controller.SetNoBoss(noBoss);
            }
            Debug.Log("[CourierNPC] Movement.SetNoBoss: " + noBoss);
        }
        
        /// <summary>
        /// 修正目标点的Y坐标到地面（使用Raycast预先计算，避免到达后下沉）
        /// </summary>
        private Vector3 CorrectTargetHeight(Vector3 pos)
        {
            RaycastHit hit;
            Vector3 rayStart = pos + Vector3.up * 5f;
            if (Physics.Raycast(rayStart, Vector3.down, out hit, 20f))
            {
                // 返回地面位置 + 一点偏移，确保不会陷入地面
                return new Vector3(pos.x, hit.point.y + 0.1f, pos.z);
            }
            return pos;
        }
        
        /// <summary>
        /// 让快递员面向玩家
        /// </summary>
        private void FacePlayer()
        {
            if (playerTransform == null) return;
            
            // 计算朝向玩家的方向（只在水平面上）
            Vector3 direction = playerTransform.position - transform.position;
            direction.y = 0;
            
            if (direction != Vector3.zero)
            {
                transform.rotation = Quaternion.LookRotation(direction);
            }
        }
        
        /// <summary>
        /// 显示气泡对话
        /// </summary>
        private void ShowBubble(string dialogue, float duration)
        {
            try
            {
                // 使用原版气泡系统显示对话（speed=-1表示一次性显示全部文字）
                float yOffset = 1.5f;
                Cysharp.Threading.Tasks.UniTaskExtensions.Forget(
                    Duckov.UI.DialogueBubbles.DialogueBubblesManager.Show(dialogue, transform, yOffset, false, false, -1f, duration)
                );
            }
            catch (Exception e)
            {
                Debug.LogWarning("[CourierNPC] 显示气泡失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 获取随机的 Boss 刷新点作为目标（预先修正Y坐标到地面）
        /// </summary>
        private Vector3 GetRandomSpawnPoint()
        {
            try
            {
                if (ModBehaviour.Instance != null)
                {
                    Vector3[] spawnPoints = ModBehaviour.Instance.GetCurrentSceneSpawnPoints();
                    if (spawnPoints != null && spawnPoints.Length > 0)
                    {
                        // 随机选择一个刷新点（排除当前位置附近的点）
                        int maxAttempts = 5;
                        for (int i = 0; i < maxAttempts; i++)
                        {
                            int randomIndex = UnityEngine.Random.Range(0, spawnPoints.Length);
                            Vector3 targetPos = spawnPoints[randomIndex];
                            
                            // 使用 Raycast 修正目标点的 Y 坐标到地面
                            targetPos = CorrectTargetHeight(targetPos);
                            
                            // 确保目标点与当前位置有一定距离（使用水平距离）
                            Vector3 diff = targetPos - transform.position;
                            diff.y = 0;
                            float distance = diff.magnitude;
                            if (distance > 3f)
                            {
                                Debug.Log("[CourierNPC] 选择刷新点 [" + randomIndex + "/" + spawnPoints.Length + "] 作为目标: " + targetPos);
                                return targetPos;
                            }
                        }
                        
                        // 如果多次尝试都找不到合适的点，就用第一个（也要修正高度）
                        Vector3 defaultPos = CorrectTargetHeight(spawnPoints[0]);
                        Debug.Log("[CourierNPC] 使用默认刷新点: " + defaultPos);
                        return defaultPos;
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[CourierNPC] 获取刷新点失败: " + e.Message);
            }
            
            // 如果获取失败，返回零向量（不移动）
            Debug.LogWarning("[CourierNPC] 无法获取刷新点，跳过移动");
            return Vector3.zero;
        }
        
        /// <summary>
        /// 获取远离玩家的 Boss 刷新点（Boss 战时使用）
        /// 找到第一个距离玩家大于5米的点即返回，避免遍历全部点浪费资源
        /// </summary>
        private Vector3 GetSpawnPointAwayFromPlayer()
        {
            try
            {
                if (ModBehaviour.Instance == null || playerTransform == null)
                {
                    return GetRandomSpawnPoint();  // 回退到随机选择
                }
                
                Vector3[] spawnPoints = ModBehaviour.Instance.GetCurrentSceneSpawnPoints();
                if (spawnPoints == null || spawnPoints.Length == 0)
                {
                    return Vector3.zero;
                }
                
                // 找到第一个距离玩家大于5米的刷新点
                for (int i = 0; i < spawnPoints.Length; i++)
                {
                    Vector3 point = CorrectTargetHeight(spawnPoints[i]);
                    
                    // 计算该点到玩家的水平距离
                    Vector3 diff = point - playerTransform.position;
                    diff.y = 0;
                    float distToPlayer = diff.magnitude;
                    
                    // 找到第一个距离玩家大于12米的点就返回
                    if (distToPlayer > 12f)
                    {
                        Debug.Log("[CourierNPC] Boss战逃跑：选择刷新点 [" + i + "]，距离玩家: " + distToPlayer.ToString("F1") + "米");
                        return point;
                    }
                }
                
                // 如果没找到合适的点，回退到随机选择
                Debug.Log("[CourierNPC] Boss战逃跑：未找到距离玩家>12米的点，使用随机刷新点");
                return GetRandomSpawnPoint();
            }
            catch (Exception e)
            {
                Debug.LogWarning("[CourierNPC] 获取远离玩家的刷新点失败: " + e.Message);
                return GetRandomSpawnPoint();
            }
        }
    }
    
    /// <summary>
    /// 快递员交互组件
    /// 参考 BossRushSignInteractable 实现，使用正确的交互方式
    /// </summary>
    public class CourierInteractable : InteractableBase
    {
        private CourierNPCController controller;
        private bool isInitialized = false;
        
        protected override void Awake()
        {
            // 参考路牌实现：设置为非组交互（只有一个选项）
            try { this.interactableGroup = false; } catch { }
            
            // 设置交互名称（使用本地化键，必须在 base.Awake 之前设置）
            try
            {
                this.overrideInteractName = true;
                this._overrideInteractNameKey = "BossRush_CourierService";
                this.InteractName = "BossRush_CourierService";
            }
            catch { }
            
            // 设置交互标记偏移（人物高度中间位置）
            try { this.interactMarkerOffset = new Vector3(0f, 1.0f, 0f); } catch { }
            
            // 确保有 Collider（参考路牌：使用 isTrigger = true）
            try
            {
                Collider existingCol = GetComponent<Collider>();
                if (existingCol == null)
                {
                    CapsuleCollider col = gameObject.AddComponent<CapsuleCollider>();
                    col.height = 2f;
                    col.radius = 0.5f;
                    col.center = new Vector3(0f, 1f, 0f);
                    col.isTrigger = true;
                    this.interactCollider = col;
                }
                else
                {
                    existingCol.isTrigger = true;
                    this.interactCollider = existingCol;
                }
            }
            catch { }
            
            // 调用基类 Awake（可能被其他 Mod Patch，需要 try-catch）
            try { base.Awake(); } catch { }
            
            // 手动设置 Layer 为 Interactable（base.Awake 可能失败）
            try
            {
                if (this.interactCollider != null)
                {
                    int interactableLayer = LayerMask.NameToLayer("Interactable");
                    if (interactableLayer != -1)
                    {
                        this.interactCollider.gameObject.layer = interactableLayer;
                    }
                }
            }
            catch { }
            
            // 获取控制器引用
            try { controller = GetComponent<CourierNPCController>(); } catch { }
            
            isInitialized = true;
            Debug.Log("[CourierNPC] CourierInteractable.Awake 完成，Layer=" + gameObject.layer);
        }
        
        protected override void Start()
        {
            try { base.Start(); } catch { }
            
            // 再次确保名称正确（使用本地化键）
            try
            {
                this.overrideInteractName = true;
                this._overrideInteractNameKey = "BossRush_CourierService";
                this.InteractName = "BossRush_CourierService";
            }
            catch { }
            
            // 显示交互标记
            try { this.MarkerActive = true; } catch { }
            
            Debug.Log("[CourierNPC] CourierInteractable.Start 完成，MarkerActive=" + this.MarkerActive + ", InteractName=" + this.InteractName);
        }
        
        /// <summary>
        /// 检查是否可交互
        /// </summary>
        protected override bool IsInteractable()
        {
            return isInitialized;
        }
        
        /// <summary>
        /// 交互开始时调用（玩家按下交互键）
        /// </summary>
        protected override void OnInteractStart(CharacterMainControl interactCharacter)
        {
            try
            {
                base.OnInteractStart(interactCharacter);
                
                Debug.Log("[CourierNPC] 玩家开始与快递员交互");
                
                // 开始对话动画
                if (controller != null)
                {
                    controller.StartTalking();
                }
                
                // 显示提示（使用本地化键）
                if (ModBehaviour.Instance != null)
                {
                    string message = LocalizationHelper.GetLocalizedText("BossRush_CourierServiceUnavailable");
                    if (string.IsNullOrEmpty(message) || message == "BossRush_CourierServiceUnavailable")
                    {
                        message = L10n.T("快递服务暂未开放，敬请期待！", "Courier service coming soon!");
                    }
                    ModBehaviour.Instance.ShowMessage(message);
                }
                
                // 结束对话动画（延迟一点）
                StartCoroutine(StopTalkingDelayed());
            }
            catch (Exception e)
            {
                Debug.LogError("[CourierNPC] OnInteractStart 出错: " + e.Message);
            }
        }
        
        /// <summary>
        /// 交互停止时调用
        /// </summary>
        protected override void OnInteractStop()
        {
            try
            {
                base.OnInteractStop();
                
                // 停止对话动画
                if (controller != null)
                {
                    controller.StopTalking();
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[CourierNPC] OnInteractStop 出错: " + e.Message);
            }
        }
        
        private IEnumerator StopTalkingDelayed()
        {
            yield return new WaitForSeconds(2f);
            if (controller != null)
            {
                controller.StopTalking();
            }
        }
    }
}
