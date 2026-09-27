using System;
using System.Collections.Generic;
using System.Linq;
using BossRush;
using Duckov.Economy;
using ItemStatsSystem;
using UnityEngine;
using UObj = UnityEngine.Object;

internal static class Program
{
    private static int assertions;
    private static void Check(bool value, string name)
    { assertions++; if (!value) throw new Exception("ASSERT: " + name); }
    private static void Near(float actual, float expected, string name)
    { Check(Math.Abs(actual - expected) < 0.00001f, name + " (" + actual + " / " + expected + ")"); }
    private static T Child<T>(GameObject parent, string name) where T : Component, new()
    { GameObject child = new GameObject(name); parent.Children.Add(child); return child.AddComponent<T>(); }
    private static CharacterMainControl Character(string name = "character")
    { return new GameObject(name).AddComponent<CharacterMainControl>(); }
    private static CharacterMainControl Fighter()
    {
        CharacterMainControl c = Character();
        c.CharacterItem = new Item();
        c.CharacterItem.Stats.Add("MaxHealth".GetHashCode(), new Stat(100));
        c.CharacterItem.Stats.Add("GunDamageMultiplier".GetHashCode(), new Stat(2));
        c.CharacterItem.Stats.Add("MeleeDamageMultiplier".GetHashCode(), new Stat(3));
        c.Health = new Health { Current = 17, MaxQuery = () => c.CharacterItem.Stats["MaxHealth".GetHashCode()].BaseValue };
        AICharacterController ai = Child<AICharacterController>(c.gameObject, "AI");
        ai.baseReactionTime = 2; ai.reactionTime = 4; ai.shootDelay = 6;
        return c;
    }
    private static Stat Stat(CharacterMainControl c, string name) { return c.CharacterItem.Stats[name.GetHashCode()]; }

    private static void Scaling()
    {
        BossStatScaling.ApplyBossStatMultiplier(null, 2, 8);
        CharacterMainControl gone = Fighter(); UObj.Destroy(gone.gameObject);
        BossStatScaling.ApplyBossStatMultiplier(gone, 2, 8);
        Near(Stat(gone, "MaxHealth").BaseValue, 100, "destroyed character is ignored");
        CharacterMainControl c = Fighter();
        BossStatScaling.ApplyBossStatMultiplier(c, 1, 8);
        Near(Stat(c, "MaxHealth").BaseValue, 100, "explicit unit multiplier overrides default");
        Near(c.Health.Current, 17, "unit multiplier does not heal");
        Near(c.GetComponentInChildren<AICharacterController>().reactionTime, 4, "unit multiplier leaves AI alone");
        BossStatScaling.ApplyBossStatMultiplier(c, null, 2);
        Near(Stat(c, "MaxHealth").BaseValue, 200, "nullable multiplier uses supplied default");
        Near(c.Health.Current, 200, "heal samples scaled maximum");
        Near(Stat(c, "GunDamageMultiplier").BaseValue, 4, "default multiplier scales gun");
        Near(Stat(c, "MeleeDamageMultiplier").BaseValue, 6, "default multiplier scales melee");
        AICharacterController ai = c.GetComponentInChildren<AICharacterController>();
        Near(ai.baseReactionTime, 1, "base reaction is divided"); Near(ai.reactionTime, 2, "reaction is divided"); Near(ai.shootDelay, 3, "shoot delay is divided");
        c = Fighter(); BossStatScaling.ApplyBossStatMultiplier(c, 5, 1);
        Near(Stat(c, "MaxHealth").BaseValue, 500, "health is not damage capped");
        Near(Stat(c, "GunDamageMultiplier").BaseValue, 6, "gun multiplier caps at three");
        Near(Stat(c, "MeleeDamageMultiplier").BaseValue, 9, "melee multiplier caps at three");
        Near(c.GetComponentInChildren<AICharacterController>().shootDelay, 1.2f, "AI uses full multiplier");
        c = Fighter(); BossStatScaling.ApplyBossStatMultiplier(c, .5f, 1);
        Near(Stat(c, "MaxHealth").BaseValue, 50, "fractional multiplier preserves reduction");
        Near(c.GetComponentInChildren<AICharacterController>().reactionTime, 8, "fractional multiplier slows AI");
        c = Fighter(); Stat(c, "MaxHealth").ThrowOnWrite = true; c.Health.ThrowOnSet = true;
        c.CharacterItem.ThrowOnRead.Add("GunDamageMultiplier".GetHashCode());
        BossStatScaling.ApplyBossStatMultiplier(c, 2, 1);
        Near(Stat(c, "MeleeDamageMultiplier").BaseValue, 6, "melee continues after health and gun exceptions");
        Near(c.GetComponentInChildren<AICharacterController>().reactionTime, 2, "AI continues after earlier exceptions");
        c = Fighter(); c.ThrowOnItem = true; BossStatScaling.ApplyBossStatMultiplier(c, 2, 1); c.ThrowOnItem = false;
        Near(c.Health.Current, 17, "item lookup exception ends this operation safely");
        c = Fighter(); c.CharacterItem = null; BossStatScaling.ApplyBossStatMultiplier(c, 2, 1);
        Near(c.GetComponentInChildren<AICharacterController>().reactionTime, 4, "null item does not mutate AI");
        c = Fighter(); c.Health = null; UObj.Destroy(c.GetComponentInChildren<AICharacterController>());
        BossStatScaling.ApplyBossStatMultiplier(c, 2, 1);
        Near(Stat(c, "GunDamageMultiplier").BaseValue, 4, "missing health and AI preserve stat scaling");
    }

