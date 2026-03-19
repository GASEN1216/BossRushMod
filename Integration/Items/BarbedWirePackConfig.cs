using System;
using System.Reflection;
using ItemStatsSystem;

namespace BossRush
{
    public static class BarbedWirePackConfig
    {
        public const int TYPE_ID = 500039;
        public const string BUNDLE_NAME = "barbed_wire_pack";
        public const string PREFAB_NAME = "BossRush_ModeF_BarbedWirePack";
        public const string LOC_KEY_DISPLAY = "BossRush_BarbedWirePack";
        public const string DISPLAY_NAME_CN = "阻滞铁丝网包";
        public const string DISPLAY_NAME_EN = "Barbed Wire Pack";
        public const string DESCRIPTION_CN = "一卷带刺铁丝网。使用后在面前部署阻滞铁丝网，阻挡并拖延敌人推进。";
        public const string DESCRIPTION_EN = "A roll of barbed wire. Use to deploy barbed wire in front of you to obstruct and delay enemy advances.";

        public static void ConfigureItem(Item item)
        {
            if (item == null) return;
            try
            {
                item.DisplayNameRaw = LOC_KEY_DISPLAY;
                item.MaxStackCount = 5;
                item.StackCount = 1;
                item.Value = 2200;
                item.Quality = 3;
                item.name = DISPLAY_NAME_EN;
                SetHiddenMember(item, "description", L10n.T(DESCRIPTION_CN, DESCRIPTION_EN));
                SetHiddenMember(item, "DescriptionRaw", L10n.T(DESCRIPTION_CN, DESCRIPTION_EN));
                EquipmentHelper.AddTagToItem(item, "Special");
                ModeFItemUsageHelper.AttachToItem(item);
                ModBehaviour.DevLog("[BarbedWirePackConfig] Item configured: TypeID=" + TYPE_ID);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[BarbedWirePackConfig] ConfigureItem failed: " + e.Message);
            }
        }

        public static void RegisterConfigurator()
        {
            ItemFactory.RegisterConfigurator(TYPE_ID, ConfigureItem);
        }

        private static void SetHiddenMember(object target, string memberName, object value)
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            PropertyInfo property = target.GetType().GetProperty(memberName, flags);
            if (property != null && property.SetMethod != null) { property.SetValue(target, value); return; }
            FieldInfo field = target.GetType().GetField(memberName, flags);
            if (field != null) { field.SetValue(target, value); }
        }
    }
}
