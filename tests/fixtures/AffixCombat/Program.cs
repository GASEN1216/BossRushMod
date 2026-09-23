using System;
using BossRush;
using ItemStatsSystem;
using UnityEngine;

internal static class Program
{
    private static int checks, failures;
    private static void Check(bool ok, string reason)
    { checks++; if (!ok) { failures++; Console.WriteLine("FAIL " + reason); } }
    private static Item Affixed(params string[] ids)
    {
        var item = new Item { TypeID = 42 };
        foreach (string id in ids) item.Affixes.Add(new AffixSlotView { AffixId = id, Tier = 1 });
        return item;
    }
    private static CharacterMainControl Reset(params string[] ids)
    {
        AffixRuntimeService.ResetStaticCaches();
        BossRushUI.Paused = false;
        ModBehaviour.Instance = new ModBehaviour(); LevelManager.Instance = new LevelManager(); Time.time = 10f;
        CharacterMainControl.Main = new CharacterMainControl();
        CharacterMainControl.Main.Health.team = Teams.player;
        CharacterMainControl.Main.Armor = Affixed(ids);
        AffixRuntimeService.EnsureRuntime();
        return CharacterMainControl.Main;
    }
    private static void ForgePricingAndEligibility()
    {
        var item = new Item { Capacity = 3, ForgeBaseCost = 1000 };
        Check(AffixForgeSystem.GetStoneCost(item) == 3, "three empty slots cost three stones");
        Check(AffixForgeSystem.GetMoneyCost(item) == 30000, "initial fee uses reforge cost for every rolled slot");
        item.Affixes.Add(new AffixSlotView { AffixId = AffixDefinitions.Id_Lifesteal, Tier = 1, Locked = true });
        Check(AffixForgeSystem.GetStoneCost(item) == 2, "locking one slot removes exactly one stone");
        Check(AffixForgeSystem.GetMoneyCost(item) == 22000, "retained common affix adds its surcharge");
        item.Affixes[0] = new AffixSlotView { AffixId = AffixDefinitions.Id_DeathBurst, Tier = 3, Locked = true };
        Check(AffixForgeSystem.GetMoneyCost(item) == 25000, "rare affix adds more than common without compounding its tier");
        item.Affixes[0] = new AffixSlotView { AffixId = AffixDefinitions.Id_DeathBurst, Tier = 1, Locked = true };
        Check(AffixForgeSystem.GetMoneyCost(item) == 25000, "rarity surcharge is independent of affix tier");
        item.ForgeBaseCost = int.MaxValue;
        Check(AffixForgeSystem.GetMoneyCost(item) == int.MaxValue, "large base prices saturate without wrapping negative");
        var restored = new Item { TypeID = 500099, Quality = 0 };
        Check(AffixForgeSystem.CanAffixForge(restored) && restored.Setting is ItemSetting_MeleeWeapon,
            "restored custom weapon is configured before eligibility is evaluated");
        var armor = new Item(); armor.Tags.Add("Armor");
        Check(AffixForgeSystem.CanAffixForge(armor), "custom armor tag is accepted");
        var gun = new Item { Setting = new ItemSetting_Gun() }; gun.Tags.Add("Weapon");
        Check(AffixItemData.GetEquipMask(gun) == AffixEquipMask.Gun, "weapon component wins over generic weapon tag");
        var backpack = new Item(); backpack.Tags.Add("Backpack");
        Check(!AffixForgeSystem.CanAffixForge(backpack), "unsupported utility gear stays excluded");
    }

