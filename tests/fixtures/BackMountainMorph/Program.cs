using System;
using System.Linq;
using System.Reflection;
using BossRush;
using ItemStatsSystem;
using ItemStatsSystem.Stats;
using UnityEngine;
using UnityEngine.SceneManagement;
using UObject = UnityEngine.Object;

internal static class FixtureWorld
{
    internal const int PlayerWeapon = 102;
    internal static CharacterMainControl Player;
    internal static ModBehaviour Owner;
    internal static Item OriginalItem;
    internal static Item[] OriginalEquipment;
    internal static Health OriginalHealth;
    internal static CapsuleCollider Body, Receiver;
    internal static readonly int[] Fruits = { 500065, 500066, 500067 };

    internal static CharacterModel MakeModel(string key)
    {
        var model = new GameObject("model-" + key).AddComponent<CharacterModel>();
        model.SourcePreset = key;
        model.HelmatSocket = Socket(model, "helmet");
        model.ArmorSocket = Socket(model, "armor");
        model.RightHandSocket = Socket(model, "right-hand");
        model.LeftHandSocket = Socket(model, "left-hand");
        model.gameObject.AddComponent<Renderer>();
        return model;
    }
    private static Transform Socket(CharacterModel model, string name)
    { var go = new GameObject(name); go.transform.SetParent(model.transform); return go.transform; }
    internal static Item Item(int id, int count = 1)
    {
        var item = new GameObject("item-" + id).AddComponent<Item>();
        item.TypeID = id; item.StackCount = count; return item;
    }
    private static void Prefab(int id, bool weapon)
    {
        Item item = Item(id);
        item.AgentUtilities = new AgentUtilities();
        var go = new GameObject("costume-" + id);
        var agent = go.AddComponent<DuckovItemAgent>();
        agent.SourceTypeID = id;
        agent.AgentType = weapon ? ItemAgent.AgentTypes.handheld : ItemAgent.AgentTypes.equipment;
        go.AddComponent<Renderer>(); go.AddComponent<Collider>();
        item.AgentUtilities.Prefabs[weapon ? "Handheld".GetHashCode() : CharacterEquipmentController.equipmentModelHash] = agent;
        ItemAssetsCollection.Prefabs[id] = item;
    }
    internal static void Reset()
    {
        BackMountainBossMorphService.Clear();
        foreach (var go in GameObject.All.ToArray()) UObject.Destroy(go);
        GameObject.All.Clear();
        Program.Check(CharacterMainControl.EquipmentSubscribers == 0, "previous world has no global subscribers");
        ItemAssetsCollection.Prefabs.Clear();
        Physics.Hits.Clear(); Physics.Blocked.Clear(); Physics.Overlaps = Physics.Lines = 0;
        Time.time = Time.deltaTime = 0;
        BossRushUI.Paused = false; SceneLoader.IsSceneLoading = false; SceneManager.Handle = 1;
        DragonKingFxShared.Colors.Clear();
        PhantomWitchScytheWeaponConfig.Prepared.Clear(); PhantomWitchScytheWeaponConfig.AllPreparedSafely = true;
        PhantomWitchScytheSwingFx.Calls = 0;
        Duckov.UI.NotificationText.Messages.Clear();
        LevelManager.Instance = new LevelManager { IsBaseLevel = false };
        Owner = new GameObject("owner").AddComponent<ModBehaviour>(); Owner.ConfiguredEnabled = true; ModBehaviour.Instance = Owner;
        Player = new GameObject("player").AddComponent<CharacterMainControl>(); CharacterMainControl.Main = Player;
        Player.Team = Teams.player; Player.CurrentAimDirection = Vector3.forward;
        Player.CharacterItem = OriginalItem = Item(0);
        foreach (var stat in new[] { "GunDamageMultiplier", "MeleeDamageMultiplier", "RunSpeed", "WalkSpeed", "ElementFactor_Fire", "MaxHealth" })
            OriginalItem.Stats[stat] = new Stat { BaseValue = stat == "MaxHealth" ? 123 : 1 };
        // Nonzero preexisting fire modifiers catch an incorrect PercentageAdd immunity implementation.
        OriginalItem.Stats["ElementFactor_Fire"].Modifiers.Add(new Modifier(ModifierType.PercentageAdd, 0.5f, "equipment"));
        OriginalEquipment = new[] { Item(101), Item(PlayerWeapon), Item(103) };
        OriginalItem.Equipment.AddRange(OriginalEquipment);
        Player.Health = OriginalHealth = Player.gameObject.AddComponent<Health>();
        Body = Player.gameObject.AddComponent<CapsuleCollider>();
        var damageGo = new GameObject("damage-receiver"); damageGo.transform.SetParent(Player.transform);
        Player.mainDamageReceiver = damageGo.AddComponent<DamageReceiver>(); Receiver = damageGo.AddComponent<CapsuleCollider>();
        Player.SetCharacterModel(null);
        Body.radius = 0.31f; Body.height = 1.2f; Body.center = new Vector3(0.1f, 0.2f, 0.3f); Body.enabled = true;
        Receiver.radius = 0.35f; Receiver.height = 1.1f; Receiver.center = new Vector3(0.4f, 0.5f, 0.6f); Receiver.enabled = true;
        ObjectCache.Presets = new[]
        {
            new CharacterRandomPreset { nameKey = "Cname_Boss_Red", CharacterModel = MakeModel("Cname_Boss_Red") },
            new CharacterRandomPreset { nameKey = "Cname_Ghost", CharacterModel = MakeModel("Cname_Ghost") }
        };
        foreach (int id in new[] { 500003, 500004, 500011, 500012 }) Prefab(id, false);
        Prefab(500044, true);
    }
    internal static void Tick(float seconds)
    {
        Time.deltaTime = seconds; // Keep nonzero even when paused to exercise the production pause gate.
        if (!BossRushUI.Paused) Time.time += seconds;
        foreach (var go in GameObject.All.ToArray())
        {
            if (go == null || !go.activeInHierarchy) continue;
            var runtime = go.GetComponent<BackMountainBossMorphRuntime>();
            if (runtime != null) typeof(BackMountainBossMorphRuntime).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(runtime, null);
        }
    }
    internal static CharacterMainControl Target(string label, Vector3 at, Teams team = Teams.enemy)
    {
        var target = new GameObject(label).AddComponent<CharacterMainControl>();
        target.transform.position = at; target.Team = team;
        target.Health = target.gameObject.AddComponent<Health>(); target.Health.CurrentHealth = 5000;
        Collider(target); return target;
    }
    internal static Collider Collider(CharacterMainControl target)
    {
        var go = new GameObject("target-collider"); go.transform.SetParent(target.transform);
        var collider = go.AddComponent<Collider>(); Physics.Hits.Add(collider); return collider;
    }
    internal static ItemAgent[] Costumes()
    { return Player.characterModel.GetComponentsInChildren<ItemAgent>(true).Where(a => a.SourceTypeID >= 500000).ToArray(); }
    internal static RaidMealUsageBehavior Usage() { return new GameObject("usage").AddComponent<RaidMealUsageBehavior>(); }
}

