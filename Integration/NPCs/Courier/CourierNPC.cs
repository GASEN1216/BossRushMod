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
using System.Threading.Tasks;
using UnityEngine;
using Duckov.Utilities;
using Pathfinding;
using Cysharp.Threading.Tasks;
using Saves;
using ItemStatsSystem;
using Dialogues;
using NodeCanvas.DialogueTrees;
using SodaCraft.Localizations;

namespace BossRush
{
    /// <summary>
    /// 快递员NPC系统
    /// </summary>
    public partial class ModBehaviour
    {
        // ============================================================================
        // 快递员实例和资源
        // ============================================================================
        
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
        
        // ============================================================================
        // 刷新位置辅助方法
        // ============================================================================
        
        /// <summary>
        /// 获取BossRush竞技场模式的刷新位置
        /// 使用 NPCSpawnConfig 中的配置
        /// </summary>
        private Vector3 GetBossRushArenaSpawnPosition(string sceneName)
        {
            // 从配置中查询BossRush模式固定位置
            if (NPCSpawnConfig.TryGetCourierBossRushPosition(sceneName, out Vector3 position))
            {
                return position;
            }
            
            // 未配置的场景使用随机刷新点
            Vector3[] spawnPoints = GetCurrentSceneSpawnPoints();
            if (spawnPoints != null && spawnPoints.Length > 0)
            {
                int randomIndex = UnityEngine.Random.Range(0, spawnPoints.Length);
                DevLog("[CourierNPC] BossRush模式随机刷新点 [" + randomIndex + "/" + spawnPoints.Length + "]");
                return spawnPoints[randomIndex];
            }
            return Vector3.zero;
        }
        
        /// <summary>
        /// 获取普通模式的刷新位置（非BossRush模式）
        /// 使用 NPCSpawnConfig 中的配置
        /// </summary>
        private Vector3 GetNormalModeSpawnPosition(string sceneName)
        {
            if (NPCSpawnConfig.TryGetCourierNormalModePosition(sceneName, out Vector3 position))
            {
                int count = NPCSpawnConfig.GetCourierNormalModeSpawnPointCount(sceneName);
                DevLog("[CourierNPC] 普通模式随机刷新点，场景: " + sceneName + ", 可选点数: " + count);
                return position;
            }
            
            // 没有配置则返回零向量表示不生成
            DevLog("[CourierNPC] 场景 " + sceneName + " 未配置普通模式刷新点");
            return Vector3.zero;
        }
        
        /// <summary>
        /// 检查当前场景是否应该在普通模式下生成快递员
        /// </summary>
        private bool ShouldSpawnCourierInNormalMode(string sceneName)
        {
            return NPCSpawnConfig.HasCourierNormalModeConfig(sceneName);
        }
        
        /// <summary>
        /// 在 BossRush 竞技场生成快递员 NPC
        /// 支持BossRush模式（固定位置）和普通模式（随机刷新点）
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
            
            // 获取生成位置
            Vector3 spawnPos;
            string currentSceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            
            // 检测是否为BossRush模式（包括ModeD和竞技场激活状态）
            bool isBossRushMode = IsActive || IsModeDActive || IsBossRushArenaActive;
            DevLog("[CourierNPC] 模式检测: IsActive=" + IsActive + ", IsModeDActive=" + IsModeDActive + ", IsBossRushArenaActive=" + IsBossRushArenaActive + " => BossRush模式=" + isBossRushMode);
            
            if (isBossRushMode)
            {
                // BossRush模式：使用竞技场固定位置
                spawnPos = GetBossRushArenaSpawnPosition(currentSceneName);
                DevLog("[CourierNPC] BossRush模式，场景: " + currentSceneName + ", 位置: " + spawnPos);
                
                // 检查是否获取到有效位置
                if (spawnPos == Vector3.zero)
                {
                    DevLog("[CourierNPC] BossRush模式无法获取刷新点，跳过生成");
                    return;
                }
            }
            else
            {
                // 普通模式：检查是否有配置的随机刷新点
                if (ShouldSpawnCourierInNormalMode(currentSceneName))
                {
                    spawnPos = GetNormalModeSpawnPosition(currentSceneName);
                    DevLog("[CourierNPC] 普通模式，场景: " + currentSceneName + ", 随机位置: " + spawnPos);
                    
                    // 检查是否获取到有效位置
                    if (spawnPos == Vector3.zero)
                    {
                        DevLog("[CourierNPC] 普通模式无法获取刷新点，跳过生成");
                        return;
                    }
                }
                else
                {
                    // 普通模式下，未配置的场景不生成快递员
                    DevLog("[CourierNPC] 普通模式，场景 " + currentSceneName + " 未配置刷新点，跳过生成");
                    return;
                }
            }
            
