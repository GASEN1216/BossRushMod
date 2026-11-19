// ============================================================================
// PhantomWitchScytheIds.cs - 幽灵女巫大镰常量定义
// ============================================================================
// 模块说明：
//   存放幽灵女巫大镰的 TypeID、Prefab 命名等常量（bundle 由 EquipmentFactory 按 Assets/Equipment 目录加载）。
//   与 FenHuangHalberdIds / FrostmourneIds 使用一致的命名范式。
// ============================================================================

namespace BossRush
{
    /// <summary>
    /// 幽灵女巫大镰常量定义
    /// </summary>
    public static class PhantomWitchScytheIds
    {
        /// <summary>
        /// 武器 TypeID（对齐 PhantomWitchConfig.ReservedScytheTypeId）
        /// </summary>
        public const int WeaponTypeId = 500044;

        /// <summary>
        /// 武器 Item Prefab 名称
        /// </summary>
        public const string WeaponPrefabName = "PhantomScythe_Melee_Item";

        /// <summary>
        /// 3D 模型基础名（对应 PhantomScythe_Melee_Model）
        /// </summary>
        public const string ModelBaseName = "PhantomScythe_Melee";

        /// <summary>
        /// EquipmentFactory 基础匹配名（用于模型/武器 prefab 的前缀匹配）
        /// </summary>
        public const string WeaponBaseName = "PhantomScythe";
    }
}
