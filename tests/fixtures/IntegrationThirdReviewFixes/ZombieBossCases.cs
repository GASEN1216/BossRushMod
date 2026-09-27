using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace BossRush
{
    // 场景与身份是替身；护盾、减伤组合、Hurt 注入器和官方 Hurt 方法体来自生产文件。
    public static class ZombieModeZoneVisuals
    {
        public static void FadeOutAndDestroy(GameObject go, MonoBehaviour runtime) { runtime.enabled = false; UnityEngine.Object.Destroy(go); }
        public static void SetCountdown(GameObject go, float value) { }
    }
    public enum ZombieModeBossKind { Titan, Hunter, Splitter, Shielder, Corruptor }
    // 替身：只记录「致死前换回官方 preset」被调用了几次（生产实现在 ZombieModeBossVisuals.cs）。
    public static class ZombieModeBossVisuals
    {
        public static int Restores;
        public static void RestoreOfficialPreset(ZombieModeEnemyRuntimeMarker marker) { Restores++; }
    }
    public enum ZombieModeEnemyKind { Normal, Elite }
    public enum ZombieModeEliteAffix { Shielded, Stalwart, Adaptive }
    public static class L10n { public static string T(string text) { return text; } }
    public sealed class ZombieModeEnemyRuntimeMarker
    {
        public int RunId;
        public CharacterMainControl Owner;
        public ZombieModeEnemyKind EnemyKind;
        public readonly List<ZombieModeEliteAffix> EliteAffixes = new List<ZombieModeEliteAffix>();
        public ZombieModeShieldedAffixRuntime ShieldedAffix;
        public float AdaptiveReductionEndTime;
        public bool AdaptiveRangedActive, AdaptiveMeleeActive;
        public int AdaptiveRangedHitCount, AdaptiveMeleeHitCount;
        public bool IsBoss, DeathSettled, RemovedFromRuntime;
        public ZombieModeBossShieldRuntime AllyShield;
    }
    public sealed class ZombieModeBossInstance
    {
        public CharacterMainControl Character;
        public ZombieModeEnemyRuntimeMarker Marker;
        public ZombieModeBossKind Kind;
        public object SkillState;
        public Life Lifecycle = new Life();
        public sealed class Life { public bool Alive = true; }
    }
    public sealed class ZombieModeTitanState { public bool DamageReductionActive; }
    public partial class ModBehaviour
    {
        public bool IsZombieModeActive;
        private bool IsZombieModeDamageFromMeleeWeapon(DamageInfo info) { return info.fromWeaponItemID == 1; }
        public int ZombieModeCurrentRunId = 7;
        public bool Paused;
        public int Impacts;
        public Vector3 ImpactPosition;
        public void TryExecuteZombieModeTelegraphedAreaDamage(int run, CharacterMainControl source, Vector3 origin, float radius, float damage)
        { Impacts++; ImpactPosition = origin; }
        public bool IsZombieModeRuntimePaused() { return Paused; }
        public float GetZombieModeRuntimeNow() { return Time.unscaledTime; }
        public readonly Dictionary<CharacterMainControl, ZombieModeEnemyRuntimeMarker> Markers = new Dictionary<CharacterMainControl, ZombieModeEnemyRuntimeMarker>();
        public bool TryGetZombieModeKnownEnemyMarker(CharacterMainControl target, out ZombieModeEnemyRuntimeMarker marker)
        { return Markers.TryGetValue(target, out marker); }
        public sealed class Run { public List<ZombieModeBossInstance> CurrentWaveBossInstances = new List<ZombieModeBossInstance>(); }
        public Run zombieModeRunState = new Run();
    }
}

internal static class ZombieBossCases
{
    private static int checks;
    private static void Check(bool value, string message)
    { if (!value) throw new Exception("FAIL ZombieBoss: " + message); checks++; }
    private static void Near(float actual, float expected, string message)
    { Check(Math.Abs(actual - expected) < 0.001f, message + " got=" + actual + " expected=" + expected); }

