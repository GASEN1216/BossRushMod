// ============================================================================
// EquipmentAbilityAction.cs - 装备能力动作基类
// ============================================================================
// 模块说明：
//   为所有装备能力提供统一的动作基类
//   继承 CharacterActionBase，封装通用逻辑
// ============================================================================

using System;
using System.Reflection;
using UnityEngine;

namespace BossRush.Common.Equipment
{
    /// <summary>
    /// 装备能力动作基类 - 所有装备能力的动作都应继承此类
    /// </summary>
    public abstract class EquipmentAbilityAction : CharacterActionBase
    {
        // ========== 配置 ==========

        /// <summary>
        /// 获取该能力的配置对象（由子类实现）
        /// </summary>
        protected abstract EquipmentAbilityConfig GetConfig();

        // ========== 状态变量 ==========

        /// <summary>
        /// 上次结束时间（用于冷却检测）
        /// </summary>
        protected float lastEndTime = -999f;

        /// <summary>
        /// 是否正在冷却中
        /// </summary>
        protected bool isCoolingDown = false;

        /// <summary>
        /// 动作累计时间（秒）
        /// </summary>
        protected float actionElapsedTime = 0f;

        // ========== 反射缓存（游戏相关） ==========

        protected static Type characterMovementType = null;
        protected static MethodInfo pauseGroundConstraintMethod = null;
        protected static MethodInfo playSoundMethod = null;
        protected static Type audioManagerType = null;

        // ========== 实例缓存（每帧使用的反射结果） ==========

        private FieldInfo cachedCharacterMovementField = null;
        private object cachedCharacterMovement = null;

        // IsOnGround 相关缓存
        private PropertyInfo cachedIsOnGroundProperty = null;
        private FieldInfo cachedIsOnGroundField = null;

        // ========== 初始化 ==========

        static EquipmentAbilityAction()
        {
            InitializeReflectionCache();
        }

