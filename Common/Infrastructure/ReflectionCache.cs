using System;
using System.Reflection;
using Duckov.Economy;
using Duckov.UI;
using Duckov.ItemUsage;

namespace BossRush
{
    // ============================================================================
    // ReflectionCache - 反射结果缓存（性能优化）
    // ============================================================================
    /// <summary>
    /// 反射缓存 - 存储常用的 FieldInfo 和 MethodInfo，避免重复反射调用
    /// </summary>
    internal static class ReflectionCache
    {
        // InteractableBase.otherInterablesInGroup (私有字段)
        public static readonly FieldInfo InteractableBase_OtherInterablesInGroup;

        // MultiInteraction.interactables (私有字段)
        public static readonly FieldInfo MultiInteraction_Interactables;

        // NotificationText.duration / durationIfPending (私有字段)
        public static readonly FieldInfo NotificationText_Duration;
        public static readonly FieldInfo NotificationText_DurationIfPending;

        // StockShop 私有字段
        public static readonly FieldInfo StockShop_MerchantID;
        public static readonly FieldInfo StockShop_ItemInstances;
        public static readonly FieldInfo StockShop_AccountAvaliable;

        // CharacterRandomPreset.characterIconType (私有字段)
        public static readonly FieldInfo CharacterRandomPreset_CharacterIconType;

        // NotificationText.ShowNext (静态方法)
        public static readonly MethodInfo NotificationText_ShowNext;

        // 缓存初始化标志
        public static readonly bool IsInitialized;

        static ReflectionCache()
        {
            try
            {
                const BindingFlags privateInstance = BindingFlags.NonPublic | BindingFlags.Instance;
                const BindingFlags publicStatic = BindingFlags.Public | BindingFlags.Static;

                // InteractableBase.otherInterablesInGroup
                InteractableBase_OtherInterablesInGroup = typeof(InteractableBase).GetField(
                    "otherInterablesInGroup", privateInstance);

                // MultiInteraction.interactables
                MultiInteraction_Interactables = typeof(MultiInteraction).GetField(
                    "interactables", privateInstance);

                // NotificationText 字段
                NotificationText_Duration = typeof(NotificationText).GetField(
                    "duration", privateInstance | BindingFlags.Public);
                NotificationText_DurationIfPending = typeof(NotificationText).GetField(
                    "durationIfPending", privateInstance | BindingFlags.Public);

                // StockShop 字段
                StockShop_MerchantID = typeof(StockShop).GetField(
                    "merchantID", privateInstance);
                StockShop_ItemInstances = typeof(StockShop).GetField(
                    "itemInstances", privateInstance);
                StockShop_AccountAvaliable = typeof(StockShop).GetField(
                    "accountAvaliable", privateInstance);

                // CharacterRandomPreset.characterIconType
                CharacterRandomPreset_CharacterIconType = typeof(CharacterRandomPreset).GetField(
                    "characterIconType", privateInstance);

                // NotificationText.ShowNext 方法
                NotificationText_ShowNext = typeof(NotificationText).GetMethod("ShowNext", publicStatic);

                IsInitialized = true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[BossRush] ReflectionCache 初始化异常: " + e.Message);
                IsInitialized = false;
            }
        }
    }
}