    private static CharacterMainControl Enemy(float hp = 1000f)
    {
        var e = new CharacterMainControl(); e.Health.CurrentHealth = hp; e.Health.MaxHealth = hp;
        e.Health.team = Teams.wolf; return e;
    }
    private static DamageInfo Hit(CharacterMainControl source, float damage = 10f)
    { return new DamageInfo(source) { damageValue = damage, fromWeaponItemID = 42 }; }
    private static void Main()
    {
        ForgePricingAndEligibility();
        CharacterMainControl player = Reset(AffixDefinitions.Id_DeathBurst);
        CharacterMainControl first = Enemy(1f), second = Enemy(), third = Enemy(), near = Enemy();
        ExplosionManager explosions = LevelManager.Instance.ExplosionManager;
        explosions.Initial = new[] { first.Health, second.Health, third.Health };
        explosions.Secondary = new[] { first.Health, near.Health };
        explosions.CreateExplosion(new Vector3(), 10f, Hit(player));
        Check(second.Health.Hits.Count == 1 && third.Health.Hits.Count == 1,
            "original explosion must damage every original target despite first kill");
        Check(explosions.MaxDepth == 1 && explosions.Calls == 1, "death burst must leave official explosion stack before damage");
        ModBehaviour.Instance.Advance();
        Check(near.Health.Hits.Count == 1 && explosions.Calls == 2, "death burst still damages nearby enemy next frame");
        Check(!explosions.LastCanHurtSelf && explosions.Last.isFromBuffOrEffect && explosions.Last.fromWeaponItemID == 0,
            "delayed blast retains player safety and effect attribution");
        Enemy(1).Health.Hurt(Hit(player)); BossRushUI.Paused = true; ModBehaviour.Instance.Advance();
        Check(explosions.Calls == 2 && ModBehaviour.Instance.Pending.Count == 1, "affix damage waits while game is paused");
        player.Equip(null); ModBehaviour.Instance.Advance();
        Check(explosions.Calls == 2 && ModBehaviour.Instance.Pending.Count == 0, "paused affix work cancels on unequip");
        BossRushUI.Paused = false;

        foreach (string cancellation in new[] { "unequip", "reequip", "death", "death_revive", "scene", "player", "owner", "shutdown", "destroy" })
        {
            player = Reset(AffixDefinitions.Id_DeathBurst);
            explosions = LevelManager.Instance.ExplosionManager;
            ModBehaviour host = ModBehaviour.Instance;
            Enemy(1).Health.Hurt(Hit(player));
            if (cancellation == "unequip") player.Equip(null);
            if (cancellation == "reequip") { Item armor = player.Armor; player.Equip(null); player.Equip(armor); }
            if (cancellation == "death" || cancellation == "death_revive")
            {
                player.Health.Hurt(Hit(Enemy(), 200));
                if (cancellation == "death_revive") { player.Health.IsDead = false; player.Health.CurrentHealth = 100; }
            }
            if (cancellation == "scene") { LevelManager.Instance = new LevelManager(); LevelManager.Initialize(); }
            if (cancellation == "player") CharacterMainControl.Main = new CharacterMainControl();
            if (cancellation == "owner") ModBehaviour.Instance = new ModBehaviour();
            if (cancellation == "shutdown") AffixRuntimeService.ShutdownRuntime();
            if (cancellation == "destroy") UnityEngine.Object.Destroy(player.gameObject);
            host.Advance();
            Check(explosions.Calls == 0 && LevelManager.Instance.ExplosionManager.Calls == 0,
                "obsolete delayed burst cancelled after " + cancellation);
        }

        player = Reset(AffixDefinitions.Id_DeathBurst);
        explosions = LevelManager.Instance.ExplosionManager;
        DamageInfo effect = Hit(player); effect.isFromBuffOrEffect = true;
        Enemy(1).Health.Hurt(effect); ModBehaviour.Instance.Advance();
        Check(explosions.Calls == 0, "effect kill cannot start another death burst");
        Enemy(1).Health.Hurt(Hit(Enemy())); ModBehaviour.Instance.Advance();
        Check(explosions.Calls == 0, "NPC kill cannot start player affix");
        player.Equip(null); player.Hold(Affixed(AffixDefinitions.Id_DeathBurst));
        DamageInfo otherWeapon = Hit(player); otherWeapon.fromWeaponItemID = 99;
        Enemy(1).Health.Hurt(otherWeapon); ModBehaviour.Instance.Advance();
        Check(explosions.Calls == 0, "held weapon attribution remains strict");
        Enemy(1).Health.Hurt(Hit(player)); ModBehaviour.Instance.Advance();
        Check(explosions.Calls == 1, "held affixed weapon remains playable");

        CheckTriggerFeedback();

        player = Reset(AffixDefinitions.Id_Thorns, AffixDefinitions.Id_Bulwark, AffixDefinitions.Id_DeathPact);
        CharacterMainControl attacker = Enemy(); int thornsShown = AffixTriggerFeedback.ThornsHits;
        player.Health.Hurt(Hit(attacker, 200));
        Check(attacker.Health.Hits.Count == 0 && player.Buffs.Count == 0, "lethal trailing OnHurt must not reflect or grant buffs");
        Check(AffixTriggerFeedback.ThornsHits == thornsShown, "no thorns feedback when nothing was reflected");
        Check(AffixRuntimeService.ActiveAffixCount == 0 && !AffixRuntimeTicker.Active, "death releases active affixes and drain ticker");
        player.Health.IsDead = false; player.Health.CurrentHealth = 100; LevelManager.Initialize();
        Check(AffixRuntimeService.ActiveAffixCount == 3 && AffixRuntimeTicker.Active, "initialization rebuilds living player equipment after death");
        player.Health.Hurt(Hit(attacker));
        Check(attacker.Health.Hits.Count == 1 && player.Buffs.Count == 1, "nonlethal thorns and bulwark remain usable");
        Check(AffixTriggerFeedback.ThornsHits == thornsShown + 1, "reflected thorns damage shows exactly one feedback ring");
        player.Health.CurrentHealth = 1.5f; AffixRuntimeService.TickDrain(1000);
        Check(player.Health.CurrentHealth == 1f && !player.Health.IsDead, "death pact drain remains nonlethal");
        AffixRuntimeService.ShutdownRuntime();
        Check(AffixRuntimeService.ActiveAffixCount == 0 && !AffixRuntimeTicker.Active, "shutdown releases active state");
        CheckMutators();
        CheckZombieExplosions();
        Console.WriteLine("AffixCombat: " + (checks - failures) + " PASS / " + failures + " FAIL");
        if (failures > 0) Environment.Exit(1);
    }

