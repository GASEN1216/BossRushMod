// ============================================================================
// ReverseScaleEffectManager.cs - 逆鳞图腾效果管理器
// ============================================================================
// 模块说明：
//   继承 EquipmentEffectManager，监听装备变化并激活逆鳞能力
//   代码已大幅简化，通用逻辑由基类处理
// ============================================================================

using UnityEngine;
using ItemStatsSystem.Items;
using ItemStatsSystem;
using BossRush.Common.Equipment;

namespace BossRush
{
    /// <summary>
    /// 逆鳞图腾效果管理器 - 监听装备变化并激活逆鳞能力
    /// 使用 EquipmentEffectManager 基类，代码更简洁
    /// </summary>
    public class ReverseScaleEffectManager : EquipmentEffectManager<ReverseScaleConfig, ReverseScaleAbilityManager>
    {
        // ========== 配置 ==========

        private ReverseScaleConfig config => ReverseScaleConfig.Instance;

        protected override ReverseScaleConfig GetConfig() => config;

        protected override ReverseScaleAbilityManager GetAbilityManager() => ReverseScaleAbilityManager.Instance;

        // ========== 单例实现 ==========

        private static ReverseScaleEffectManager _instance;

        /// <summary>
        /// 单例实例
        /// </summary>
        public new static ReverseScaleEffectManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    ReverseScaleEffectManager[] instances = FindObjectsOfType<ReverseScaleEffectManager>();
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
                        GameObject go = new GameObject("ReverseScaleEffectManager");
                        _instance = go.AddComponent<ReverseScaleEffectManager>();
                        DontDestroyOnLoad(go);
                    }
                }
                return _instance;
            }
        }

        /// <summary>
        /// 确保管理器实例存在
        /// </summary>
        public new static void EnsureInstance()
        {
            var _ = Instance;
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
        
        /// <summary>
        /// 场景加载完成时调用 - 延迟检查装备状态
        /// 重要：玩家进入存档时，需要主动检查是否已装备逆鳞图腾
        /// </summary>
        protected override void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
        {
            base.OnSceneLoaded(scene, mode);
            
            // 延迟检查装备状态，确保角色已完全加载
            StartCoroutine(DelayedCheckEquipment());
        }
        
        /// <summary>
        /// 延迟检查装备状态（等待角色完全加载）
        /// </summary>
        private System.Collections.IEnumerator DelayedCheckEquipment()
        {
            // 等待一段时间，确保角色和装备都已加载
            yield return new UnityEngine.WaitForSeconds(0.5f);
            
            CharacterMainControl mainCharacter = CharacterMainControl.Main;
            if (mainCharacter != null)
            {
                ModBehaviour.DevLog($"{config.LogPrefix} 场景加载后延迟检查装备状态");
                CheckAllSlots(mainCharacter);
            }
        }

        // ========== 装备检测 ==========

        /// <summary>
        /// 检查物品是否是逆鳞图腾
        /// </summary>
        protected override bool IsMatchingItem(Item item)
        {
            return CheckItemByTypeId(item, ReverseScaleConfig.TotemTypeId);
        }
        
        // ========== 状态重置 ==========
        
        /// <summary>
        /// 重置激活状态（当逆鳞效果触发后调用，以便下次装备新逆鳞时能正确激活）
        /// </summary>
        public void ResetActivationState()
        {
            abilityActivated = false;
            equippedItem = null;
            lastRegisteredCharacter = null;
            ModBehaviour.DevLog($"{config.LogPrefix} 激活状态已重置，下次装备逆鳞将重新注册能力");
        }

    }
}
