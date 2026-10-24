using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using BossRush;
using ItemStatsSystem;
using UnityEngine;
using UnityEngine.SceneManagement;

internal static partial class Program
{
    private static int checks, failures;

    private sealed class Battle
    {
        internal CharacterMainControl Boss;
        internal EclipseBloomAbilityController Controller;
        internal EclipseBloomBody Body;
        internal Item Reward;
        internal CharacterRandomPreset Preset;
        internal Collider OriginalHit;
        internal bool Current = true;
        internal EclipseBloomRules Rules { get { return Get<EclipseBloomRules>(Controller, "rules"); } }
    }

    private static void Check(bool value, string message)
    { checks++; if (!value) throw new InvalidOperationException(message); }

    private static T Get<T>(object value, string field)
    { return (T)value.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(value); }

    private static void Call(object value, string method, params object[] args)
    {
        try { value.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(value, args); }
        catch (TargetInvocationException error) { throw error.InnerException; }
    }

    private static void Case(string name, Action test)
    {
        Reset();
        try { test(); Console.WriteLine("PASS " + name); }
        catch (Exception error) { failures++; Console.WriteLine("FAIL " + name + ": " + error.GetBaseException().Message); }
    }

    private static void Reset()
    {
        if (EclipseBloomRuntimeModule.Current != null) EclipseBloomRuntimeModule.Current.OnDestroy();
        GameObject.Reset();
        AsyncProbe.Reset();
        CharacterRandomPreset.Pending.Clear(); CharacterRandomPreset.Clones.Clear();
        CharacterRandomPreset.PendingScenes.Clear();
        CharacterRandomPreset.RequestedScenes.Clear();
        CharacterRandomPreset.FailCreate = false; CharacterRandomPreset.CreateCalls = 0;
        Resources.Assets.Clear(); Debug.Warnings.Clear(); Item.Created.Clear();
        SavesCounter.Keys.Clear(); ExtraDrops.Calls = 0;
        Physics.Blocked = false; Physics.IgnoredContacts = 0;
        Physics.RaycastCalls = 0;
        LayerMask.MissingDamageLayer = false; LayerMask.FailDamageLayerAt = LayerMask.DamageLayerCalls = 0;
        ItemAssetsCollection.Missing = false; ItemAssetsCollection.OverrideType = 0;
        ResidualSunShieldWeaponConfig.Fails = false; EclipseBloomBody.FailAttach = false; EclipseBloomBody.Created.Clear();
        BossRushUI.Paused = false; CodexKillCollector.Calls = 0; SpawnPositionHelper.Reject = false;
        BossRushAudioManager.Instance = new BossRushAudioManager();
        SceneLoader.IsSceneLoading = false;
        SceneManager.Loaded.Clear();
        SceneManager.Current = new Scene { handle = 10, buildIndex = 20, name = "Level_SnowMilitaryBase_Main" };
        SceneManager.Loaded.Add(SceneManager.Current.handle, SceneManager.Current);
        Scene exploration = new Scene { handle = 11, buildIndex = 21, name = "Level_SnowMilitaryBase" };
        SceneManager.Loaded.Add(exploration.handle, exploration);
        Time.deltaTime = Time.time = Time.unscaledTime = 0f;
        Time.frameCount = 0;
        CharacterMainControl.Main = CharacterMainControl.Create("player", Teams.player);
        CharacterMainControl.Main.Health.IsMainCharacterHealth = true;
        CharacterMainControl.Main.transform.position = new Vector3(0, 0, 6);
        SceneManager.MoveGameObjectToScene(CharacterMainControl.Main.gameObject, exploration);
        LevelManager.Instance = new GameObject("level owner").AddComponent<LevelManager>();
        LevelManager.Instance.MainCharacter = CharacterMainControl.Main;
        LevelManager.Instance.ControllingCharacter = CharacterMainControl.Main;
        Duckov.Scenes.MultiSceneCore.Instance = new GameObject("multi scene owner").AddComponent<Duckov.Scenes.MultiSceneCore>();
        LevelManager.AfterInit = true;
        ModBehaviour.Instance = new GameObject("host").AddComponent<ModBehaviour>();
        ModBehaviour.MapRegistered = true;
        BossRushMapSelectionHelper.Pending = BossRushPendingEntryKind.None;
        EclipseBloomIntroduction.CanSpawnEncounter = true;
        EclipseBloomIntroduction.Defeats = EclipseBloomIntroduction.Spawns = 0;
        WavesArenaRuntimeModule.ArenaPlanned = WavesArenaRuntimeModule.ArenaActive = false;
        Duckov.Scenes.MultiSceneCore.ActiveSubScene = null;
        ModBehaviour.Instance.SpawnPoints = new[] { new Vector3(0, 0, 16) };
        Resources.Assets.Add(new CharacterRandomPreset { nameKey = "Cname_Boss_Red", hasSkill = true, defaultWeaponOut = true, skillPfb = new object() });
    }

