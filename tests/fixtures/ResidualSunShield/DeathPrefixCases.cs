using System;
using System.Collections.Generic;
using BossRush;
using UnityEngine;

// 在同一进程执行真实死亡前缀和原样抽取的官方 OnDead；不模拟计数/入箱的执行顺序。
internal static class DeathPrefixCases
{
    internal static readonly List<string> Order = new List<string>();
    internal static int ExtraDropCalls;

    internal static void Run(Action<bool, string> expect)
    {
        LevelManager.Instance = new LevelManager();
        var main = new GameObject("main").AddComponent<CharacterMainControl>();
        CharacterMainControl.Main = main;
        var original = new CharacterRandomPreset { nameKey = "Cname_Boss_Red" };
        var runtime = new CharacterRandomPreset { nameKey = "BossRush_EclipseBloom_Name" };
        var bloom = new GameObject("bloom").AddComponent<CharacterMainControl>();
        bloom.characterPreset = runtime;
        bloom.dropBoxOnDead = true;
        var controller = bloom.gameObject.AddComponent<EclipseBloomAbilityController>();
        controller.originalPreset = original;
        bloom.BeforeCharacterSpawnLootOnDead += info => Order.Add("reward");
        Order.Clear(); ExtraDropCalls = 0;
        bloom.InvokeOfficialDeath(new DamageInfo(main));
        expect(ReferenceEquals(bloom.characterPreset, original), "prefix restored original preset before official death");
        expect(Order.Count == 3 && Order[0] == "counter:Cname_Boss_Red" && Order[1] == "reward" && Order[2] == "box",
            "official counter precedes reward preparation and corpse creation with original identity");
        expect(ExtraDropCalls == 0, "bloom skips inherited boss extra drop handlers");
        expect(runtime.nameKey == "BossRush_EclipseBloom_Name", "restoring character identity does not mutate runtime preset");

        var normal = new GameObject("normal").AddComponent<CharacterMainControl>();
        normal.characterPreset = original;
        normal.dropBoxOnDead = true;
        normal.BeforeCharacterSpawnLootOnDead += info => Order.Add("reward");
        Order.Clear(); ExtraDropCalls = 0;
        normal.InvokeOfficialDeath(new DamageInfo(main));
        expect(ExtraDropCalls == 4, "normal boss retains all four extra drop handlers");
        expect(Order.Count == 3 && Order[0] == "counter:Cname_Boss_Red" && Order[1] == "reward" && Order[2] == "box",
            "normal boss official order is unchanged");
        var incomplete = new GameObject("incomplete").AddComponent<CharacterMainControl>();
        incomplete.gameObject.AddComponent<EclipseBloomAbilityController>();
        expect(!EclipseBloomAbilityController.RestoreOfficialPresetBeforeDeath(incomplete), "uninitialized controller cannot suppress other drops");
        expect(!EclipseBloomAbilityController.RestoreOfficialPresetBeforeDeath(null), "null death identity rejected");
    }
}

public sealed partial class CharacterMainControl
{
    internal CharacterRandomPreset characterPreset;
    private bool hasHandledDeath;
    private Component characterModel;
    internal bool dropBoxOnDead;
    private object characterItem, deadLootBoxPrefab;
    private int relatedScene = -1;
    internal event Action<DamageInfo> BeforeCharacterSpawnLootOnDead;
    internal void InvokeOfficialDeath(DamageInfo info) { BossRushCharacterOnDeadPatch.Prefix(this); OnDead(info); }
}
public sealed class CharacterRandomPreset : UnityEngine.Object { public string nameKey; }
public sealed class Inventory { public bool NeedInspection; }
public sealed class InteractableLootbox
{
    public bool needInspect;
    public Inventory Inventory;
    public static InteractableLootbox CreateFromItem(object item, Vector3 position, Quaternion rotation,
        bool a, object prefab, bool main)
    { DeathPrefixCases.Order.Add("box"); return new InteractableLootbox(); }
}
public sealed class LevelConfig : UnityEngine.Object { public static LevelConfig Instance; public bool NeedInspectDeadLootBox; }
public static class SavesCounter { public static void AddKillCount(string key) { DeathPrefixCases.Order.Add("counter:" + key); } }
public static class SetActiveByPlayerDistance { public static void Unregister(GameObject value, int scene) { } }
namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class)] public sealed class HarmonyPatch : Attribute { public HarmonyPatch(Type type, string method) { } }
    [AttributeUsage(AttributeTargets.Method)] public sealed class HarmonyPrefix : Attribute { }
}
namespace BossRush
{
    internal static class SandstormChampionMinionMarker { internal static bool IsSummonedMinion(CharacterMainControl value) { return false; } }
    internal static class ModeGRuntimeGates
    {
        internal static bool IsModeGSuppressionActive;
        internal static bool IsModeGOnDeadSuppressionActive(Health value) { return false; }
    }
    internal static class ModeHDeathSuppressionRegistry
    {
        internal static bool IsSuppressionArmed;
        internal static bool IsModeHOnDeadSuppressionActive(Health value) { return false; }
    }
    internal static class PetNestDeathSuppressionRegistry
    {
        internal static bool IsSuppressionArmed;
        internal static bool IsPetNestOnDeadSuppressionActive(Health value) { return false; }
    }
    internal static class FrostmourneBlueBossDropHandler { internal static void TryHandleBlueBossDeath(CharacterMainControl value) { DeathPrefixCases.ExtraDropCalls++; } }
    internal static class PhantomWitchScytheBossDropHandler { internal static void TryHandlePhantomWitchDeath(CharacterMainControl value) { DeathPrefixCases.ExtraDropCalls++; } }
    internal static class SetBonusBossDropHandler { internal static void TryHandleSetBonusBossDeath(CharacterMainControl value) { DeathPrefixCases.ExtraDropCalls++; } }
    internal static class NewWeaponBossDropHandler { internal static void TryHandleNewWeaponBossDeath(CharacterMainControl value) { DeathPrefixCases.ExtraDropCalls++; } }
}