            // 使用 Raycast 修正落点到地面
            RaycastHit hit;
            if (Physics.Raycast(spawnPos + Vector3.up * 5f, Vector3.down, out hit, 20f))
            {
                spawnPos = hit.point + new Vector3(0f, 0.1f, 0f);
                DevLog("[CourierNPC] Raycast修正后位置: " + spawnPos);
            }
            else
            {
                DevLog("[CourierNPC] Raycast修正失败，使用原始坐标: " + spawnPos);
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
                
                // 设置移动模式：普通模式使用 NPCSpawnConfig 刷新点，BossRush模式使用 Boss 刷新点
                if (!isBossRushMode)
                {
                    movement.SetNormalMode(true, currentSceneName);
                    DevLog("[CourierNPC] 设置为普通模式，使用场景 " + currentSceneName + " 的刷新点");
                }
                
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
        
        /// <summary>
        /// 传送玩家到快递员NPC身边（调试功能，F12 调用）
        /// </summary>
        public void TeleportToCourierNPC()
        {
            // 检查快递员是否存在
            if (courierNPCInstance == null)
            {
                DevLog("[BossRush] F12 传送：快递员NPC不存在");
                return;
            }
            
            // 获取玩家引用
            CharacterMainControl main = CharacterMainControl.Main;
            if (main == null)
            {
                DevLog("[BossRush] F12 传送：未找到玩家 CharacterMainControl");
                return;
            }
            
            // 计算传送目标位置（快递员位置偏移2米，避免重叠）
            Vector3 courierPos = courierNPCInstance.transform.position;
            Vector3 offset = new Vector3(2f, 0f, 0f);  // X轴偏移2米
            Vector3 targetPos = courierPos + offset;
            
            // 使用 Raycast 修正落点到地面
            RaycastHit hit;
            if (Physics.Raycast(targetPos + Vector3.up * 5f, Vector3.down, out hit, 20f))
            {
                targetPos = hit.point + new Vector3(0f, 0.1f, 0f);
            }
            
            // 执行传送
            main.transform.position = targetPos;
            DevLog("[BossRush] F12 传送：已将玩家传送到快递员身边，位置: " + targetPos);
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
        
        // ============================================================================
        // 首次见面对话功能
        // ============================================================================
        
        // 存档持久化 Key（每个存档独立）
        private const string FIRST_MEET_SAVE_KEY = "BossRush_CourierFirstMeet";
        
        // Wiki Book 物品 TypeID
        private const int WIKI_BOOK_TYPE_ID = 500007;
        
        // 对话进行中标志
        private bool isInFirstMeetDialogue = false;
        
        // DuckovDialogueActor 组件引用（用于大对话显示）
        private DuckovDialogueActor dialogueActor = null;
        
        // 注：首次见面对话内容已移至 LocalizationInjector.COURIER_FIRST_MEET_DIALOGUES
        // 使用 LocalizationInjector.GetCourierFirstMeetDialogueKeys() 获取本地化键
        
        /// <summary>
        /// 检查是否已触发首次见面（从存档读取）
        /// </summary>
        private bool HasTriggeredFirstMeet
        {
            get
            {
                try
                {
                    if (Saves.SavesSystem.KeyExisits(FIRST_MEET_SAVE_KEY))
                    {
                        return Saves.SavesSystem.Load<bool>(FIRST_MEET_SAVE_KEY);
                    }
                    return false;
                }
                catch
                {
                    return false;
                }
            }
        }
        
        /// <summary>
        /// 设置首次见面状态（保存到存档）
        /// </summary>
        private void SetFirstMeetTriggered()
        {
            try
            {
                Saves.SavesSystem.Save<bool>(FIRST_MEET_SAVE_KEY, true);
                ModBehaviour.DevLog("[CourierNPC] 首次见面状态已保存到存档");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[CourierNPC] [WARNING] 保存首次见面状态失败: " + e.Message);
            }
        }
        
        // ============================================================================
        // 游戏原生动画参数哈希值（与 CharacterAnimationControl 一致）
        // ============================================================================
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
                ModBehaviour.DevLog("[CourierNPC] Controller.Awake: 找到 Animator 组件");
                if (animator.runtimeAnimatorController != null)
                {
                    ModBehaviour.DevLog("[CourierNPC] Animator Controller: " + animator.runtimeAnimatorController.name);
                    // 列出所有参数
                    foreach (var param in animator.parameters)
                    {
                        ModBehaviour.DevLog("[CourierNPC]   参数: " + param.name + " (" + param.type + ")");
                    }
                }
                else
                {
                    ModBehaviour.DevLog("[CourierNPC] [WARNING] 警告：Animator 没有 RuntimeAnimatorController！");
                }
            }
            else
            {
                ModBehaviour.DevLog("[CourierNPC] [WARNING] Controller.Awake: 未找到 Animator 组件！");
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
                    ModBehaviour.DevLog("[CourierNPC] Controller.Start: 获取到玩家引用");
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[CourierNPC] [WARNING] 获取 CharacterMainControl.Main 失败: " + e.Message);
            }
            
            if (playerTransform == null)
            {
                var player = GameObject.FindGameObjectWithTag("Player");
                if (player != null)
                {
                    playerTransform = player.transform;
                    ModBehaviour.DevLog("[CourierNPC] Controller.Start: 通过 Tag 获取到玩家引用");
                }
            }
            
            // 初始化游戏原生参数默认值
            InitializeDefaultAnimatorParams();
            
            // 创建名字标签
            CreateNameTag();
            
            // 设置对话Actor组件（用于大对话显示）- 使用新的工厂类
            SetupDialogueActor();
        }
        