    private static Battle Begin()
    {
        var battle = new Battle();
        battle.Boss = CharacterMainControl.Create("bloom", Teams.wolf);
        battle.Preset = new CharacterRandomPreset { nameKey = EclipseBloomBoss.NameKey };
        battle.Reward = Item.Create(BossRushItemIds.ResidualSunShield);
        battle.Boss.dropBoxOnDead = true;
        battle.Boss.characterPreset = battle.Preset;
        var hit = new GameObject("OriginalHit"); hit.transform.SetParent(battle.Boss.transform);
        hit.layer = 17; hit.AddComponent<DamageReceiver>().health = battle.Boss.Health;
        battle.OriginalHit = hit.AddComponent<SphereCollider>();
        battle.Controller = battle.Boss.gameObject.AddComponent<EclipseBloomAbilityController>();
        battle.Controller.Initialize(battle.Boss, battle.Preset, battle.Reward, 1f, () => battle.Current, SceneManager.Current.handle,
            Resources.FindObjectsOfTypeAll<CharacterRandomPreset>().First());
        battle.Body = EclipseBloomBody.Created.Last();
        return battle;
    }

    private static void Frame(Battle battle, float delta = 0.05f)
    {
        Time.deltaTime = delta; Time.time += delta; Time.unscaledTime += delta;
        Time.frameCount++;
        GameObject.StartPending();
        if (battle.Controller != null) Call(battle.Controller, "Update");
    }

    private static void Until(Battle battle, Func<bool> condition)
    {
        for (int frame = 0; frame < 1000 && !condition(); frame++) Frame(battle);
        Check(condition(), "production controller reaches the requested stage within a bounded number of frames");
    }

    private static EclipseBloomRuntimeModule Module()
    {
        CharacterMainControl.Main.transform.position = new Vector3(481.5f, 0f, 520f);
        var module = new EclipseBloomRuntimeModule(); module.OnAwake(ModBehaviour.Instance); return module;
    }

    private static void ModuleFrame(EclipseBloomRuntimeModule module, float delta = 0.25f)
    { Time.unscaledTime += delta; Time.frameCount++; module.OnUpdate(delta, delta); }

    private static void UntilSpawn(EclipseBloomRuntimeModule module, int expected)
    {
        for (int frame = 0; frame < 200 && CharacterRandomPreset.CreateCalls < expected; frame++) ModuleFrame(module);
        Check(CharacterRandomPreset.CreateCalls == expected,
            "natural exploration encounter reaches exactly the expected asynchronous creation boundary");
    }

