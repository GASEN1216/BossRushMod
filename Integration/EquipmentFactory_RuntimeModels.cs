using ItemStatsSystem;

namespace BossRush
{
    public static partial class EquipmentFactory
    {
        /// <summary>运行时绑定沿用装备工厂的官方装备、地面物品和可见性接线。</summary>
        internal static bool RegisterRuntimeEquipmentModel(Item item, ItemAgent model)
        {
            if (item == null || model == null) return false;
            EnsureModelSubVisuals(model.gameObject);
            SetAgentUtilityPrefab(item, "EquipmentModel", model);
            InjectItemGraphicForEquipment(item, model, true);
            if (item.ItemGraphic == null || item.ItemGraphic.gameObject != model.gameObject) return false;
            loadedModels[item.TypeID] = model;
            return true;
        }
    }
}
