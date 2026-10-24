using System;
using System.Linq;
using BossRush;
using ItemStatsSystem;
using UnityEngine;
using UnityEngine.SceneManagement;

internal static partial class Program
{
    private static void PreparationDeathAndRetry()
    {
        EclipseBloomRuntimeModule module = Module(); UntilSpawn(module, 1);
        CharacterMainControl character = CharacterRandomPreset.CompleteCharacter();
        Check(!character.gameObject.activeSelf && AsyncProbe.Frames.Count == 1,
            "preparation death probe reaches the protected inactive yield");
        character.Health.IsDead = true; character.Health.CurrentHealth = 0f; AsyncProbe.Frame();
        Check(character == null && CharacterRandomPreset.Clones.Last() == null && !EclipseBloomRuntimeModule.IsEncounterActive,
            "death during preparation never commits a dead boss and releases the encounter for retry");
        Check(!Item.Created.Any(item => item != null && item.TypeID == BossRushItemIds.ResidualSunShield)
            && GameObject.Live<EclipseBloomPetal>().Length == 0 && GameObject.Live<EclipseBloomBody>().Length == 0,
            "preparation death leaves no uncommitted reward or detached combat objects");
        Check(!Get<bool>(module, "consumed"), "preparation failure does not consume this raid encounter");
        UntilSpawn(module, 2);
        CharacterMainControl successor = CharacterRandomPreset.CompleteCharacter(); AsyncProbe.Frame();
        Check(EclipseBloomRuntimeModule.IsEncounterActive && Get<CharacterMainControl>(module, "boss") == successor
            && successor != null && !successor.Health.IsDead && successor.gameObject.activeSelf,
            "the retry commits a live successor only after all combat and reward ownership is ready");
        module.OnDestroy();
    }

    private static void AsyncGenerationCancel()
    {
        EclipseBloomRuntimeModule module = Module(); UntilSpawn(module, 1); ModuleFrame(module); ModuleFrame(module);
        Check(CharacterRandomPreset.CreateCalls == 1 && EclipseBloomRuntimeModule.IsEncounterActive,
            "repeated natural update cannot start a second in-flight boss spawn");
        var late = CharacterRandomPreset.Pending.Dequeue();
        SceneLoader.IsSceneLoading = true; ModuleFrame(module);
        Check(!EclipseBloomRuntimeModule.IsEncounterActive, "scene loading immediately releases the active encounter slot");
        SceneLoader.IsSceneLoading = false; UntilSpawn(module, 2);
        CharacterMainControl successor = CharacterRandomPreset.CompleteCharacter(); AsyncProbe.Frame();
        Check(Get<CharacterMainControl>(module, "boss") == successor && Get<bool>(module, "consumed"),
            "a replacement generation commits its own live encounter before an old result arrives");
        CharacterMainControl character = CharacterRandomPreset.CompleteCharacter(late);
        Check(character == null && Get<CharacterMainControl>(module, "boss") == successor
            && EclipseBloomRuntimeModule.IsEncounterActive && GameObject.Live<EclipseBloomPetal>().Length == 4,
            "late completion of an old generation cannot replace or clean the committed successor");
        module.OnDestroy();

        Reset(); module = Module(); UntilSpawn(module, 1);
        character = CharacterRandomPreset.CompleteCharacter();
        Check(AsyncProbe.Frames.Count == 1, "second cancellation probe reaches postcharacter preparation yield");
        ModBehaviour.Instance.CampaignModeDActiveForRuntime = true; ModuleFrame(module); AsyncProbe.Frame();
        Check(character == null && !EclipseBloomRuntimeModule.IsEncounterActive && GameObject.Live<EclipseBloomPetal>().Length == 0,
            "a competing mode cancels in-flight preparation without leaking a committed boss");
    }

