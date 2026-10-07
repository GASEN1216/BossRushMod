using System;
using System.IO;
using System.Threading.Tasks;
using BossRush;
using BossRush.Utils;
using ItemStatsSystem;
using UnityEngine;

internal static class Program
{
    private static int checks;
    private static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    private static void Sequence(params string[] expected)
    { Check(string.Join("|", Probe.Events) == string.Join("|", expected), "sequence: " + string.Join("|", Probe.Events)); }
    private static void Ordered(params string[] expected)
    {
        int position = 0;
        foreach (string name in expected)
        { int found = Probe.Events.FindIndex(position, e => e == name); Check(found >= 0, "missing/order: " + name); position = found + 1; }
    }
    private static void Reset()
    {
        Probe.Events.Clear(); NPCAssetBundleHelper.CanLoad = true; AffinityManager.Married = false;
        NPCSpawnConfig.Configured = true; NPCSpawnConfig.Avoided = true; GameObject.ThrowOnComponent = null;
        ModBehaviour.Shared = new[] { new Vector3(3, 0, 4) }; UnityEngine.Random.Calls = 0;
        ItemAssetsCollection.Registered.Clear(); ItemAssetsCollection.ThrowAdd = false;
        IntegrationRuntimeModule.BossRushTicketTypeId = -1; IntegrationRuntimeModule.DynamicItemsInitialized = false;
        ResourceBundleLoader.CanLoad = true; ResourceBundleLoader.Assets = new UnityEngine.Object[0];
        CharacterMainControl.Main = new CharacterMainControl(); MutatorManager.Throw = false;
    }
    private static void Nurse()
    {
        Reset(); var owner = new ModBehaviour();
        AffinityManager.Married = true; owner.SpawnNurseNPC(); Sequence("decay", "married");
        Reset(); NPCAssetBundleHelper.CanLoad = false; owner.SpawnNurseNPC(); Sequence("decay", "married", "load-nurse");
        Check(NurseNpcRuntimeModule.GetPrefabForRuntime() == null, "failed nurse loader exposed prefab");
        Reset(); NPCSpawnConfig.Configured = false; owner.SpawnNurseNPC(); Sequence("decay", "married", "load-nurse", "scene-gate");
        Reset(); NPCSpawnConfig.Avoided = false; owner.SpawnNurseNPC(); Check(!owner.IsNurseSpawned() && UnityEngine.Random.Calls == 0, "crowded configured points consumed fallback random");
        Reset(); ModBehaviour.Shared = new Vector3[0]; owner.SpawnNurseNPC();
        Check(owner.IsNurseSpawned() && UnityEngine.Random.Calls == 1, "unconfigured nurse fallback random count");
        var oldController = owner.GetNurseController(); UnityEngine.Object.Destroy(owner.Nurse);
        Check(!owner.IsNurseSpawned() && oldController == null, "destroyed nurse did not destroy components");
        Reset(); var courier = new GameObject(); courier.transform.position = new Vector3(1, 0, 2);
        var goblin = new GameObject(); goblin.transform.position = new Vector3(5, 0, 6); owner.SetNeighbors(courier, goblin);
        owner.SpawnNurseNPC(null, true);
        Ordered("decay", "married", "load-nurse", "scene-gate", "raycast", "instantiate-nurse", "active", "shaders", "layer", "component:NurseNPCController", "component:NurseMovement", "scene", "stop", "component:NurseInteractable");
        Check(NPCSpawnConfig.AvoidPositions[0] == courier.transform.position && NPCSpawnConfig.AvoidPositions[1] == goblin.transform.position, "neighbor query lost avoidance");
        Check(owner.Nurse.transform.position == new Vector3(8, .1f, 9), "nurse raycast correction changed");
        Check(owner.Nurse.GetComponent<NurseMovement>().Stopped && !owner.Nurse.GetComponent<NurseMovement>().enabled, "wedding stationary mode changed");
        var first = owner.Nurse; Probe.Events.Clear(); owner.SpawnNurseNPC(); Sequence("decay", "married"); Check(owner.Nurse == first, "duplicate nurse spawned");
        var other = new ModBehaviour(); other.SpawnNurseNPC(new Vector3(2, 0, 3), false, true);
        Check(other.Nurse != first && other.GetNurseController() != owner.GetNurseController(), "nurse owner state was shared");
        Check(ReferenceEquals(NurseNpcRuntimeModule.GetPrefabForRuntime(), NurseNpcRuntimeModule.GetPrefabForRuntime()), "nurse static prefab cache changed");
        owner.ShutdownNurse(); Check(owner.Nurse == first, "runtime shutdown reordered NPC registry cleanup");
        owner.DestroyNurseNPC(); Check(!owner.IsNurseSpawned() && owner.GetNurseController() == null && other.IsNurseSpawned(), "nurse destroy crossed owners");
        Reset(); var broken = new ModBehaviour(); GameObject.ThrowOnComponent = "NurseInteractable"; broken.SpawnNurseNPC();
        Check(!broken.IsNurseSpawned() && broken.GetNurseController() == null, "failed spawn leaked nurse/controller");
    }
    private static GameObject Prefab(int id)
    { var go = new GameObject(); go.AddComponent<Item>().TypeID = id; return go; }
    private static void Ticket()
    {
        string assets = Path.Combine(Path.GetDirectoryName(typeof(ModBehaviour).Assembly.Location), "Assets");
        Directory.CreateDirectory(assets); File.WriteAllText(Path.Combine(assets, "bossrush_ticket"), "fixture marker");
        Reset(); var owner = new ModBehaviour(); ItemAssetsCollection.Registered.Add(BossRushItemIds.BossRushTicket);
        Check(owner.EnsureBossRushTicketItemRegisteredForDynamicRegistry() && IntegrationRuntimeModule.BossRushTicketTypeId == 500001, "ticket registered fast path lost published ID"); Sequence();
        Check(BossRushMapSelectionHelper.GetBossRushTicketTypeId() == 500001, "map ticket lookup lost registered runtime owner ID");
        var cost = BossRushMapSelectionHelper.CreateBossRushCost();
        Check(cost.money == 0L && cost.items.Length == 1 && cost.items[0].id == 500001 && cost.items[0].amount == 1L, "map cost must charge one registered ticket and no cash");
        Reset(); ResourceBundleLoader.CanLoad = false;
        Check(BossRushMapSelectionHelper.GetBossRushTicketTypeId() == BossRushItemIds.BossRushTicket, "unregistered map ticket must use the published content ID");
        Check(BossRushMapSelectionHelper.CreateBossRushCost().items[0].id == BossRushItemIds.BossRushTicket,
            "unregistered map cost must not charge the old template item");
        Check(!owner.EnsureBossRushTicketItemRegisteredForDynamicRegistry(), "failed ticket load returned true"); Sequence("load-ticket", "unload-null");
        Reset(); ResourceBundleLoader.Assets = new UnityEngine.Object[] { null, new AssetBundle(), new GameObject(), Prefab(91), Prefab(500001), Prefab(92) }; Probe.Events.Clear();
        Check(owner.EnsureBossRushTicketItemRegisteredForDynamicRegistry(), "ticket target not registered");
        Sequence("load-ticket", "tags:91:Key,SpecialKey", "register:91", "tags:500001:Key,SpecialKey", "register:500001", "tags:92:Key,SpecialKey", "register:92", "unload-ticket");
        Check(IntegrationRuntimeModule.BossRushTicketTypeId == 500001, "later non-target overwrote published ticket ID");
        Reset(); ResourceBundleLoader.Assets = new UnityEngine.Object[] { Prefab(0), Prefab(42), Prefab(43) }; Probe.Events.Clear();
        Check(!owner.EnsureBossRushTicketItemRegisteredForDynamicRegistry() && IntegrationRuntimeModule.BossRushTicketTypeId == 42, "first positive fallback ID changed");
        Check(BossRushMapSelectionHelper.CreateBossRushCost().items[0].id == 42, "map cost ignored runtime fallback prefab ID");
        IntegrationRuntimeModule.BossRushTicketTypeId = 500001;
        Check(BossRushMapSelectionHelper.CreateBossRushCost().items[0].id == 500001, "map cost cached a stale registration ID");
        Reset(); ResourceBundleLoader.Assets = new UnityEngine.Object[] { Prefab(500001) }; ItemAssetsCollection.ThrowAdd = true; Probe.Events.Clear();
        Check(!owner.EnsureBossRushTicketItemRegisteredForDynamicRegistry(), "ticket registration failure escaped"); Ordered("load-ticket", "register:500001", "unload-ticket");
        Reset(); ResourceBundleLoader.CanLoad = false; new IntegrationRuntimeModule().InitializeDynamicItems_Integration();
        Ordered("load-ticket", "unload-null", "configurators", "loaded-count", "awen", "invitation", "beacon", "safe-zone", "peace");
        Probe.Events.Clear(); new IntegrationRuntimeModule().InitializeDynamicItems_Integration(); Sequence();
        Check(IntegrationRuntimeModule.DynamicItemsInitialized, "dynamic registration latch lost static lifetime");
        owner.EnsureItemContentConfiguratorsRegisteredForDynamicRegistry(); owner.EnsureBirthdayCakeItemRegisteredForDynamicRegistry(); owner.EnsureAdventureJournalItemRegisteredForDynamicRegistry(); Sequence("configurators", "cake", "journal");
    }
    private static void Mutators()
    {
        Reset(); var owner = new ModBehaviour(); owner.config = null; owner.TryRollMutatorsForMode("ModeF"); Sequence();
        owner.config = new ModBehaviour.Config { enableMutators = false }; owner.TryRollMutatorsForMode("ModeF"); Sequence();
        owner.config.enableMutators = true; CharacterMainControl.Main = null; owner.TryRollMutatorsForMode("ModeF"); Sequence();
        CharacterMainControl.Main = new CharacterMainControl();
        foreach (int count in new[] { -5, 3, 99 })
        {
            Probe.Events.Clear(); owner.config.mutatorCount = count; owner.TryRollMutatorsForMode("ModeF"); Sequence("roll", "banner");
            Check(MutatorManager.Count == Math.Min(10, Math.Max(1, count)) && MutatorManager.Mode == "ModeF", "mutator range/mode changed");
        }
        Probe.Events.Clear(); MutatorManager.Throw = true; owner.TryRollMutatorsForMode(null); Sequence("roll");
        Probe.Events.Clear(); owner.ClearMutatorsForMode("ModeD"); Sequence("remove");
        Probe.Events.Clear(); MutatorManager.Throw = false; owner.ClearMutatorsForMode("ModeD"); Sequence("remove", "hide");
    }
    private static async Task Managed()
    {
        Reset(); var owner = new ModBehaviour(); ModeGRunContext.Current = new ModeGRunState();
        Check(ModeGManagedCharacterService.IsManagedOwnerValid(null), "null managed context owner predicate changed");
        Check(!ModeGManagedCharacterService.IsManagedOwnerValid(new ManagedBossSpawnContext { IsOwnerValid = () => { throw new Exception(); } }), "owner predicate exception must reject");
        var context = new ManagedBossSpawnContext(); var preset = new CharacterRandomPreset(); CharacterMainControl created = null;
        CharacterRandomPreset.Factory = staging =>
        {
            Check(staging.team == Teams.middle && !staging.dropBoxOnDead && !staging.setActiveByPlayerDistance && staging.canDieIfNotRaidMap && staging.exp == 0, "managed staging protections changed");
            created = new CharacterMainControl(); return Task.FromResult(created);
        };
        var character = await ModeGManagedCharacterService.CreateModeGManagedCharacterAsync(preset, Vector3.zero, context, "runtime-key", "runtime-preset");
        Check(character == created && character.Health.Invincible && !character.gameObject.activeSelf, "managed prepare did not freeze");
        Check(character.characterPreset.team == Teams.wolf && !character.characterPreset.dropBoxOnDead && character.CharacterItem.Exp == 77 && character.characterPreset.nameKey == "runtime-key", "managed configured preset changed");
        Check(ModeGRunContext.Current.Presets.Count == 0 && ModeGRunContext.Current.Bosses.Count == 1, "managed staging lifetime changed");
        Ordered("stage-preset", "factory", "stage-boss", "invincible", "inactive", "exp", "unstage-preset", "destroy-preset");
        Probe.Events.Clear(); ModeGManagedCharacterService.ActivateModeGManagedCharacter(owner, character); Sequence("active", "team", "aggro", "healthbar", "vulnerable");
        Probe.Events.Clear(); ModeGManagedCharacterService.CleanupModeGManagedCharacter(owner, character, "runtime-key", "runtime-preset", "test");
        Sequence("unstage-boss", "untrack-boss", "recovery", "random-loot", "lootbox", "cleanup-preset", "destroy-preset", "destroy-object");
        Check(character.gameObject == null && ModeGRunContext.Current.Bosses.Count == 0, "managed cleanup leaked object/staging identity");
        Check(character == null && character.Health == null, "managed game object destruction did not destroy its components");
        Probe.Events.Clear(); ModeGManagedCharacterService.CleanupModeGManagedCharacter(owner, character, "runtime-key", "runtime-preset", "test"); Sequence();
        foreach (int fault in new[] { 0, 1, 2, 3 })
        {
            Probe.Events.Clear(); ModeGRunContext.Current = new ModeGRunState { AcceptPreset = fault != 0, AcceptBoss = fault != 1 };
            created = null;
            CharacterRandomPreset.Factory = staging =>
            {
                created = new CharacterMainControl();
                if (fault == 2) created.BuffManager.Buffs.Add(new Buff { fromWho = CharacterMainControl.Main });
                if (fault == 3) created.ThrowBuffRead = true;
                return Task.FromResult(created);
            };
            Check(await ModeGManagedCharacterService.CreateModeGManagedCharacterAsync(preset, Vector3.zero, context, "key", "name") == null, "managed failure returned prepared character: " + fault);
            Check(ModeGRunContext.Current.Presets.Count == 0 && ModeGRunContext.Current.Bosses.Count == 0, "failed managed prepare leaked registration: " + fault);
            Check(created == null || created.gameObject == null, "failed managed prepare leaked object: " + fault);
        }
        Probe.Events.Clear(); ModeGRunContext.Current = new ModeGRunState(); bool valid = true;
        context = new ManagedBossSpawnContext { IsOwnerValid = () => valid };
        var pending = new TaskCompletionSource<CharacterMainControl>(); CharacterRandomPreset.Factory = staging => pending.Task;
        var task = ModeGManagedCharacterService.CreateModeGManagedCharacterAsync(preset, Vector3.zero, context, "key", "name").AsTask();
        Check(!task.IsCompleted && ModeGRunContext.Current.Presets.Count == 1, "managed await/staging registration disappeared");
        valid = false; created = new CharacterMainControl(); pending.SetResult(created);
        Check(await task == null && created.gameObject == null && ModeGRunContext.Current.Bosses.Count == 0, "owner invalidation after await did not clean up");
    }
    private static async Task Main()
    {
        Nurse(); Ticket(); Mutators(); await Managed();
        Console.WriteLine("IntegrationLeafOwners: PASS (" + checks + " assertions)");
    }
}
