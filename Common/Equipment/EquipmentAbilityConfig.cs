// ============================================================================
// EquipmentAbilityConfig.cs - 装备能力配置基类
// ============================================================================
// 模块说明：
//   为所有装备能力提供统一的配置结构
//   支持分层配置（基础配置 + 阶段配置）
// ============================================================================

using System;

namespace BossRush.Common.Equipment
{
    /// <summary>
    /// 装备能力配置基类 - 定义所有装备能力的通用配置
    /// </summary>
    public abstract class EquipmentAbilityConfig
    {
        // ========== 物品基础信息 ==========

        /// <summary>
        /// 物品 TypeID（唯一标识）
        /// </summary>
        public abstract int ItemTypeId { get; }

        /// <summary>
        /// 物品名称（中文）
        /// </summary>
        public abstract string DisplayNameCN { get; }

        /// <summary>
        /// 物品名称（英文）
        /// </summary>
        public abstract string DisplayNameEN { get; }

        /// <summary>
        /// 物品描述（中文）
        /// </summary>
        public abstract string DescriptionCN { get; }

        /// <summary>
        /// 物品描述（英文）
        /// </summary>
        public abstract string DescriptionEN { get; }

        /// <summary>
        /// 物品品质（1-8）
        /// </summary>
        public virtual int ItemQuality => 3;

        /// <summary>
        /// 物品标签（如 "Totem", "Accessory" 等）
        /// </summary>
        public virtual string[] ItemTags => Array.Empty<string>();

        /// <summary>
        /// 图标资源名称（AssetBundle 中的名称）
        /// </summary>
        public virtual string IconAssetName => null;

        // ========== 能力参数 ==========

        /// <summary>
        /// 能力冷却时间（秒）
        /// </summary>
        public virtual float CooldownTime => 0.5f;

        /// <summary>
        /// 起始体力消耗
        /// </summary>
        public virtual float StartupStaminaCost => 5f;

        /// <summary>
        /// 持续体力消耗（每秒）
        /// </summary>
        public virtual float StaminaDrainPerSecond => 50f;

        // ========== 音效配置 ==========

        /// <summary>
        /// 能力开始音效键
        /// </summary>
        public virtual string StartSFX => "Char/Footstep/dash";

        /// <summary>
        /// 能力循环音效键（null 表示无）
        /// </summary>
        public virtual string LoopSFX => null;

        /// <summary>
        /// 能力结束音效键（null 表示无）
        /// </summary>
        public virtual string EndSFX => null;

        // ========== 调试配置 ==========

        /// <summary>
        /// 日志输出前缀
        /// </summary>
        public virtual string LogPrefix => "[Ability]";
    }
}
