using System;
using System.Linq;
using BossRush;
using UnityEngine;
using UnityEngine.SceneManagement;

internal static partial class Program
{
    private static void PoolCatalogueAndOwnership()
    {
        ModBehaviour owner = ModBehaviour.Instance;
        EclipseBloomPoolAdapter.Register(owner); EclipseBloomPoolAdapter.Register(owner);
        EnemyPresetInfo preset = owner.PresetCatalog.Single();
        Check(EclipseBloomPoolAdapter.IsPreset(preset) && EclipseBloomPoolAdapter.IsPreset(EclipseBloomBoss.NameKey)
            && !EclipseBloomPoolAdapter.IsPreset((EnemyPresetInfo)null) && !EclipseBloomPoolAdapter.IsPreset("unrelated"),
            "Boss pool catalogue registers exactly one recognizable Eclipse Bloom identity");
        Check(preset.team == (int)Teams.wolf && preset.baseHealth == EclipseBloomTuning.BaseHealth && preset.baseDamage == 34f,
            "Boss pool catalogue retains flower hostility health and damage tuning");
        owner.IsActive = true; EclipseBloomRuntimeModule natural = Module(); ModuleFrame(natural);
        Check(CharacterRandomPreset.CreateCalls == 0, "active BossRush excludes natural map generation before the pool factory runs");
        var spawn = EclipseBloomPoolAdapter.SpawnAsync(owner, new Vector3(0f, 0f, 16f), () => true, false).Inner;
        CharacterMainControl character = CharacterRandomPreset.CompleteCharacter(); AsyncProbe.Frame();
        Check(spawn.IsCompleted && spawn.Result == character && owner.WaveBosses.Single() == character && owner.CurrentBoss == character,
            "ordinary pool spawn commits its own wave registration through the production adapter");
        natural.OnDestroy();
        Check(character != null && character.GetComponent<EclipseBloomAbilityController>().IsFighting,
            "native map module cleanup cannot destroy a separately owned Boss pool encounter");
        var secondSpawn = EclipseBloomPoolAdapter.SpawnAsync(owner, new Vector3(8f, 0f, 16f), () => true, false).Inner;
        CharacterMainControl second = CharacterRandomPreset.CompleteCharacter(); AsyncProbe.Frame();
        Check(secondSpawn.IsCompleted && secondSpawn.Result == second && owner.WaveBosses.Count == 2
            && owner.CurrentBoss == second && character.GetComponent<EclipseBloomAbilityController>().IsFighting,
            "concurrent pool flowers retain independent controllers and wave leases");
        Scene transit = new Scene { handle = 91, buildIndex = 91, name = "Pool_Transit_Additive" };
        SceneManager.Loaded.Add(transit.handle, transit); SceneManager.Current = transit;
        Call(character.GetComponent<EclipseBloomAbilityController>(), "Update");
        Call(second.GetComponent<EclipseBloomAbilityController>(), "Update");
        Check(character.GetComponent<EclipseBloomAbilityController>().IsFighting
            && second.GetComponent<EclipseBloomAbilityController>().IsFighting,
            "Boss pool encounters remain current when an unrelated additive scene becomes active");
        Duckov.Scenes.MultiSceneCore.Instance.IsLoading = true;
        Call(second.GetComponent<EclipseBloomAbilityController>(), "Update");
        int beforeLoadingAttempt = CharacterRandomPreset.CreateCalls;
        var duringLoading = EclipseBloomPoolAdapter.SpawnAsync(owner, Vector3.zero, () => true, true).Inner;
        Check(duringLoading.IsCompleted && duringLoading.Result == null
            && CharacterRandomPreset.CreateCalls == beforeLoadingAttempt && second.GetComponent<EclipseBloomAbilityController>().IsFighting,
            "temporary additive loading rejects new pool factory calls while preserving committed pool encounters");
        Duckov.Scenes.MultiSceneCore.Instance.IsLoading = false;
        UnityEngine.Object.Destroy(character.gameObject);
        Check(owner.WaveBosses.Single() == second && owner.CurrentBoss == second
            && second.GetComponent<EclipseBloomAbilityController>().IsFighting,
            "destroying one pool flower cannot clear a later flower controller or current wave slot");
        UnityEngine.Object.Destroy(second.gameObject);
        Check(owner.WaveBosses.Count == 0 && owner.CurrentBoss == null,
            "pool wave lease removes destroyed flower from wave and current boss ownership");

        spawn = EclipseBloomPoolAdapter.SpawnAsync(owner, new Vector3(0f, 0f, 16f), () => true, true).Inner;
        character = CharacterRandomPreset.CompleteCharacter(); AsyncProbe.Frame();
        Check(spawn.IsCompleted && spawn.Result == character && owner.WaveBosses.Count == 0 && owner.CurrentBoss == null,
            "nonwave pool event gets an independent flower without claiming a wave boss slot");
        character.Health.Kill(new DamageInfo(CharacterMainControl.Main));
        Check(character.CharacterItem.Inventory.Content.Count(item => item.TypeID == BossRushItemIds.ResidualSunShield) == 1,
            "nonwave pool death still transfers the preconfigured exclusive reward exactly once");
    }

    private static void PoolCancellation()
    {
        ModBehaviour owner = ModBehaviour.Instance;
        foreach (bool modeE in new[] { true, false })
        {
            owner.CampaignModeEActiveForRuntime = modeE; owner.CampaignModeFActiveForRuntime = !modeE;
            var excluded = EclipseBloomPoolAdapter.SpawnAsync(owner, Vector3.zero, () => true, false).Inner;
            Check(excluded.IsCompleted && excluded.Result == null && CharacterRandomPreset.CreateCalls == 0,
                "special team modes reject Boss pool flowers before creating an ordinary wolf enemy");
        }
        owner.CampaignModeEActiveForRuntime = owner.CampaignModeFActiveForRuntime = false;
        bool current = true;
        var spawn = EclipseBloomPoolAdapter.SpawnAsync(owner, Vector3.zero, () => current, false).Inner;
        current = false; CharacterMainControl character = CharacterRandomPreset.CompleteCharacter();
        Check(spawn.IsCompleted && spawn.Result == null && character == null && owner.WaveBosses.Count == 0,
            "pool cancellation at character completion rejects all wave ownership and uncommitted resources");

        current = true; spawn = EclipseBloomPoolAdapter.SpawnAsync(owner, Vector3.zero, () => current, false).Inner;
        character = CharacterRandomPreset.CompleteCharacter(); current = false; AsyncProbe.Frame();
        Check(spawn.IsCompleted && spawn.Result == null && character == null && GameObject.Live<EclipseBloomPetal>().Length == 0,
            "pool cancellation at the production preparation yield destroys its uncommitted flower");

        current = true; spawn = EclipseBloomPoolAdapter.SpawnAsync(owner, Vector3.zero, () => current, false, true).Inner;
        character = CharacterRandomPreset.CompleteCharacter(); AsyncProbe.Frame();
        Check(!spawn.IsCompleted && AsyncProbe.Frames.Count == 1 && owner.WaveBosses.Count == 0,
            "pool deferred return remains outside the host wave ownership until its extra yield completes");
        current = false; AsyncProbe.Frame();
        Check(spawn.IsCompleted && spawn.Result == null && character == null && owner.WaveBosses.Count == 0,
            "pool cancellation during deferred return cannot leave a committed orphan or stale wave slot");
    }
}
