using System;
using System.Collections.Generic;

namespace BossRush
{
    internal static class EquipmentBootstrapProbe
    {
        internal static readonly List<string> Calls = new List<string>();
        internal static readonly Item Halberd = new Item();
        internal static readonly Item Frostmourne = new Item();
        internal static string ThrowAt;
        internal static bool MissingModels;

        internal static void Record(string stage)
        {
            Calls.Add(stage);
            if (stage == ThrowAt) throw new InvalidOperationException(stage);
        }

        private static void Check(bool condition, string reason)
        {
            if (!condition) throw new Exception(reason + ": " + string.Join(",", Calls));
        }

        private static void Reset(string throwAt = null, bool missingModels = false)
        {
            Calls.Clear();
            ThrowAt = throwAt;
            MissingModels = missingModels;
        }

        internal static void Run()
        {
            var module = new IntegrationRuntimeModule();
            Reset();
            module.LoadEquipmentContent();
            Check(string.Join(",", Calls) == "count,new-placeholders,set-placeholders,gun,get-halberd,configure-halberd,get-frost,configure-frost,new-weapons",
                "post-load phases retain their original order");
            int initialCount = Calls.Count;
            module.LoadEquipmentContent();
            Check(Calls.Count == initialCount * 2, "repeated bootstrap must retain the existing recovery path");

            Reset("new-placeholders");
            module.LoadEquipmentContent();
            Check(!Calls.Contains("set-placeholders") && Calls.Contains("gun") && Calls.Contains("new-weapons"),
                "placeholder failure skips the rest of that try block while later phases still run");

            Reset("configure-halberd");
            module.LoadEquipmentContent();
            Check(Calls.Contains("configure-frost") && Calls.Contains("new-weapons"),
                "one model configurator failure must not suppress other weapons");

            Reset("configure-frost");
            module.LoadEquipmentContent();
            Check(Calls.Contains("new-weapons"), "frost model failure must not suppress generic weapon configuration");

            Reset(missingModels: true);
            module.LoadEquipmentContent();
            Check(!Calls.Contains("configure-halberd") && !Calls.Contains("configure-frost") && Calls.Contains("new-weapons"),
                "missing bundle models must keep the fallback path usable");

            Reset("gun");
            bool propagated = false;
            try { module.LoadEquipmentContent(); }
            catch (InvalidOperationException) { propagated = true; }
            Check(propagated && !Calls.Contains("get-halberd"),
                "gun runtime failures keep the original outer bootstrap error boundary");
        }
    }

    public static partial class EquipmentFactory
    {
        public static int LoadedBundleCount { get { EquipmentBootstrapProbe.Record("count"); return 4; } }
    }

    internal static class ItemFactory
    {
        internal static Item GetLoadedItem(int typeId)
        {
            if (typeId == FenHuangHalberdIds.WeaponTypeId)
            {
                EquipmentBootstrapProbe.Record("get-halberd");
                return EquipmentBootstrapProbe.MissingModels ? null : EquipmentBootstrapProbe.Halberd;
            }
            if (typeId == FrostmourneIds.WeaponTypeId)
            {
                EquipmentBootstrapProbe.Record("get-frost");
                return EquipmentBootstrapProbe.MissingModels ? null : EquipmentBootstrapProbe.Frostmourne;
            }
            throw new Exception("unexpected item lookup");
        }
    }

    internal static class NewWeaponPlaceholderRegistry
    {
        internal static void EnsureAllRegistered() { EquipmentBootstrapProbe.Record("new-placeholders"); }
    }

    internal static class SetBonusPlaceholderRegistry
    {
        internal static void EnsureAllRegistered() { EquipmentBootstrapProbe.Record("set-placeholders"); }
    }

    internal static class DragonKingBossGunRuntime
    {
        internal static void InitializeRuntime() { EquipmentBootstrapProbe.Record("gun"); }
    }

    internal static class NewWeaponRuntime
    {
        internal static void ConfigureAfterLoad() { EquipmentBootstrapProbe.Record("new-weapons"); }
    }

    internal static class FenHuangHalberdIds { internal const int WeaponTypeId = 1; }
    internal static class FrostmourneIds { internal const int WeaponTypeId = 2; }

    internal static class FenHuangHalberdWeaponConfig
    {
        internal static void TryConfigure(Item item, string name)
        {
            if (!ReferenceEquals(item, EquipmentBootstrapProbe.Halberd) || name != "FenHuangHalberd")
                throw new Exception("halberd model identity or bundle name changed");
            EquipmentBootstrapProbe.Record("configure-halberd");
        }
    }

    internal static class FrostmourneWeaponConfig
    {
        internal static void TryConfigure(Item item, string name)
        {
            if (!ReferenceEquals(item, EquipmentBootstrapProbe.Frostmourne) || name != "Frostmourne")
                throw new Exception("frost model identity or bundle name changed");
            EquipmentBootstrapProbe.Record("configure-frost");
        }
    }

    internal static class ModBehaviour { internal static void DevLog(string message) { } }
}
