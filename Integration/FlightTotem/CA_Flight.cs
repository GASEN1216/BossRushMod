// ============================================================================
// CA_Flight.cs - 飞行动作类
// ============================================================================
// 模块说明：
//   继承 EquipmentAbilityAction，实现飞行运动逻辑
//   长按空格持续向上飞行，体力耗尽后缓慢下降
//   支持移动键控制飞行方向
// ============================================================================

using System;
using System.Reflection;
using UnityEngine;
using BossRush.Common.Equipment;
using BossRush.Common.Utils;

namespace BossRush
{
    /// <summary>
    /// 飞行动作类 - 持续飞行
    /// 使用 EquipmentAbilityAction 基类，代码更简洁
    /// </summary>
    public class CA_Flight : EquipmentAbilityAction
    {
        // ========== 配置 ==========

        private FlightConfig config => FlightConfig.Instance;

        protected override EquipmentAbilityConfig GetConfig() => config;

        // ========== 飞行专用状态变量 ==========

        /// <summary>
        /// 起始Y坐标
        /// </summary>
        private float startY = 0f;

        /// <summary>
        /// 锁定的最低Y坐标（防止下降）
        /// </summary>
        private float lockedMinY = float.MinValue;

        /// <summary>
        /// 是否处于缓慢下落模式
        /// </summary>
        private bool isSlowDescending = false;

        /// <summary>
        /// 空格按住计数器
        /// </summary>
        private int spaceHeldCount = 0;

        /// <summary>
        /// 空格释放计数器
        /// </summary>
        private int spaceReleaseCount = 0;

        /// <summary>
        /// 当前向上速度（用于加速度机制）
        /// </summary>
        private float currentUpwardSpeed = 0f;

        /// <summary>
        /// 本帧需要应用的垂直位移
        /// </summary>
        private float pendingVerticalDelta = 0f;


        /// <summary>
        /// 云雾特效实例
        /// </summary>
        private FlightCloudEffect cloudEffect;

        // ========== 临时飞行平台 ==========

        private GameObject flightPlatform;

        // ========== CharacterMovement 反射缓存 ==========

        private static PropertyInfo velocityProperty = null;
        private static bool reflectionCached = false;

        /// <summary>
        /// 缓存玩家移动速度相关的反射信息（静态，只执行一次）
        /// </summary>
        private static void CacheMoveSpeedReflection()
        {
            if (reflectionCached) return;

            try
            {
                // 缓存 velocity 属性 (从 EquipmentAbilityAction.characterMovementType 获取)
                if (characterMovementType != null)
                {
                    velocityProperty = BossRush.Common.Utils.ReflectionCache.GetProperty(
                        characterMovementType,
                        "velocity",
                        BindingFlags.Public | BindingFlags.Instance
                    );
                }

                reflectionCached = true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog($"[FlightTotem] 缓存速度反射失败: {e.Message}");
            }
        }

        // ========== 重写基类钩子方法 ==========

        protected override bool OnAbilityStart()
        {
            // 首次启动时缓存反射信息（静态缓存，只执行一次）
            CacheMoveSpeedReflection();

            // 创建临时飞行平台（玩家脚下）
            CreateFlightPlatform();

            startY = characterController.transform.position.y;
            lockedMinY = startY; // 锁定最低Y为起始位置，防止下降
            isSlowDescending = false;
            spaceHeldCount = 0;
            spaceReleaseCount = 0;
            currentUpwardSpeed = 0f;
            pendingVerticalDelta = 0f;

            // 创建云雾特效
            CreateCloudEffect();

            LogIfVerbose($"飞行开始！起始Y={startY}");
            return true;
        }

        protected override void OnAbilityStop()
        {
            float currentY = characterController.transform.position.y;
            float heightGained = currentY - startY;
            LogIfVerbose($"飞行结束！结束Y={currentY}, 上升了{heightGained:F1}");

            // 销毁飞行平台
            DestroyFlightPlatform();

            // 停止云雾特效
            DestroyCloudEffect();
            pendingVerticalDelta = 0f;

            // 修复：停止飞行时重置垂直速度，防止惯性导致的极速冲向地面
            try
            {
                object characterMovement = GetCharacterMovement();
                if (characterMovement != null && velocityProperty != null)
                {
                    Vector3 velocity = (Vector3)velocityProperty.GetValue(characterMovement);
                    // 仅清空垂直速度，保留水平速度以增加手感，或者全部清空
                    velocity.y = 0f;
                    velocityProperty.SetValue(characterMovement, velocity);
                    LogIfVerbose("已重置垂直速度");
                }
            }
            catch (Exception e)
            {
                LogIfVerbose($"重置垂直速度失败: {e.Message}");
            }
        }

