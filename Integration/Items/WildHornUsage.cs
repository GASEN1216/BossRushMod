// ============================================================================
// WildHornUsage.cs - 荒野号角使用行为
// ============================================================================
// 模块说明：
//   实现荒野号角物品的使用逻辑：
//   - 场景中无坐骑时：通过 testVehicle.CreateCharacterAsync 生成马匹
//   - 场景中有坐骑时：调用 CallHorse() 呼唤坐骑到身边
//   - 包含冷却时间检查（默认3秒）
//   - 场景切换时自动清理缓存引用
// ============================================================================

using System;
using UnityEngine;
using Duckov.ItemUsage;
using Duckov.UI.DialogueBubbles;
using Duckov.Utilities;
using ItemStatsSystem;
using Cysharp.Threading.Tasks;

namespace BossRush
{
    /// <summary>
    /// 荒野号角使用行为 - 召唤/呼唤坐骑
    /// </summary>
    public class WildHornUsage : UsageBehavior
    {
        // ============================================================================
        // 静态状态（跨实例共享）
        // ============================================================================

        /// <summary>缓存的坐骑AI引用（场景级别，切换时清空）</summary>
        private static AISpecialAttachment_Horse cachedHorseAI;

        /// <summary>上次使用时间戳（用于冷却计算）</summary>
        private static float lastUseTime = -999f;

        // ============================================================================
        // UsageBehavior 重写
        // ============================================================================

        /// <summary>
        /// 显示设置（物品描述中显示的使用说明）
        /// </summary>
        public override DisplaySettingsData DisplaySettings
        {
            get
            {
                return new DisplaySettingsData
                {
                    display = true,
                    description = WildHornConfig.GetUsageDescription()
                };
            }
        }

        /// <summary>
        /// 检查物品是否可以使用（冷却时间检查）
        /// </summary>
        public override bool CanBeUsed(Item item, object user)
        {
            // 冷却时间检查：当前时间 - 上次使用时间 < 冷却秒数 → 不可使用
            if (Time.time - lastUseTime < WildHornConfig.COOLDOWN_SECONDS)
            {
                return false;
            }
            return true;
        }

        /// <summary>
        /// 使用物品时调用 - 判断坐骑是否存在，分支处理召唤/呼唤
        /// </summary>
        protected override void OnUse(Item item, object user)
        {
            try
            {
                // 获取玩家角色
                CharacterMainControl player = user as CharacterMainControl ?? CharacterMainControl.Main;
                if (player == null)
                {
                    // 玩家引用为空，安全返回（需求 5.1）
                    ModBehaviour.DevLog("[WildHorn] 玩家角色引用为空，取消使用");
                    return;
                }

                // 记录使用时间，开始冷却（需求 4.3）
                lastUseTime = Time.time;

                // 判断坐骑是否已存在
                if (HasExistingMount(player))
                {
                    // 坐骑已存在：呼唤坐骑到身边（需求 3.1）
                    player.CallHorse();
                    ShowBubbleHint(player, WildHornConfig.GetCallMountHint());
                    ModBehaviour.DevLog("[WildHorn] 呼唤已有坐骑");
                }
                else
                {
                    // 坐骑不存在：异步生成新坐骑（需求 2.1）
                    SpawnMountAsync(player).Forget();
                }
            }
            catch (Exception e)
            {
                // 捕获未预期异常（需求 5.3）
                ModBehaviour.DevLog("[WildHorn] 使用物品出错: " + e.Message);
            }
        }

        // ============================================================================
        // 坐骑生成与检查
        // ============================================================================

