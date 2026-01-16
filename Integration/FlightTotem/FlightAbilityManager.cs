// ============================================================================
// FlightAbilityManager.cs - 飞行能力管理器
// ============================================================================
// 模块说明：
//   继承 EquipmentAbilityManager，管理飞行能力的注册和输入拦截
//   代码已大幅简化，通用逻辑由基类处理
// ============================================================================

using System;
using UnityEngine;
using BossRush.Common.Equipment;

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

        protected override void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
            }
            base.OnDestroy();
        }

        // ========== 公开属性 ==========

        /// <summary>
        /// 飞行能力是否正在运行
        /// </summary>
        public bool IsFlying => IsActionRunning;

        // ========== 向后兼容的方法 ==========

        /// <summary>
        /// 注册飞行能力（向后兼容）
        /// </summary>
        public void RegisterFlightAbility(CharacterMainControl character)
        {
            RegisterAbility(character);
        }

        /// <summary>
        /// 注销飞行能力（向后兼容）
        /// </summary>
        public void UnregisterFlightAbility()
        {
            UnregisterAbility();
        }

        /// <summary>
        /// 尝试执行飞行（向后兼容）
        /// </summary>
        public bool TryFlight()
        {
            return TryExecuteAbility();
        }

        /// <summary>
        /// 处理 dash 输入（向后兼容）
        /// </summary>
        public bool HandleDashInput()
        {
            if (!IsAbilityEnabled) return false;
            return TryExecuteAbility();
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
                _instance.UnregisterFlightAbility();
            }
        }
    }
}