        private static void InitializeReflectionCache()
        {
            try
            {
                // 缓存 CharacterMovement 类型
                characterMovementType = Utils.ReflectionCache.GetType("ECM2.CharacterMovement", "ECM2");
                if (characterMovementType == null)
                {
                    characterMovementType = Utils.ReflectionCache.GetType("ECM2.CharacterMovement", "Assembly-CSharp");
                }

                // 缓存 PauseGroundConstraint 方法
                if (characterMovementType != null)
                {
                    pauseGroundConstraintMethod = Utils.ReflectionCache.GetMethod(
                        characterMovementType,
                        "PauseGroundConstraint",
                        new Type[] { typeof(float) }
                    );
                }

                // 缓存 AudioManager 类型
                audioManagerType = Utils.ReflectionCache.GetType("AudioManager", "TeamSoda.Duckov.Core");
                if (audioManagerType == null)
                {
                    audioManagerType = Utils.ReflectionCache.GetType("Duckov.AudioManager", "TeamSoda.Duckov.Core");
                }

                // 缓存 Play/Post 方法
                if (audioManagerType != null)
                {
                    playSoundMethod = Utils.ReflectionCache.GetMethod(
                        audioManagerType,
                        "Post",
                        new Type[] { typeof(string), typeof(GameObject) }
                    );
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog($"[EquipmentAbility] 反射初始化失败: {e.Message}");
            }
        }

        // ========== CharacterActionBase 接口实现 ==========

        public override CharacterActionBase.ActionPriorities ActionPriority()
        {
            return CharacterActionBase.ActionPriorities.Skills;
        }

        public override bool CanMove()
        {
            return true;
        }

        public override bool CanRun()
        {
            return false;
        }

        public override bool CanUseHand()
        {
            return CanUseHandWhileActive();
        }

        public override bool CanControlAim()
        {
            return true;
        }

        public override bool IsReady()
        {
            var config = GetConfig();
            if (Time.time - lastEndTime < config.CooldownTime)
            {
                return false;
            }
            if (Running) return false;
            return IsReadyInternal();
        }

        protected override bool OnStart()
        {
            var config = GetConfig();

            // 检查体力
            if (characterController.CurrentStamina < config.StartupStaminaCost)
            {
                LogIfVerbose("体力不足，无法启动能力");
                return false;
            }

            // 消耗启动体力
            characterController.UseStamina(config.StartupStaminaCost);

            // 重置状态
            actionElapsedTime = 0f;
            isCoolingDown = false;

            // 调用子类的启动逻辑
            if (!OnAbilityStart())
            {
                return false;
            }

            // 暂停地面约束（空中能力需要）
            PauseGroundConstraint(0.5f);

            // 播放音效
            PlaySound(config.StartSFX);

            // 触发硬件同步事件
            TriggerHardwareSyncEvent();

            LogIfVerbose($"能力启动: {GetType().Name}");
            return true;
        }

        protected override void OnStop()
        {
            // 调用子类的停止逻辑
            OnAbilityStop();

            lastEndTime = Time.time;
            isCoolingDown = true;

            // 播放结束音效
            var config = GetConfig();
            if (config.EndSFX != null)
            {
                PlaySound(config.EndSFX);
            }

            LogIfVerbose($"能力停止: {GetType().Name}");
        }

        protected override void OnUpdateAction(float deltaTime)
        {
            var config = GetConfig();

            // 检查死亡
            if (characterController.Health != null && characterController.Health.IsDead)
            {
                StopAction();
                return;
            }

            // 更新累计时间
            actionElapsedTime += deltaTime;

            // 消耗持续体力（子类可以通过 OverrideAutoStaminaConsumption 禁用自动消耗）
            if (ShouldAutoConsumeStamina() && config.StaminaDrainPerSecond > 0)
            {
                float drainAmount = config.StaminaDrainPerSecond * deltaTime;
                characterController.UseStamina(drainAmount);
            }

            // 检查体力是否耗尽
            if (characterController.CurrentStamina <= 0.1f)
            {
                OnStaminaDepleted();
                return;
            }

            // 调用子类的更新逻辑
            OnAbilityUpdate(deltaTime);
        }

        // ========== 子类可重写的钩子方法 ==========

        /// <summary>
        /// 子类重写：能力启动时的自定义逻辑
        /// </summary>
        /// <returns>返回 false 表示启动失败</returns>
        protected virtual bool OnAbilityStart()
        {
            return true;
        }

        /// <summary>
        /// 子类重写：能力停止时的自定义逻辑
        /// </summary>
        protected virtual void OnAbilityStop()
        {
        }

        /// <summary>
        /// 子类重写：能力每帧更新逻辑
        /// </summary>
        protected virtual void OnAbilityUpdate(float deltaTime)
        {
        }

        /// <summary>
        /// 子类重写：是否自动消耗体力（返回 false 表示子类自己处理体力消耗）
        /// </summary>
        protected virtual bool ShouldAutoConsumeStamina()
        {
            return true;
        }

        /// <summary>
        /// 子类重写：能力是否就绪（额外条件检查）
        /// </summary>
        protected virtual bool IsReadyInternal()
        {
            return true;
        }

        /// <summary>
        /// 子类重写：能力激活时是否可以使用手（射击等）
        /// </summary>
        protected virtual bool CanUseHandWhileActive()
        {
            return false;
        }

        /// <summary>
        /// 子类重写：体力耗尽时的处理
        /// </summary>
        protected virtual void OnStaminaDepleted()
        {
            StopAction();
        }

        // ========== 辅助方法 ==========

        /// <summary>
        /// 暂停地面约束（用于空中移动）
        /// </summary>
        /// <param name="duration">持续时间（秒）</param>
        protected void PauseGroundConstraint(float duration)
        {
            try
            {
                if (characterController?.movementControl != null && pauseGroundConstraintMethod != null)
                {
                    var characterMovement = GetCharacterMovement();
                    if (characterMovement != null)
                    {
                        pauseGroundConstraintMethod.Invoke(characterMovement, new object[] { duration });
                    }
                }
            }
            catch
            {
                // 静默失败
            }
        }

        /// <summary>
        /// 获取 CharacterMovement 对象（带缓存）
        /// </summary>
        protected object GetCharacterMovement()
        {
            if (characterController?.movementControl == null) return null;

            try
            {
                // 如果已缓存且角色没变，直接返回
                if (cachedCharacterMovement != null && cachedCharacterMovementField != null)
                {
                    // 验证缓存是否仍然有效
                    try
                    {
                        var currentValue = cachedCharacterMovementField.GetValue(characterController.movementControl);
                        if (currentValue != null && currentValue.GetType() == characterMovementType)
                        {
                            return currentValue;
                        }
                    }
                    catch
                    {
                        // 缓存失效，重新获取
                    }
                }

                // 获取字段（首次或缓存失效后）
                if (cachedCharacterMovementField == null)
                {
                    var movementType = characterController.movementControl.GetType();
                    cachedCharacterMovementField = Utils.ReflectionCache.GetField(
                        movementType,
                        "characterMovement",
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
                    );
                }

                cachedCharacterMovement = cachedCharacterMovementField?.GetValue(characterController.movementControl);
                return cachedCharacterMovement;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 检查角色是否在地面上（带缓存）
        /// </summary>
        protected bool IsOnGround()
        {
            if (characterController?.movementControl == null) return false;

            try
            {
                // 尝试通过属性获取（缓存）
                if (cachedIsOnGroundProperty == null)
                {
                    cachedIsOnGroundProperty = Utils.ReflectionCache.GetProperty(
                        characterController.movementControl.GetType(),
                        "IsOnGround",
                        BindingFlags.Public | BindingFlags.Instance
                    );
                }

                if (cachedIsOnGroundProperty != null)
                {
                    object result = cachedIsOnGroundProperty.GetValue(characterController.movementControl);
                    if (result is bool onGround)
                    {
                        return onGround;
                    }
                }
            }
            catch
            {
                // 继续尝试其他方法
            }

            try
            {
                // 尝试从 CharacterMovement 获取
                var characterMovement = GetCharacterMovement();
                if (characterMovement != null)
                {
                    if (cachedIsOnGroundField == null && characterMovementType != null)
                    {
                        cachedIsOnGroundField = Utils.ReflectionCache.GetField(
                            characterMovementType,
                            "isOnGround",
                            BindingFlags.Public | BindingFlags.Instance
                        );
                    }

                    if (cachedIsOnGroundField != null)
                    {
                        object result = cachedIsOnGroundField.GetValue(characterMovement);
                        if (result is bool onGround)
                        {
                            return onGround;
                        }
                    }
                }
            }
            catch
            {
                // 静默失败
            }

            return false;
        }

        /// <summary>
        /// 播放音效
        /// </summary>
        /// <param name="soundKey">音效键</param>
        protected void PlaySound(string soundKey)
        {
            if (string.IsNullOrWhiteSpace(soundKey)) return;

            try
            {
                if (playSoundMethod != null)
                {
                    playSoundMethod.Invoke(null, new object[] { soundKey, gameObject });
                }
            }
            catch
            {
                // 静默失败
            }
        }

        /// <summary>
        /// 触发硬件同步事件（用于多人同步）
        /// </summary>
        protected void TriggerHardwareSyncEvent()
        {
            try
            {
                var hwSyncType = Utils.ReflectionCache.GetType("HardwareSyncingManager", "TeamSoda.Duckov.Core");
                if (hwSyncType != null)
                {
                    var setEventMethod = Utils.ReflectionCache.GetMethod(
                        hwSyncType,
                        "SetEvent",
                        BindingFlags.Public | BindingFlags.Static
                    );
                    setEventMethod?.Invoke(null, new object[] { "Dodge" });
                }
            }
            catch
            {
                // 静默失败
            }
        }

        /// <summary>
        /// 获取移动输入（WASD/方向键）
        /// </summary>
        /// <returns>归一化的输入向量</returns>
        protected Vector2 GetMovementInput()
        {
            Vector2 input = Vector2.zero;
            if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) input.y += 1f;
            if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) input.y -= 1f;
            if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) input.x -= 1f;
            if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) input.x += 1f;

            if (input.magnitude > 1f)
            {
                input.Normalize();
            }

            return input;
        }

        /// <summary>
        /// 输出日志
        /// </summary>
        protected void LogIfVerbose(string message)
        {
            ModBehaviour.DevLog($"{GetConfig().LogPrefix} {message}");
        }

        /// <summary>
        /// 重置能力状态
        /// </summary>
        public virtual void ResetState()
        {
            isCoolingDown = false;
            actionElapsedTime = 0f;
        }

        // ========== Update 生命周期 ==========

        private void Update()
        {
            // 检查冷却状态
            if (isCoolingDown && IsOnGround())
            {
                isCoolingDown = false;
                LogIfVerbose("已落地，冷却结束");
            }
        }
    }
}