        /// <summary>
        /// 异步生成坐骑（通过 testVehicle.CreateCharacterAsync）
        /// </summary>
        private async UniTaskVoid SpawnMountAsync(CharacterMainControl player)
        {
            try
            {
                // 获取马匹预设
                var vehiclePreset = GameplayDataSettings.CharacterRandomPresetData.testVehicle;
                if (vehiclePreset == null)
                {
                    // 预设为空，显示失败提示（需求 5.2）
                    ModBehaviour.DevLog("[WildHorn] testVehicle 预设为空，无法生成坐骑");
                    ShowBubbleHint(player, WildHornConfig.GetSummonFailHint());
                    return;
                }

                // 在玩家位置附近生成马匹
                Vector3 spawnPos = player.transform.position + player.transform.forward * 2f + Vector3.up * 0.25f;
                int sceneIndex = UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex;

                var horse = await vehiclePreset.CreateCharacterAsync(
                    spawnPos, Vector3.forward, sceneIndex, null, false);

                if (horse == null)
                {
                    // 生成返回空，显示失败提示（需求 5.2）
                    ModBehaviour.DevLog("[WildHorn] CreateCharacterAsync 返回空，生成坐骑失败");
                    ShowBubbleHint(player, WildHornConfig.GetSummonFailHint());
                    return;
                }

                // 获取马匹AI组件并设置主人引用（需求 2.2, 2.3）
                var horseAI = horse.GetComponentInChildren<AISpecialAttachment_Horse>();
                if (horseAI != null)
                {
                    horseAI.master = player;
                    player.horseAI = horseAI;
                    cachedHorseAI = horseAI;
                    ModBehaviour.DevLog("[WildHorn] 坐骑生成成功，已设置 horseAI 和 master 引用");
                }
                else
                {
                    // 没有找到马匹AI组件，仍然缓存角色引用
                    ModBehaviour.DevLog("[WildHorn] 警告: 生成的角色缺少 AISpecialAttachment_Horse 组件");
                }

                // 根据配置决定是否使用狼模型
                bool useWolfModel = ModBehaviour.Instance != null && ModBehaviour.Instance.GetUseWolfModelForWildHorn();
                
                if (useWolfModel)
                {
                    // 修改喂食物品ID为饺子（TypeID 449）- 仅狼模型时生效
                    ChangeHorseFeedItem(horse.gameObject);

                    // 调整狼坐骑的骑乘位置
                    AdjustWolfRidePosition(horse.gameObject);

                    // 替换马匹模型为狼模型
                    ReplaceWithWolfModel(horse.gameObject);
                }
                else
                {
                    ModBehaviour.DevLog("[WildHorn] 配置关闭狼模型，保持原版马匹外观");
                }

                // 显示召唤成功提示（需求 2.4）
                ShowBubbleHint(player, WildHornConfig.GetSummonSuccessHint());
            }
            catch (Exception e)
            {
                // 捕获异步异常（需求 5.3）
                ModBehaviour.DevLog("[WildHorn] 生成坐骑异常: " + e.Message);
                try
                {
                    ShowBubbleHint(player, WildHornConfig.GetSummonFailHint());
                }
                catch { }
            }
        }

        /// <summary>
        /// 检查坐骑是否已存在（需求 7.2）
        /// 同时验证缓存引用非空且 GameObject 未被销毁
        /// </summary>
        private bool HasExistingMount(CharacterMainControl player)
        {
            // 优先检查缓存引用（Unity 对象 null 检查会识别已销毁的对象）
            if (cachedHorseAI != null && cachedHorseAI.gameObject != null)
            {
                return true;
            }

            // 兜底检查游戏原生引用
            if (player.horseAI != null)
            {
                // 同步缓存
                cachedHorseAI = player.horseAI;
                return true;
            }

            return false;
        }

        /// <summary>
        /// 清理坐骑缓存引用（场景切换时调用）（需求 7.1）
        /// </summary>
        public static void ClearMountCache()
        {
            cachedHorseAI = null;
            ModBehaviour.DevLog("[WildHorn] 坐骑缓存已清理");
        }

        // ============================================================================
        // 辅助方法
        // ============================================================================

        /// <summary>
        /// 显示气泡提示（参考 BrickStoneUsage 的模式）
        /// </summary>
        private void ShowBubbleHint(CharacterMainControl player, string hint)
        {
            try
            {
                DialogueBubblesManager.Show(hint, player.transform, 2.5f, false, false, -1f, 2f);
            }
            catch { }
        }