    private static void Main()
    {
        Case("production_model_geometry", () => GeometryRegression.Run(Check));
        Case("production_ground_projection", () => GroundProjectionRegression.Run(Check));
        Case("petal_official_health_pipeline", PetalHealthPipeline);
        Case("core_receiver_and_official_death_identity", CoreReceiverAndIdentity);
        Case("warning_damage_pause_height_cover", DamageAndPause);
        Case("rest_motion_and_armor_owner", RestMotionAndArmor);
        Case("cancel_and_scene_change_cleanup", CancelAndSceneCleanup);
        Case("dedicated_music_owner_lifecycle", DedicatedMusicLifecycle);
        Case("death_reward_exactly_once", DeathReward);
        Case("owner_invalidates_during_actual_death_reward", OwnerInvalidatesAtDeath);
        Case("reward_inventory_fallback", RewardFallback);
        Case("spawn_prepared_and_failure_cleanup", SpawnAndFailures);
        Case("concurrent_factory_preserves_official_source", ConcurrentFactorySource);
        Case("preparation_death_rejects_and_retries", PreparationDeathAndRetry);
        Case("async_generation_cancel", AsyncGenerationCancel);
        Case("natural_entry_mode_exclusion", EntryModes);
        Case("normal_raid_once_and_scene_ownership", NaturalRaid);
        Case("main_scene_waits_for_target_and_guide", SceneAndGuideReadiness);
        Case("boss_pool_catalogue_and_independent_owners", PoolCatalogueAndOwnership);
        Case("boss_pool_cancellation_boundaries", PoolCancellation);
        Case("objective_circle_candidate_spread", CandidateSpread);
        Console.WriteLine("EclipseBloomRuntime: " + checks + " assertions, " + failures + " failures");
        if (failures > 0) Environment.Exit(1);
    }

    private static void CandidateSpread()
    {
        Vector3 anchor = EclipseBloomEncounterPlacement.ObjectiveAnchor;
        Vector3 player = anchor + new Vector3(0f, 0f, 40f);
        Check(EclipseBloomEncounterPlacement.CandidateCount >= 20, "the objective circle offers a wide candidate spread, not a single cramped spot");
        var seen = new System.Collections.Generic.List<Vector3>();
        for (int i = 0; i < EclipseBloomEncounterPlacement.CandidateCount; i++)
        {
            Vector3 position;
            Check(EclipseBloomEncounterPlacement.TryResolveCandidate(i, player, false, out position), "an open candidate passes placement");
            Vector3 offset = position - anchor; offset.y = 0f;
            Check(offset.magnitude <= 18.01f, "every candidate stays inside the 22 m map objective circle");
            foreach (Vector3 other in seen) Check((other - position).sqrMagnitude > 0.25f, "candidates are distinct spots");
            seen.Add(position);
        }
        Vector3 ignored;
        Check(!EclipseBloomEncounterPlacement.TryResolveCandidate(0, anchor + new Vector3(0f, 0f, 3f), true, out ignored),
            "a candidate on top of the player is still rejected");
        Physics.Blocked = true;
        Check(!EclipseBloomEncounterPlacement.TryResolveCandidate(0, player, true, out ignored), "walls on every ring reject even the relaxed pass");
        Physics.Blocked = false;
    }

    private static void PetalHealthPipeline()
    {
        Battle battle = Begin();
        EclipseBloomPetal[] petals = GameObject.Live<EclipseBloomPetal>();
        Check(petals.Length == 4, "production initialization creates exactly four petal receivers");
        Check(battle.Boss.BeforeLootListeners == 1 && battle.Boss.Health.OnDeadEvent.Count == 2,
            "controller owns exactly one death and preloot subscription");
        Check(Physics.IgnoredContacts == 10, "four petals and the exposed core ignore player and boss physical contact");
        foreach (EclipseBloomPetal petal in petals)
        {
            var receiver = petal.GetComponent<DamageReceiver>(); var health = petal.GetComponent<HealthSimpleBase>();
            Check(receiver.useSimpleHealth && receiver.simpleHealth == health && health.dmgReceiver == receiver,
                "production petal connects both directions of the official simple health pipeline");
            Check(health.HealthValue == 55f && receiver.Team == Teams.wolf,
                "official Awake observes the configured petal health and hostile team");
            var capsule = petal.GetComponent<CapsuleCollider>();
            Check(!capsule.isTrigger && petal.gameObject.layer == 17,
                "petal remains hittable by the official nontrigger damage layer query");
            Check(capsule.direction == 2 && capsule.height >= 3.4f && capsule.radius >= 0.78f,
                "petal hit volume spans its long local Z axis through the visible leaf tip");
            int index = Array.IndexOf(petals, petal);
            Check((petal.transform.localScale - battle.Body.GetPetalScale(index)).sqrMagnitude < 0.000001f,
                "initial petal hit volume uses the matching visible leaf scale");
        }
        battle.Body.PetalScaleFactor = 0.23f; Frame(battle);
        for (int index = 0; index < petals.Length; index++)
            Check((petals[index].transform.localScale - battle.Body.GetPetalScale(index)).sqrMagnitude < 0.000001f,
                "petal hit volume tracks visual unfolding scale during entrance");
        Until(battle, () => battle.Rules.IsDamaging);
        int before = battle.Body.Frames;
        var target = petals[0];
        target.GetComponent<DamageReceiver>().Hurt(new DamageInfo(CharacterMainControl.Main) { damageValue = 10f, critRate = -1f });
        Check(target != null && battle.Rules.IntactMask == 15, "nonlethal official petal damage preserves the part");
        target.GetComponent<DamageReceiver>().Hurt(new DamageInfo(CharacterMainControl.Main) { damageValue = 100f, critRate = -1f });
        Check(target == null && target.gameObject == null && battle.Rules.IntactMask == 14,
            "official Hurt to Dead destroys the struck petal and permanently clears its bit");
        Check(!battle.Rules.IsDamaging && battle.Body.Frames > before && battle.Body.AttackMask == 0,
            "petal death clears dangerous geometry in the callback before another Update");
        Check(battle.Body.Breaks == 1, "one petal death produces one break presentation");
    }

