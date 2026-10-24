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

        /// <summary>只借用工厂已经拥有的资源，不创建另一份 bundle 或改变资源生命周期。</summary>
        internal static T GetLoadedEquipmentAsset<T>(string bundleName, string assetPath) where T : UnityEngine.Object
        {
            if (string.IsNullOrEmpty(bundleName) || string.IsNullOrEmpty(assetPath)) return null;
            foreach (UnityEngine.AssetBundle bundle in ownedBundles)
                if (bundle != null && string.Equals(bundle.name, bundleName, System.StringComparison.OrdinalIgnoreCase))
                    return bundle.LoadAsset<T>(assetPath);
            return null;
        }
    }
}