    // VA-24: lifesteal/overcharge feedback fires after the effect resolves and reports the real heal.
    private static void CheckTriggerFeedback()
    {
        CharacterMainControl player = Reset();
        player.Hold(Affixed(AffixDefinitions.Id_Lifesteal, AffixDefinitions.Id_Overcharge));
        player.Health.CurrentHealth = 50f;
        int lifesteals = AffixTriggerFeedback.Lifesteals, overcharges = AffixTriggerFeedback.Overcharges;
        CharacterMainControl target = Enemy();
        target.Health.Hurt(Hit(player));
        Check(AffixTriggerFeedback.Lifesteals == lifesteals + 1 && AffixTriggerFeedback.LastHealed > 0f
            && Math.Abs(AffixTriggerFeedback.LastHealed - (player.Health.CurrentHealth - 50f)) < 0.001f,
            "lifesteal feedback receives the real healed amount");
        Check(AffixTriggerFeedback.Overcharges == overcharges + 1 && target.Health.Hits.Count == 2,
            "overcharge feedback follows its extra electric hit");
        player.Health.CurrentHealth = player.Health.MaxHealth; Time.time += 1f;
        target.Health.Hurt(Hit(player));
        Check(AffixTriggerFeedback.Lifesteals == lifesteals + 2 && AffixTriggerFeedback.LastHealed == 0f,
            "full-health lifesteal reports zero so no popup is shown");
        AffixRuntimeService.ShutdownRuntime();
    }