    private static void CoreReceiverAndIdentity()
    {
        Battle battle = Begin();
        Check(!battle.OriginalHit.enabled && battle.Boss.GetComponent<Collider>().enabled,
            "flower replaces hidden vanilla hit colliders while preserving the movement collider");
        GameObject core = GameObject.Scene.Single(go => go != null && go.name == "EclipseBloom_CoreHit");
        var receiver = core.GetComponent<DamageReceiver>();
        Check(!receiver.useSimpleHealth && receiver.health == battle.Boss.Health && core.layer == 17,
            "exposed core damage receiver binds directly to the real boss health");
        Check(Math.Abs(core.GetComponent<SphereCollider>().radius - battle.Body.GetCoreRadius()) < 0.0001f
            && (core.transform.position - battle.Body.GetCorePosition()).sqrMagnitude < 0.0001f,
            "core hit volume consumes the visible body core radius and position");
        float before = battle.Boss.Health.CurrentHealth;
        receiver.Hurt(new DamageInfo(CharacterMainControl.Main) { damageValue = 17f });
        Check(battle.Boss.Health.CurrentHealth == before - 17f && battle.Rules.ElapsedSeconds == 0f,
            "core remains damageable even during the harmless entrance period");
        Check(!SavesCounter.Keys.Contains(EclipseBloomBoss.NameKey), "custom identity is not written into official kill counters before death");
        battle.Boss.Health.Kill(new DamageInfo(CharacterMainControl.Main));
        Check(battle.Boss.characterPreset == Resources.FindObjectsOfTypeAll<CharacterRandomPreset>().First()
            && SavesCounter.Keys.Single() == "Cname_Boss_Red",
            "real death prefix restores the original preset before the official counter boundary");
        Check(ExtraDrops.Calls == 0, "flower death prefix skips all four vanilla extra reward handlers");
        Check(core == null && battle.OriginalHit.enabled, "stop destroys the core receiver and restores replaced vanilla hit colliders");
        var ordinary = CharacterMainControl.Create("ordinary", Teams.wolf);
        BossRushCharacterOnDeadPatch.Prefix(ordinary);
        Check(ExtraDrops.Calls == 4, "ordinary bosses still traverse the existing four extra reward handlers");
    }