        /// <summary>
        /// 修改马匹的喂食物品ID为饺子（TypeID 449）
        /// 原版马匹吃胡萝卜，狼坐骑改为吃饺子
        /// </summary>
        private void ChangeHorseFeedItem(GameObject horse)
        {
            try
            {
                // 查找马匹身上的所有 InteractableBase 组件（喂食交互）
                var interactables = horse.GetComponentsInChildren<InteractableBase>(true);
                foreach (var interactable in interactables)
                {
                    // 检查是否是喂食交互（requireItem 为 true）
                    if (interactable.requireItem && interactable.requireItemId != 0)
                    {
                        // 修改为饺子的 TypeID
                        interactable.requireItemId = WildHornConfig.WOLF_FEED_ITEM_ID;
                        ModBehaviour.DevLog($"[WildHorn] 已修改喂食物品ID: {interactable.requireItemId} -> {WildHornConfig.WOLF_FEED_ITEM_ID}");
                    }
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[WildHorn] 修改喂食物品ID失败: " + e.Message);
            }
        }

        /// <summary>
        /// 调整狼坐骑的骑乘位置
        /// 由于狼模型的骨骼结构与马匹不同，需要调整 VehicleSocket 的位置
        /// </summary>
        private void AdjustWolfRidePosition(GameObject horse)
        {
            try
            {
                var charCtrl = horse.GetComponent<CharacterMainControl>();
                if (charCtrl == null || charCtrl.characterModel == null)
                {
                    ModBehaviour.DevLog("[WildHorn] 警告: 未找到 CharacterMainControl 或 characterModel");
                    return;
                }

                // 获取原始的 VehicleSocket
                Transform originalSocket = charCtrl.characterModel.VehicleSocket;
                if (originalSocket == null)
                {
                    ModBehaviour.DevLog("[WildHorn] 警告: 未找到原始 VehicleSocket");
                    return;
                }

                // 创建一个新的 Transform 作为狼的骑乘点
                GameObject wolfSocket = new GameObject("WolfRideSocket");
                wolfSocket.transform.SetParent(originalSocket.parent, false);
                
                // 复制原始 socket 的本地位置和旋转
                wolfSocket.transform.localPosition = originalSocket.localPosition;
                wolfSocket.transform.localRotation = originalSocket.localRotation;
                
                // 应用狼坐骑的位置偏移
                wolfSocket.transform.localPosition += WildHornConfig.WOLF_RIDE_POSITION_OFFSET;
                wolfSocket.transform.localRotation *= Quaternion.Euler(WildHornConfig.WOLF_RIDE_ROTATION_OFFSET);

                // 通过反射修改 characterModel.VehicleSocket
                var vehicleSocketField = charCtrl.characterModel.GetType().GetField("VehicleSocket",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                
                if (vehicleSocketField != null)
                {
                    vehicleSocketField.SetValue(charCtrl.characterModel, wolfSocket.transform);
                    ModBehaviour.DevLog($"[WildHorn] 已调整骑乘位置: offset={WildHornConfig.WOLF_RIDE_POSITION_OFFSET}");
                }
                else
                {
                    ModBehaviour.DevLog("[WildHorn] 警告: 无法通过反射修改 VehicleSocket");
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[WildHorn] 调整骑乘位置失败: " + e.Message);
            }
        }

        // ============================================================================
        // 狼模型替换
        // ============================================================================

        /// <summary>
        /// 将马匹的视觉模型替换为狼模型
        /// 保留马匹的 AI、骑乘、寻路等所有逻辑组件
        /// </summary>
        private void ReplaceWithWolfModel(GameObject horse)
        {
            try
            {
                // 1. 找到马身上真正会旋转的 modelRoot Transform
                //    游戏角色的旋转由 Movement.rotationRoot 控制，
                //    而 rotationRoot 就是 CharacterMainControl.modelRoot
                var charCtrl = horse.GetComponent<CharacterMainControl>();
                Transform parentTransform;
                if (charCtrl != null && charCtrl.modelRoot != null)
                {
                    // 挂到 modelRoot 上，子物体自动跟随旋转，零开销
                    parentTransform = charCtrl.modelRoot;
                    ModBehaviour.DevLog("[WildHorn] 使用 modelRoot 作为父节点");
                }
                else if (charCtrl != null)
                {
                    parentTransform = charCtrl.transform;
                    ModBehaviour.DevLog("[WildHorn] modelRoot 为空，回退到 CharacterMainControl.transform");
                }
                else
                {
                    parentTransform = horse.transform;
                    ModBehaviour.DevLog("[WildHorn] 未找到 CharacterMainControl，使用 horse.transform");
                }

                // 2. 隐藏马匹的所有渲染器
                var renderers = horse.GetComponentsInChildren<Renderer>(true);
                foreach (var r in renderers)
                {
                    r.enabled = false;
                }
                ModBehaviour.DevLog("[WildHorn] 已隐藏马匹模型渲染器，共 " + renderers.Length + " 个");

                // 3. 通过 EntityModelFactory 加载狼模型
                var wolfModel = EntityModelFactory.Create(
                    WildHornConfig.WOLF_PREFAB_NAME,
                    Vector3.zero,
                    Quaternion.identity);

                if (wolfModel == null)
                {
                    ModBehaviour.DevLog("[WildHorn] 警告: 狼模型加载失败，保持马匹原始外观");
                    foreach (var r in renderers) r.enabled = true;
                    return;
                }

                // 4. 将狼模型挂载到 modelRoot 下
                //    子物体自动跟随父物体的位置和旋转，无需每帧同步
                wolfModel.transform.SetParent(parentTransform, false);
                wolfModel.transform.localPosition = Vector3.zero;
                wolfModel.transform.localRotation = Quaternion.identity;

                // 5. 添加动画驱动组件（使用 CharacterMainControl 的 AnimationMoveSpeedValue）
                var animator = wolfModel.GetComponentInChildren<Animator>();
                if (animator != null && charCtrl != null)
                {
                    // 传入 charCtrl 来获取游戏原生的动画速度值
                    var driver = wolfModel.AddComponent<WolfAnimationDriver>();
                    driver.Initialize(animator, charCtrl);
                    ModBehaviour.DevLog("[WildHorn] 狼模型动画驱动已挂载");
                }
                else
                {
                    if (animator == null)
                        ModBehaviour.DevLog("[WildHorn] 狼模型未找到 Animator，跳过动画驱动");
                    if (charCtrl == null)
                        ModBehaviour.DevLog("[WildHorn] 未找到 CharacterMainControl，跳过动画驱动");
                }

                ModBehaviour.DevLog("[WildHorn] 狼模型替换完成, 父节点: " + parentTransform.name);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[WildHorn] 狼模型替换失败: " + e.Message);
            }
        }
    }

    /// <summary>
    /// 狼坐骑动画驱动
    /// - 根据坐骑的 CharacterMainControl.AnimationMoveSpeedValue 自动调整动画速度
    /// - 该值由游戏原生系统计算，包含移动、冲刺等所有状态
    /// - 旋转由父子关系自动处理，无需每帧同步
    /// </summary>
    public class WolfAnimationDriver : MonoBehaviour
    {
        private Animator animator;
        private CharacterMainControl mountCharacter;

        /// <summary>初始化动画驱动</summary>
        public void Initialize(Animator anim, CharacterMainControl charCtrl)
        {
            animator = anim;
            mountCharacter = charCtrl;

            if (animator != null)
            {
                // 初始速度为0（静止状态），循环播放在Unity端设置
                animator.speed = 0f;
            }

            if (mountCharacter == null)
            {
                ModBehaviour.DevLog("[WolfAnimationDriver] 警告: CharacterMainControl 为空，动画驱动无法工作");
            }
        }

        void Update()
        {
            if (animator == null || mountCharacter == null) return;

            // 使用游戏原生的 AnimationMoveSpeedValue
            // 该值已包含移动、冲刺、装备影响等所有因素
            // 游戏原生：正常移动 1.0x，冲刺 2.0x
            float animSpeed = mountCharacter.AnimationMoveSpeedValue;

            // 当冲刺时（animSpeed >= 2.0），应用额外的速度倍率
            // 使冲刺动画达到 3.0x 速度（2.0 * 1.5 = 3.0）
            if (animSpeed >= 2.0f)
            {
                animSpeed *= WildHornConfig.WOLF_ANIM_RUN_SPEED_MULTIPLIER;
            }

            animator.speed = animSpeed;
        }
    }
}