    private static WaitForSeconds[] WaitsA()
    { return new[] { BossRushWaitCache.sharedWait01s, BossRushWaitCache.sharedWait05s, BossRushWaitCache.sharedWait1s }; }
    private static WaitForSeconds[] WaitsB() { return WaitsA(); }
    private static void Waits()
    {
        WaitForSeconds[] a = WaitsA(), b = WaitsB();
        Check(a.Zip(b, (x, y) => ReferenceEquals(x, y)).All(x => x), "independent consumers share all three wait identities");
        Check(!ReferenceEquals(a[0], a[1]) && !ReferenceEquals(a[1], a[2]) && !ReferenceEquals(a[0], a[2]), "three durations use distinct cached objects");
        Check(a.Select(x => x.Seconds).SequenceEqual(new[] { .1f, .5f, 1f }), "wait durations preserve 0.1 0.5 and 1 seconds");
    }

    private sealed class Zombie
    {
        internal CharacterMainControl C;
        internal AICharacterController AI;
        internal SkillBase Live, Prefab;
        internal AISpecialAttachment_BoomCar Boom;
        internal Zombie()
        {
            C = Character("zombie"); AI = Child<AICharacterController>(C.gameObject, "AI");
            Live = Child<Skill_Grenade>(C.gameObject, "live-grenade");
            Prefab = new GameObject("prefab").AddComponent<Skill_Grenade>();
            AI.hasSkill = true; AI.skillInstance = Live; AI.skillPfb = Prefab; AI.CharacterMainControl = C;
            Boom = Child<AISpecialAttachment_BoomCar>(C.gameObject, "boom");
            C.characterPreset = new CharacterRandomPreset { specialAttachmentBases = new List<AISpecialAttachmentBase> { Boom } };
        }
    }
    private static void Sanitizer()
    {
        Zombie z = new Zombie();
        ZombieModeEnemyRuntimeMarker marker = z.C.gameObject.AddComponent<ZombieModeEnemyRuntimeMarker>();
        marker.EnemyKind = ZombieModeEnemyKind.Special; marker.SpecialKind = ZombieModeSpecialKind.OfficialExploder;
        Check(ZombieModeRuntimeModule.ShouldKeepBossRushZombieSelfDestructionSkill(z.C), "official nonboss exploder policy retains skill");
        int queries = 0;
        ZombieSpawnSanitizer keep = new ZombieSpawnSanitizer(c => { queries++; return ZombieModeRuntimeModule.ShouldKeepBossRushZombieSelfDestructionSkill(c); });
        keep.SanitizeBossRushZombieSpawn(z.C, "exploder");
        Check(queries == 2 && z.AI.hasSkill && !z.Live.Destroyed && !z.Boom.Destroyed, "exploder preserves both independent cleanup stages");
        marker.IsBoss = true;
        Check(!ZombieModeRuntimeModule.ShouldKeepBossRushZombieSelfDestructionSkill(z.C), "boss marker is excluded from exploder exemption");
        marker.IsBoss = false; marker.EnemyKind = ZombieModeEnemyKind.Normal;
        Check(!ZombieModeRuntimeModule.ShouldKeepBossRushZombieSelfDestructionSkill(z.C), "normal enemy is excluded from exploder exemption");
        marker.EnemyKind = ZombieModeEnemyKind.Special; marker.SpecialKind = ZombieModeSpecialKind.None;
        Check(!ZombieModeRuntimeModule.ShouldKeepBossRushZombieSelfDestructionSkill(z.C), "other special enemy is excluded from exploder exemption");
        Check(!ZombieModeRuntimeModule.ShouldKeepBossRushZombieSelfDestructionSkill(null), "null policy input is false");
        UObj.Destroy(z.C.gameObject);
        Check(!ZombieModeRuntimeModule.ShouldKeepBossRushZombieSelfDestructionSkill(z.C), "destroyed policy input is false");

        z = new Zombie(); Trace.Events.Clear();
        ZombieSpawnSanitizer remove = new ZombieSpawnSanitizer(c => false);
        remove.SanitizeBossRushZombieSpawn(z.C, "normal");
        Check(!z.AI.hasSkill && z.AI.skillInstance == null && z.AI.skillPfb == null, "AI skill references are cleared together");
        Check(z.C.ClearSkillCalls == 1 && z.Live.Destroyed && z.Live.gameObject.Destroyed, "character slot and live skill object are cleared");
        Check(!z.Prefab.Destroyed && !z.Prefab.gameObject.Destroyed, "shared skill prefab is retained");
        Check(z.Boom.Destroyed && !z.Boom.enabled, "boom attachment is disabled and destroyed");
        Check(Trace.Events.IndexOf("clear-skill") < Trace.Events.IndexOf("destroy:AISpecialAttachment_BoomCar"), "skill removal precedes attachment destruction");

        z = new Zombie(); Zombie changing = z; queries = 0; bool secondSawCleanup = false;
        new ZombieSpawnSanitizer(c => { queries++; if (queries == 2) secondSawCleanup = !changing.AI.hasSkill && changing.C.ClearSkillCalls == 1; return queries == 2; }).SanitizeBossRushZombieSpawn(z.C, "policy-changes");
        Check(secondSawCleanup, "second policy query observes completed skill cleanup");
        Check(queries == 2 && z.Live.Destroyed && !z.Boom.Destroyed, "second policy result independently protects attachment");
        z = new Zombie(); queries = 0;
        new ZombieSpawnSanitizer(c => ++queries == 1).SanitizeBossRushZombieSpawn(z.C, "inverse-policy");
        Check(z.AI.hasSkill && !z.Live.Destroyed && z.Boom.Destroyed, "first policy result only protects skill stage");
        queries = 0; new ZombieSpawnSanitizer(c => { queries++; return false; }).SanitizeBossRushZombieSpawn(null, "null");
        Check(queries == 0, "null sanitizer input never queries policy");

        z = new Zombie(); UObj.Destroy(z.AI); remove.SanitizeBossRushZombieSpawn(z.C, "no-ai");
        Check(z.Boom.Destroyed && !z.Live.Destroyed, "no AI still allows attachment cleanup");
        z = new Zombie(); z.C.characterPreset = null; remove.SanitizeBossRushZombieSpawn(z.C, "no-preset");
        Check(z.Live.Destroyed && !z.Boom.Destroyed, "no preset keeps attachment scan gated while skill clears");
        z = new Zombie(); z.AI.CharacterMainControl = null; remove.SanitizeBossRushZombieSpawn(z.C, "fallback-owner");
        Check(z.C.ClearSkillCalls == 1, "missing AI owner falls back to supplied character");
        z = new Zombie(); CharacterMainControl actualOwner = Character("different-owner"); z.AI.CharacterMainControl = actualOwner;
        remove.SanitizeBossRushZombieSpawn(z.C, "actual-owner");
        Check(actualOwner.ClearSkillCalls == 1 && z.C.ClearSkillCalls == 0, "AI owner identity is used when present");
        z = new Zombie(); z.AI.skillInstance = Child<skill_grenade>(z.C.gameObject, "case-skill"); z.AI.skillPfb = null;
        AISpecialAttachmentBase lower = Child<aispecialattachment_boomcar>(z.C.gameObject, "case-attachment");
        AISpecialAttachmentBase derived = Child<AISpecialAttachment_BoomCarVariant>(z.C.gameObject, "derived-attachment");
        remove.SanitizeBossRushZombieSpawn(z.C, "exact-names");
        Check(z.AI.hasSkill && z.AI.skillInstance != null, "skill name comparison is ordinal and exact");
        Check(!lower.Destroyed && !derived.Destroyed && z.Boom.Destroyed, "attachment type name comparison is exact without derived matches");
        z = new Zombie(); z.AI.skillInstance = Child<OtherSkill>(z.C.gameObject, "other-live"); SkillBase other = z.AI.skillInstance;
        remove.SanitizeBossRushZombieSpawn(z.C, "grenade-prefab");
        Check(other.Destroyed && !z.AI.hasSkill, "grenade prefab also triggers cleanup of current instance");
        z = new Zombie(); z.C.OnSetSkill = () => { throw new InvalidOperationException("slot"); };
        remove.SanitizeBossRushZombieSpawn(z.C, "skill-exception");
        Check(!z.AI.hasSkill && !z.Live.Destroyed && z.Boom.Destroyed, "skill callback exception is isolated from attachment stage");
        z = new Zombie(); z.Boom.ThrowOnDestroy = true; AISpecialAttachment_BoomCar next = Child<AISpecialAttachment_BoomCar>(z.C.gameObject, "second-boom");
        remove.SanitizeBossRushZombieSpawn(z.C, "attachment-exception");
        Check(!z.Boom.Destroyed && !z.Boom.enabled && next.Destroyed, "attachment destruction exception does not abort later attachment cleanup");
        z = new Zombie(); queries = 0;
        new ZombieSpawnSanitizer(c => { queries++; throw new InvalidOperationException("policy"); }).SanitizeBossRushZombieSpawn(z.C, "policy-exception");
        Check(queries == 1 && z.AI.hasSkill && !z.Boom.Destroyed, "policy exception preserves the original outer abort boundary");
    }