    private static void DamageAndPause()
    {
        Battle battle = Begin();
        for (int frame = 0; frame < 30; frame++) Frame(battle);
        Check(CharacterMainControl.Main.Health.HurtCount == 0 && battle.Rules.ElapsedSeconds == 0f,
            "entrance period never damages or starts the first attack clock");
        Until(battle, () => battle.Rules.IsDamaging);
        int hits = CharacterMainControl.Main.Health.HurtCount;
        Check(hits == 1, "a shown warning transitions into a real official receiver hit");
        float clock = battle.Rules.ElapsedSeconds;
        BossRushUI.Paused = true;
        for (int frame = 0; frame < 50; frame++) Frame(battle, 1f);
        Check(CharacterMainControl.Main.Health.HurtCount == hits && battle.Rules.ElapsedSeconds == clock,
            "menu pause stops both attack advancement and repeated damage");
        BossRushUI.Paused = false;
        for (int frame = 0; frame < 50; frame++) Frame(battle, 0f);
        Check(battle.Rules.ElapsedSeconds == clock, "zero scaled time also freezes the encounter");
        GetTypeDamage(battle, new Vector3(0, 3, 6), false, Teams.player, false);
        GetTypeDamage(battle, new Vector3(0, 0, 6), true, Teams.player, false);
        GetTypeDamage(battle, new Vector3(0, 0, 6), false, Teams.wolf, false);
        GetTypeDamage(battle, new Vector3(0, 0, 6), false, Teams.player, true);
        battle.Body.GroundWarning = false;
        GetTypeDamage(battle, new Vector3(0, 0, 6), false, Teams.player, false);
    }

    private static void GetTypeDamage(Battle battle, Vector3 position, bool blocked, Teams team, bool expected)
    {
        CharacterMainControl.Main.transform.position = position; CharacterMainControl.Main.SetTeam(team); Physics.Blocked = blocked;
        Until(battle, () => battle.Rules.IsResting);
        Until(battle, () => !battle.Rules.IsResting);
        int index = 0; while ((battle.Rules.AttackMask & (1 << index)) == 0) index++;
        float x, z; EclipseBloomRules.GetSectorPoint(index, battle.Rules.RotationDegrees, 6f, 0f, out x, out z);
        CharacterMainControl.Main.transform.position = new Vector3(x, position.y, z);
        int before = CharacterMainControl.Main.Health.HurtCount;
        Until(battle, () => battle.Rules.IsDamaging);
        Check((CharacterMainControl.Main.Health.HurtCount > before) == expected,
            "height cover and friendly team gates are applied before official damage");
    }

    private static void RestMotionAndArmor()
    {
        Battle battle = Begin();
        Check(battle.Boss.CharacterItem.BodyArmor.Modifiers.Count == 1 && battle.Boss.CharacterItem.HeadArmor.Modifiers.Count == 1,
            "controller owns one armor suppression modifier on body and head");
        CharacterMainControl.Main.transform.position = new Vector3(0, 0, 20);
        Until(battle, () => battle.Rules.IsDamaging);
        Check(battle.Boss.LastMove.sqrMagnitude == 0f, "warning and damage keep the flower stationary");
        Until(battle, () => battle.Rules.IsResting);
        Check(battle.Boss.LastMove.z > 0f && Math.Abs(battle.Boss.LastMove.magnitude - 1f) < 0.0001f,
            "only harmless recovery pursues a far unobstructed player at full speed");
        int launched = EclipseBloomSolarFlares.Launches.Count;
        Until(battle, () => !battle.Rules.IsResting);
        Check(EclipseBloomSolarFlares.Launches.Count > launched, "a fresh telegraph calls solar flares onto a kiting player");
        Check(Math.Abs(EclipseBloomSolarFlares.Launches[launched].z - 20f) < 0.001f, "the first flare is aimed at the player's feet");
        Physics.Blocked = true; Frame(battle);
        Check(battle.Boss.LastMove.sqrMagnitude == 0f, "recovery pursuit stops at a wall");
        Physics.Blocked = false; CharacterMainControl.Main.transform.position = new Vector3(0, 0, 7); Frame(battle);
        Check(battle.Boss.LastMove.sqrMagnitude == 0f, "recovery movement stops inside eight meters");
        battle.Controller.StopBattle(false); battle.Controller.StopBattle(false);
        Check(battle.Boss.CharacterItem.BodyArmor.Modifiers.Count == 0 && battle.Boss.CharacterItem.HeadArmor.Modifiers.Count == 0,
            "idempotent stop releases both owned armor modifiers");
        Check(battle.Boss.LastMove.sqrMagnitude == 0f, "stop cancels any outstanding movement input");
    }

