using System;
using ItemStatsSystem;

namespace BossRush
{
    /// <summary>克隆图腾槽位数据后绑定独立盾片模型与图标。</summary>
    public static class ResidualSunShieldWeaponConfig
    {
        private const string LocKey = "BossRush_ResidualSunShield";
        private static readonly NewWeaponTotemSpec Spec = new NewWeaponTotemSpec
        {
            TypeId = BossRushItemIds.ResidualSunShield,
            BaseName = ResidualSunShieldConfig.BaseName,
            ModelBaseName = ResidualSunShieldModel.ModelBaseName,
            LogPrefix = ResidualSunShieldConfig.LogPrefix,
            DisplayLabelCN = ResidualSunShieldConfig.DisplayNameCN,
            DisplayNameCN = ResidualSunShieldConfig.DisplayNameCN,
            DisplayNameEN = ResidualSunShieldConfig.DisplayNameEN,
            DescriptionCN = ResidualSunShieldConfig.DescriptionCN,
            DescriptionEN = ResidualSunShieldConfig.DescriptionEN
        };

        public static void RegisterEquipmentConfigurator()
        {
            EquipmentFactory.RegisterConfigurator("ResidualSunShieldWeaponConfig",
                (item, baseName) => { TryConfigure(item, baseName); });
        }

        public static bool TryConfigure(Item item, string baseName)
        {
            if (!NewWeaponConfiguratorCore.ConfigureTotem(item, baseName, Spec)) return false;
            return Configure(item);
        }

        public static bool TryConfigure(Item item)
        {
            return TryConfigure(item, ResidualSunShieldConfig.BaseName);
        }

        public static bool Configure(Item item)
        {
            if (item == null || item.TypeID != BossRushItemIds.ResidualSunShield) return false;
            try
            {
                EquipmentHelper.AddTagToItem(item, "Totem");
                EquipmentHelper.AddTagToItem(item, "DontDropOnDeadInSlot");
                EquipmentHelper.AddTagToItem(item, "Special");
                item.DisplayNameRaw = LocKey;
                item.Quality = ResidualSunShieldConfig.ItemQuality;
                item.Value = ResidualSunShieldConfig.ItemValue;
                item.MaxStackCount = 1;
                if (item.StackCount <= 0) item.StackCount = 1;
                item.MaxDurability = 0f;
                ModeFItemConfigHelper.SetHiddenMember(item, "weight", ResidualSunShieldConfig.ItemWeight);
                ModeFItemConfigHelper.ClearInheritedUsage(item);
                if (!ResidualSunShieldModel.Bind(item)) return false;
                UnityEngine.Sprite icon = ResidualSunShieldIcon.GetSprite(item);
                if (icon == null) return false;
                item.Icon = icon;
                InjectLocalization();
                return true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(ResidualSunShieldConfig.LogPrefix + " 配置失败: " + e.Message);
                return false;
            }
        }

        public static void InjectLocalization()
        {
            string name = L10n.T(ResidualSunShieldConfig.DisplayNameCN, ResidualSunShieldConfig.DisplayNameEN);
            string description = L10n.T(ResidualSunShieldConfig.DescriptionCN, ResidualSunShieldConfig.DescriptionEN);
            LocalizationHelper.InjectLocalization(LocKey, name);
            LocalizationHelper.InjectLocalization(LocKey + "_Desc", description);
            LocalizationHelper.InjectLocalization("Item_" + BossRushItemIds.ResidualSunShield, name);
            LocalizationHelper.InjectLocalization("Item_" + BossRushItemIds.ResidualSunShield + "_Desc", description);
        }
    }
}
