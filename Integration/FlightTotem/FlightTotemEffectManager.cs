// ============================================================================
// FlightTotemEffectManager.cs - 飞行图腾效果管理器
// ============================================================================
// 模块说明：
//   继承 EquipmentEffectManager，监听装备变化并激活飞行能力
//   代码已大幅简化，通用逻辑由基类处理
// ============================================================================

using UnityEngine;
using ItemStatsSystem.Items;
using ItemStatsSystem;
using BossRush.Common.Equipment;

namespace BossRush
{
    /// <summary>
    /// 飞行图腾效果管理器 - 监听装备变化并激活飞行能力
    /// 使用 EquipmentEffectManager 基类，代码更简洁
    /// </summary>
    public class FlightTotemEffectManager : EquipmentEffectManager<FlightConfig, FlightAbilityManager>
    {
        // ========== 配置 ==========

        private FlightConfig config => FlightConfig.Instance;

        protected override FlightConfig GetConfig() => config;

        protected override FlightAbilityManager GetAbilityManager() => FlightAbilityManager.Instance;

        // ========== 单例实现 ==========

        private static FlightTotemEffectManager _instance;

        /// <summary>
        /// 单例实例
        /// </summary>
        public new static FlightTotemEffectManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    FlightTotemEffectManager[] instances = FindObjectsOfType<FlightTotemEffectManager>();
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
                        GameObject go = new GameObject("FlightTotemEffectManager");
                        _instance = go.AddComponent<FlightTotemEffectManager>();
                        DontDestroyOnLoad(go);
                    }
                }
                return _instance;
            }
        }

        protected override void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;

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

        // ========== 装备检测 ==========

        /// <summary>
        /// 检查物品是否是飞行图腾
        /// </summary>
        protected override bool IsMatchingItem(Item item)
        {
            return CheckItemByTypeId(item, FlightConfig.TotemTypeIdBase);
        }

    }
}