    private static void DedicatedMusicLifecycle()
    {
        var audio = BossRushAudioManager.Instance;
        Battle first = Begin(), second = Begin();
        Check(audio.Starts == 2 && audio.StartKeys.All(key => key == "EclipseBloom"),
            "each flower acquires its original dedicated EclipseBloom music key");
        Check(ReferenceEquals(audio.StartOwners[0], first.Boss) && ReferenceEquals(audio.StartOwners[1], second.Boss),
            "concurrent flowers acquire music with their distinct boss instance owners");
        first.Controller.StopBattle(false); first.Controller.StopBattle(false);
        UnityEngine.Object.Destroy(first.Boss.gameObject);
        Check(audio.Stops == 1 && audio.StopKeys[0] == "EclipseBloom" && ReferenceEquals(audio.StopOwners[0], first.Boss),
            "repeated cancellation and destruction release exactly the first flower music owner once");
        Call(second.Controller, "OnBossDead", new DamageInfo(CharacterMainControl.Main));
        UnityEngine.Object.Destroy(second.Boss.gameObject);
        Check(audio.Stops == 2 && audio.StopKeys[1] == "EclipseBloom" && ReferenceEquals(audio.StopOwners[1], second.Boss),
            "death and subsequent destruction release exactly the second flower music owner once");
        Battle cancelled = Begin(); cancelled.Current = false; Frame(cancelled);
        Check(audio.Stops == 3 && audio.StopKeys[2] == "EclipseBloom" && ReferenceEquals(audio.StopOwners[2], cancelled.Boss),
            "invalid encounter ownership releases the dedicated music lease through actual Update cleanup");
        Battle destroyed = Begin(); UnityEngine.Object.Destroy(destroyed.Boss.gameObject);
        Check(audio.Stops == 4 && audio.StopKeys[3] == "EclipseBloom" && ReferenceEquals(audio.StopOwners[3], destroyed.Boss),
            "direct pool destruction releases dedicated music through actual OnDestroy");
    }

    private static void CancelAndSceneCleanup()
    {
        Battle battle = Begin(); Until(battle, () => battle.Rules.IsDamaging);
        battle.Current = false; Frame(battle);
        Check(battle.Controller == null && battle.Boss == null && battle.Preset == null && battle.Reward == null,
            "invalid owner token destroys boss preset and unissued reward");
        Check(GameObject.Live<EclipseBloomPetal>().Length == 0 && GameObject.Live<EclipseBloomBody>().Length == 0,
            "cancel destroys all detached petals and body");
        Check(battle.Boss.BeforeLootListeners == 0 && battle.Boss.Health.OnDeadEvent.Count == 1,
            "cancel unsubscribes both controller events");
        Check(battle.Rules.IsStopped, "cancel stops the production rules");
        battle = Begin();
        SceneManager.Current = SceneManager.GetSceneByName("Level_SnowMilitaryBase");
        Frame(battle);
        Check(battle.Boss != null && !battle.Rules.IsStopped,
            "an additive active-scene change preserves combat whose owning scene is still loaded");
        SceneManager.Unload(battle.Boss.gameObject.scene);
        Check(battle.Boss == null && battle.Reward == null && GameObject.Live<EclipseBloomPetal>().Length == 0,
            "actual owning-scene unload destroys the boss graph and all detached hazards and reward");
    }

    private static void DeathReward()
    {
        Battle battle = Begin();
        DamageInfo info = new DamageInfo(CharacterMainControl.Main);
        battle.Boss.Health.Kill(info);
        battle.Boss.DispatchBeforeLoot(info);
        Call(battle.Controller, "OnBossDead", info);
        Check(battle.Boss.CharacterItem.Inventory.Content.Count(item => item.TypeID == BossRushItemIds.ResidualSunShield) == 1,
            "normal official event order emits exactly one exclusive reward");
        Check(battle.Boss.CharacterItem.Inventory.AddCalls == 1 && battle.Reward.DropCalls == 0 && battle.Reward != null,
            "successful corpse inventory transfer is not repeated dropped or destroyed");
        Check(CodexKillCollector.Calls == 1 && battle.Boss.LootBoxes == 1,
            "repeat death notifications do not repeat the known boss kill record");
        Check(battle.Body.Dissipations == 1 && battle.Rules.IsStopped && GameObject.Live<EclipseBloomPetal>().Length == 0,
            "death stops all attacks and dissolves the body once");

        battle = Begin();
        battle.Boss.DispatchBeforeLoot(info);
        Check(battle.Boss.CharacterItem.Inventory.AddCalls == 0 && battle.Reward != null,
            "a live committed boss cannot emit its exclusive reward through a premature preloot callback");
        battle.Boss.Health.IsDead = true;
        Call(battle.Controller, "OnBossDead", info);
        battle.Boss.DispatchBeforeLoot(info);
        Check(battle.Boss.CharacterItem.Inventory.AddCalls == 1 && battle.Reward != null,
            "reversed host death event order still transfers the reward once");
    }

