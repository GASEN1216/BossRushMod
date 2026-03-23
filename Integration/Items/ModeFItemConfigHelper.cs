using System.Reflection;

namespace BossRush
{
    internal static class ModeFItemConfigHelper
    {
        internal static void SetHiddenMember(object target, string memberName, object value)
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            PropertyInfo property = target.GetType().GetProperty(memberName, flags);
            if (property != null && property.SetMethod != null) { property.SetValue(target, value); return; }
            FieldInfo field = target.GetType().GetField(memberName, flags);
            if (field != null) { field.SetValue(target, value); }
        }
    }
}
