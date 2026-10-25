using System;
using BossRush;
using ItemStatsSystem;
using UnityEngine.SceneManagement;

static class Program
{
    static int checks;
    static void Check(bool ok, string message) { if (!ok) throw new Exception(message); checks++; }

    static void Main()
    {
        NewWeaponRuntime.RegisterRuntimeConfigs();
        NewWeaponRuntime.RegisterRuntimeConfigs();
        foreach (int id in new[] { 500048, 500049, 500051 })
        {
            Check(CustomItemRuntimeStateHelper.IsRuntimeConfiguredType(id), "actual melee ID must be registered: " + id);
            foreach (float wear in new[] { 0f, 17f, 800f })
            {
                var item = new Item { TypeID = id, MaxDurability = 999, Durability = wear,
                    DurabilityLoss = 0.1f, SavedReforgeBonus = 35f, ReforgeRestored = true };
                Check(AffixForgeSystem.CanAffixForge(item), "restore missing components before eligibility");
                Check(item.Stats.Damage.BaseValue == 135f && item.RestoreCalls == 1,
                    "qualifying query must restore RF bonus after replacing base stats");
                Check(item.Durability == wear && item.DurabilityLoss == 0.1f,
                    "qualifying query must not repair a worn or broken weapon");
                Check(AffixForgeSystem.CanAffixForge(item) && item.ConfigureCalls == 1 && item.RestoreCalls == 1,
                    "repeat query must neither configure nor stack RF bonus");
            }
            var fresh = new Item { TypeID = id };
            Check(AffixForgeSystem.CanAffixForge(fresh) && fresh.Durability == 999f,
                "new item with no durability contract still receives its default profile");
        }
        foreach (int id in new[] { 500050, 500052, 123 })
            Check(!CustomItemRuntimeStateHelper.IsRuntimeConfiguredType(id), "utility/official IDs stay outside melee registration");
        Check(CustomItemRuntimeStateHelper.IsRuntimeConfiguredType(BossRushItemIds.AstralStaff), "Astral Staff restore registration survives integration");
        var astral = new Item { TypeID = BossRushItemIds.AstralStaff, MaxDurability = 100f, Durability = 17f,
            DurabilityLoss = 0.1f, SavedReforgeBonus = 35f, ReforgeRestored = true };
        Check(AffixForgeSystem.CanAffixForge(astral) && astral.Stats.Damage.BaseValue == 135f,
            "Astral Staff restoration preserves saved reforge gain");
        Check(astral.Durability == 17f && AffixForgeSystem.CanAffixForge(astral)
            && astral.ConfigureCalls == 1 && astral.RestoreCalls == 1,
            "Astral Staff repeated restoration neither repairs nor stacks");
        var armor = new Item { TypeID = 500054, Quality = 6 }; armor.Tags.Add("Armor");
        Check(AffixForgeSystem.CanAffixForge(armor), "armor remains forgeable without melee registration");

        CharacterMainControl.Main = new CharacterMainControl();
        var player = CharacterMainControl.Main;
        var action = new SummonStaffAction();
        var manager = new SummonStaffManager(action);
        int first = action.Start();
        action.Advance(1.3f);
        Check(!action.Running && action.Valid(first, player), "slow creation survives normal animation completion");
        action.Advance(3f);
        Check(action.Requests == 1, "normal completion does not restart async creation");
        manager.HoldChanged(new CharacterMainControl());
        Check(action.Valid(first, player), "NPC hold events cannot cancel player request");
        SummonStaffManager.Holding = false;
        manager.HoldChanged(player);
        SummonStaffManager.Holding = true;
        manager.HoldChanged(player);
        Check(!action.Valid(first, player), "switch away and back permanently invalidates the old request");
        int second = action.Start();
        Check(action.Valid(second, player) && !action.Valid(first, player), "new generation accepts only its own response");
        action.StopAction();
        Check(!action.Valid(second, player), "interrupted cast cancels pending creation");
        int third = action.Start(); action.Advance(1.3f);
        player.Health.IsDead = true;
        Check(!action.Valid(third, player), "late creation after death is rejected");
        player.Health.IsDead = false;
        SceneManager.Index = 4;
        Check(!action.Valid(third, player), "late creation after scene change is rejected");
        SceneManager.Index = 3; ModBehaviour.RuntimeActive = false;
        Check(!action.Valid(third, player), "inactive gameplay runtime rejects late creation");
        ModBehaviour.RuntimeActive = true;
        action.Destroy();
        Check(!action.Valid(third, player), "destroyed owner rejects late creation");
        var completed = new SummonStaffAction();
        int completedId = completed.Start(); completed.Complete();
        Check(!completed.Running && !completed.Valid(completedId, player), "finished batch closes its request");
        var frost = new FrostmourneAction();
        var frostManager = new FrostmourneAbilityManager(frost);
        int frostFirst = frost.Start();
        frost.Advance(1.6f);
        Check(!frost.Running && frost.Valid(frostFirst, player), "Frostmourne cold creation survives normal cast completion");
        frost.Advance(3f);
        Check(frost.Requests == 1, "Frostmourne normal completion never starts a duplicate batch");
        frostManager.HoldChanged(new CharacterMainControl());
        Check(frost.Valid(frostFirst, player), "NPC hand changes do not cancel Frostmourne");
        FrostmourneAbilityManager.Holding = false;
        Check(!frost.Valid(frostFirst, player), "unheld Frostmourne cannot accept a late spawn");
        frostManager.HoldChanged(player);
        FrostmourneAbilityManager.Holding = true;
        frostManager.HoldChanged(player);
        Check(!frost.Valid(frostFirst, player), "Frostmourne switch away and back invalidates old response");
        int frostSecond = frost.Start(); frost.StopAction();
        Check(!frost.Valid(frostSecond, player), "Frostmourne interrupted cast rejects late response");
        int frostThird = frost.Start(); frost.Advance(1.6f);
        player.Health.IsDead = true;
        Check(!frost.Valid(frostThird, player), "Frostmourne death rejects late spawn");
        player.Health.IsDead = false; SceneLoader.IsSceneLoading = true;
        Check(!frost.Valid(frostThird, player), "Frostmourne loading rejects spawn before scene-loaded callback");
        SceneLoader.IsSceneLoading = false; SceneManager.Index = 4;
        Check(!frost.Valid(frostThird, player), "Frostmourne scene change rejects spawn");
        SceneManager.Index = 3; ModBehaviour.RuntimeActive = false;
        Check(!frost.Valid(frostThird, player), "Frostmourne inactive gameplay rejects spawn");
        ModBehaviour.RuntimeActive = true;
        frost.Destroy();
        Check(!frost.Valid(frostThird, player), "Frostmourne destroyed action rejects spawn");
        var frostFinished = new FrostmourneAction();
        int frostFinishedId = frostFinished.Start(); frostFinished.Complete();
        Check(!frostFinished.Running && !frostFinished.Valid(frostFinishedId, player), "Frostmourne finished batch closes request");
        Console.WriteLine("ManualEquipmentRecovery: PASS " + checks);
    }
}