    private static void EntryModes()
    {
        EclipseBloomRuntimeModule module = Module();
        Action<bool>[] switches = {
            value => ModBehaviour.Instance.IsActive = value,
            value => ModBehaviour.Instance.IsCampaignFinalBossActive = value,
            value => ModBehaviour.Instance.CampaignArenaActiveForRuntime = value,
            value => WavesArenaRuntimeModule.ArenaPlanned = value,
            value => WavesArenaRuntimeModule.ArenaActive = value,
            value => ModBehaviour.Instance.CampaignModeDActiveForRuntime = value,
            value => ModBehaviour.Instance.CampaignModeEActiveForRuntime = value,
            value => ModBehaviour.Instance.CampaignModeFActiveForRuntime = value,
            value => ModBehaviour.Instance.CampaignModeGActiveForRuntime = value,
            value => ModBehaviour.Instance.CampaignZombieRunForRuntime = value ? new ZombieRun { LifecyclePhase = ZombieModeLifecyclePhase.Active } : null,
            value => ModBehaviour.Instance.ModeHRuntime = value ? new ModeHModule { RunState = new ModeHRun { Lifecycle = ModeHLifecycle.Active } } : null,
        };
        foreach (Action<bool> change in switches)
        {
            change(true); Check(module.OtherModeActive(), "every existing active mode excludes natural flower spawning");
            for (int frame = 0; frame < 40; frame++) ModuleFrame(module);
            Check(CharacterRandomPreset.CreateCalls == 0, "excluded exploration never reaches the boss factory"); change(false);
        }
        foreach (BossRushPendingEntryKind kind in new[] { BossRushPendingEntryKind.ModeG, BossRushPendingEntryKind.ModeH, BossRushPendingEntryKind.Zombie })
        {
            BossRushMapSelectionHelper.Pending = kind;
            for (int frame = 0; frame < 40; frame++) ModuleFrame(module);
            Check(CharacterRandomPreset.CreateCalls == 0, "pending map mode entry excludes natural encounter before its mode state becomes active");
        }
        BossRushMapSelectionHelper.Pending = BossRushPendingEntryKind.None; SpawnPositionHelper.Reject = true;
        for (int frame = 0; frame < 40; frame++) ModuleFrame(module);
        Check(!EclipseBloomRuntimeModule.IsEncounterActive && CharacterRandomPreset.CreateCalls == 0,
            "unreachable exploration ground refuses the spawn before claiming the encounter");
        SpawnPositionHelper.Reject = false; UntilSpawn(module, 1);
        Check(GameObject.Live<EclipseBloomInteractable>().Length == 0,
            "ordinary exploration naturally spawns without adding an arena altar or interaction");
    }