    private static void CheckMutators()
    {
        CharacterMainControl player = Reset();
        var context = new MutatorContext { Player = player };
        MutatorManager._currentContext = context; MutatorManager.IsActive = true;
        MutatorManager.BindVolatile(context);
        Health.OnDead += MutatorManager.Emit;
        try
        {
            CharacterMainControl first = Enemy(1), second = Enemy(), near = Enemy(1);
            var explosions = LevelManager.Instance.ExplosionManager;
            explosions.Initial = new[] { first.Health, second.Health };
            explosions.Secondary = new[] { near.Health };
            explosions.CreateExplosion(new Vector3(), 10f, Hit(player));
            Check(explosions.Calls == 1 && second.Health.Hits.Count == 1, "mutator kill callback leaves original blast untouched");
            ModBehaviour.Instance.Advance();
            Check(explosions.Calls == 2 && near.Health.IsDead && ModBehaviour.Instance.Pending.Count == 0,
                "volatile remains deals one delayed blast without recursive deaths");
            Check(explosions.LastCanHurtSelf && explosions.Last.isFromBuffOrEffect,
                "volatile remains retains self-damage risk but marks effect attribution");
            DamageInfo effect = Hit(player); effect.isFromBuffOrEffect = true;
            Enemy(1).Health.Hurt(effect); ModBehaviour.Instance.Advance();
            Check(explosions.Calls == 2, "indirect effects cannot retrigger death mutators");
            Enemy(1).Health.Hurt(Hit(player));
            BossRushUI.Paused = true; ModBehaviour.Instance.Advance();
            Check(explosions.Calls == 2 && ModBehaviour.Instance.Pending.Count == 1, "mutator damage waits while paused");
            MutatorManager._currentContext = new MutatorContext { Player = player };
            ModBehaviour.Instance.Advance();
            Check(explosions.Calls == 2, "new mutator context invalidates old delayed callback");
        }
        finally { Health.OnDead -= MutatorManager.Emit; MutatorManager.IsActive = false; MutatorManager._currentContext = null; }
    }

