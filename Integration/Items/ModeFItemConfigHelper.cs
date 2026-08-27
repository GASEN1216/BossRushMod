using System.Reflection;
using ItemStatsSystem;

namespace BossRush
{
    internal static class ModeFItemConfigHelper
    {
        /// <summary>
        /// Mode F 工事包 / 应急维修喷雾这类简单消耗品的统一配置模板。
        ///
        /// 这四个物品的 ConfigureItem 此前是逐字复制的 45 行，实际差异只有
        /// MaxStackCount / Value / Quality 三个数字和日志前缀。收成一处后，
        /// 新增同类消耗品只要填参数，不用再抄一遍赋值顺序、隐藏成员写法、
        /// tag 与 Usage 挂载。
        ///
        /// 行为与此前逐字一致：赋值顺序、两个隐藏成员的写法、"Special" tag、
        /// Usage 挂载时机、成功与失败两条日志的文案都不变。
        /// </summary>
        internal static void ConfigureSimpleConsumable(
            Item item,
            string logTag,
            int typeId,
            string locKeyDisplay,
            string displayNameEn,
            string descriptionCn,
            string descriptionEn,
            int maxStackCount,
            int value,
            int quality)
        {
            if (item == null) return;
            try
            {
                item.DisplayNameRaw = locKeyDisplay;
                item.MaxStackCount = maxStackCount;
                item.StackCount = 1;
                item.Value = value;
                item.Quality = quality;
                item.name = displayNameEn;
                SetHiddenMember(item, "description", L10n.T(descriptionCn, descriptionEn));
                SetHiddenMember(item, "DescriptionRaw", L10n.T(descriptionCn, descriptionEn));
                EquipmentHelper.AddTagToItem(item, "Special");
                ModeFItemUsageHelper.AttachToItem(item);
                ModBehaviour.DevLog("[" + logTag + "] Item configured: TypeID=" + typeId);
            }
            catch (System.Exception e)
            {
                ModBehaviour.DevLog("[" + logTag + "] ConfigureItem failed: " + e.Message);
            }
        }

        internal static void SetHiddenMember(object target, string memberName, object value)
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            for (System.Type type = target.GetType(); type != null; type = type.BaseType)
            {
                PropertyInfo property = type.GetProperty(memberName, flags);
                if (property != null && property.SetMethod != null)
                {
                    property.SetValue(target, value);
                    return;
                }

                FieldInfo field = type.GetField(memberName, flags);
                if (field != null)
                {
                    field.SetValue(target, value);
                    return;
                }
            }
        }

        internal static void BindUsageUtilitiesToItem(object item, object usageUtils, float useTime)
        {
            if (item == null || usageUtils == null)
            {
                return;
            }

            SetHiddenMember(usageUtils, "master", item);
            SetHiddenMember(item, "usageUtilities", usageUtils);
            SetHiddenMember(usageUtils, "useTime", useTime);
        }
    }
}
