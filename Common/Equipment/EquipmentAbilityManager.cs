// ============================================================================
// EquipmentAbilityManager.cs - 装备能力管理器基类
// ============================================================================
// 模块说明：
//   为所有装备能力提供统一的管理器基类
//   处理能力注册、输入拦截、生命周期管理
// ============================================================================

using System;
using System.Reflection;
using UnityEngine;

namespace BossRush.Common.Equipment
{
    /// <summary>
    /// 装备能力管理器基类 - 管理装备能力的激活和输入处理
    /// </summary>
    public abstract class EquipmentAbilityManager<TConfig, TAction> : MonoBehaviour
        where TConfig : EquipmentAbilityConfig
        where TAction : EquipmentAbilityAction
    {
        // ========== 单例 ==========

        private static EquipmentAbilityManager<TConfig, TAction> _instance;

        /// <summary>
        /// 单例实例
        /// </summary>
        public static EquipmentAbilityManager<TConfig, TAction> Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindObjectOfType<EquipmentAbilityManager<TConfig, TAction>>();
                    if (_instance == null)
                    {
                        GameObject go = new GameObject($"Manager_{typeof(TConfig).Name}");
                        _instance = go.AddComponent<EquipmentAbilityManager<TConfig, TAction>>();
                        DontDestroyOnLoad(go);
                    }
                }
                return _instance;
            }
        }

        // ========== 状态变量 ==========

        /// <summary>
        /// 能力是否已启用
        /// </summary>
        protected bool abilityEnabled = false;

        /// <summary>
        /// 能力动作组件
        /// </summary>
        protected TAction abilityAction = null;

        /// <summary>
        /// 目标角色
        /// </summary>
        protected CharacterMainControl targetCharacter = null;

        /// <summary>
        /// 动作所在的 GameObject
        /// </summary>
        protected GameObject actionObject = null;

        // ========== 输入拦截相关 ==========

        /// <summary>
        /// 缓存的 InputAction 对象
        /// </summary>
        protected object cachedInputAction = null;

        /// <summary>
        /// 缓存的检测方法
        /// </summary>
        protected MethodInfo cachedIsPressedMethod = null;

        /// <summary>
        /// 是否已成功缓存输入动作
        /// </summary>
        protected bool inputActionCached = false;

        // ========== 配置 ==========

        /// <summary>
        /// 获取该能力的配置对象（由子类实现）
        /// </summary>
        protected abstract TConfig GetConfig();

        /// <summary>
        /// 获取要拦截的输入动作名称（如 "Dash"）
        /// </summary>
        protected abstract string GetInputActionName();

        /// <summary>
        /// 创建能力动作实例（由子类实现）
        /// </summary>
        protected abstract TAction CreateAbilityAction();

        // ========== 公开属性 ==========

        /// <summary>
        /// 能力是否已启用
        /// </summary>
        public bool IsAbilityEnabled => abilityEnabled;

        /// <summary>
        /// 能力动作是否正在运行
        /// </summary>
        public bool IsActionRunning => abilityAction != null && abilityAction.Running;

        // ========== 生命周期 ==========

        protected virtual void Awake()
        {
            // 确保单例唯一性
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            DontDestroyOnLoad(gameObject);

            OnManagerInitialized();
        }

        protected virtual void Update()
        {
            if (!abilityEnabled) return;

            // 检测输入并尝试执行能力
            CheckAndInterceptInput();
        }

        protected virtual void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
            }

            CleanupAbilityAction();
            CleanupInputCache();
        }

        // ========== 子类钩子 ==========

        /// <summary>
        /// 管理器初始化完成时调用（子类可重写）
        /// </summary>
        protected virtual void OnManagerInitialized()
        {
            LogIfVerbose($"{GetType().Name} 初始化完成");
        }

        /// <summary>
        /// 能力激活前调用（子类可重写，返回 false 阻止激活）
        /// </summary>
        protected virtual bool OnBeforeActivate(CharacterMainControl character)
        {
            return true;
        }

        /// <summary>
        /// 能力激活后调用（子类可重写）
        /// </summary>
        protected virtual void OnAfterActivate(CharacterMainControl character)
        {
        }

        /// <summary>
        /// 能力停用后调用（子类可重写）
        /// </summary>
        protected virtual void OnAfterDeactivate()
        {
        }

        /// <summary>
        /// 尝试执行能力前调用（子类可重写，返回 false 阻止执行）
        /// </summary>
        protected virtual bool OnBeforeTryExecute()
        {
            return true;
        }

        // ========== 输入拦截 ==========

        /// <summary>
        /// 检测输入并尝试拦截执行能力
        /// </summary>
        protected virtual void CheckAndInterceptInput()
        {
            // 尝试缓存 InputAction
            if (!inputActionCached)
            {
                TryCacheInputAction();
            }

            // 检测输入是否刚刚按下
            bool inputPressed = IsInputPressed();

            if (inputPressed)
            {
                LogIfVerbose($"检测到 {GetInputActionName()} 输入，尝试执行能力...");
                bool success = TryExecuteAbility();
                LogIfVerbose($"能力执行结果: {(success ? "成功" : "失败")}");
            }
        }

        /// <summary>
        /// 尝试缓存 InputAction 引用
        /// </summary>
        protected virtual void TryCacheInputAction()
        {
            try
            {
                var inputControlType = Utils.ReflectionCache.GetType("CharacterInputControl", "TeamSoda.Duckov.Core");
                if (inputControlType == null)
                {
                    LogIfVerbose("找不到 CharacterInputControl 类型");
                    return;
                }

                var instanceProperty = Utils.ReflectionCache.GetProperty(
                    inputControlType,
                    "Instance",
                    BindingFlags.Public | BindingFlags.Static
                );
                if (instanceProperty == null)
                {
                    LogIfVerbose("找不到 CharacterInputControl.Instance 属性");
                    return;
                }

                var inputControlInstance = instanceProperty.GetValue(null);
                if (inputControlInstance == null)
                {
                    return; // 场景可能还没加载完
                }

                // 获取 inputActions 字段
                var inputActionsField = Utils.ReflectionCache.GetField(
                    inputControlType,
                    "inputActions",
                    BindingFlags.NonPublic | BindingFlags.Instance
                );
                if (inputActionsField == null)
                {
                    LogIfVerbose("找不到 inputActions 字段");
                    return;
                }

                var inputActions = inputActionsField.GetValue(inputControlInstance);
                if (inputActions == null) return;

                // 获取指定的输入字段
                var actionName = GetInputActionName();
                var actionField = Utils.ReflectionCache.GetField(
                    inputActions.GetType(),
                    actionName,
                    BindingFlags.Public | BindingFlags.Instance
                );
                if (actionField == null)
                {
                    LogIfVerbose($"找不到 {actionName} 输入字段");
                    return;
                }

                cachedInputAction = actionField.GetValue(inputActions);
                if (cachedInputAction != null)
                {
                    // 缓存检测方法
                    cachedIsPressedMethod = Utils.ReflectionCache.GetMethod(
                        cachedInputAction.GetType(),
                        "WasPressedThisFrame",
                        BindingFlags.Public | BindingFlags.Instance
                    );

                    if (cachedIsPressedMethod != null)
                    {
                        inputActionCached = true;
                        LogIfVerbose($"成功缓存 {actionName} InputAction");
                    }
                }
            }
            catch (Exception e)
            {
                LogIfVerbose($"缓存 InputAction 失败: {e.Message}");
            }
        }

        /// <summary>
        /// 检测输入是否按下
        /// </summary>
        protected virtual bool IsInputPressed()
        {
            // 方法1：使用缓存的 InputAction
            if (cachedInputAction != null && cachedIsPressedMethod != null)
            {
                try
                {
                    object result = cachedIsPressedMethod.Invoke(cachedInputAction, null);
                    if (result is bool pressed)
                    {
                        return pressed;
                    }
                }
                catch
                {
                    // 继续尝试备用方法
                }
            }

            // 方法2：备用键盘检测（子类可重写）
            return IsInputPressedFallback();
        }

        /// <summary>
        /// 备用输入检测方法（子类可重写）
        /// </summary>
        protected virtual bool IsInputPressedFallback()
        {
            // 默认检测 Space 键（通常是 dash）
            return Input.GetKeyDown(KeyCode.Space);
        }

        // ========== 公开方法 ==========

        /// <summary>
        /// 注册能力到指定角色
        /// </summary>
        /// <param name="character">目标角色</param>
        public virtual void RegisterAbility(CharacterMainControl character)
        {
            if (character == null)
            {
                LogIfVerbose("RegisterAbility: character is null");
                return;
            }

            // 如果已经注册到同一个角色，跳过
            if (abilityEnabled && targetCharacter == character)
            {
                LogIfVerbose("能力已注册到该角色");
                return;
            }

            // 清理之前的能力动作
            CleanupAbilityAction();

            // 设置目标角色
            targetCharacter = character;

            // 检查是否可以激活
            if (!OnBeforeActivate(character))
            {
                LogIfVerbose("能力激活被阻止");
                return;
            }

            // 创建能力动作
            SetupAbilityAction();

            // 启用能力
            abilityEnabled = true;

            OnAfterActivate(character);
            LogIfVerbose($"能力已注册到角色: {character.name}");
        }

        /// <summary>
        /// 注销能力
        /// </summary>
        public virtual void UnregisterAbility()
        {
            if (!abilityEnabled) return;

            abilityEnabled = false;
            CleanupAbilityAction();
            targetCharacter = null;

            OnAfterDeactivate();
            LogIfVerbose("能力已注销");
        }

        /// <summary>
        /// 尝试执行能力
        /// </summary>
        /// <returns>是否成功执行</returns>
        public virtual bool TryExecuteAbility()
        {
            if (!abilityEnabled) return false;
            if (!OnBeforeTryExecute()) return false;
            if (abilityAction == null)
            {
                LogIfVerbose("TryExecuteAbility: abilityAction is null");
                return false;
            }
            if (targetCharacter == null)
            {
                LogIfVerbose("TryExecuteAbility: targetCharacter is null");
                return false;
            }
            if (!abilityAction.IsReady())
            {
                return false;
            }

            bool success = targetCharacter.StartAction(abilityAction);
            if (success)
            {
                LogIfVerbose("能力动作已启动");
            }

            return success;
        }

        /// <summary>
        /// 场景切换时重置
        /// </summary>
        public virtual void OnSceneChanged()
        {
            // 场景切换后 InputAction 可能失效，需要重新缓存
            cachedInputAction = null;
            inputActionCached = false;

            if (!abilityEnabled)
            {
                LogIfVerbose("场景切换，但能力未启用，跳过");
                return;
            }

            LogIfVerbose("场景切换，保留角色引用并重建动作");

            // 清理旧的动作
            CleanupAbilityAction();
            // 注意：不自动清空 targetCharacter，等待 RebindToCharacter 调用
        }

        /// <summary>
        /// 重新绑定到新场景的角色
        /// </summary>
        /// <param name="character">新场景的角色</param>
        public virtual void RebindToCharacter(CharacterMainControl character)
        {
            if (character == null)
            {
                LogIfVerbose("RebindToCharacter: character is null");
                return;
            }

            if (!abilityEnabled)
            {
                LogIfVerbose("RebindToCharacter: 能力未启用");
                return;
            }

            LogIfVerbose($"RebindToCharacter: 重新绑定到角色 {character.name}");

            targetCharacter = character;
            SetupAbilityAction();

            LogIfVerbose("RebindToCharacter: 重新绑定完成");
        }

        // ========== 内部方法 ==========

        /// <summary>
        /// 设置能力动作
        /// </summary>
        protected virtual void SetupAbilityAction()
        {
            if (targetCharacter == null) return;

            // 创建动作 GameObject
            actionObject = new GameObject($"Action_{typeof(TAction).Name}");
            actionObject.transform.position = targetCharacter.transform.position;
            actionObject.transform.SetParent(transform);

            // 添加动作组件
            abilityAction = CreateAbilityAction();

            LogIfVerbose("能力动作组件已创建");
        }

        /// <summary>
        /// 清理能力动作
        /// </summary>
        protected virtual void CleanupAbilityAction()
        {
            if (abilityAction != null)
            {
                abilityAction.ResetState();
                if (abilityAction.Running)
                {
                    abilityAction.StopAction();
                }
                abilityAction = null;
            }

            if (actionObject != null)
            {
                Destroy(actionObject);
                actionObject = null;
            }
        }

        /// <summary>
        /// 清理输入缓存
        /// </summary>
        protected void CleanupInputCache()
        {
            cachedInputAction = null;
            cachedIsPressedMethod = null;
            inputActionCached = false;
        }

        /// <summary>
        /// 输出日志
        /// </summary>
        protected void LogIfVerbose(string message)
        {
            var config = GetConfig();
            ModBehaviour.DevLog($"{config.LogPrefix} {message}");
        }

        // ========== 静态方法 ==========

        /// <summary>
        /// 确保管理器实例存在
        /// </summary>
        public static void EnsureInstance()
        {
            var _ = Instance;
        }

        /// <summary>
        /// 清理静态实例
        /// </summary>
        public static void CleanupStatic()
        {
            if (_instance != null)
            {
                _instance.UnregisterAbility();
            }
        }
    }
}
