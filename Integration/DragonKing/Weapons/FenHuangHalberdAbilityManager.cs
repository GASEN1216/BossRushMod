// ============================================================================
// FenHuangHalberdAbilityManager.cs - 焚皇断界戟右键输入管理器
// ============================================================================
// 模块说明：
//   继承 EquipmentAbilityManager，监听 ADS (鼠标右键) 输入
//   当且仅当玩家手持焚皇断界戟时，拦截右键触发龙皇裂地
// ============================================================================

using BossRush.Common.Equipment;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// 焚皇断界戟右键输入管理器
    /// </summary>
    public class FenHuangHalberdAbilityManager
        : EquipmentAbilityManager<FenHuangHalberdConfig, FenHuangHalberdAction>
    {
        // ========== 配置 ==========

        private FenHuangHalberdConfig configInstance = new FenHuangHalberdConfig();

        protected override FenHuangHalberdConfig GetConfig() => configInstance;

        /// <summary>
        /// 监听 ADS 输入（鼠标右键）
        /// </summary>
        protected override string GetInputActionName()
        {
            return "ADS";
        }

        /// <summary>
        /// 创建能力动作实例
        /// </summary>
        protected override FenHuangHalberdAction CreateAbilityAction()
        {
            return actionObject.AddComponent<FenHuangHalberdAction>();
        }

        /// <summary>
        /// 备用输入检测：右键
        /// </summary>
        protected override bool IsInputPressedFallback()
        {
            return Input.GetMouseButtonDown(1);
        }

        /// <summary>
        /// 只有当前手持焚皇断界戟时才允许执行右键技能
        /// </summary>
        protected override bool OnBeforeTryExecute()
        {
            CharacterMainControl player = CharacterMainControl.Main;
            if (player == null) return false;

            ItemAgent_MeleeWeapon melee = player.GetMeleeWeapon();
            if (melee == null) return false;
            if (melee.Item == null) return false;

            return melee.Item.TypeID == FenHuangHalberdIds.WeaponTypeId;
        }

        /// <summary>
        /// 管理器初始化时设置配置
        /// </summary>
        protected override void OnManagerInitialized()
        {
            FenHuangHalberdAction.SetConfig(configInstance);
            LogIfVerbose("焚皇断界戟右键技能管理器已初始化");
        }
    }
}
