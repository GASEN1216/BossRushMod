using System;
using BossRush;
using UnityEngine;

internal static partial class Program
{
    private static void CheckPreludeSpawnBalance()
    {
        Reset();
        int forgeOffset = SkyIslandBossForge.Applied.Count;
        // CreateCharacterAsync 替身只在调用当刻将 preset.health 写入角色，事后补数值不能假绿。
        float[] multipliers = { 1f, 0.1f, 2f };
        for (int attempt = 0; attempt < multipliers.Length; attempt++)
        {
            var flow = new SkyIslandPreludeFlow();
            flow.SpawnForTest(multipliers[attempt]);
            Check(!flow.IsSpawning && flow.Spawned != null, "prelude completes normal spawn");
            var clone = CharacterRandomPreset.Clones[attempt];
            var actor = CharacterRandomPreset.Created[attempt];
            // 250 × 1.5 = 375，先抬到专属 Boss 下限 1000（owner 2026-10-02），再乘序章系数 2。
            NearCombat(clone.health, 2000f * multipliers[attempt], "prelude prepares original baseline and configured difficulty before factory");
            NearCombat(actor.Health.MaxHealth, 2000f * multipliers[attempt], "prelude factory receives Warden health");
            NearCombat(actor.Health.CurrentHealth, 2000f * multipliers[attempt], "prelude spawns at full health");
            NearCombat(clone.damageMultiplier, 1.5f, "prelude Warden gun damage");
            NearCombat(clone.meleeDamageMultiplier, 1.5f, "prelude Warden melee damage");
            NearCombat(clone.moveSpeedFactor, 1.725f, "prelude Warden movement");
            NearCombat(clone.reactionTime, 0.2f, "prelude Warden reaction");
            NearCombat(CharacterRandomPreset.Source.health, 45f, "prelude source unchanged across respawns");
            Check(actor.Team == Teams.wolf && clone.dropBoxOnDead && !clone.setActiveByPlayerDistance,
                "prelude keeps hostility loot and activation safety");
            Check(SkyIslandBossForge.Applied.Count == forgeOffset + attempt + 1
                && SkyIslandBossForge.Applied[forgeOffset + attempt] == "K3_Relay#0", "prelude retains Warden gear and skill profile");
            var owner = actor.GetComponent<SkyIslandPreludeBossOwner>();
            Check(owner != null && owner.OwnedPreset == clone, "prelude retains preset lifetime owner");
        }
        Reset();
        var unaccepted = new SkyIslandPreludeFlow();
        unaccepted.Flags = 0;
        Check(!unaccepted.ObjectiveAllowed, "real prelude gate rejects an unaccepted quest");
        unaccepted.Flags = (int)SkyIslandStoryFlag.PreludeAccepted;
        Check(unaccepted.ObjectiveAllowed, "real prelude gate accepts an unfinished objective");
        unaccepted.HoldsInstrument = true;
        Check(!unaccepted.ObjectiveAllowed, "real prelude gate rejects an already carried instrument");
        unaccepted.HoldsInstrument = false;
        unaccepted.Flags |= (int)SkyIslandStoryFlag.RouteUnlocked;
        Check(!unaccepted.ObjectiveAllowed, "real prelude gate rejects an unlocked route");
        for (int failure = 0; failure < 6; failure++)
        {
            Reset();
            var flow = new SkyIslandPreludeFlow();
            var pending = new System.Threading.Tasks.TaskCompletionSource<CharacterMainControl>();
            CharacterRandomPreset.Block = pending;
            flow.SpawnForTest();
            Check(flow.IsSpawning, "prelude waits for the real production await boundary");
            if (failure == 0) flow.Flags = 0;
            if (failure == 1) flow.Flags |= (int)SkyIslandStoryFlag.RouteUnlocked;
            if (failure == 2) flow.HoldsInstrument = true;
            if (failure == 3) UnityEngine.SceneManagement.SceneManager.Name = "another_map";
            if (failure == 4) flow.ChangeGeneration();
            if (failure == 5) flow.CloseForTest();
            var late = CharacterMainControl.Create(true);
            pending.SetResult(late);
            Check(!flow.IsSpawning && flow.Spawned == null && late.gameObject == null,
                "production async acceptance recycles late objective after invalidation " + failure);
        }
        Reset();
    }
}

namespace BossRush
{
    // 仅存根正式 SpawnBoss 所调用的场景、剧情和资源边界；方法正文由 run.py 逐字抽取。
    internal sealed partial class SkyIslandPreludeFlow
    {
        private sealed class DifficultyOwner { internal float Scale; internal float GetBossHealthMultiplier() { return Scale; } }
        private DifficultyOwner owner;
        private readonly GameObject objectiveRoot = new GameObject("prelude objective");
        private bool bossSpawning;
        private bool disposed, holdsInstrument;
        private int generation = 1;
        private sealed class StoryBoundary { internal SkyIslandStoryData Current = new SkyIslandStoryData { flags = (int)SkyIslandStoryFlag.PreludeAccepted }; }
        private StoryBoundary story = new StoryBoundary();
        private CharacterMainControl boss;
        private float nextBossAttempt;
        internal bool IsSpawning { get { return bossSpawning; } }
        internal CharacterMainControl Spawned { get { return boss; } }
        internal bool ObjectiveAllowed { get { return ShouldRunObjective(); } }
        internal int Flags { get { return story.Current.flags; } set { story.Current.flags = value; } }
        internal bool HoldsInstrument { set { holdsInstrument = value; } }
        internal void ChangeGeneration() { generation++; }
        internal void CloseForTest() { disposed = true; }
        internal void SpawnForTest(float scale = 1f)
        { UnityEngine.SceneManagement.SceneManager.Name = GroundZeroScene; owner = new DifficultyOwner { Scale = scale }; SpawnBoss(new Vector3(), generation); }
        private CharacterRandomPreset FindPreset() { return CharacterRandomPreset.Source; }
        private Vector3 GroundPoint(Vector3 position) { return position; }
        private void OnBossDead() { }
        private void Report(string message, bool error) { }
    }

    internal sealed class SkyIslandPreludeBossOwner : MonoBehaviour
    {
        internal CharacterRandomPreset OwnedPreset;
        internal void Bind(CharacterMainControl character, CharacterRandomPreset preset, Action onDead)
        { OwnedPreset = preset; }
    }
}

namespace UnityEngine.SceneManagement
{
    internal struct Scene { internal string name; }
    internal static class SceneManager
    {
        internal static string Name;
        internal static Scene GetActiveScene() { return new Scene { name = Name }; }
    }
}