        // 重写：体力消耗由子类自己处理（因为飞行和滑翔的消耗速率不同）
        protected override bool ShouldAutoConsumeStamina()
        {
            return false;
        }

        protected override void OnAbilityUpdate(float deltaTime)
        {

            // 检测空格是否仍被按住（使用基类的 GetMovementInput 或直接检测）
            // 由于飞行需要持续按住，使用 Input.GetKey 检测持续状态
            bool spaceKey = IsFlightInputHeld();

            // 累加计数器
            if (spaceKey)
            {
                spaceHeldCount++;
                spaceReleaseCount = 0;
            }
            else
            {
                spaceReleaseCount++;
                spaceHeldCount = 0;
            }

            // 如果连续5帧没有检测到空格，停止飞行
            // 约 83ms @ 60fps，足够避免误判
            if (spaceReleaseCount > 5)
            {
                LogIfVerbose("检测到松开空格，停止飞行");
                StopAction();
                return;
            }

            // 检查体力（阈值设为0.5，确保体力能够持续消耗直到接近0）
            bool hasStamina = characterController.CurrentStamina > 0.5f;

            if (hasStamina)
            {
                // 有体力：向上飞行
                isSlowDescending = false;
                UpdateFlight(deltaTime, false);
            }
            else
            {
                // 无体力：缓慢下降
                if (!isSlowDescending)
                {
                    isSlowDescending = true;
                    LogIfVerbose("体力耗尽，切换到缓慢下降模式");
                }
                UpdateFlight(deltaTime, true);
            }
        }

        protected override bool CanUseHandWhileActive()
        {
            // 飞行时允许使用手（不拦截其他动作）
            return true;
        }

        protected override void OnStaminaDepleted()
        {
            // 切换到缓慢下降模式而不是立即停止（OnAbilityUpdate 中已处理此逻辑）
            isSlowDescending = true;
        }

        // ========== 飞行运动逻辑 ==========

        private void UpdateFlight(float deltaTime, bool isGliding)
        {
            // 消耗体力
            float drainAmount = isGliding
                ? config.SlowDescentStaminaDrainPerSecond * deltaTime
                : config.StaminaDrainPerSecond * deltaTime;
            characterController.UseStamina(drainAmount);

            // 计算垂直速度（加速度机制）
            float targetVerticalSpeed;
            if (isGliding)
            {
                // 滑翔时使用固定下降速度
                targetVerticalSpeed = config.SlowDescentSpeed;
            }
            else
            {
                // 向上飞行时使用加速度机制
                currentUpwardSpeed = Mathf.Min(
                    currentUpwardSpeed + config.UpwardAcceleration * deltaTime,
                    config.MaxUpwardSpeed
                );
                targetVerticalSpeed = currentUpwardSpeed;
            }

            // 存储目标垂直速度，在 FixedUpdate 中应用
            pendingVerticalDelta = targetVerticalSpeed;

            // 持续暂停地面约束（增加持续时间确保不会被重新激活）
            PauseGroundConstraint(0.5f);
        }

        // ========== 辅助方法 ==========

        private bool IsFlightInputHeld()
        {
            if (FlightAbilityManager.Instance != null)
            {
                return FlightAbilityManager.Instance.IsFlightInputHeld();
            }

            return Input.GetKey(KeyCode.Space);
        }


        // ========== 云雾特效方法 ==========

        /// <summary>
        /// 创建云雾特效
        /// </summary>
        private void CreateCloudEffect()
        {
            if (cloudEffect != null)
            {
                cloudEffect.StopEffect();
            }

            // 使用静态工厂方法创建特效
            cloudEffect = FlightCloudEffect.Create<FlightCloudEffect>(
                characterController.transform,
                characterController.transform.position + new Vector3(0f, -0.3f, 0f)
            );

            LogIfVerbose("云雾特效已创建");
        }

        /// <summary>
        /// 销毁云雾特效
        /// </summary>
        private void DestroyCloudEffect()
        {
            if (cloudEffect != null)
            {
                cloudEffect.StopEffect();
                cloudEffect = null;
                LogIfVerbose("云雾特效已停止");
            }
        }