        /// <summary>
        /// 设置 DuckovDialogueActor 组件（用于大对话显示）
        /// 使用 DialogueActorFactory 统一创建，符合官方实现方式
        /// </summary>
        private void SetupDialogueActor()
        {
            try
            {
                // 使用工厂类创建 Actor（自动处理反射和本地化）
                dialogueActor = DialogueActorFactory.CreateBilingual(
                    gameObject,
                    "courier_npc",           // Actor ID
                    "阿稳",                   // 中文名称
                    "Awen",                  // 英文名称
                    new Vector3(0, 2f, 0)    // 对话指示器偏移量
                );
                
                if (dialogueActor != null)
                {
                    ModBehaviour.DevLog("[CourierNPC] DuckovDialogueActor 组件已通过工厂创建");
                }
                else
                {
                    ModBehaviour.DevLog("[CourierNPC] [WARNING] DialogueActorFactory 创建失败");
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[CourierNPC] [WARNING] 设置 DuckovDialogueActor 失败: " + e.Message);
            }
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
                // 直接使用 L10n.T 获取本地化名称（不依赖注入）
                string courierName = L10n.T("阿稳", "Awen");
                nameTagText.text = courierName;
                nameTagText.fontSize = 4f;  // 字号大一点
                nameTagText.alignment = TMPro.TextAlignmentOptions.Center;
                nameTagText.color = Color.white;  // 白色
                
                // 设置文字始终面向相机
                nameTagText.enableAutoSizing = false;
                
                // 设置排序层级确保可见
                nameTagText.sortingOrder = 100;
                
                ModBehaviour.DevLog("[CourierNPC] 名字标签创建成功: " + courierName);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[CourierNPC] [WARNING] 创建名字标签失败: " + e.Message);
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
                
                ModBehaviour.DevLog("[CourierNPC] 动画参数初始化完成");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[CourierNPC] [WARNING] 初始化动画参数出错: " + e.Message);
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
            
            // 检查首次见面触发
            CheckFirstMeetTrigger();
        }
        
        /// <summary>
        /// 检查并触发首次见面对话
        /// </summary>
        private void CheckFirstMeetTrigger()
        {
            // 如果已经在对话中，跳过
            if (isInFirstMeetDialogue) return;
            
            // 如果已经触发过首次见面，永久跳过（DevMode 和非 DevMode 统一逻辑）
            if (HasTriggeredFirstMeet) return;
            
            // 如果玩家引用为空，跳过
            if (playerTransform == null) return;
            
            // 检测玩家距离
            float distance = Vector3.Distance(transform.position, playerTransform.position);
            if (distance <= NEAR_DISTANCE)
            {
                // 触发首次见面对话
                ModBehaviour.DevLog("[CourierNPC] 玩家进入范围，触发首次见面对话");
                TriggerFirstMeetDialogue().Forget();
            }
        }
        
        /// <summary>
        /// 触发首次见面对话序列（异步）
        /// 使用 DialogueManager 统一管理，符合官方实现方式
        /// </summary>
        private async Cysharp.Threading.Tasks.UniTaskVoid TriggerFirstMeetDialogue()
        {
            // 获取移动组件引用（在 try 外部，以便 finally 中使用）
            CourierMovement movement = GetComponent<CourierMovement>();
            
            try
            {
                // 标记对话进行中
                isInFirstMeetDialogue = true;
                
                // 立即保存状态到存档（防止中途退出后重复触发）
                SetFirstMeetTriggered();
                
                // 停止移动
                if (movement != null)
                {
                    movement.SetInService(true);
                }
                
                // 面向玩家
                FacePlayer();
                
                // 开始对话动画
                StartTalking();
                
                // 使用 DialogueManager 显示对话序列（使用本地化键）
                // 获取首次见面对话的本地化键数组
                string[] dialogueKeys = LocalizationInjector.GetCourierFirstMeetDialogueKeys();
                
                ModBehaviour.DevLog("[CourierNPC] 开始首次见面对话序列，共 " + dialogueKeys.Length + " 条对话");
                
                // 使用 DialogueManager 显示对话序列
                await DialogueManager.ShowDialogueSequence(dialogueActor, dialogueKeys);
                
                ModBehaviour.DevLog("[CourierNPC] 对话序列完成");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[CourierNPC] [ERROR] 首次见面对话出错: " + e.Message + "\n" + e.StackTrace);
                // 确保对话系统状态正确
                DialogueManager.ForceEndDialogue();
            }
            
            // 无论对话是否成功，都执行后续逻辑（生成物品等）
            try
            {
                // 对话结束，停止对话动画
                StopTalking();
                
                // 显示"给你"气泡（使用本地化键）
                string giveText = "BossRush_CourierGive".ToPlainText();
                float yOffset = 1.5f;
                Cysharp.Threading.Tasks.UniTaskExtensions.Forget(
                    Duckov.UI.DialogueBubbles.DialogueBubblesManager.Show(giveText, transform, yOffset, false, false, -1f, 3f)
                );
                ModBehaviour.DevLog("[CourierNPC] 显示气泡: " + giveText);
                
                // 等待一小段时间让气泡显示
                await Cysharp.Threading.Tasks.UniTask.Delay(500);
                
                // 生成 Wiki Book 物品
                SpawnWikiBook();
                
                // 恢复移动
                if (movement != null)
                {
                    movement.SetInService(false);
                }
                
                ModBehaviour.DevLog("[CourierNPC] 首次见面对话序列完成");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[CourierNPC] [ERROR] 后续处理出错: " + e.Message);
            }
            finally
            {
                isInFirstMeetDialogue = false;
            }
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
        /// 生成 Wiki Book 物品（在NPC脚下）
        /// </summary>
        private void SpawnWikiBook()
        {
            try
            {
                // 使用 ItemAssetsCollection 生成物品
                Item wikiBook = ItemAssetsCollection.InstantiateSync(WIKI_BOOK_TYPE_ID);
                
                if (wikiBook == null)
                {
                    ModBehaviour.DevLog("[CourierNPC] [ERROR] 无法生成 Wiki Book 物品，TypeID=" + WIKI_BOOK_TYPE_ID);
                    return;
                }
                
                // 在NPC脚下生成物品（而不是直接发送给玩家）
                Vector3 dropPosition = transform.position;
                Vector3 dropDirection = Vector3.forward;
                wikiBook.Drop(dropPosition, true, dropDirection, 0f);
                
                ModBehaviour.DevLog("[CourierNPC] Wiki Book 物品已生成在NPC脚下，位置=" + dropPosition);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[CourierNPC] [ERROR] 生成 Wiki Book 失败: " + e.Message);
            }
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
                ModBehaviour.DevLog("[CourierNPC] 开始对话动画");
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
                // 随机选择一句对话（直接使用 L10n.T）
                string dialogue = GetRandomCourierDialogue();
                
                // 使用原版气泡系统显示对话（speed=-1表示一次性显示全部文字）
                // yOffset 设置为名字标签高度附近（1.5f，比名字标签稍低）
                float yOffset = 1.5f;
                Cysharp.Threading.Tasks.UniTaskExtensions.Forget(
                    Duckov.UI.DialogueBubbles.DialogueBubblesManager.Show(dialogue, transform, yOffset, false, false, -1f, 3f)
                );
                
                ModBehaviour.DevLog("[CourierNPC] 显示对话: " + dialogue);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[CourierNPC] [WARNING] 显示对话气泡失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 获取随机快递员对话（直接使用 L10n.T，不依赖注入）
        /// </summary>
        private string GetRandomCourierDialogue()
        {
            // 快递员随机对话列表
            string[][] dialogues = new string[][]
            {
                new string[] { "补给到了……先把伞可乐灌了，灵魂别掉地上。", "Supplies arrived... drink your Umbrella Cola first, don't let your soul drop." },
                new string[] { "这地方路况真差，比拎着XO钥匙去洗脚房还折磨。", "The roads here are terrible, worse than carrying XO keys to a foot spa." },
                new string[] { "别盯着我背包看，都是J-Lab登记过的，少一件杰夫要开会。", "Stop staring at my backpack, everything's registered with J-Lab. Jeff will call a meeting if anything's missing." },
                new string[] { "Boss也得排队，先去祭坛交羽毛，图腾按流程发。", "Even bosses have to queue. Go to the altar with feathers first, totems are distributed by procedure." },
                new string[] { "哎，这里谁点了'急件'？紫色空间能量都溢出来了。", "Hey, who ordered 'express delivery' here? Purple space energy is overflowing." },
                new string[] { "星球都快崩了还要准点，J-Lab的KPI不讲情面。", "The planet's about to collapse and we still need to be on time. J-Lab's KPIs show no mercy." },
                new string[] { "你要是能活到下一波，我给你盖个章，再塞你一瓶'有糖的'——有灵魂那种。", "If you survive the next wave, I'll stamp your card and slip you a 'sugared' one - the kind with soul." },
                new string[] { "别吵，听见没？那边在打碟……蓝皮人可能又在看热闘。", "Quiet, hear that? Someone's DJing over there... the blue guys are probably watching again." },
                new string[] { "我这把年纪了还在跑单，外星水熊虫母舰来了都得排队签收。", "At my age still running deliveries. Even alien tardigrade motherships have to queue for pickup." },
                new string[] { "箱子里是什么？浓缩浆质、绷带，还有一张'无糖可乐慎用'的说明。", "What's in the box? Concentrated plasma, bandages, and a 'use sugar-free cola with caution' note." },
                new string[] { "你要投诉？可以，去找蓝皮人，他一个响指就能把你的工单传送走。", "Want to complain? Sure, find the blue guy. One snap and he'll teleport your ticket away." },
                new string[] { "路线规划又被风暴改了……行，绕开机器蜘蛛，走那条最紫的。", "Route changed by the storm again... fine, avoid the mech spiders, take the most purple path." },
                new string[] { "我不怕Boss，我怕紫毒把快递标签腐蚀了——到时候谁也别想对账。", "I'm not afraid of bosses. I'm afraid the purple poison will corrode the delivery labels - then no one can reconcile accounts." },
                new string[] { "签收方式：按爪印、按羽毛、或者交一块蓝色方块当押金。", "Sign for delivery: paw print, feather, or leave a blue cube as deposit." },
                new string[] { "别跟我讲热血，我只认单号、撤离路线，以及'有糖才有灵魂'。", "Don't talk passion to me. I only care about order numbers, evacuation routes, and 'sugar means soul'." },
                new string[] { "看到那只到处乱创的火龙了吗？", "See that fire dragon causing chaos everywhere?" },
                new string[] { "嗯...火龙怕毒，哪天毒死它", "Hmm... fire dragons fear poison. Maybe poison it someday." },
                new string[] { "你知道火龙也怕冰吗？我有一次都把它打坠机了哈哈哈哈", "Did you know fire dragons also fear ice? I once made it crash land hahaha" },
                new string[] { "这该死的火龙把我的快递都创飞了", "That damn fire dragon knocked all my deliveries flying" },
                new string[] { "那头火龙在叽里咕噜的时候最好跑远点", "When that fire dragon starts gurgling, you better run far away" },
                new string[] { "离火龙太近可是会被炸的哦", "Get too close to the fire dragon and you'll get blown up" },
                new string[] { "有时候送的快也很重要，直接就把钱拿过来，概不赊账！", "Sometimes speed matters, just hand over the money, no credit!" }
            };
            
            int index = UnityEngine.Random.Range(0, dialogues.Length);
            return L10n.T(dialogues[index][0], dialogues[index][1]);
        }
        
        /// <summary>
        /// 结束交互，返回之前的状态
        /// </summary>
        public void StopTalking()
        {
            if (hasAnimator)
            {
                SafeSetBool(hash_IsTalking, false);
                ModBehaviour.DevLog("[CourierNPC] 结束对话动画");
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
                ModBehaviour.DevLog("[CourierNPC] 设置 IsArrived: " + arrived);
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
                ModBehaviour.DevLog("[CourierNPC] 设置 BossFight: " + isFighting);
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
                ModBehaviour.DevLog("[CourierNPC] 设置 NoBoss: " + noBoss);
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
                ModBehaviour.DevLog("[CourierNPC] 设置通关状态");
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
                ModBehaviour.DevLog("[CourierNPC] 重置状态");
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
        private const float IDLE_DURATION = 5f;  // 待机5秒
        
        // 快递服务状态（服务期间停止移动）
        private bool isInService = false;
        
        // 普通模式标志和刷新点缓存（非BossRush模式时使用NPCSpawnConfig中的刷新点）
        private bool isNormalMode = false;
        private Vector3[] normalModeSpawnPoints = null;
        
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
        
        // 延迟恢复移动的协程引用（用于取消）
        private Coroutine delayedResumeCoroutine = null;
        
        /// <summary>
        /// 设置普通模式（非BossRush模式）
        /// 普通模式下使用 NPCSpawnConfig 中配置的刷新点作为漫步目标
        /// </summary>
        /// <param name="normalMode">是否为普通模式</param>
        /// <param name="sceneName">场景名称，用于获取对应的刷新点配置</param>
        public void SetNormalMode(bool normalMode, string sceneName = null)
        {
            isNormalMode = normalMode;
            normalModeSpawnPoints = null;
            
            if (normalMode && !string.IsNullOrEmpty(sceneName))
            {
                // 从 NPCSpawnConfig 获取普通模式刷新点
                if (NPCSpawnConfig.CourierNormalModeConfigs.TryGetValue(sceneName, out NPCSceneSpawnConfig config))
                {
                    normalModeSpawnPoints = config.spawnPoints;
                    ModBehaviour.DevLog("[CourierNPC] 设置普通模式，场景: " + sceneName + ", 刷新点数: " + (normalModeSpawnPoints?.Length ?? 0));
                }
                else
                {
                    ModBehaviour.DevLog("[CourierNPC] 普通模式场景 " + sceneName + " 未配置刷新点");
                }
            }
            else
            {
                ModBehaviour.DevLog("[CourierNPC] 设置为BossRush模式");
            }
        }
        
        /// <summary>
        /// 设置快递服务状态（服务期间停止移动）
        /// </summary>
        public void SetInService(bool inService)
        {
            if (inService)
            {
                // 进入服务状态：取消之前的延迟恢复协程，立即停止移动
                if (delayedResumeCoroutine != null)
                {
                    StopCoroutine(delayedResumeCoroutine);
                    delayedResumeCoroutine = null;
                    ModBehaviour.DevLog("[CourierNPC] 取消之前的延迟恢复协程");
                }
                isInService = true;
                StopMove();
                ModBehaviour.DevLog("[CourierNPC] 进入快递服务状态，停止移动");
            }
            else
            {
                // 退出服务状态
                // 如果正在待机期间，重置待机计时器并重新触发待机动画，避免滑步
                if (isIdling)
                {
                    idleTimer = 0f;
                    if (controller != null)
                    {
                        controller.StartTalking();  // 重新触发待机动画
                    }
                    ModBehaviour.DevLog("[CourierNPC] 退出服务状态，继续待机动画");
                }
                // 延迟1秒后恢复移动
                ModBehaviour.DevLog("[CourierNPC] 退出快递服务状态，1秒后恢复移动");
                delayedResumeCoroutine = StartCoroutine(DelayedResumeMovement());
            }
        }
        
        /// <summary>
        /// 延迟恢复移动（UI关闭后等待1秒再开始走动）
        /// </summary>
        private IEnumerator DelayedResumeMovement()
        {
            yield return new WaitForSeconds(1f);
            // 只有在仍处于非服务状态时才恢复移动（双重保险）
            if (!isInService)
            {
                ModBehaviour.DevLog("[CourierNPC] 延迟结束，恢复移动");
            }
            isInService = false;
            delayedResumeCoroutine = null;
        }
        
        void Start()
        {
            ModBehaviour.DevLog("[CourierNPC] CourierMovement.Start 开始");
            
            controller = GetComponent<CourierNPCController>();
            animator = GetComponentInChildren<Animator>();
            
            // 获取玩家引用
            try
            {
                if (CharacterMainControl.Main != null)
                {
                    playerTransform = CharacterMainControl.Main.transform;
                    ModBehaviour.DevLog("[CourierNPC] 获取到玩家引用");
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[CourierNPC] [WARNING] 获取玩家引用失败: " + e.Message);
            }
            
            if (playerTransform == null)
            {
                var player = GameObject.FindGameObjectWithTag("Player");
                if (player != null)
                {
                    playerTransform = player.transform;
                    ModBehaviour.DevLog("[CourierNPC] 通过 Tag 获取到玩家引用");
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
            
            ModBehaviour.DevLog("[CourierNPC] 开始初始化移动系统，当前位置: " + transform.position);
            
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
                ModBehaviour.DevLog("[CourierNPC] 添加 CharacterController 组件");
            }
            
            // 2. 添加 Seeker 组件（A* Pathfinding 核心组件，与原版 AI_PathControl 一致）
            seeker = gameObject.GetComponent<Seeker>();
            if (seeker == null)
            {
                seeker = gameObject.AddComponent<Seeker>();
                ModBehaviour.DevLog("[CourierNPC] 添加 Seeker 组件");
            }
            else
            {
                ModBehaviour.DevLog("[CourierNPC] Seeker 组件已存在");
            }
            
            // 3. 检查 A* 是否可用
            if (AstarPath.active == null)
            {
                ModBehaviour.DevLog("[CourierNPC] [WARNING] 警告：A* Pathfinding 未激活！快递员将无法寻路");
            }
            else
            {
                ModBehaviour.DevLog("[CourierNPC] A* Pathfinding 已激活，图数量: " + AstarPath.active.graphs.Length);
                
                // 列出所有图的类型
                for (int i = 0; i < AstarPath.active.graphs.Length; i++)
                {
                    var graph = AstarPath.active.graphs[i];
                    if (graph != null)
                    {
                        ModBehaviour.DevLog("[CourierNPC]   图[" + i + "]: " + graph.GetType().Name);
                    }
                }
            }
            
            isInitialized = true;
            ModBehaviour.DevLog("[CourierNPC] 移动系统初始化完成（使用 A* Seeker）");
            
            // 立即尝试一次寻路测试
            yield return new WaitForSeconds(0.5f);
            if (seeker != null && AstarPath.active != null)
            {
                Vector3 testTarget = transform.position + new Vector3(2f, 0f, 2f);
                ModBehaviour.DevLog("[CourierNPC] 测试寻路到: " + testTarget);
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
                ModBehaviour.DevLog("[CourierNPC] [WARNING] MoveToPos 失败：Seeker 为空");
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
            ModBehaviour.DevLog("[CourierNPC] 开始寻路到: " + pos);
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
                ModBehaviour.DevLog("[CourierNPC] 路径计算成功，路点数: " + p.vectorPath.Count);
            }
            else
            {
                ModBehaviour.DevLog("[CourierNPC] [WARNING] 路径计算失败: " + p.errorLog);
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
            
            // 快递服务期间停止所有移动逻辑
            if (isInService)
            {
                UpdateMoveAnimation(0f);
                return;
            }
            
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
                    ModBehaviour.DevLog("[CourierNPC] 待机结束，准备继续移动");
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
                    ModBehaviour.DevLog("[CourierNPC] 到达目标点，开始待机动画");
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
                // [性能优化] 调试日志：每5秒输出一次位置（降低日志频率）
                if (Time.frameCount % 300 == 0)
                {
                    ModBehaviour.DevLog("[CourierNPC] 移动中: 位置=" + transform.position + ", 速度=" + currentSpeed + ", 方向=" + direction + ", 路点=" + currentWaypoint + "/" + path.vectorPath.Count);
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
                
                ModBehaviour.DevLog("[CourierNPC] 位置修正到地面: " + newPos);
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
            ModBehaviour.DevLog("[CourierNPC] SetBossFight: " + fighting);
        }
        
        public void SetCompleted(bool completed)
        {
            isCompleted = completed;
            if (completed)
            {
                StopMove();
            }
            ModBehaviour.DevLog("[CourierNPC] SetCompleted: " + completed);
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
            ModBehaviour.DevLog("[CourierNPC] Movement.SetNoBoss: " + noBoss);
        }
        
        /// <summary>
        /// 修正目标点的Y坐标到地面（使用Raycast预先计算，避免到达后下沉）
        /// [修复] 从更高位置发射射线，并使用多次射线检测找到最低的地面点
        /// 这样可以避免在室内场景中错误地返回房顶高度
        /// </summary>
        private Vector3 CorrectTargetHeight(Vector3 pos)
        {
            RaycastHit hit;
            // [修复] 从更高位置发射射线（50米），确保能穿过多层建筑
            Vector3 rayStart = pos + Vector3.up * 50f;
            
            // 使用 RaycastAll 获取所有碰撞点，然后选择最低的地面点
            RaycastHit[] hits = Physics.RaycastAll(rayStart, Vector3.down, 100f);
            if (hits != null && hits.Length > 0)
            {
                // 找到最低的碰撞点（最接近配置的 Y 坐标）
                float lowestY = float.MaxValue;
                float configY = pos.y;
                float bestY = pos.y;
                
                foreach (var h in hits)
                {
                    // 优先选择接近配置 Y 坐标的点（允许 1 米误差）
                    if (Mathf.Abs(h.point.y - configY) < 1f)
                    {
                        bestY = h.point.y + 0.1f;
                        break;
                    }
                    // 否则选择最低的点
                    if (h.point.y < lowestY)
                    {
                        lowestY = h.point.y;
                        bestY = h.point.y + 0.1f;
                    }
                }
                
                return new Vector3(pos.x, bestY, pos.z);
            }
            
            // 如果没有碰撞，使用单次射线检测
            if (Physics.Raycast(rayStart, Vector3.down, out hit, 100f))
            {
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
                ModBehaviour.DevLog("[CourierNPC] [WARNING] 显示气泡失败: " + e.Message);
            }
        }
        
        /// <summary>
        /// 获取随机的刷新点作为目标（预先修正Y坐标到地面）
        /// 普通模式使用 NPCSpawnConfig 中的刷新点，BossRush模式使用 Boss 刷新点
        /// </summary>
        private Vector3 GetRandomSpawnPoint()
        {
            try
            {
                Vector3[] spawnPoints = null;
                
                // 根据模式选择刷新点来源
                if (isNormalMode && normalModeSpawnPoints != null && normalModeSpawnPoints.Length > 0)
                {
                    // 普通模式：使用 NPCSpawnConfig 中配置的刷新点
                    spawnPoints = normalModeSpawnPoints;
                    ModBehaviour.DevLog("[CourierNPC] 使用普通模式刷新点，共 " + spawnPoints.Length + " 个");
                }
                else if (ModBehaviour.Instance != null)
                {
                    // BossRush模式：使用 Boss 刷新点
                    spawnPoints = ModBehaviour.Instance.GetCurrentSceneSpawnPoints();
                }
                
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
                            ModBehaviour.DevLog("[CourierNPC] 选择刷新点 [" + randomIndex + "/" + spawnPoints.Length + "] 作为目标: " + targetPos);
                            return targetPos;
                        }
                    }
                    
                    // 如果多次尝试都找不到合适的点，就用第一个（也要修正高度）
                    Vector3 defaultPos = CorrectTargetHeight(spawnPoints[0]);
                    ModBehaviour.DevLog("[CourierNPC] 使用默认刷新点: " + defaultPos);
                    return defaultPos;
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[CourierNPC] [WARNING] 获取刷新点失败: " + e.Message);
            }
            
            // 如果获取失败，返回零向量（不移动）
            ModBehaviour.DevLog("[CourierNPC] [WARNING] 无法获取刷新点，跳过移动");
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
                        ModBehaviour.DevLog("[CourierNPC] Boss战逃跑：选择刷新点 [" + i + "]，距离玩家: " + distToPlayer.ToString("F1") + "米");
                        return point;
                    }
                }
                
                // 如果没找到合适的点，回退到随机选择
                ModBehaviour.DevLog("[CourierNPC] Boss战逃跑：未找到距离玩家>12米的点，使用随机刷新点");
                return GetRandomSpawnPoint();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[CourierNPC] [WARNING] 获取远离玩家的刷新点失败: " + e.Message);
                return GetRandomSpawnPoint();
            }
        }
    }
    
    // CourierDeliveryInteractable 已移除，快递服务现在由 CourierInteractable 主选项处理
    
    /// <summary>
    /// 快递员交互组件 - 寄存服务选项
    /// 直接打开原版 PlayerStorage（玩家仓库）
    /// </summary>
    public class CourierStorageInteractable : InteractableBase
    {
        private CourierNPCController controller;
        private bool isInitialized = false;
        
        protected override void Awake()
        {
            // 子选项不需要设置 interactableGroup，它们是被主交互组件管理的
            
            try
            {
                this.overrideInteractName = true;
                this._overrideInteractNameKey = "BossRush_StorageService";
                this.InteractName = "BossRush_StorageService";
            }
            catch { }
            
            try { this.interactMarkerOffset = new Vector3(0f, 1.0f, 0f); } catch { }
            
            try { base.Awake(); } catch { }
            
            try { controller = GetComponentInParent<CourierNPCController>(); } catch { }
            
            // 子选项不需要自己的 Collider，隐藏交互标记
            try { this.MarkerActive = false; } catch { }
            
            isInitialized = true;
        }
        
        protected override void Start()
        {
            try { base.Start(); } catch { }
        }
        
        protected override bool IsInteractable()
        {
            return isInitialized;
        }
        
        protected override void OnInteractStart(CharacterMainControl interactCharacter)
        {
            try
            {
                base.OnInteractStart(interactCharacter);
                ModBehaviour.DevLog("[CourierNPC] 玩家选择寄存服务");
                
                // 调用寄存服务
                StorageDepositService.OpenService(controller?.transform);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[CourierNPC] [ERROR] 寄存服务交互出错: " + e.Message);
            }
        }
        
        protected override void OnInteractStop()
        {
            try { base.OnInteractStop(); } catch { }
        }
    }
    
    /// <summary>
    /// 快递员主交互组件
    /// 使用 InteractableBase 的 interactableGroup 模式，与路牌一样的方式实现多选项
    /// 玩家按 F 交互后弹出选项列表（快递服务、寄存服务）
    /// </summary>
    public class CourierInteractable : InteractableBase
    {
        private bool optionsInjected = false;
        private List<InteractableBase> groupOptions = new List<InteractableBase>();
        
        protected override void Awake()
        {
            try
            {
                // 设置为交互组（启用多选项模式）
                this.interactableGroup = true;
                
                // 设置主交互名称（显示为第一个选项"快递服务"）
                this.overrideInteractName = true;
                this._overrideInteractNameKey = "BossRush_CourierService";
                this.InteractName = "BossRush_CourierService";
                
                // 设置交互标记偏移（显示在人物中间）
                this.interactMarkerOffset = new Vector3(0f, 1.0f, 0f);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[CourierNPC] [ERROR] CourierInteractable.Awake 设置属性失败: " + e.Message);
            }
            
            try
            {
                base.Awake();
            }
            catch (Exception e)
            {
                // 捕获可能的异常，确保 Mod 能继续运行
                ModBehaviour.DevLog("[CourierNPC] [WARNING] CourierInteractable base.Awake 异常: " + e.Message);
            }
            
            // 确保有 Collider
            try
            {
                Collider col = GetComponent<Collider>();
                if (col == null)
                {
                    CapsuleCollider capsule = gameObject.AddComponent<CapsuleCollider>();
                    capsule.height = 2f;
                    capsule.radius = 0.8f;
                    capsule.center = new Vector3(0f, 1f, 0f);
                    capsule.isTrigger = false;  // 不是触发器，是实体碰撞器
                    this.interactCollider = capsule;
                }
                else
                {
                    this.interactCollider = col;
                }
                
                // 设置 Layer 为 Interactable（让玩家能检测到交互点）
                int interactableLayer = LayerMask.NameToLayer("Interactable");
                if (interactableLayer != -1)
                {
                    gameObject.layer = interactableLayer;
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[CourierNPC] [ERROR] CourierInteractable 设置 Collider 失败: " + e.Message);
            }
            
            ModBehaviour.DevLog("[CourierNPC] CourierInteractable.Awake 完成");
        }
        
        protected override void Start()
        {
            try
            {
                base.Start();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[CourierNPC] [WARNING] CourierInteractable base.Start 异常: " + e.Message);
            }
            
            // 注入所有选项
            if (!optionsInjected)
            {
                InjectAllOptions();
            }
        }
        
        /// <summary>
        /// 注入所有交互选项（寄存服务作为子选项，快递服务由主交互处理）
        /// </summary>
        private void InjectAllOptions()
        {
            try
            {
                optionsInjected = true;
                
                // 获取或创建 otherInterablesInGroup 列表
                var field = typeof(InteractableBase).GetField("otherInterablesInGroup",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (field == null)
                {
                    ModBehaviour.DevLog("[CourierNPC] [ERROR] 无法获取 otherInterablesInGroup 字段");
                    return;
                }
                
                var list = field.GetValue(this) as List<InteractableBase>;
                if (list == null)
                {
                    list = new List<InteractableBase>();
                    field.SetValue(this, list);
                }
                
                // 主选项是"快递服务"（由 OnTimeOut 处理）
                // 子选项只有"寄存服务"
                GameObject storageObj = new GameObject("CourierOption_Storage");
                storageObj.transform.SetParent(transform);
                storageObj.transform.localPosition = Vector3.zero;
                var storageInteract = storageObj.AddComponent<CourierStorageInteractable>();
                list.Add(storageInteract);
                groupOptions.Add(storageInteract);
                
                ModBehaviour.DevLog("[CourierNPC] CourierInteractable: 已注入选项（主选项=快递服务，子选项=寄存服务）");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[CourierNPC] [ERROR] InjectAllOptions 失败: " + e.Message + "\n" + e.StackTrace);
            }
        }
        
        protected override bool IsInteractable()
        {
            // 快递员始终可交互
            return true;
        }
        
        protected override void OnInteractStart(CharacterMainControl interactCharacter)
        {
            base.OnInteractStart(interactCharacter);
            ModBehaviour.DevLog("[CourierNPC] 玩家开始与快递员交互");
        }
        
        protected override void OnTimeOut()
        {
            // 主交互选项"快递服务"被选中
            try
            {
                ModBehaviour.DevLog("[CourierNPC] 玩家选择快递服务（主选项）");
                
                // 获取控制器并开始对话
                var controller = GetComponent<CourierNPCController>();
                if (controller != null)
                {
                    controller.StartTalking();
                }
                
                // 打开快递服务
                CourierService.OpenService(transform);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[CourierNPC] [ERROR] 快递服务交互出错: " + e.Message);
            }
        }
    }
}