    private static readonly int[] ExpectedIds = { 105,648,870,871,649,612,613,615,616,698,603,604,606,607,694,594,595,597,598,691,640,708,709,710,630,631,633,634,707,621,622,700,701,702,650,1162,918,944,1262,326,23,24,67,66,942,660,933,941,1366,10,17,16,15 };
    private static void Shop()
    {
        StockShop.Constructed = null; StockShop.UiCallback = null; ItemAssetsCollection.Factory = null; ItemAssetsCollection.Requested.Clear();
        IntegrationRuntimeModule module = new IntegrationRuntimeModule(); int before = GameObject.Created.Count;
        Check(module.ProbeShop == null && GameObject.Created.Count == before, "shop owner construction is lazy");
        module.EnsureAmmoShop_Utilities(); StockShop first = module.ProbeShop;
        StockShop unrelated = new GameObject("unrelated-shop").AddComponent<StockShop>();
        Check(module.IsIntegrationAmmoShop(first) && !module.IsIntegrationAmmoShop(unrelated) && !module.IsIntegrationAmmoShop(null), "purchase routing accepts only this owner shop identity");
        Check(first != null && first.gameObject.Persistent && first.gameObject.name == "BossRush_AmmoShop", "first use creates persistent named shop");
        Check(first.MerchantID == "BossRushAmmo" && first.AccountAvaliable, "official shop identity and bank payment are configured");
        Check(first.entries.Select(e => e.Raw.typeID).SequenceEqual(ExpectedIds) && ExpectedIds.Length == 53, "ammo roster preserves all 53 IDs and order");
        Check(first.entries.All(e => e.MaxStock == 9999 && e.CurrentStock == 9999 && e.Raw.forceUnlock && !e.Raw.lockInDemo && e.Raw.priceFactor == 1.1f && e.Raw.possibility == 1f), "stock unlock and price policy remain unchanged for every entry");
        Check(first.Items.Count == 53 && ItemAssetsCollection.Requested.SequenceEqual(ExpectedIds), "item cache creates each roster item once");
        module.Item105PurchaseCount = 7; module.EnsureAmmoShop_Utilities();
        Check(ReferenceEquals(first, module.ProbeShop) && ItemAssetsCollection.Requested.Count == 53 && module.Item105PurchaseCount == 7, "ensure preserves shop identity cache and purchase count");
        List<int> seenCounters = new List<int>(); List<StockShop> seenShops = new List<StockShop>();
        StockShop.UiCallback = s => { seenCounters.Add(module.Item105PurchaseCount); seenShops.Add(s); };
        module.ShowAmmoShop(); module.Item105PurchaseCount = 3; module.ShowAmmoShop();
        Check(seenCounters.SequenceEqual(new[] { 0, 0 }) && seenShops.All(s => ReferenceEquals(s, first)), "purchase counter resets before official UI callback");
        Check(first.UiCalls == 2, "repeated opening uses one shop");
        module.CleanupAmmoShop();
        Check(first == null && first.gameObject == null && module.ProbeShop == null, "scene cleanup destroys shop GameObject and component");
        module.CleanupAmmoShop(); StockShop.UiCallback = null; module.EnsureAmmoShop_Utilities();
        Check(module.ProbeShop != null && !ReferenceEquals(first, module.ProbeShop), "next scene recreates shop after cleanup");
        StockShop external = module.ProbeShop; UObj.Destroy(external.gameObject); module.CleanupAmmoShop(); module.EnsureAmmoShop_Utilities();
        Check(module.ProbeShop != null && !ReferenceEquals(external, module.ProbeShop), "Unity destroyed-null shop is replaced");
        Check(!module.IsIntegrationAmmoShop(external), "destroyed old shop cannot route purchases to replacement");

        StockShop dying = module.ProbeShop; List<string> logs = new List<string>();
        module.CleanupAmmoShopOnPlayerDeath((key, message, error) => logs.Add(key));
        Check(dying.Destroyed && module.ProbeShop == null && logs.Count == 0, "player death destroys shop without warning on success");
        module.EnsureAmmoShop_Utilities(); dying = module.ProbeShop; dying.gameObject.ThrowOnDestroy = true;
        Exception seenError = null;
        module.CleanupAmmoShopOnPlayerDeath((key, message, error) => { seenError = error; logs.Add(key); });
        Check(seenError != null && seenError.Message == "destroy", "death cleanup forwards original destruction exception");
        Check(ReferenceEquals(module.ProbeShop, null) && logs.SequenceEqual(new[] { "OnPlayerDeathInBossRush_ammoShopDestroy" }), "death destruction warning preserves key and releases owner reference");
        module.EnsureAmmoShop_Utilities(); dying = module.ProbeShop; dying.gameObject.ThrowOnDestroy = true; logs.Clear();
        module.CleanupAmmoShopOnPlayerDeath((key, message, error) => { logs.Add(key); if (logs.Count == 1) throw new InvalidOperationException("logger"); });
        Check(logs.SequenceEqual(new[] { "OnPlayerDeathInBossRush_ammoShopDestroy", "OnPlayerDeathInBossRush_ammoShopCleanup" }) && ReferenceEquals(module.ProbeShop, dying), "inner logger failure reaches original outer cleanup boundary");
        dying.gameObject.ThrowOnDestroy = false; module.CleanupAmmoShop();

        IntegrationRuntimeModule seeded = new IntegrationRuntimeModule(); Item cached = new Item { TypeId = 105 };
        StockShop.Constructed = s => { s.entries = new List<StockShop.Entry> { new StockShop.Entry(new StockShopDatabase.ItemEntry { typeID = -1 }) }; s.Items = new Dictionary<int, Item> { { 105, cached } }; };
        ItemAssetsCollection.Requested.Clear();
        ItemAssetsCollection.Factory = id => { if (id == 648) throw new InvalidOperationException("item factory"); if (id == 870) return null; return new Item { TypeId = id }; };
        UObj.ThrowOnPersist = true; seeded.EnsureAmmoShop_Utilities(); UObj.ThrowOnPersist = false;
        Check(seeded.ProbeShop.entries.Select(e => e.Raw.typeID).SequenceEqual(ExpectedIds), "existing official entries are cleared before roster insertion");
        Check(ReferenceEquals(seeded.ProbeShop.Items[105], cached) && !ItemAssetsCollection.Requested.Contains(105), "preexisting item identity is retained");
        Check(!seeded.ProbeShop.Items.ContainsKey(648) && !seeded.ProbeShop.Items.ContainsKey(870) && seeded.ProbeShop.Items.ContainsKey(871) && seeded.ProbeShop.Items.ContainsKey(15), "per-item exception and null skip only that cache entry");
        seeded.Item105PurchaseCount = 9; StockShop.UiCallback = s => { throw new InvalidOperationException("UI"); }; seeded.ShowAmmoShop();
        Check(seeded.Item105PurchaseCount == 0 && seeded.ProbeShop.UiCalls == 1, "UI failure retains reset purchase count");
        StockShop.UiCallback = null; StockShop.Constructed = null; ItemAssetsCollection.Factory = null;
        var merchantField = BossRushEagerReflectionCache.StockShop_MerchantID;
        var accountField = BossRushEagerReflectionCache.StockShop_AccountAvaliable;
        var itemsField = BossRushEagerReflectionCache.StockShop_ItemInstances;
        try
        {
            BossRushEagerReflectionCache.StockShop_MerchantID = null; BossRushEagerReflectionCache.StockShop_AccountAvaliable = null; BossRushEagerReflectionCache.StockShop_ItemInstances = null;
            IntegrationRuntimeModule fallback = new IntegrationRuntimeModule(); fallback.ShowAmmoShop();
            Check(fallback.ProbeShop.entries.Count == 53 && fallback.ProbeShop.UiCalls == 1, "missing optional reflection fields still permit stock and UI setup");
        }
        finally
        { BossRushEagerReflectionCache.StockShop_MerchantID = merchantField; BossRushEagerReflectionCache.StockShop_AccountAvaliable = accountField; BossRushEagerReflectionCache.StockShop_ItemInstances = itemsField; }
    }

    private static int Main()
    {
        try { Scaling(); Waits(); Sanitizer(); Shop(); Console.WriteLine("HostUtilityOwners: PASS (" + assertions + " assertions)"); return 0; }
        catch (Exception error) { Console.WriteLine(error); return 1; }
    }
}