        /// <summary>
        /// 重置飞行状态
        /// </summary>
        public void ResetFlightState()
        {
            base.ResetState();
            isSlowDescending = false;
            spaceHeldCount = 0;
            spaceReleaseCount = 0;
            currentUpwardSpeed = 0f;
            pendingVerticalDelta = 0f;
        }

        /// <summary>
        /// 当前向上速度（供外部查询）
        /// </summary>
        public float UpwardSpeed => currentUpwardSpeed;

        private void FixedUpdate()
        {
            if (!Running)
            {
                pendingVerticalDelta = 0f;
                return;
            }

            // 更新飞行平台位置（同时移动玩家）
            UpdateFlightPlatform();
        }

        private void LateUpdate()
        {
            if (!Running || characterController == null) return;

            Vector3 playerPos = characterController.transform.position;

            // 滑翔时更新锁定值（允许下降），向上飞行时锁定最低Y（防止抖动）
            if (isSlowDescending)
            {
                lockedMinY = playerPos.y;
            }
            else if (playerPos.y < lockedMinY - 0.01f)
            {
                playerPos.y = lockedMinY;
                characterController.transform.position = playerPos;
            }
            else if (playerPos.y > lockedMinY)
            {
                lockedMinY = playerPos.y;
            }

            // 更新平台位置（统一在此处理，避免重复）
            UpdatePlatformPosition(playerPos);
        }

        // ========== 飞行平台管理 ==========

        /// <summary>
        /// 创建飞行平台（在玩家脚下）
        /// </summary>
        private void CreateFlightPlatform()
        {
            if (flightPlatform != null)
            {
                DestroyFlightPlatform();
            }

            try
            {
                // 创建不可见的游戏对象
                flightPlatform = new GameObject("FlightPlatform");
                // 不设置 layer，避免出错
                flightPlatform.hideFlags = HideFlags.HideInHierarchy;

                // 添加 BoxCollider 作为地面
                var boxCollider = flightPlatform.AddComponent<BoxCollider>();
                boxCollider.isTrigger = false;

                // 设置平台大小
                boxCollider.center = Vector3.zero;
                boxCollider.size = new Vector3(5f, 0.1f, 5f);

                ModBehaviour.DevLog("[FlightTotem] 飞行平台已创建");
            }
            catch (System.Exception e)
            {
                ModBehaviour.DevLog($"[FlightTotem] 创建飞行平台失败: {e.Message}");
            }
        }

        /// <summary>
        /// 更新飞行平台位置（始终在玩家脚下），并直接移动玩家
        /// </summary>
        private void UpdateFlightPlatform()
        {
            if (flightPlatform == null || characterController == null) return;

            // 优先尝试通过修改 Velocity 来移动
            object characterMovement = GetCharacterMovement();
            if (characterMovement != null && velocityProperty != null)
            {
                try
                {
                    Vector3 velocity = (Vector3)velocityProperty.GetValue(characterMovement);
                    velocity.y = pendingVerticalDelta;
                    velocityProperty.SetValue(characterMovement, velocity);
                    pendingVerticalDelta = 0f;
                    return;
                }
                catch { /* 回退到手动修改位置 */ }
            }

            // 回退：直接修改玩家位置
            if (Mathf.Abs(pendingVerticalDelta) > 0.0001f)
            {
                Vector3 playerPos = characterController.transform.position;
                playerPos.y += pendingVerticalDelta * Time.fixedDeltaTime;
                characterController.transform.position = playerPos;
                pendingVerticalDelta = 0f;
            }
        }

        /// <summary>
        /// 更新平台位置（统一入口，避免重复计算）
        /// </summary>
        private void UpdatePlatformPosition(Vector3 playerPos)
        {
            if (flightPlatform == null) return;
            float offset = 0.06f;
            flightPlatform.transform.position = new Vector3(playerPos.x, playerPos.y - offset, playerPos.z);
        }

        /// <summary>
        /// 销毁飞行平台
        /// </summary>
        private void DestroyFlightPlatform()
        {
            if (flightPlatform != null)
            {
                Destroy(flightPlatform);
                flightPlatform = null;
                ModBehaviour.DevLog("[FlightTotem] 飞行平台已销毁");
            }
        }
    }
}
