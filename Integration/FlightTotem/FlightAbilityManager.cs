// ============================================================================
// FlightAbilityManager.cs - 飞行能力管理器
// ============================================================================
// 模块说明：
//   继承 EquipmentAbilityManager，管理飞行能力的注册和输入拦截
//   代码已大幅简化，通用逻辑由基类处理
// ============================================================================

using System;
using System.Reflection;
using UnityEngine;
using BossRush.Common.Equipment;
using BossRush.Common.Utils;

namespace BossRush
{
    /// <summary>
    /// 飞行能力管理器 - 管理飞行能力的注册和 dash 输入拦截
    /// 使用 EquipmentAbilityManager 基类，代码更简洁
    /// </summary>
    public class FlightAbilityManager : EquipmentAbilityManager<FlightConfig, CA_Flight>
    {
        // ========== 配置 ==========

        private FlightConfig config => FlightConfig.Instance;

        protected override FlightConfig GetConfig() => config;

        protected override string GetInputActionName() => "Dash";

        protected override CA_Flight CreateAbilityAction()
        {
            // 基类 SetupAbilityAction 会先创建 actionObject
            // 这里直接添加组件，如果 actionObject 为 null 会抛出异常
            // 这样可以让问题更早暴露，而不是返回 null
            return actionObject.AddComponent<CA_Flight>();
        }

        // ========== 单例实现（使用基类的单例逻辑） ==========

        private static FlightAbilityManager _instance;

        /// <summary>
        /// 单例实例
        /// </summary>
        public new static FlightAbilityManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    FlightAbilityManager[] instances = FindObjectsOfType<FlightAbilityManager>();
                    if (instances.Length > 0)
                    {
                        _instance = instances[0];
                        // 如果有多个实例，销毁多余的
                        for (int i = 1; i < instances.Length; i++)
                        {
                            Destroy(instances[i].gameObject);
                        }
                    }
                    else
                    {
                        GameObject go = new GameObject("FlightAbilityManager");
                        _instance = go.AddComponent<FlightAbilityManager>();
                        DontDestroyOnLoad(go);
                    }
                }
                return _instance;
            }
        }

        protected override void Awake()
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

        protected override void OnAfterActivate(CharacterMainControl character)
        {
            base.OnAfterActivate(character);
            CacheAndDisableDash(character);
        }

        protected override void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
            }
            RestoreDash();
            base.OnDestroy();
        }

        protected override void Update()
        {
            base.Update();

            if (!abilityEnabled || abilityAction == null || !abilityAction.Running)
            {
                return;
            }

            abilityAction.UpdateAction(Time.deltaTime);
        }

        // ========== 公开属性 ==========

        /// <summary>
        /// 飞行能力是否正在运行
        /// </summary>
        public bool IsFlying => IsActionRunning;

        public override bool TryExecuteAbility()
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

            bool success = abilityAction.StartActionByCharacter(targetCharacter);
            if (success)
            {
                LogIfVerbose("能力动作已启动（并行模式）");
            }

            return success;
        }

        public override void UnregisterAbility()
        {
            RestoreDash();
            base.UnregisterAbility();
        }

        public override void RebindToCharacter(CharacterMainControl character)
        {
            RestoreDash();
            base.RebindToCharacter(character);
            CacheAndDisableDash(character);
        }

        public bool IsFlightInputHeld()
        {
            if (!inputActionCached)
            {
                TryCacheInputAction();
            }

            if (cachedInputAction != null)
            {
                try
                {
                    if (cachedReadValueAsButtonMethod == null)
                    {
                        cachedReadValueAsButtonMethod = BossRush.Common.Utils.ReflectionCache.GetMethod(
                            cachedInputAction.GetType(),
                            "ReadValueAsButton",
                            BindingFlags.Public | BindingFlags.Instance
                        );
                    }

                    if (cachedReadValueAsButtonMethod != null)
                    {
                        object result = cachedReadValueAsButtonMethod.Invoke(cachedInputAction, null);
                        if (result is bool pressed)
                        {
                            return pressed;
                        }
                    }
                }
                catch
                {
                    // 静默失败，回退到旧输入
                }
            }

            return Input.GetKey(KeyCode.Space);
        }

        // ========== 静态方法 ==========

        /// <summary>
        /// 确保管理器实例存在
        /// </summary>
        public new static void EnsureInstance()
        {
            var _ = Instance;
        }

        /// <summary>
        /// 清理静态实例
        /// </summary>
        public static void Cleanup()
        {
            if (_instance != null)
            {
                _instance.UnregisterAbility();
            }
        }

        private void CacheAndDisableDash(CharacterMainControl character)
        {
            if (character == null) return;
            if (cachedDashCharacter == character) return;

            if (dashActionField == null)
            {
                dashActionField = BossRush.Common.Utils.ReflectionCache.GetField(
                    character.GetType(),
                    "dashAction",
                    BindingFlags.Public | BindingFlags.Instance
                );
            }

            if (dashActionField == null) return;

            cachedDashAction = dashActionField.GetValue(character);
            cachedDashCharacter = character;

            if (cachedDashAction != null)
            {
                dashActionField.SetValue(character, null);
            }
        }

        private void RestoreDash()
        {
            if (cachedDashCharacter == null || dashActionField == null)
            {
                cachedDashAction = null;
                cachedDashCharacter = null;
                return;
            }

            try
            {
                if (dashActionField.GetValue(cachedDashCharacter) == null)
                {
                    dashActionField.SetValue(cachedDashCharacter, cachedDashAction);
                }
            }
            catch
            {
                // 静默失败
            }
            finally
            {
                cachedDashAction = null;
                cachedDashCharacter = null;
            }
        }

        private MethodInfo cachedReadValueAsButtonMethod;
        private FieldInfo dashActionField;
        private object cachedDashAction;
        private CharacterMainControl cachedDashCharacter;
    }
}
