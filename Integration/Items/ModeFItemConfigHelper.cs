using System.Reflection;

namespace BossRush
{
    internal static class ModeFItemConfigHelper
    {
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
