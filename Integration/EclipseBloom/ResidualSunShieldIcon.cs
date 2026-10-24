using System;
using ItemStatsSystem;
using UnityEngine;

namespace BossRush
{
    /// <summary>只借用盾片专属包内的 Blender 512 成品图标；EquipmentFactory 统一拥有其资源。</summary>
    internal sealed class ResidualSunShieldIcon : MonoBehaviour
    {
        internal const string IconAssetPath = "Assets/Items/residual_sun_shield_icon.png";
        internal const string BundleIconAssetPath = "assets/eclipsebloom/icons/residual_sun_shield_icon.png";
        private const int Size = 512;
        private Sprite sprite;

        internal static Sprite GetSprite(Item item)
        {
            if (item == null) return null;
            DuckovItemAgent model = ResidualSunShieldModel.GetTemplate();
            if (model == null) return null;
            ResidualSunShieldIcon owner = model.GetComponent<ResidualSunShieldIcon>();
            return owner != null ? owner.EnsureSprite() : null;
        }

        private Sprite EnsureSprite()
        {
            if (sprite != null) return sprite;
            try
            {
                // 独立包里的成品不会被 production_icons 中旧的同路径草稿遮住。
                Sprite loaded = EquipmentFactory.GetLoadedEquipmentAsset<Sprite>(ResidualSunShieldModel.BundleName, BundleIconAssetPath);
                if (loaded == null || loaded.texture == null || loaded.texture.width != Size || loaded.texture.height != Size) return null;
                sprite = loaded;
                return sprite;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(ResidualSunShieldConfig.LogPrefix + " 正式图标加载失败: " + e.Message);
                return null;
            }
        }

        private void OnDestroy() { sprite = null; }
    }
}