    private static void RewardFallback()
    {
        foreach (bool throwingInventory in new[] { false, true })
        {
            Battle battle = Begin();
            battle.Boss.CharacterItem.Inventory.Reject = !throwingInventory;
            battle.Boss.CharacterItem.Inventory.Throw = throwingInventory;
            battle.Boss.Health.Kill(new DamageInfo(CharacterMainControl.Main));
            Check(battle.Reward != null && battle.Reward.DropCalls == 1 && battle.Boss.CharacterItem.Inventory.Content.Count == 0,
                "rejected or throwing corpse inventory falls back to exactly one world drop");
            battle.Boss.DispatchBeforeLoot(new DamageInfo());
            Check(battle.Reward.DropCalls == 1, "later preloot callbacks cannot repeat a successful fallback drop");
            UnityEngine.Object.Destroy(battle.Boss.gameObject);
        }
    }

    private static void OwnerInvalidatesAtDeath()
    {
        Battle battle = Begin();
        battle.Current = false;
        CharacterMainControl.Main.Health.IsDead = true;
        DamageInfo info = new DamageInfo(CharacterMainControl.Main);
        battle.Boss.Health.Kill(info);
        battle.Boss.DispatchBeforeLoot(info);
        Call(battle.Controller, "OnBossDead", info);
        Check(battle.Boss.CharacterItem.Inventory.Content.Count(item => item.TypeID == BossRushItemIds.ResidualSunShield) == 1
            && battle.Boss.CharacterItem.Inventory.AddCalls == 1 && battle.Reward != null,
            "committed boss actual death keeps exactly one exclusive reward when player death invalidates owner in the same frame");
        Check(EclipseBloomIntroduction.Defeats == 0,
            "simultaneous player death cannot advance the natural introduction while preserving the killed boss loot");
    }

    private static void SpawnAndFailures()
    {
        Task<CharacterMainControl> pending = EclipseBloomBoss.SpawnAsync(ModBehaviour.Instance, new Vector3(0, 0, 16), () => true).Inner;
        Check(!pending.IsCompleted && CharacterRandomPreset.Pending.Count == 1, "production spawn awaits the real host character boundary");
        CharacterRandomPreset runtimePreset = CharacterRandomPreset.Clones.Single();
        Check(!runtimePreset.hasSkill && runtimePreset.skillPfb == null && !runtimePreset.defaultWeaponOut,
            "runtime preset suppresses original skills and weapons before asynchronous character creation");
        CharacterMainControl character = CharacterRandomPreset.CompleteCharacter();
        Check(!pending.IsCompleted && AsyncProbe.Frames.Count == 1, "production spawn yields after core stat preparation");
        Check(!character.gameObject.activeSelf,
            "uncommitted boss stays inactive during the preparation yield before its reward and death subscription exist");
        Check(character.Team == Teams.wolf && character.CharacterItem.MaxHealth.BaseValue == 5000f,
            "spawn applies hostile safety net and boss health");
        Check(character.Primary.Content == null && character.Secondary.Content == null && character.Melee.Content == null,
            "spawn removes all three original weapon slots");
        var ai = character.GetComponentInChildren<AICharacterController>(true);
        Check(!ai.enabled && !ai.gameObject.activeSelf && ai.combatTree.Stopped && ai.patrolTree.Stopped,
            "spawn stops the vanilla AI subtree and original behavior trees");
        AsyncProbe.Frame();
        Check(pending.IsCompleted && pending.Result == character && GameObject.Live<EclipseBloomPetal>().Length == 4,
            "boss is committed only after reward body and all four petals exist");
        Item reward = Item.Created.Last(item => item.TypeID == BossRushItemIds.ResidualSunShield);
        Check(reward.Durability == reward.MaxDurability && SpawnedEnemyActivationHelper.Releases > 0,
            "committed boss has a prepared repaired reward and is released from distance sleep");
        UnityEngine.Object.Destroy(character.gameObject);

        foreach (int failure in new[] { 0, 1, 2, 3, 4 })
        {
            ItemAssetsCollection.Missing = failure == 0;
            ResidualSunShieldWeaponConfig.Fails = failure == 1;
            EclipseBloomBody.FailAttach = failure == 2;
            LayerMask.FailDamageLayerAt = failure == 3 ? LayerMask.DamageLayerCalls + 3 : 0;
            ItemAssetsCollection.OverrideType = failure == 4 ? 999 : 0;
            pending = EclipseBloomBoss.SpawnAsync(ModBehaviour.Instance, Vector3.zero, () => true).Inner;
            character = CharacterRandomPreset.CompleteCharacter();
            runtimePreset = CharacterRandomPreset.Clones.Last();
            AsyncProbe.Frame();
            Check(pending.IsCompleted && pending.Result == null && character == null && runtimePreset == null,
                "every preparation failure recycles its character and runtime preset");
            Check(GameObject.Live<EclipseBloomPetal>().Length == 0 && GameObject.Live<EclipseBloomBody>().Length == 0,
                "partial petal creation failure cannot leak detached hazards or body");
            Check(!Item.Created.Any(item => item != null && (item.TypeID == BossRushItemIds.ResidualSunShield || item.TypeID == 999)),
                "failed spawn destroys every uncommitted exclusive reward");
        }
    }