    private static void NaturalRaid()
    {
        EclipseBloomRuntimeModule module = Module();
        Check(!ModBehaviour.Instance.CampaignArenaActiveForRuntime && ModBehaviour.Instance.CampaignArenaSignForRuntime == null,
            "normal-map fixture starts without arena mode or its sign");
        UntilSpawn(module, 1);
        Scene target = SceneManager.GetSceneByName("Level_SnowMilitaryBase");
        Check(CharacterRandomPreset.RequestedScenes.Single() == target.buildIndex,
            "production character factory receives the target subscene build index instead of the active main scene");
        module.OnSceneLoaded(new SceneRuntimeContext(SceneManager.Current, LoadSceneMode.Additive));
        module.OnSceneLoaded(new SceneRuntimeContext(target, LoadSceneMode.Additive));
        Check(CharacterRandomPreset.CreateCalls == 1 && EclipseBloomRuntimeModule.IsEncounterActive,
            "duplicate main and target loaded callbacks preserve the same in-flight raid encounter");
        CharacterMainControl character = CharacterRandomPreset.CompleteCharacter(); AsyncProbe.Frame();
        Check(EclipseBloomRuntimeModule.IsEncounterActive && Get<CharacterMainControl>(module, "boss") == character,
            "module commits the successfully prepared boss");
        Check(character.gameObject.scene == target && character.transform.parent == null
            && GameObject.Live<EclipseBloomPetal>().All(p => p.gameObject.scene == target)
            && GameObject.Scene.Single(go => go != null && go.name == "EclipseBloom_CoreHit").scene == target,
            "boss core and detached petals belong to the target subscene after leaving the official main-scene parent");
        Scene unrelated = new Scene { handle = 12, buildIndex = 22, name = "Transit_Additive" };
        SceneManager.Loaded.Add(unrelated.handle, unrelated); SceneManager.Current = unrelated;
        module.OnSceneLoaded(new SceneRuntimeContext(unrelated, LoadSceneMode.Additive)); ModuleFrame(module);
        Call(character.GetComponent<EclipseBloomAbilityController>(), "Update");
        Check(character != null && EclipseBloomRuntimeModule.IsEncounterActive && CharacterRandomPreset.CreateCalls == 1,
            "unrelated additive notification and active scene changes preserve the live encounter and its one spawn");
        Duckov.Scenes.MultiSceneCore.Instance.IsLoading = true;
        module.OnSceneLoaded(new SceneRuntimeContext(unrelated, LoadSceneMode.Additive)); ModuleFrame(module);
        Call(character.GetComponent<EclipseBloomAbilityController>(), "Update");
        Check(character != null && EclipseBloomRuntimeModule.IsEncounterActive && Get<bool>(module, "consumed"),
            "temporary same owner additive loading preserves an already committed encounter");
        Duckov.Scenes.MultiSceneCore.Instance.IsLoading = false;
        CharacterMainControl foreign = CharacterMainControl.Create("foreign pool identity", Teams.wolf);
        EclipseBloomRuntimeModule.NotifyOwnedBossDefeated(foreign);
        Check(EclipseBloomIntroduction.Defeats == 0,
            "unrelated Boss pool identity cannot advance the native introduction");
        UnityEngine.Object.Destroy(foreign.gameObject);
        character.Health.Kill(new DamageInfo(CharacterMainControl.Main));
        Item reward = character.CharacterItem.Inventory.Content.Single(); ModuleFrame(module);
        Check(EclipseBloomIntroduction.Defeats >= 1,
            "owned native boss death reaches the introduction notification");
        Check(!EclipseBloomRuntimeModule.IsEncounterActive && character != null && reward != null,
            "dead natural encounter releases combat while preserving the corpse and delivered reward");
        for (int frame = 0; frame < 200; frame++) ModuleFrame(module);
        module.OnSceneLoaded(new SceneRuntimeContext(target, LoadSceneMode.Additive)); ModuleFrame(module);
        Check(CharacterRandomPreset.CreateCalls == 1 && Get<bool>(module, "consumed"),
            "successful natural encounter consumes this raid even after death time and repeated scene callbacks");

        SceneManager.Unload(target);
        UnityEngine.Object.Destroy(LevelManager.Instance.gameObject);
        UnityEngine.Object.Destroy(Duckov.Scenes.MultiSceneCore.Instance.gameObject); Duckov.Scenes.MultiSceneCore.Instance = null;
        Scene next = new Scene { handle = 31, buildIndex = 41, name = "Level_SnowMilitaryBase" };
        SceneManager.Loaded.Add(next.handle, next); SceneManager.Current = next;
        LevelManager.Instance = new GameObject("next single-scene level owner").AddComponent<LevelManager>();
        CharacterMainControl.Main = CharacterMainControl.Create("new raid player", Teams.player);
        CharacterMainControl.Main.transform.position = new Vector3(481.5f, 0f, 520f);
        LevelManager.Instance.MainCharacter = CharacterMainControl.Main; LevelManager.Instance.ControllingCharacter = CharacterMainControl.Main;
        module.OnSceneLoaded(new SceneRuntimeContext(next, LoadSceneMode.Single)); UntilSpawn(module, 2);
        Check(!Get<bool>(module, "consumed"), "a new level owner opens a fresh raid slot even in the same named map");
        module.OnDestroy(); Check(EclipseBloomRuntimeModule.Current == null, "module destroy clears static encounter ownership");
        CharacterMainControl late = CharacterRandomPreset.CompleteCharacter();
        Check(late == null, "module destruction rejects a late unfinished natural character spawn");
    }

    private static void SceneAndGuideReadiness()
    {
        Scene target = SceneManager.GetSceneByName("Level_SnowMilitaryBase"); SceneManager.Loaded.Remove(target.handle);
        EclipseBloomRuntimeModule module = Module(); module.OnSceneLoaded(new SceneRuntimeContext(SceneManager.Current, LoadSceneMode.Single));
        for (int frame = 0; frame < 40; frame++) ModuleFrame(module);
        Check(CharacterRandomPreset.CreateCalls == 0, "main-scene load alone cannot spawn into an unloaded target subscene");
        SceneManager.Loaded.Add(target.handle, target); EclipseBloomIntroduction.CanSpawnEncounter = false;
        for (int frame = 0; frame < 40; frame++) ModuleFrame(module);
        Check(CharacterRandomPreset.CreateCalls == 0, "unfinished introduction blocks natural combat before accepting its encounter objective");
        EclipseBloomIntroduction.CanSpawnEncounter = true; ModBehaviour.MapRegistered = false;
        for (int frame = 0; frame < 40; frame++) ModuleFrame(module);
        Check(CharacterRandomPreset.CreateCalls == 0, "map must be one of the existing registered official selector maps");
        ModBehaviour.MapRegistered = true; Duckov.Scenes.MultiSceneCore.Instance.IsLoading = true;
        for (int frame = 0; frame < 40; frame++) ModuleFrame(module);
        Check(CharacterRandomPreset.CreateCalls == 0, "multi-scene loading remains an entry gate after the target handle exists");
        Duckov.Scenes.MultiSceneCore.Instance.IsLoading = false; UntilSpawn(module, 1); module.OnDestroy();
    }
}
