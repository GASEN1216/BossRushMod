using System;
using System.Collections.Generic;
using UnityEngine;
using ItemStatsSystem;

namespace BossRush
{
    internal sealed partial class ModeDRuntimeModule
    {
        #region Mode D 交互选项引用

        /// <summary>Mode D "冲下一波" 选项 GameObject</summary>
        private GameObject modeDNextWaveOption = null;

        // 注：modeDClearAllOption 和 modeDClearEmptyOption 已移至 TrashCanInteractable

        #endregion

        /// <summary>
        /// 设置路牌为 Mode D 模式（隐藏难度选项，显示 Mode D 选项）
        /// </summary>
        internal void SetupSignForModeD(BossRushSignInteractable signInteract)
        {
            try
            {
                if (signInteract == null)
                {
                    ModBehaviour.DevLog("[ModeD] SetupSignForModeD: 路牌未找到");
                    return;
                }

                // 移除原有难度选项
                signInteract.RemoveDifficultyOptions();

                // 添加 Mode D 专用选项
                AddModeDOptionsToSign(signInteract);

                ModBehaviour.DevLog("[ModeD] 路牌已设置为 Mode D 模式");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeD] [ERROR] SetupSignForModeD 失败: " + e.Message);
            }
        }

        /// <summary>
        /// 添加 Mode D 选项到路牌
        /// 注意：清空箱子选项已移至垃圾桶，此处仅添加"冲下一波"选项
        /// </summary>
        private void AddModeDOptionsToSign(BossRushSignInteractable signInteract)
        {
            try
            {
                if (signInteract == null) return;

                Transform signTransform = signInteract.transform;

                // 使用缓存的 FieldInfo 获取路牌的 otherInterablesInGroup 列表
                var field = BossRushEagerReflectionCache.InteractableBase_OtherInterablesInGroup;
                if (field == null)
                {
                    ModBehaviour.DevLog("[ModeD] [ERROR] 无法获取 otherInterablesInGroup 字段");
                    return;
                }

                var list = field.GetValue(signInteract) as List<InteractableBase>;
                if (list == null)
                {
                    list = new List<InteractableBase>();
                    field.SetValue(signInteract, list);
                }

                // 确保交互组启用
                signInteract.interactableGroup = true;

                // 添加"冲下一波"选项
                if (modeDNextWaveOption == null)
                {
                    modeDNextWaveOption = new GameObject("ModeD_NextWave");
                    modeDNextWaveOption.transform.SetParent(signTransform);
                    modeDNextWaveOption.transform.localPosition = Vector3.zero;
                    var nextWaveInteract = modeDNextWaveOption.AddComponent<ModeDNextWaveInteractable>();
                    list.Add(nextWaveInteract);
                }
                modeDNextWaveOption.SetActive(true);

                // 清空箱子选项已移至垃圾桶（TrashCanInteractable），不再添加到路牌

                ModBehaviour.DevLog("[ModeD] 已添加 Mode D 选项到路牌（清空箱子选项已移至垃圾桶），当前选项数: " + list.Count);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeD] [ERROR] AddModeDOptionsToSign 失败: " + e.Message);
            }
        }

        /// <summary>
        /// 显示 Mode D "冲下一波" 选项
        /// </summary>
        internal void ShowModeDNextWaveOption()
        {
            if (modeDNextWaveOption != null)
            {
                modeDNextWaveOption.SetActive(true);
            }
        }

        /// <summary>
        /// 隐藏 Mode D "冲下一波" 选项（波次进行中）
        /// </summary>
        internal void HideModeDNextWaveOption()
        {
            if (modeDNextWaveOption != null)
            {
                modeDNextWaveOption.SetActive(false);
            }
        }

        /// <summary>
        /// 清空所有 BossRush 产生的箱子
        /// </summary>
        internal void ClearAllBossRushLootboxes()
        {
            try
            {
                int count = BossRushLootboxUtility.DestroyMarkedLootboxes();

                ModBehaviour.DevLog("[ModeD] 已清空 " + count + " 个 BossRush 箱子");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeD] [ERROR] ClearAllBossRushLootboxes 失败: " + e.Message);
            }
        }

        /// <summary>
        /// 清空空的 BossRush 箱子（已被搜刮过的）
        /// </summary>
        internal void ClearEmptyBossRushLootboxes()
        {
            try
            {
                int count = BossRushLootboxUtility.DestroyMarkedLootboxes(int.MinValue, true);

                ModBehaviour.DevLog("[ModeD] 已清空 " + count + " 个空的 BossRush 箱子");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[ModeD] [ERROR] ClearEmptyBossRushLootboxes 失败: " + e.Message);
            }
        }
    }
}
