using System;
using ItemStatsSystem;
using UnityEngine;

namespace BossRush
{
    /// <summary>只借用 EquipmentFactory 管理的 Blender 作者模型；工厂统一拥有模板、网格、材质与 bundle。</summary>
    internal static class ResidualSunShieldModel
    {
        internal const string BundleName = "residual_sun_shield_model";
        internal const string ModelBaseName = "ResidualSunShield_Totem";
        private static DuckovItemAgent template;

        internal static bool Bind(Item item)
        {
            if (item == null) return false;
            DuckovItemAgent model = GetTemplate();
            if (model == null || !EquipmentFactory.RegisterRuntimeEquipmentModel(item, model)) return false;
            // 官方 Pickup 先检查此标志；true 会绕过 ItemGraphic 强制使用平面图标。
            item.useSpriteForPickup = false;
            return item.ItemGraphic != null && item.ItemGraphic.gameObject == model.gameObject
                && item.AgentUtilities.GetPrefab("EquipmentModel") == model;
        }

        internal static DuckovItemAgent GetTemplate()
        {
            if (template != null) return template;
            try
            {
                ItemAgent loaded;
                if (!EquipmentFactory.TryGetLoadedModel(ModelBaseName, out loaded) || loaded == null)
                {
                    EquipmentFactory.LoadBundle(BundleName);
                    if (!EquipmentFactory.TryGetLoadedModel(ModelBaseName, out loaded) || loaded == null) return null;
                }
                DuckovItemAgent model = loaded as DuckovItemAgent;
                if (model == null || model.transform.Find("GroundPoint") == null
                    || model.GetComponentsInChildren<MeshFilter>(true).Length != 6) return null;
                // 工厂已创建可修改的运行时 wrapper，不能向只读 bundle prefab 添加组件。
                if (model.GetComponent<ResidualSunShieldIcon>() == null) model.gameObject.AddComponent<ResidualSunShieldIcon>();
                template = model;
                return template;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(ResidualSunShieldConfig.LogPrefix + " 作者模型加载失败: " + e.Message);
                return null;
            }
        }

        internal static void ResetStaticCaches()
        {
            // 工厂负责销毁共享 wrapper 和 Unload；此处不能提前销毁仍被掉落 / 装备实例借用的资源。
            template = null;
        }
    }
}
