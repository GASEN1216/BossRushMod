// ============================================================================
// SetBonusManager.cs - 套装效果统一管理器
// ============================================================================
// 模块说明：
//   统一管理所有套装（冰霜套、雷霆套）的检测与激活/停用生命周期。
//   挂载到 OnMainCharacterSlotContentChangedEvent 事件，
//   在装备槽变化时检测套装状态。
//
// 设计原则：
//   - 与 DragonSetBonus 并行运行，互不干扰
//   - 套装之间天然互斥（同时只能穿一套头盔+护甲）
//   - 使用 TypeID 匹配，比名称匹配更可靠
// ============================================================================

using System;
using System.Reflection;
using UnityEngine;
using ItemStatsSystem;
using ItemStatsSystem.Items;

namespace BossRush
{
    /// <summary>
    /// 套装效果统一管理器 - 检测装备变化并分发到各套装效果模块
    /// </summary>
    public partial class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        #region 套装管理器配置

        // 套装 TypeID 定义
        private const int FROST_HELMET_TYPE_ID = 500053;  // 霜冠
        private const int FROST_ARMOR_TYPE_ID = 500054;   // 寒冰铠甲
        private const int THUNDER_HELMET_TYPE_ID = 500055; // 雷神之角
        private const int THUNDER_ARMOR_TYPE_ID = 500056;  // 雷霆战甲

        // 套装管理器状态
        private bool setBonusEventRegistered = false;
        private bool setBonusLevelEventRegistered = false;

        #endregion

        #region 事件注册

        /// <summary>
        /// 注册套装管理器事件（在 Integration 初始化时调用）
        /// </summary>
        private void RegisterSetBonusEvents()
        {
            if (setBonusEventRegistered && setBonusLevelEventRegistered) return;

            try
            {
                // 复用 DragonSetBonus 已缓存的 FieldInfo
                if (!setBonusEventRegistered)
                {
                    FieldInfo eventField = GetCachedSlotChangedEventField();

                    if (eventField != null)
                    {
                        var currentDelegate = eventField.GetValue(null) as Delegate;
                        var newDelegate = Delegate.Combine(currentDelegate,
                            new Action<CharacterMainControl, Slot>(OnSlotChangedForSetBonus));
                        eventField.SetValue(null, newDelegate);

                        setBonusEventRegistered = true;
                        DevLog("[SetBonus] 已注册装备槽变化事件");
                    }
                    else
                    {
                        DevLog("[SetBonus] 未找到 OnMainCharacterSlotContentChangedEvent 字段");
                    }
                }

                // 订阅场景加载事件，处理已穿戴装备进入游戏的情况
                if (!setBonusLevelEventRegistered)
                {
                    LevelManager.OnAfterLevelInitialized += OnLevelInitializedCheckSetBonus;
                    setBonusLevelEventRegistered = true;
                    DevLog("[SetBonus] 已注册场景加载事件");
                }
            }
            catch (Exception e)
            {
                DevLog("[SetBonus] 注册事件失败: " + e.Message);
            }
        }

        /// <summary>
        /// 取消注册套装管理器事件（在 Integration 销毁时调用）
        /// </summary>
        private void UnregisterSetBonusEvents()
        {
            try
            {
                if (setBonusEventRegistered)
                {
                    FieldInfo eventField = GetCachedSlotChangedEventField();

                    if (eventField != null)
                    {
                        var currentDelegate = eventField.GetValue(null) as Delegate;
                        var newDelegate = Delegate.Remove(currentDelegate,
                            new Action<CharacterMainControl, Slot>(OnSlotChangedForSetBonus));
                        eventField.SetValue(null, newDelegate);
                    }

                    setBonusEventRegistered = false;
                    DevLog("[SetBonus] 已取消注册装备槽变化事件");
                }

                if (setBonusLevelEventRegistered)
                {
                    LevelManager.OnAfterLevelInitialized -= OnLevelInitializedCheckSetBonus;
                    setBonusLevelEventRegistered = false;
                    DevLog("[SetBonus] 已取消注册场景加载事件");
                }
            }
            catch (Exception e)
            {
                DevLog("[SetBonus] 取消注册事件失败: " + e.Message);
            }

            // 停用所有套装效果
            DeactivateFrostSetBonus();
            DeactivateThunderSetBonus();
        }

        #endregion

        #region 套装检测

        /// <summary>
        /// 场景加载完成后检测套装状态
        /// </summary>
        private void OnLevelInitializedCheckSetBonus()
        {
            try
            {
                DevLog("[SetBonus] 场景加载完成，检测套装状态...");
                CharacterMainControl main = CharacterMainControl.Main;
                if (main != null)
                {
                    CheckSetBonusStatus(main);
                }
            }
            catch (Exception e)
            {
                DevLog("[SetBonus] OnLevelInitializedCheckSetBonus 出错: " + e.Message);
            }
        }

        /// <summary>
        /// 装备槽变化回调
        /// </summary>
        private void OnSlotChangedForSetBonus(CharacterMainControl character, Slot slot)
        {
            if (character == null || slot == null) return;

            // 只关心护甲槽和头盔槽
            string slotKey = slot.Key;
            if (slotKey != ARMOR_SLOT_NAME && slotKey != HELMET_SLOT_NAME) return;

            CheckSetBonusStatus(character);
        }

        /// <summary>
        /// 检测所有套装状态
        /// </summary>
        private void CheckSetBonusStatus(CharacterMainControl character)
        {
            try
            {
                if (character == null || character.CharacterItem == null)
                {
                    // 角色无效，停用所有套装
                    if (frostSetActive) DeactivateFrostSetBonus();
                    if (thunderSetActive) DeactivateThunderSetBonus();
                    return;
                }

                // 获取当前装备的 TypeID
                Item helmetItem = character.GetHelmatItem();
                Item armorItem = character.GetArmorItem();

                int helmetId = helmetItem != null ? helmetItem.TypeID : 0;
                int armorId = armorItem != null ? armorItem.TypeID : 0;

                // 冰霜套检测
                bool newFrostSet = (helmetId == FROST_HELMET_TYPE_ID && armorId == FROST_ARMOR_TYPE_ID);
                if (newFrostSet != frostSetActive)
                {
                    if (newFrostSet)
                    {
                        ActivateFrostSetBonus(character);
                    }
                    else
                    {
                        DeactivateFrostSetBonus();
                    }
                }

                // 雷霆套检测
                bool newThunderSet = (helmetId == THUNDER_HELMET_TYPE_ID && armorId == THUNDER_ARMOR_TYPE_ID);
                if (newThunderSet != thunderSetActive)
                {
                    if (newThunderSet)
                    {
                        ActivateThunderSetBonus(character);
                    }
                    else
                    {
                        DeactivateThunderSetBonus();
                    }
                }
            }
            catch (Exception e)
            {
                DevLog("[SetBonus] CheckSetBonusStatus 出错: " + e.Message);
            }
        }

        #endregion
    }
}
