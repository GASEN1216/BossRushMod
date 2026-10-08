using System;
using System.Collections.Generic;
using ItemStatsSystem;

namespace UnityEngine
{
    public enum SystemLanguage { Chinese, ChineseSimplified, ChineseTraditional, English }
    public class GameObject { }
}

namespace SodaCraft.Localizations
{
    public static class LocalizationManager
    {
        public static UnityEngine.SystemLanguage CurrentLanguage;
        public static readonly Dictionary<string, string> Text = new Dictionary<string, string>(StringComparer.Ordinal);
        public static void SetOverrideText(string key, string value) { Text[key] = value; }
        public static string GetPlainText(string key) { string value; return Text.TryGetValue(key, out value) ? value : "*" + key + "*"; }
    }
}

namespace ItemStatsSystem
{
    public class Item
    {
        public int TypeID;
        public string DisplayNameRaw;
        public string DisplayName { get { return SodaCraft.Localizations.LocalizationManager.GetPlainText(DisplayNameRaw); } }
        public string Description { get { return SodaCraft.Localizations.LocalizationManager.GetPlainText(DisplayNameRaw + "_Desc"); } }
        public UnityEngine.GameObject gameObject = new UnityEngine.GameObject();
        public int ConfigureCalls;
        public bool FailStats;
    }
    public class ItemAgent { public UnityEngine.GameObject gameObject; }
}

namespace BossRush
{
    internal class ModBehaviour
    {
        public static void DevLog(string message) { }
        public void InjectReverseScaleLocalizationFromRuntimeModule() { }
    }
    internal static class PhantomWitchConfig
    {
        public const string ScytheNameCN = "噬魂挽歌";
        public const string ScytheNameEN = "Soulreaper's Requiem";
    }
    public static partial class EquipmentFactory
    {
        public static void TryGetLoadedModel(string name, out ItemAgent agent) { agent = null; }
        public static void TryBindLoadedMeleeModel(Item item, string model, string name) { }
    }
    internal static class EquipmentHelper { public static void ConfigureGemSlots(Item item, int count) { } }
    public static partial class PhantomWitchScytheWeaponConfig
    {
        private static void ConfigureStats(Item item)
        {
            item.ConfigureCalls++;
            if (item.FailStats) throw new InvalidOperationException("injected stat setup failure");
        }
        private static void ConfigureMeleeAgent(Item item, ItemAgent agent) { }
        private static void ConfigureMeleeSetting(Item item) { }
        private static void SyncMeleeSettingToAgents(Item item, ItemAgent agent) { }
        private static void ConfigureTags(Item item) { }
        private static void SyncItemValueFromRawValue(Item item) { }
        private static void DisableMotionBlur(UnityEngine.GameObject go) { }
        private static void FixModelGraphics(UnityEngine.GameObject go) { }
        private static void DisableItemRenderers(UnityEngine.GameObject go) { }
    }
}