    private static Health Target(float hp, bool boss, BossRush.ZombieModeBossKind kind, float shieldAmount = 0)
    {
        var owner = new BossRush.ModBehaviour { IsZombieModeActive = true };
        BossRush.ModBehaviour.Instance = owner;
        var target = new CharacterMainControl { IsMainCharacter = false };
        var health = new GameObject().AddComponent<Health>();
        health.CurrentHealth = hp; health.Owner = target; health.team = Teams.wolf; target.Health = health;
        var marker = new BossRush.ZombieModeEnemyRuntimeMarker { IsBoss = boss, RunId = owner.ZombieModeCurrentRunId };
        owner.Markers.Add(target, marker);
        owner.zombieModeRunState.CurrentWaveBossInstances.Add(new BossRush.ZombieModeBossInstance
            { Character = target, Kind = kind, Marker = marker });
        if (shieldAmount > 0)
        {
            marker.AllyShield = new GameObject().AddComponent<BossRush.ZombieModeBossShieldRuntime>();
            marker.AllyShield.ActivateShield(owner.ZombieModeCurrentRunId, shieldAmount, 5f);
        }
        Health.OnDead = null; Health.OnHurt = null;
        return health;
    }

    private static float Hit(Health target, float damage, bool player = true)
    {
        var attacker = new CharacterMainControl { IsMainCharacter = player };
        LevelManager.Instance.MainCharacter = attacker;
        float observed = -1;
        Health.OnHurt = (h, info) => observed = info.finalDamage;
        var hit = new DamageInfo(attacker) { damageValue = damage, ignoreArmor = true, critRate = 0 };
        hit.AddElementFactor(ElementTypes.physics, 1);
        target.Hurt(hit);
        return observed;
    }

