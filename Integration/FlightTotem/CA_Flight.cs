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
using ItemStatsSystem;
using ItemStatsSystem.Items;

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
        /// 飞行开始时间（用于起飞缓冲期）
        /// </summary>
        private float flightStartTime = 0f;

        /// <summary>
        /// 起飞缓冲时间（秒），在此期间不检查落地
        /// </summary>
        private const float TakeoffBufferTime = 0.2f;

        // ========== 静态反射缓存（共享给所有实例） ==========

        private static PropertyInfo characterItemProperty = null;
        private static FieldInfo moveSpeedStatField = null;
        private static MethodInfo getStatMethod = null;
        private static bool reflectionCached = false;

        /// <summary>
        /// 缓存玩家移动速度相关的反射信息（静态，只执行一次）
        /// </summary>
        private static void CacheMoveSpeedReflection()
        {
            if (reflectionCached) return;

            try
            {
                // 缓存 CharacterController.CharacterItem 属性
                var controllerType = BossRush.Common.Utils.ReflectionCache.GetType("CharacterController", "Assembly-CSharp");
                if (controllerType != null)
                {
                    characterItemProperty = BossRush.Common.Utils.ReflectionCache.GetProperty(
                        controllerType,
                        "CharacterItem",
                        BindingFlags.Public | BindingFlags.Instance
                    );
                }

                // 缓存 Item.GetStat 方法
                var itemType = BossRush.Common.Utils.ReflectionCache.GetType("ItemStatsSystem.Items.Item", "ItemStatsSystem");
                if (itemType != null)
                {
                    getStatMethod = BossRush.Common.Utils.ReflectionCache.GetMethod(
                        itemType,
                        "GetStat",
                        new Type[] { typeof(string) }
                    );
                }

                // 缓存 Stat.Value 字段
                var statType = BossRush.Common.Utils.ReflectionCache.GetType("ItemStatsSystem.Stat", "ItemStatsSystem");
                if (statType != null)
                {
                    moveSpeedStatField = BossRush.Common.Utils.ReflectionCache.GetField(
                        statType,
                        "value",
                        BindingFlags.Public | BindingFlags.Instance
                    );
                }

                reflectionCached = true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog($"[FlightTotem] 缓存移动速度反射失败: {e.Message}");
            }
        }

        // ========== 重写基类钩子方法 ==========

        protected override bool OnAbilityStart()
        {
            // 首次启动时缓存反射信息（静态缓存，只执行一次）
            CacheMoveSpeedReflection();

            startY = characterController.transform.position.y;
            isSlowDescending = false;
            spaceHeldCount = 0;
            spaceReleaseCount = 0;
            currentUpwardSpeed = 0f;
            flightStartTime = Time.time;

            LogIfVerbose($"飞行开始！起始Y={startY}");
            return true;
        }

        protected override void OnAbilityStop()
        {
            float currentY = characterController.transform.position.y;
            float heightGained = currentY - startY;
            LogIfVerbose($"飞行结束！结束Y={currentY}, 上升了{heightGained:F1}");
        }

        // 重写：体力消耗由子类自己处理（因为飞行和滑翔的消耗速率不同）
        protected override bool ShouldAutoConsumeStamina()
        {
            return false;
        }

        protected override void OnAbilityUpdate(float deltaTime)
        {
            // 检查是否在起飞缓冲期内，如果是则跳过落地检查
            bool isInTakeoffBuffer = (Time.time - flightStartTime) < TakeoffBufferTime;

            // 检查是否落地（跳过起飞缓冲期），落地则停止飞行
            if (!isInTakeoffBuffer && IsOnGround())
            {
                LogIfVerbose("检测到落地，停止飞行");
                StopAction();
                return;
            }

            // 检测空格是否仍被按住（使用基类的 GetMovementInput 或直接检测）
            // 由于飞行需要持续按住，使用 Input.GetKey 检测持续状态
            bool spaceKey = Input.GetKey(KeyCode.Space);

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
            // 飞行时不能使用手（不能射击）
            return false;
        }

        protected override void OnStaminaDepleted()
        {
            // 切换到缓慢下降模式而不是立即停止
            if (!isSlowDescending)
            {
                isSlowDescending = true;
                LogIfVerbose("体力耗尽，切换到缓慢下降模式");
            }
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
            float verticalSpeed;
            if (isGliding)
            {
                // 滑翔时使用固定下降速度
                verticalSpeed = config.SlowDescentSpeed;
            }
            else
            {
                // 向上飞行时使用加速度机制
                currentUpwardSpeed = Mathf.Min(
                    currentUpwardSpeed + config.UpwardAcceleration * deltaTime,
                    config.MaxUpwardSpeed
                );
                verticalSpeed = currentUpwardSpeed;
            }

            // 获取玩家移动速度
            float playerMoveSpeed = GetPlayerMoveSpeed();
            float horizontalSpeed = isGliding
                ? playerMoveSpeed * config.GlidingHorizontalSpeedMultiplier
                : playerMoveSpeed;

            // 获取移动输入
            Vector2 moveInput = GetMovementInput();

            // 计算相对于摄像机视角的水平移动方向
            Vector3 horizontalMovement = GetCameraRelativeMovement(moveInput, horizontalSpeed * deltaTime);

            // 计算移动向量
            Vector3 currentPos = characterController.transform.position;
            Vector3 newPos = currentPos;

            // 垂直移动
            newPos.y += verticalSpeed * deltaTime;

            // 水平移动（相对于摄像机视角）
            newPos.x += horizontalMovement.x;
            newPos.z += horizontalMovement.z;

            // 使用 ForceSetPosition 直接设置位置
            characterController.movementControl.ForceSetPosition(newPos);

            // 同时设置速度（相对于摄像机视角）
            Vector3 horizontalVelocity = GetCameraRelativeMovement(moveInput, horizontalSpeed);
            characterController.SetForceMoveVelocity(new Vector3(
                horizontalVelocity.x,
                verticalSpeed,
                horizontalVelocity.z
            ));

            // 持续暂停地面约束
            if (!isSlowDescending)
            {
                PauseGroundConstraint(0.1f);
            }
        }

        /// <summary>
        /// 获取相对于摄像机视角的水平移动向量
        /// </summary>
        /// <param name="input">WASD 输入（x=左右，y=前后）</param>
        /// <param name="magnitude">移动量</param>
        /// <returns>世界坐标系下的水平移动向量</returns>
        private Vector3 GetCameraRelativeMovement(Vector2 input, float magnitude)
        {
            if (input.sqrMagnitude < 0.001f) return Vector3.zero;

            // 获取主摄像机
            Camera mainCamera = Camera.main;
            if (mainCamera == null)
            {
                // 备用：直接使用世界坐标
                return new Vector3(input.x * magnitude, 0f, input.y * magnitude);
            }

            // 获取摄像机的前方和右方向量（忽略Y轴，只取水平分量）
            Vector3 cameraForward = mainCamera.transform.forward;
            Vector3 cameraRight = mainCamera.transform.right;

            // 将Y分量置零，只保留水平方向
            cameraForward.y = 0f;
            cameraRight.y = 0f;

            // 归一化（避免斜向移动时速度变快）
            cameraForward.Normalize();
            cameraRight.Normalize();

            // 计算相对于摄像机的移动方向
            // input.y 是前后（W/S），input.x 是左右（A/D）
            Vector3 movement = (cameraForward * input.y + cameraRight * input.x) * magnitude;

            return movement;
        }

        // ========== 辅助方法 ==========

        /// <summary>
        /// 获取玩家的移动速度（使用静态缓存的反射）
        /// </summary>
        private float GetPlayerMoveSpeed()
        {
            if (characterController == null) return 4f;

            try
            {
                // 使用静态缓存的反射获取 CharacterItem
                object characterItem = null;
                if (characterItemProperty != null)
                {
                    characterItem = characterItemProperty.GetValue(characterController);
                }

                if (characterItem == null)
                {
                    // 备用：通过 GetComponent 获取
                    var item = characterController.GetComponent<Item>();
                    if (item == null)
                    {
                        item = characterController.GetComponentInChildren<Item>();
                    }
                    characterItem = item;
                }

                if (characterItem != null)
                {
                    // 使用静态缓存的方法获取 Stat
                    object stat = null;
                    if (getStatMethod != null)
                    {
                        stat = getStatMethod.Invoke(characterItem, new object[] { "MoveSpeed" });
                    }

                    if (stat != null && moveSpeedStatField != null)
                    {
                        object value = moveSpeedStatField.GetValue(stat);
                        if (value is float speed)
                        {
                            return speed;
                        }
                    }
                }
            }
            catch
            {
                // 静默失败，返回默认值
            }

            return 4f; // 默认值
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
        }

        /// <summary>
        /// 当前向上速度（供外部查询）
        /// </summary>
        public float UpwardSpeed => currentUpwardSpeed;
    }
}
