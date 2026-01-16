// ============================================================================
// EquipmentEffectManager.cs - 装备效果管理器基类
// ============================================================================
// 模块说明：
//   为所有装备效果提供统一的管理器基类
///  通过全局事件监听装备槽位变化，自动激活/停用能力
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using ItemStatsSystem;
using ItemStatsSystem.Items;

namespace BossRush.Common.Equipment
{
    /// <summary>
    /// 装备效果管理器基类 - 监听装备变化并管理能力激活
    /// </summary>
    public abstract class EquipmentEffectManager<TConfig, TManager> : MonoBehaviour
        where TConfig : EquipmentAbilityConfig
        where TManager : class
    {
        // ========== 单例 ==========

        private static EquipmentEffectManager<TConfig, TManager> _instance;

        /// <summary>
        /// 单例实例
        /// </summary>
        public static EquipmentEffectManager<TConfig, TManager> Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindObjectOfType<EquipmentEffectManager<TConfig, TManager>>();
                }
                return _instance;
            }
        }

        // ========== 状态变量 ==========

        /// <summary>
        /// 能力是否已激活
        /// </summary>
        protected bool abilityActivated = false;

        /// <summary>
        /// 当前装备的物品
        /// </summary>
        protected Item equippedItem = null;

        /// <summary>
        /// 上次注册的角色（用于检测角色变化）
        /// </summary>
        protected CharacterMainControl lastRegisteredCharacter = null;

        /// <summary>
        /// 缓存角色的 Item 组件（避免每帧 GetComponent）
        /// </summary>
        protected Item cachedCharacterItem = null;
        protected CharacterMainControl cachedCharacterForItem = null;

        // ========== 抽象属性和方法 ==========

        /// <summary>
        /// 获取该能力的配置对象（由子类实现）
        /// </summary>
        protected abstract TConfig GetConfig();

        /// <summary>
        /// 获取能力管理器实例（由子类实现）
        /// </summary>
        protected abstract TManager GetAbilityManager();

        /// <summary>
        /// 检查物品是否是该能力的装备（由子类实现）
        /// </summary>
        /// <param name="item">要检查的物品</param>
        /// <returns>是否是匹配的装备</returns>
        protected abstract bool IsMatchingItem(Item item);

        // ========== 生命周期 ==========

        protected virtual void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(this);
                return;
            }
            _instance = this;

            OnManagerInitialized();
        }

        protected virtual void OnEnable()
        {
            // 订阅全局装备槽位变化事件
            CharacterMainControl.OnMainCharacterSlotContentChangedEvent += OnMainCharacterSlotContentChanged;
            LogIfVerbose("已订阅 OnMainCharacterSlotContentChangedEvent");
        }

        protected virtual void OnDisable()
        {
            // 取消订阅
            CharacterMainControl.OnMainCharacterSlotContentChangedEvent -= OnMainCharacterSlotContentChanged;
            DeactivateAbility();
        }

        protected virtual void OnDestroy()
        {
            // 取消订阅
            CharacterMainControl.OnMainCharacterSlotContentChangedEvent -= OnMainCharacterSlotContentChanged;
            DeactivateAbility();

            if (_instance == this)
            {
                _instance = null;
            }
        }

        // ========== 子类钩子 ==========

        /// <summary>
        /// 管理器初始化完成时调用（子类可重写）
        /// </summary>
        protected virtual void OnManagerInitialized()
        {
            LogIfVerbose($"{GetType().Name} 初始化");
        }

        /// <summary>
        /// 能力激活前调用（子类可重写，返回 false 阻止激活）
        /// </summary>
        protected virtual bool OnBeforeActivate(CharacterMainControl character, Item item)
        {
            return true;
        }

        /// <summary>
        /// 能力激活后调用（子类可重写）
        /// </summary>
        protected virtual void OnAfterActivate(CharacterMainControl character, Item item)
        {
        }

        /// <summary>
        /// 能力停用后调用（子类可重写）
        /// </summary>
        protected virtual void OnAfterDeactivate()
        {
        }

        // ========== 事件处理 ==========

        /// <summary>
        /// 主角色槽位内容变化事件处理
        /// </summary>
        protected virtual void OnMainCharacterSlotContentChanged(CharacterMainControl character, Slot slot)
        {
            if (character == null || slot == null) return;

            LogIfVerbose($"OnMainCharacterSlotContentChanged - slot={slot.Key}");
            CheckAllSlots(character);
        }

        /// <summary>
        /// 检查所有装备槽位（带缓存优化）
        /// </summary>
        protected virtual void CheckAllSlots(CharacterMainControl character)
        {
            if (character == null) return;

            // 使用缓存的 Item 组件
            if (cachedCharacterItem == null || cachedCharacterForItem != character)
            {
                cachedCharacterItem = character.GetComponent<Item>();
                if (cachedCharacterItem == null)
                {
                    cachedCharacterItem = character.GetComponentInChildren<Item>();
                }
                cachedCharacterForItem = character;
            }

            Item characterItem = cachedCharacterItem;

            if (characterItem == null)
            {
                LogIfVerbose("未找到角色的 Item 组件");
                return;
            }

            // 遍历所有槽位，查找匹配的装备
            bool foundMatchingItem = false;
            Item foundItem = null;

            foreach (Slot slot in characterItem.Slots)
            {
                if (slot == null || slot.Content == null) continue;

                if (IsMatchingItem(slot.Content))
                {
                    foundMatchingItem = true;
                    foundItem = slot.Content;
                    LogIfVerbose($"在槽位 {slot.Key} 找到匹配装备: {slot.Content.name}");
                    break;
                }
            }

            // 根据是否找到装备来激活/停用能力
            bool needActivate = foundMatchingItem && !abilityActivated;
            bool needRebind = foundMatchingItem && abilityActivated && lastRegisteredCharacter != character;

            LogIfVerbose($"CheckAllSlots - found: {foundMatchingItem}, activated: {abilityActivated}, needActivate: {needActivate}, needRebind: {needRebind}");

            if (needActivate)
            {
                if (OnBeforeActivate(character, foundItem))
                {
                    equippedItem = foundItem;
                    ActivateAbility(character);
                    lastRegisteredCharacter = character;
                    OnAfterActivate(character, foundItem);
                }
            }
            else if (needRebind)
            {
                LogIfVerbose("检测到角色变化，调用 RebindToCharacter");
                equippedItem = foundItem;
                var manager = GetAbilityManager();
                if (manager != null)
                {
                    // 使用反射调用 RebindToCharacter
                    var rebindMethod = manager.GetType().GetMethod("RebindToCharacter", new Type[] { typeof(CharacterMainControl) });
                    rebindMethod?.Invoke(manager, new object[] { character });
                }
                lastRegisteredCharacter = character;
            }
            else if (!foundMatchingItem && abilityActivated)
            {
                equippedItem = null;
                lastRegisteredCharacter = null;
                DeactivateAbility();
                OnAfterDeactivate();
            }
        }

        // ========== 能力管理 ==========

        /// <summary>
        /// 激活能力
        /// </summary>
        protected virtual void ActivateAbility(CharacterMainControl character)
        {
            if (abilityActivated)
            {
                LogIfVerbose("ActivateAbility: 已经激活，跳过");
                return;
            }
            if (character == null)
            {
                LogIfVerbose("ActivateAbility: character is null");
                return;
            }

            LogIfVerbose($"ActivateAbility: 开始激活能力，角色: {character.name}");

            var manager = GetAbilityManager();
            if (manager != null)
            {
                // 使用反射调用 RegisterAbility
                var registerMethod = manager.GetType().GetMethod("RegisterAbility", new Type[] { typeof(CharacterMainControl) });
                registerMethod?.Invoke(manager, new object[] { character });
                abilityActivated = true;
                LogIfVerbose("ActivateAbility: 能力已激活");
            }
            else
            {
                LogIfVerbose("ActivateAbility: 能力管理器为 null");
            }
        }

        /// <summary>
        /// 停用能力
        /// </summary>
        protected virtual void DeactivateAbility()
        {
            if (!abilityActivated)
            {
                LogIfVerbose("DeactivateAbility: 能力未激活，跳过");
                return;
            }

            LogIfVerbose("DeactivateAbility: 开始停用能力");

            var manager = GetAbilityManager();
            if (manager != null)
            {
                // 使用反射调用 UnregisterAbility
                var unregisterMethod = manager.GetType().GetMethod("UnregisterAbility");
                unregisterMethod?.Invoke(manager, null);
            }

            abilityActivated = false;
            LogIfVerbose("DeactivateAbility: 能力已停用");
        }

        // ========== 公开方法 ==========

        /// <summary>
        /// 手动检查当前装备状态（场景加载后调用）
        /// </summary>
        public void CheckCurrentEquipment()
        {
            CharacterMainControl mainCharacter = CharacterMainControl.Main;
            if (mainCharacter != null)
            {
                LogIfVerbose("检查装备状态");
                CheckAllSlots(mainCharacter);
            }
        }

        /// <summary>
        /// 确保管理器实例存在
        /// </summary>
        public static void EnsureInstance()
        {
            if (_instance == null)
            {
                GameObject go = new GameObject($"EffectManager_{typeof(TConfig).Name}");
                _instance = go.AddComponent<EquipmentEffectManager<TConfig, TManager>>();
                DontDestroyOnLoad(go);
            }
        }

        // ========== 辅助方法 ==========

        /// <summary>
        /// 通过 TypeID 检查物品是否匹配
        /// </summary>
        protected bool CheckItemByTypeId(Item item, int typeId)
        {
            return item != null && item.TypeID == typeId;
        }

        /// <summary>
        /// 通过名称检查物品是否匹配
        /// </summary>
        protected bool CheckItemByName(Item item, string nameContains)
        {
            return item != null && item.name != null && item.name.Contains(nameContains);
        }

        /// <summary>
        /// 通过 Bool 标记检查物品是否匹配
        /// </summary>
        protected bool CheckItemByBoolFlag(Item item, string flagName)
        {
            if (item == null) return false;
            try
            {
                return item.GetBool(flagName);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 输出日志
        /// </summary>
        protected void LogIfVerbose(string message)
        {
            var config = GetConfig();
            ModBehaviour.DevLog($"{config.LogPrefix} {message}");
        }
    }
}