    private static void TickDash(BossRush.ZombieModeSprinterDashRuntime dash, float now)
    {
        Time.unscaledTime = now;
        if (dash != null && dash.isActiveAndEnabled)
            typeof(BossRush.ZombieModeTimedRunScopedRuntime).GetMethod("Update", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(dash, null);
    }
    private static void DashCases()
    {
        Time.unscaledTime = 0;
        var health = Target(100, true, BossRush.ZombieModeBossKind.Hunter);
        var owner = BossRush.ModBehaviour.Instance;
        var source = health.Owner;
        source.Component = owner.Markers[source];
        var dash = new GameObject().AddComponent<BossRush.ZombieModeSprinterDashRuntime>();
        dash.Initialize(7, source, new Vector3(10,0,0), 15, 0.5f, 0.18f, 3.5f, 40);
        TickDash(dash, 0.4f); Near(source.LastForce.sqrMagnitude, 0, "Hunter does not move during warning");
        Check(owner.Impacts == 0, "no damage before dash");
        owner.Paused = true; TickDash(dash, 0.45f); TickDash(dash, 10);
        Check(owner.Impacts == 0 && source.LastForce.sqrMagnitude == 0, "pause does not advance windup");
        owner.Paused = false; TickDash(dash, 10.01f);
        Near(source.LastForce.sqrMagnitude, 0, "resume preserves remaining windup");
        TickDash(dash, 10.1f); Check(source.LastForce.x > 0 && source.transform.position.sqrMagnitude == 0, "dash uses movement force, never teleports");
        source.transform.position = new Vector3(4,0,0); // collision host stops at obstacle; production must use actual location
        TickDash(dash, 10.6f); TickDash(dash, 11);
        Check(owner.Impacts == 1 && owner.ImpactPosition.x == 4 && source.LastForce.sqrMagnitude == 0,
            "hitch crossing dash end deals once at actual position and releases force");

        Time.unscaledTime = 0; health = Target(100, true, BossRush.ZombieModeBossKind.Hunter);
        owner = BossRush.ModBehaviour.Instance; source = health.Owner; source.Component = owner.Markers[source];
        dash = new GameObject().AddComponent<BossRush.ZombieModeSprinterDashRuntime>();
        dash.Initialize(7, source, new Vector3(10,0,0), 15, 0.5f, 0.18f, 3.5f, 40);
        TickDash(dash, 0.51f); health.CurrentHealth = 0; owner.Markers[source].DeathSettled = true;
        TickDash(dash, 0.6f); Check(owner.Impacts == 0 && source.LastForce.sqrMagnitude == 0, "death during dash cancels impact and force");
        Time.unscaledTime = 0; health = Target(100, true, BossRush.ZombieModeBossKind.Hunter);
        owner = BossRush.ModBehaviour.Instance; source = health.Owner;
        dash = new GameObject().AddComponent<BossRush.ZombieModeSprinterDashRuntime>();
        dash.Initialize(7, source, new Vector3(10,0,0), 15, 0.5f, 0.18f, 3.5f, 40);
        TickDash(dash, 0.51f); owner.ZombieModeCurrentRunId++;
        TickDash(dash, 0.6f); Check(owner.Impacts == 0 && source.LastForce.sqrMagnitude == 0 && dash == null,
            "run change destroys dash subtree and releases force");
        BossRush.ModBehaviour.Instance = null;
    }

    internal static void Run()
    {
        var harmony = new Harmony("test.zombie-boss-defense");
        MethodInfo hurt = typeof(Health).GetMethod("Hurt");
        harmony.Patch(hurt, transpiler: new HarmonyMethod(typeof(BossRush.ZombieModeDamageRuntime), "InjectBeforeHealthLoss"));
        try
        {
            Time.unscaledTime = 0;
            var target = Target(20, true, BossRush.ZombieModeBossKind.Hunter, 100);
            BossRush.ZombieModeBossVisuals.Restores = 0;
            Near(Hit(target, 80), 0, "shield consumes final damage before lethal branch");
            Check(!target.IsDead, "absorbed lethal hit does not emit death");
            Check(BossRush.ZombieModeBossVisuals.Restores == 0, "absorbed hit keeps the boss display preset");
            Near(target.CurrentHealth, 20, "full shield prevents health loss");
            Near(Hit(target, 30), 10, "remaining shield only consumed once");
            Near(target.CurrentHealth, 10, "partial shield passes remainder");
            int deaths = 0; Health.OnDead = (h, d) => deaths++;
            Hit(target, 50); Check(target.IsDead && deaths == 1, "exhausted shield permits exactly one death");
            Check(BossRush.ZombieModeBossVisuals.Restores == 1, "lethal boss hit restores official preset before OnDead");

            target = Target(20, true, BossRush.ZombieModeBossKind.Hunter, 40);
            Hit(target, 200); Check(target.IsDead, "overkill is absorbed before HP+1 cap; insufficient shield cannot make target immortal");
            target = Target(20, true, BossRush.ZombieModeBossKind.Titan);
            BossRush.ModBehaviour.Instance.zombieModeRunState.CurrentWaveBossInstances[0].SkillState =
                new BossRush.ZombieModeTitanState { DamageReductionActive = true };
            Near(Hit(target, 30), 18, "Titan reduction precedes lethal test");
            Near(target.CurrentHealth, 2, "Titan survives raw lethal hit");

            target = Target(100, true, BossRush.ZombieModeBossKind.Hunter, 20);
            BossRush.ModBehaviour.Instance.zombieModeRunState.CurrentWaveBossInstances.Add(
                new BossRush.ZombieModeBossInstance { Character = new CharacterMainControl(), Kind = BossRush.ZombieModeBossKind.Shielder });
            Near(Hit(target, 50), 25.5f, "shield then aura reduction");
            target = Target(20, false, BossRush.ZombieModeBossKind.Hunter, 50);
            BossRush.ZombieModeBossVisuals.Restores = 0;
            Near(Hit(target, 30), 0, "group shield also protects ordinary ally from lethal hit");
            Check(BossRush.ZombieModeBossVisuals.Restores == 0, "ordinary zombies never touch the boss preset");
            target = Target(20, true, BossRush.ZombieModeBossKind.Hunter, 100);
            Time.unscaledTime = 6; Hit(target, 30); Check(target.IsDead, "expired shield cannot protect");
            Time.unscaledTime = 0;
            target = Target(20, true, BossRush.ZombieModeBossKind.Hunter, 100);
            BossRush.ModBehaviour.Instance.IsZombieModeActive = false;
            Hit(target, 30); Check(target.IsDead, "outside mode original damage unchanged");
            target = Target(20, true, BossRush.ZombieModeBossKind.Hunter, 100);
            BossRush.ModBehaviour.Instance.Markers[target.Owner].RunId--;
            Hit(target, 30); Check(target.IsDead, "previous run marker ignored");
            target = Target(20, true, BossRush.ZombieModeBossKind.Hunter, 100);
            Hit(target, 30, false); Check(target.IsDead, "preserve existing main-player source boundary");

            target = Target(20, false, BossRush.ZombieModeBossKind.Hunter);
            var elite = BossRush.ModBehaviour.Instance.Markers[target.Owner];
            elite.EnemyKind = BossRush.ZombieModeEnemyKind.Elite;
            elite.EliteAffixes.Add(BossRush.ZombieModeEliteAffix.Stalwart);
            Near(Hit(target, 100), 10, "Stalwart reduces actual final damage before death");
            Near(target.CurrentHealth, 10, "ranged reduction cannot heal above pre-hit health");
            target = Target(20, false, BossRush.ZombieModeBossKind.Hunter);
            elite = BossRush.ModBehaviour.Instance.Markers[target.Owner];
            elite.EnemyKind = BossRush.ZombieModeEnemyKind.Elite;
            elite.EliteAffixes.Add(BossRush.ZombieModeEliteAffix.Shielded);
            elite.ShieldedAffix = new GameObject().AddComponent<BossRush.ZombieModeShieldedAffixRuntime>();
            elite.ShieldedAffix.ActivateShield(7, 100, 5);
            Near(Hit(target, 80), 0, "elite shield protects against lethal hit");
            target = Target(100, false, BossRush.ZombieModeBossKind.Hunter);
            elite = BossRush.ModBehaviour.Instance.Markers[target.Owner];
            elite.EnemyKind = BossRush.ZombieModeEnemyKind.Elite;
            elite.EliteAffixes.Add(BossRush.ZombieModeEliteAffix.Adaptive);
            for (int i = 1; i < BossRush.ZombieModeTuning.AdaptiveAffixHitThreshold; i++) Near(Hit(target, 10), 10, "adaptive counts previous hits");
            Near(Hit(target, 10), 10 * (1 - BossRush.ZombieModeTuning.AdaptiveAffixReductionPercent), "adaptive reduces threshold hit once");
            Time.unscaledTime += BossRush.ZombieModeTuning.AdaptiveAffixDurationSeconds + 1;
            Near(Hit(target, 10), 10, "adaptive expiry restores normal damage");
            Time.unscaledTime = 0;

            // 原元素观察补丁与减伤同装：观察保持原贡献，读取按真实 finalDamage 分摊。
            harmony.CreateClassProcessor(typeof(BossRush.SetBonusDamageObservation)).Patch();
            target = Target(100, true, BossRush.ZombieModeBossKind.Hunter, 20);
            Near(Hit(target, 50), 30, "coexists with element observation transpiler");
            var instructions = PatchProcessor.GetOriginalInstructions(hurt).ToList();
            instructions.RemoveAll(c => c.opcode == System.Reflection.Emit.OpCodes.Stfld
                && Equals(c.operand, AccessTools.Field(typeof(DamageInfo), "finalDamage")));
            bool rejected = false;
            try { BossRush.ZombieModeDamageRuntime.InjectBeforeHealthLoss(instructions).ToList(); }
            catch (InvalidOperationException) { rejected = true; }
            Check(rejected, "changed official IL rejected instead of false success");
        }
        finally { harmony.UnpatchAll(harmony.Id); BossRush.ModBehaviour.Instance = null; }
        DashCases();
        Console.WriteLine("PASS ZombieBoss: " + checks + " production defense assertions over official Hurt body");
    }
}