    private static void ConcurrentFactorySource()
    {
        CharacterRandomPreset official = Resources.Assets.OfType<CharacterRandomPreset>().Single();
        var first = EclipseBloomBoss.SpawnAsync(ModBehaviour.Instance, Vector3.zero, () => true).Inner;
        CharacterRandomPreset temporary = CharacterRandomPreset.Clones.Single();
        Check(temporary.name == "BossRush_EclipseBloom_Runtime" && temporary.nameKey == official.nameKey && !first.IsCompleted,
            "concurrent source probe reaches the real temporary clone before the official asynchronous factory returns");
        Resources.Assets.Clear(); Resources.Assets.Add(temporary); Resources.Assets.Add(official);
        var second = EclipseBloomBoss.SpawnAsync(ModBehaviour.Instance, new Vector3(8f, 0f, 0f), () => true).Inner;
        Resources.Assets.Remove(official);
        int calls = CharacterRandomPreset.CreateCalls;
        var withoutOfficial = EclipseBloomBoss.SpawnAsync(ModBehaviour.Instance, Vector3.zero, () => true).Inner;
        Check(withoutOfficial.IsCompleted && withoutOfficial.Result == null && CharacterRandomPreset.CreateCalls == calls,
            "only an in flight flower temporary clone cannot be used as a source preset for another encounter");
        Resources.Assets.Add(official);
        CharacterMainControl a = CharacterRandomPreset.CompleteCharacter(); AsyncProbe.Frame();
        CharacterMainControl b = CharacterRandomPreset.CompleteCharacter(); AsyncProbe.Frame();
        Check(first.IsCompleted && second.IsCompleted && first.Result == a && second.Result == b
            && Get<CharacterRandomPreset>(a.GetComponent<EclipseBloomAbilityController>(), "originalPreset") == official
            && Get<CharacterRandomPreset>(b.GetComponent<EclipseBloomAbilityController>(), "originalPreset") == official,
            "concurrent encounters both retain the actual official source even when the temporary clone is first in Resources");
        b.Health.Kill(new DamageInfo(CharacterMainControl.Main));
        Check(b.characterPreset == official && SavesCounter.Keys.Last() == official.nameKey && ExtraDrops.Calls == 0,
            "concurrent encounter death restores the official source object and keeps its exclusive reward path");
        UnityEngine.Object.Destroy(a.gameObject); UnityEngine.Object.Destroy(b.gameObject);
    }

}