internal static class Program
{
    private static int checks;
    internal static void Check(bool ok, string label)
    { checks++; if (!ok) throw new InvalidOperationException("ASSERT: " + label); }
    private static void Near(float got, float expected, string label)
    { Check(Math.Abs(got - expected) < 0.0001f, label + " (got " + got + ", expected " + expected + ")"); }
    private static void Vector(Vector3 got, Vector3 expected, string label)
    { Near((got - expected).sqrMagnitude, 0, label); }
    private static void Preserved(string label)
    {
        var p = FixtureWorld.Player;
        Check(ReferenceEquals(p.CharacterItem, FixtureWorld.OriginalItem), label + " character item identity");
        Check(p.CharacterItem.Equipment.SequenceEqual(FixtureWorld.OriginalEquipment), label + " original equipment identities and order");
        Check(FixtureWorld.OriginalEquipment.All(i => i != null), label + " equipment not destroyed");
        Check(ReferenceEquals(p.Health, FixtureWorld.OriginalHealth), label + " health identity");
        Near(p.Health.CurrentHealth, 73, label + " current health"); Near(p.Health.MaxHealth, 123, label + " max health");
        Near(p.CharacterItem.GetStat("MaxHealth").Value, 123, label + " max health stat");
        Check(p.Health.SetHealthCalls == 0, label + " never reset health");
        Collision(label);
    }
    private static void Collision(string label)
    {
        Near(FixtureWorld.Body.radius, 0.31f, label + " body radius"); Near(FixtureWorld.Body.height, 1.2f, label + " body height");
        Vector(FixtureWorld.Body.center, new Vector3(0.1f, 0.2f, 0.3f), label + " body center");
        Check(FixtureWorld.Body.enabled, label + " body still enabled");
        Near(FixtureWorld.Receiver.radius, 0.35f, label + " damage radius"); Near(FixtureWorld.Receiver.height, 1.1f, label + " damage height");
        Vector(FixtureWorld.Receiver.center, new Vector3(0.4f, 0.5f, 0.6f), label + " damage center");
        Check(FixtureWorld.Receiver.enabled, label + " Ghost does not disable damage collider");
    }
    private static void Clean(CharacterMainControl p, string label, bool checkCharacter = true)
    {
        Check(p.GetComponent<BackMountainBossMorphRuntime>() == null, label + " component removed");
        Check(CharacterMainControl.EquipmentSubscribers == 0, label + " static event detached");
        if (checkCharacter)
        {
            Check(p.ShootSubscribers == 0 && p.AttackSubscribers == 0 && p.HoldSubscribers == 0, label + " character events detached");
            Check(p.characterModel.SourcePreset == "player", label + " default player model restored");
        }
        Check(FixtureWorld.OriginalItem.Stats.Values.Sum(s => s.Modifiers.Count) == 1, label + " only original equipment modifier remains");
        Near(FixtureWorld.OriginalItem.GetStat("ElementFactor_Fire").Value, 1.5f, label + " original fire stat restored");
        Check(GameObject.All.All(g => g == null || !g.name.StartsWith("costume-") || !g.name.EndsWith("(Clone)")), label + " no costume clones left");
        Check(GameObject.All.All(g => g == null || g.name != "BossFruitCostumeStaging"), label + " no staging root left");
    }
    private static void FormsAndConsumption()
    {
        for (int i = 0; i < FixtureWorld.Fruits.Length; i++)
        {
            FixtureWorld.Reset(); int id = FixtureWorld.Fruits[i];
            var usage = FixtureWorld.Usage(); Item fruit = FixtureWorld.Item(id, 2);
            Check(usage.CanBeUsed(fruit, null), id + " edible outside base");
            usage.FinishLikeOfficial(fruit);
            Check(fruit.StackCount == 1, id + " exactly one consumed");
            Check(BackMountainBossMorphService.IsActive && !BackMountainBossMorphService.CanUse, id + " active prevents stacking");
            Check(!usage.CanBeUsed(fruit, null), id + " UI rejects duplicate");
            Check(!BackMountainBossMorphService.TryBegin(id, FixtureWorld.Owner) && fruit.StackCount == 1,
                id + " duplicate service startup rejected");
            var p = FixtureWorld.Player;
            Check(p.ShootSubscribers == 1 && p.AttackSubscribers == 1 && p.HoldSubscribers == 1 && CharacterMainControl.EquipmentSubscribers == 1,
                id + " exactly one event subscription per source");
            Check(p.characterModel.SourcePreset == (i == 2 ? "Cname_Ghost" : "Cname_Boss_Red"), id + " corresponding base model");
            Near(p.characterModel.transform.localScale.x, i == 2 ? 2 : 1, id + " model scale");
            Vector(p.transform.localScale, Vector3.one, id + " player root never scaled");
            var costumes = FixtureWorld.Costumes();
            var expected = i == 0 ? new[] { 500003, 500004 } : i == 1 ? new[] { 500011, 500012 } : new[] { 500044 };
            Check(costumes.Select(a => a.SourceTypeID).OrderBy(v => v).SequenceEqual(expected), id + " full corresponding costume");
            foreach (var costume in costumes)
            {
                Check(costume.BoundItem == null, id + " costume never binds Item");
                Check(costume.gameObject.activeInHierarchy, id + " costume visible");
                Check(costume.GetComponentsInChildren<MonoBehaviour>(true).All(c => !c.enabled), id + " cosmetic scripts disabled");
                Check(costume.GetComponentsInChildren<Collider>(true).All(c => !c.enabled), id + " cosmetic collisions disabled");
                Check(costume.GetComponentsInChildren<Renderer>(true).All(r => !r.forceRenderingOff), id + " costume renderers remain visible");
            }
            foreach (var visual in p.PlayerVisuals)
            {
                Check(visual.gameObject.activeSelf && visual.enabled, id + " real equipped agent remains functional");
                Check(visual.GetComponent<Renderer>().forceRenderingOff == (visual.AgentType == ItemAgent.AgentTypes.equipment || i == 2),
                    id + " correct original equipment hidden");
            }
            Near(p.CharacterItem.GetStat("GunDamageMultiplier").Value, i == 0 ? 1.3f : i == 1 ? 1.15f : 1, id + " gun modifier");
            Near(p.CharacterItem.GetStat("MeleeDamageMultiplier").Value, i == 0 ? 1.3f : i == 1 ? 1.5f : 1.4f, id + " melee modifier");
            Near(p.CharacterItem.GetStat("RunSpeed").Value, i == 2 ? 1.2f : 1, id + " run speed");
            Near(p.CharacterItem.GetStat("WalkSpeed").Value, i == 2 ? 1.2f : 1, id + " walk speed");
            Near(p.CharacterItem.GetStat("ElementFactor_Fire").Value, i == 2 ? 1.5f : 0, id + " actual fire immunity or original factor");
            if (i == 2)
            {
                Check(PhantomWitchScytheWeaponConfig.Prepared.Count == 1 && PhantomWitchScytheWeaponConfig.AllPreparedSafely, "scythe prepared while inactive and disabled");
                Check(costumes[0].transform.parent == p.characterModel.RightHandSocket, "scythe attached to right hand");
                Check(p.CurrentHoldItemAgent.handAnimationType == HandheldAnimationType.meleeWeapon, "witch uses melee hand pose");
            }
            Preserved(id + " during morph");
            var savedVisuals = p.PlayerVisuals.ToArray();
            BackMountainBossMorphService.Clear(); BackMountainBossMorphService.Clear();
            Clean(p, id + " clear twice"); Preserved(id + " after restore");
            Check(savedVisuals.All(v => v.Destroyed || !v.GetComponent<Renderer>().forceRenderingOff), id + " original renderer cleanup");
            Check(p.CurrentHoldItemAgent.handAnimationType == HandheldAnimationType.normal, id + " original weapon animation restored");
        }
    }
    private static void RejectionAndRollback()
    {
        foreach (int id in FixtureWorld.Fruits)
        {
            foreach (int count in new[] { 1, 20 })
            {
                FixtureWorld.Reset(); var usage = FixtureWorld.Usage(); var fruit = FixtureWorld.Item(id, count);
                ItemAssetsCollection.Prefabs.Remove(id == 500065 ? 500003 : id == 500066 ? 500012 : 500044);
                usage.FinishLikeOfficial(fruit);
                Check(fruit.StackCount == count && !BackMountainBossMorphService.IsActive, id + " missing costume retains count " + count);
                Clean(FixtureWorld.Player, "resource rejection"); Preserved("resource rejection");
            }
            FixtureWorld.Reset();
            var failStat = FixtureWorld.OriginalItem.GetStat(id == 500067 ? "WalkSpeed" : "MeleeDamageMultiplier"); failStat.Reject = true;
            var lateFruit = FixtureWorld.Item(id, 20); FixtureWorld.Usage().FinishLikeOfficial(lateFruit);
            Check(lateFruit.StackCount == 20 && !BackMountainBossMorphService.IsActive, id + " partial modifier failure retains fruit");
            Clean(FixtureWorld.Player, "partial initialization rollback"); Preserved("partial initialization rollback");
            FixtureWorld.Reset();
            var preset = ObjectCache.Presets[id == 500067 ? 1 : 0];
            if (id == 500067) preset.CharacterModel.RightHandSocket = null; else preset.CharacterModel.HelmatSocket = null;
            var socketFruit = FixtureWorld.Item(id); FixtureWorld.Usage().FinishLikeOfficial(socketFruit);
            Check(socketFruit.StackCount == 1 && !BackMountainBossMorphService.IsActive, id + " absent required socket rejects");
            Clean(FixtureWorld.Player, "socket rejection");
            Check(GameObject.All.All(g => g == null || !g.name.StartsWith("model-") || !g.name.EndsWith("(Clone)")), "failed model clone destroyed");
        }
        FixtureWorld.Reset(); var ui = FixtureWorld.Usage();
        foreach (int id in new[] { 500062, 500063, 500064, 123, 0 }) Check(!ui.CanBeUsed(FixtureWorld.Item(id), null), "inedible ID " + id);
        Check(!BackMountainBossMorphService.TryBegin(123, FixtureWorld.Owner), "unknown profile rejected");
        ObjectCache.Presets = null;
        Check(!BackMountainBossMorphService.TryBegin(500065, FixtureWorld.Owner), "missing preset cache rejected");
        FixtureWorld.Reset(); FixtureWorld.Owner.ConfiguredEnabled = false;
        Check(!ui.CanBeUsed(FixtureWorld.Item(500065), null) && !BackMountainBossMorphService.TryBegin(500065, FixtureWorld.Owner), "disabled owner rejects use");
        FixtureWorld.Reset(); UObject.Destroy(FixtureWorld.Owner.gameObject);
        Check(!BackMountainBossMorphService.TryBegin(500065, FixtureWorld.Owner), "destroyed owner is fake null");
    }
    private static void TimersAndOwnership()
    {
        foreach (int id in FixtureWorld.Fruits)
        {
            FixtureWorld.Reset(); var p = FixtureWorld.Player;
            Check(BackMountainBossMorphService.TryBegin(id, FixtureWorld.Owner), "timer begins " + id);
            BossRushUI.Paused = true; FixtureWorld.Tick(120); p.Shoot(); p.Attack();
            Check(BackMountainBossMorphService.IsActive && Physics.Overlaps == 0, "pause freezes lifetime and cannot trigger ability");
            BossRushUI.Paused = false; FixtureWorld.Tick(29.9f);
            Check(BackMountainBossMorphService.IsActive && Physics.Overlaps == 0, "29.9 seconds active without paused attack queue");
            FixtureWorld.Tick(0.2f); Clean(p, "30-second timeout"); Preserved("timeout");
        }
        for (int reason = 0; reason < 7; reason++)
        {
            FixtureWorld.Reset(); var p = FixtureWorld.Player;
            Check(BackMountainBossMorphService.TryBegin(500067, FixtureWorld.Owner), "cleanup setup " + reason);
            p.Shoot();
            if (reason == 0) p.Health.IsDead = true;
            if (reason == 1) SceneLoader.IsSceneLoading = true;
            if (reason == 2) SceneManager.Handle++;
            if (reason == 3) FixtureWorld.Owner.ConfiguredEnabled = false;
            if (reason == 4) UObject.Destroy(FixtureWorld.Owner.gameObject);
            if (reason == 5) UObject.Destroy(p.GetComponent<BackMountainBossMorphRuntime>());
            if (reason == 6) UObject.Destroy(p.gameObject);
            FixtureWorld.Tick(0.1f);
            Clean(p, "cleanup reason " + reason, reason != 6);
            Check(Physics.Overlaps == 0, "cleanup discards pending attack " + reason);
            if (reason != 6) Preserved("lifecycle cleanup " + reason);
            else Check(p == null && p.Health == null, "scene object destruction cascades with Unity null semantics");
        }
        FixtureWorld.Reset(); var old = FixtureWorld.Player;
        Check(BackMountainBossMorphService.TryBegin(500065, FixtureWorld.Owner), "character swap starts");
        CharacterMainControl.Main = new GameObject("replacement-player").AddComponent<CharacterMainControl>();
        FixtureWorld.Tick(0.1f);
        Check(old.GetComponent<BackMountainBossMorphRuntime>() == null && old.ShootSubscribers == 0 && old.AttackSubscribers == 0 && old.HoldSubscribers == 0, "old character detaches on main swap");
        Check(CharacterMainControl.EquipmentSubscribers == 0 && FixtureWorld.OriginalItem.Stats.Values.Sum(s => s.Modifiers.Count) == 1, "main swap removes old modifiers and static event");
        UObject.Destroy(old.gameObject);
        Check(old == null, "swapped-out character is actually destroyed by scene");
    }
    private static void Combat()
    {
        for (int i = 0; i < FixtureWorld.Fruits.Length; i++)
        {
            FixtureWorld.Reset(); int id = FixtureWorld.Fruits[i]; var p = FixtureWorld.Player;
            Check(BackMountainBossMorphService.TryBegin(id, FixtureWorld.Owner), "combat starts " + id);
            var front = FixtureWorld.Target("front", new Vector3(0, 0, 3)); FixtureWorld.Collider(front);
            var side = FixtureWorld.Target("side", new Vector3(3, 0, 0));
            var diagonal = FixtureWorld.Target("diagonal", new Vector3(3, 0, 3));
            var rear = FixtureWorld.Target("rear", new Vector3(0, 0, -3));
            var middle = FixtureWorld.Target("5.5m", new Vector3(0, 0, 5.5f));
            var far = FixtureWorld.Target("7m", new Vector3(0, 0, 7));
            var outOfRange = FixtureWorld.Target("outside", new Vector3(0, 0, 10));
            var ally = FixtureWorld.Target("ally", new Vector3(0, 0, 2), Teams.friend);
            var wall = FixtureWorld.Target("behind-wall", new Vector3(0, 0, 4)); Physics.Blocked.Add(wall.transform.position + Vector3.up * 0.8f);
            var dead = FixtureWorld.Target("dead", new Vector3(0, 0, 2.5f)); dead.Health.IsDead = true;
            var destroyed = FixtureWorld.Target("destroyed", new Vector3(0, 0, 2.5f)); UObject.Destroy(destroyed.gameObject);
            FixtureWorld.Collider(p); Physics.Hits.Add(null); Physics.Hits.Add(new GameObject("unrelated").AddComponent<Collider>());
            p.Shoot(); p.Attack();
            Check(front.Health.Damage.Count == 0 && Physics.Overlaps == 0, "attack callback only queues");
            FixtureWorld.Tick(0.01f);
            Check(front.Health.Damage.Count == 1 && Physics.Overlaps == 1, "one ability per paired event and duplicate health");
            Near(front.Health.Damage[0].damageValue, i == 0 ? 24 : i == 1 ? 36 : 32, "form ability damage");
            Check(front.Health.Damage[0].Elements.ContainsKey(ElementTypes.fire) == (i != 2), "form damage element");
            Check(front.Health.Damage[0].Attacker == p && front.Health.Damage[0].isFromBuffOrEffect && front.Health.Damage[0].fromWeaponItemID == 0, "ability carries player source and effect metadata");
            Check(side.Health.Damage.Count == (i == 1 ? 1 : 0) && rear.Health.Damage.Count == (i == 1 ? 1 : 0), "emperor radial and other directional shapes");
            Check(diagonal.Health.Damage.Count == (i == 0 ? 0 : 1), "witch wider cone than dragon");
            Check(middle.Health.Damage.Count == (i == 2 ? 0 : 1) && far.Health.Damage.Count == (i == 0 ? 1 : 0), "distinct ability ranges");
            Check(outOfRange.Health.Damage.Count == 0 && ally.Health.Damage.Count == 0 && wall.Health.Damage.Count == 0 && dead.Health.Damage.Count == 0 && p.Health.Damage.Count == 0,
                "range team walls dead and self filters");
            Check(PhantomWitchScytheSwingFx.Calls == (i == 2 ? 1 : 0), "witch triggers actual scythe swing renderer hook");
            p.Attack(); FixtureWorld.Tick(0.01f);
            Check(front.Health.Damage.Count == 1, "cooldown rejects immediate repeat");
            float cooldown = i == 0 ? 0.8f : i == 1 ? 1.2f : 0.65f;
            FixtureWorld.Tick(cooldown - 0.04f); p.Shoot(); FixtureWorld.Tick(0.01f);
            Check(front.Health.Damage.Count == 1, "cooldown rejects just before boundary");
            FixtureWorld.Tick(0.04f); p.Attack(); FixtureWorld.Tick(0.01f);
            Check(front.Health.Damage.Count == 2, "melee triggers after cooldown");
            Preserved("combat preserves player"); BackMountainBossMorphService.Clear(); Clean(p, "combat end");
        }
    }
    private static void EquipmentRefresh()
    {
        FixtureWorld.Reset(); var p = FixtureWorld.Player;
        Check(BackMountainBossMorphService.TryBegin(500067, FixtureWorld.Owner), "equipment refresh starts");
        var go = new GameObject("late-held-agent"); go.transform.SetParent(p.characterModel.transform);
        var agent = go.AddComponent<DuckovItemAgent>(); agent.AgentType = ItemAgent.AgentTypes.handheld;
        var renderer = go.AddComponent<Renderer>();
        p.CurrentHoldItemAgent = agent; p.HoldChanged();
        Check(!renderer.forceRenderingOff, "hold event defers renderer changes");
        FixtureWorld.Tick(0.01f);
        Check(renderer.forceRenderingOff && agent.enabled && go.activeSelf && agent.handAnimationType == HandheldAnimationType.meleeWeapon, "late weapon still functional under witch appearance");
        var alreadyHidden = new GameObject("already-hidden"); alreadyHidden.transform.SetParent(p.characterModel.transform);
        alreadyHidden.AddComponent<ItemAgent>().AgentType = ItemAgent.AgentTypes.equipment;
        alreadyHidden.AddComponent<Renderer>().forceRenderingOff = true;
        p.EquipmentChanged(); p.EquipmentChanged(); FixtureWorld.Tick(0.01f);
        Check(FixtureWorld.Costumes().All(a => a.GetComponent<Renderer>().forceRenderingOff == false), "refresh never hides own costume");
        var replacement = FixtureWorld.MakeModel("external-system");
        p.characterModel = replacement; // Keep old visual alive to observe restoration before real scene destruction.
        FixtureWorld.Tick(0.01f);
        Check(p.characterModel == replacement, "cleanup respects model replaced by another system");
        Check(!renderer.forceRenderingOff && agent.handAnimationType == HandheldAnimationType.normal, "still-live old visual renderer and animation restored");
        Check(alreadyHidden.GetComponent<Renderer>().forceRenderingOff, "prehidden renderer remains hidden");
        Check(CharacterMainControl.EquipmentSubscribers == 0 && p.ShootSubscribers == 0, "model replacement detaches events");
    }
    private static int Main()
    {
        try
        {
            FormsAndConsumption(); RejectionAndRollback(); TimersAndOwnership(); Combat(); EquipmentRefresh();
            FixtureWorld.Reset(); BackMountainBossMorphService.Clear();
            Console.WriteLine("BackMountainMorph: " + checks + " assertions PASS"); return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }
}
