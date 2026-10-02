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
        for (int attempt = 0; attempt < 2; attempt++)
        {
            var flow = new SkyIslandPreludeFlow();
            flow.SpawnForTest();
            Check(!flow.IsSpawning && flow.Spawned != null, "prelude completes normal spawn");
            var clone = CharacterRandomPreset.Clones[attempt];
            var actor = CharacterRandomPreset.Created[attempt];
            // 250 × 1.5 = 375，先抬到专属 Boss 下限 1000（owner 2026-10-02），再乘序章系数 2。
            NearCombat(clone.health, 2000f, "prelude prepares Warden baseline x1.5, boss floor 1000 and prelude factor x2 before factory");
            NearCombat(actor.Health.MaxHealth, 2000f, "prelude factory receives Warden health");
            NearCombat(actor.Health.CurrentHealth, 2000f, "prelude spawns at full health");
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
    }
}

namespace BossRush
{
    // 仅存根正式 SpawnBoss 所调用的场景、剧情和资源边界；方法正文由 run.py 逐字抽取。
    internal sealed partial class SkyIslandPreludeFlow
    {
        private readonly GameObject objectiveRoot = new GameObject("prelude objective");
        private bool bossSpawning;
        private CharacterMainControl boss;
        private float nextBossAttempt;
        internal bool IsSpawning { get { return bossSpawning; } }
        internal CharacterMainControl Spawned { get { return boss; } }
        internal void SpawnForTest() { SpawnBoss(new Vector3(), 1); }
        private CharacterRandomPreset FindPreset() { return CharacterRandomPreset.Source; }
        private Vector3 GroundPoint(Vector3 position) { return position; }
        private bool IsObjectiveGeneration(int expected) { return expected == 1; }
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