    private static void CheckZombieExplosions()
    {
        CharacterMainControl player = Reset();
        ModBehaviour host = ModBehaviour.Instance;
        var explosions = LevelManager.Instance.ExplosionManager;
        var first = Enemy(); var second = Enemy(); var near = Enemy();
        explosions.Initial = new[] { first.Health, second.Health };
        explosions.Secondary = new[] { near.Health };
        Action<Health, DamageInfo> hurt = (health, info) => { if (!info.isFromBuffOrEffect) host.OptionExplosion(); };
        Health.OnHurt += hurt;
        try
        {
            explosions.CreateExplosion(new Vector3(), 10f, Hit(player));
            Check(second.Health.Hits.Count == 1 && explosions.Calls == 1, "zombie reward explosion leaves original blast untouched");
            Check(host.zombieModeRunState.RunOnlyObjects.Count == 2, "pending reward blasts owned by existing run registry");
            host.Advance();
            Check(explosions.Calls == 3 && explosions.MaxDepth == 1 && !explosions.LastCanHurtSelf,
                "zombie reward blasts keep damage and player safety");
            Check(host.zombieModeRunState.RunOnlyObjects.Count == 0 && host.Pending.Count == 0,
                "completed reward explosions release coroutine records");
        }
        finally { Health.OnHurt -= hurt; }

        foreach (string cancel in new[] { "cleanup", "new_run", "scene", "death", "destroy" })
        {
            player = Reset(); host = ModBehaviour.Instance; explosions = LevelManager.Instance.ExplosionManager;
            host.OptionExplosion();
            if (cancel == "cleanup")
            {
                host.zombieModeRunState.IsCleaningUp = true;
                RunScopedRegistry.ForEachReverse(host.zombieModeRunState.RunOnlyObjects, record => record.Cleanup(true));
            }
            if (cancel == "new_run") host.zombieModeRunState.RunId = 2;
            if (cancel == "scene") LevelManager.Instance = new LevelManager();
            if (cancel == "death") player.Health.IsDead = true;
            if (cancel == "destroy") UnityEngine.Object.Destroy(player.gameObject);
            host.Advance();
            Check(explosions.Calls == 0 && host.zombieModeRunState.RunOnlyObjects.Count == 0,
                "zombie delayed explosion cancelled and record removed after " + cancel);
        }

        player = Reset(); host = ModBehaviour.Instance; explosions = LevelManager.Instance.ExplosionManager;
        host.OptionExplosion(); host.Paused = true; host.Advance();
        Check(explosions.Calls == 0 && host.Pending.Count == 1, "pause holds pending damage");
        host.Paused = false; host.Advance();
        Check(explosions.Calls == 1 && host.zombieModeRunState.RunOnlyObjects.Count == 0, "resume completes pending damage once");
        for (int i = 0; i < 200; i++) { host.OptionExplosion(); host.Advance(); }
        Check(host.zombieModeRunState.RunOnlyObjects.Count == 0 && host.Pending.Count == 0,
            "sustained reward triggers do not accumulate completed coroutine records");
        host.FailScheduling = true; host.OptionExplosion();
        Check(host.zombieModeRunState.RunOnlyObjects.Count == 0, "failed scheduling rolls back run registry record");
        host.FailScheduling = false;

        explosions.Calls = 0;
        var source = Enemy();
        host.DealZombieModeExplosionAreaDamage(1, source, new Vector3(), 4, 30);
        Check(explosions.Calls == 0, "enemy death/area explosion also defers");
        host.Advance();
        Check(explosions.Calls == 1 && !explosions.LastCanHurtSelf && explosions.Last.fromCharacter == source,
            "enemy area explosion retains source and enemy-team safety");
        explosions.Fail = true;
        host.DealZombieModeExplosionAreaDamage(1, source, new Vector3(), 4, 30); host.Advance();
        Check(host.Fallbacks == 1 && host.zombieModeRunState.RunOnlyObjects.Count == 0, "failed official explosion retains original fallback");
        explosions.Fail = false;
        host.DealZombieModeExplosionAreaDamage(1, player, new Vector3(), 4, 30); host.Advance();
        Check(explosions.Last.isFromBuffOrEffect, "player aura and projectile trail retain effect attribution");
        explosions.Fail = true;
        host.DealZombieModeExplosionAreaDamage(1, player, new Vector3(), 4, 30); host.Advance();
        Check(host.Fallbacks == 1, "player-safe area skill must not fall back to hurting its owner");
        explosions.Fail = false;
        LevelManager.Instance = null;
        host.DealZombieModeExplosionAreaDamage(1, source, new Vector3(), 4, 30); host.Advance();
        Check(host.Fallbacks == 2, "missing LevelManager still uses original area fallback");
        LevelManager.Instance = new LevelManager(); explosions = LevelManager.Instance.ExplosionManager;
        explosions.Calls = 0;
        host.DoomPulse(3); Check(explosions.Calls == 0 && host.Pending.Count == 3, "doom pulse schedules three distinct blasts");
        host.Advance();
        Check(explosions.Calls == 3 && explosions.Last.damageValue == 50,
            "doom pulse count and damage remain unchanged");
        host.DoomPulse(3);
        int otherCleanups = 0;
        host.zombieModeRunState.RunOnlyObjects.Insert(1, new ZombieModeRunOnlyRecord { CleanupAction = () => otherCleanups++ });
        RunScopedRegistry.ForEachReverse(host.zombieModeRunState.RunOnlyObjects, record => record.Cleanup(true));
        Check(host.Pending.Count == 0, "run cleanup stops pending coroutines immediately");
        host.zombieModeRunState.RunOnlyObjects.Clear(); host.Advance();
        Check(otherCleanups == 1 && explosions.Calls == 3 && host.Pending.Count == 0,
            "real reverse cleanup cancels all blasts without skipping unrelated records");
        int points = 0; bool geometry = true;
        ZombieModeRuntimeModule.TriggerDoomPulse(player, 3, (point, radius, damage) =>
        { points++; geometry &= Math.Abs(point.sqrMagnitude - 4f) < .001f && radius == 2.75f && damage == 50f; });
        Check(points == 3 && geometry, "extracted pulse geometry retains three equally distant origins");
    }
}
