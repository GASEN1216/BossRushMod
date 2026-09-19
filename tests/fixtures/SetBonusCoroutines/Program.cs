using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Duckov.Modding { public class ModBehaviour { } }
namespace UnityEngine
{
    public sealed class WaitForSeconds { public WaitForSeconds(float seconds) { } }
    public sealed class Coroutine { }
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x=x; this.y=y; this.z=z; }
        public static Vector3 zero { get { return new Vector3(); } }
        public static Vector3 up { get { return new Vector3(0,1,0); } }
        public static Vector3 forward { get { return new Vector3(0,0,1); } }
        public float sqrMagnitude { get { return x*x+y*y+z*z; } }
        public Vector3 normalized { get { return this * (1f/(float)Math.Sqrt(sqrMagnitude)); } }
        public static float Dot(Vector3 a, Vector3 b) { return a.x*b.x+a.y*b.y+a.z*b.z; }
        public static bool operator ==(Vector3 a, Vector3 b) { return a.x==b.x && a.y==b.y && a.z==b.z; }
        public static bool operator !=(Vector3 a, Vector3 b) { return !(a==b); }
        public override bool Equals(object other) { return other is Vector3 && this==(Vector3)other; }
        public override int GetHashCode() { return x.GetHashCode() ^ y.GetHashCode() ^ z.GetHashCode(); }
        public static Vector3 operator +(Vector3 a, Vector3 b) { return new Vector3(a.x+b.x,a.y+b.y,a.z+b.z); }
        public static Vector3 operator -(Vector3 a, Vector3 b) { return new Vector3(a.x-b.x,a.y-b.y,a.z-b.z); }
        public static Vector3 operator *(Vector3 a, float b) { return new Vector3(a.x*b,a.y*b,a.z*b); }
    }
    public class Transform { public Vector3 position, right; }
    public static class Time { public static float time = 10f; }
public static class Random { public static float Range(float min, float max) { return min; } }
    public struct Color { public Color(float r, float g, float b) {} }
    public static class Mathf {
        public const float Deg2Rad = (float)Math.PI/180f;
        public static float Cos(float v) { return (float)Math.Cos(v); }
        public static float Min(float a, float b) { return Math.Min(a,b); }
        public static float Pow(float a, float b) { return (float)Math.Pow(a, b); }
    }
}
public enum DamageTypes { normal }
public enum ElementTypes { electricity, ice }
public struct DamageInfo
{
    public CharacterMainControl fromCharacter;
    public float damageValue, finalDamage;
    public bool isFromBuffOrEffect;
    public int fromWeaponItemID;
    public Vector3 damagePoint;
    public DamageTypes damageType;
    public DamageInfo(CharacterMainControl player) { this = default(DamageInfo); fromCharacter = player; }
    public void AddElementFactor(ElementTypes type, float amount) { }
}
public sealed class CharacterMainControl
{
    public static CharacterMainControl Main = new CharacterMainControl();
    public Transform transform = new Transform();
    public Health Health = new Health();
    public Vector3 CurrentAimDirection = Vector3.forward;
}
public sealed class Health
{
    public bool IsDead, IsMainCharacterHealth;
    public int Hits;
    public float TotalDamage;
    public float CurrentHealth = 100f, MaxHealth = 100f;
    public void AddHealth(float amount) { CurrentHealth = Math.Min(MaxHealth, CurrentHealth + amount); }
    public bool KillOnHit;
    public Transform transform = new Transform();
    public void Hurt(DamageInfo info) { Hits++; TotalDamage += info.damageValue; if (KillOnHit) IsDead = true; }
    public CharacterMainControl TryGetCharacter() { return new CharacterMainControl(); }
}
namespace FX { public static class PopText { public static string Last; public static void Pop(string text, Vector3 p, Color color, float size, object sprite) { Last = text; } } }
namespace BossRush
{
    internal static class NewWeaponIds { internal const int EnergyShieldTypeId = 500045; }
    internal static class NewWeaponEquipState { internal static bool Equipped = true; internal static bool IsTotemEquipped(int id) { return Equipped; } }
    internal static class NewWeaponPalette { internal static Color ShieldCore = new Color(); }
    internal static class NewWeaponSfx { internal const string ShieldAbsorb = "shield"; }
    internal static class NewWeaponFx { internal static void PlayBurst(Vector3 p, Color c, float r, float t, int n) {} internal static void PlaySound(string s) {} }

    public partial class ModBehaviour
    {
        private bool thunderSetActive = true, frostSetActive = true;
        private int setBonusGeneration;
        private float lastThunderTriggerTime, lastFrostTriggerTime;
        private const int THUNDER_SET_ARC_COLOR = 0, THUNDER_SET_BURST_COLOR = 0, FROST_SET_BURST_COLOR = 0;
        private const float FROST_SET_ICE_HEAL_RATIO = 0.5f, FROST_SET_COOLDOWN = 5f, FROST_SET_CLOSE_RANGE = 5f, FROST_SET_FREEZE_CHANCE = 1f;
        private const float THUNDER_SET_ELEC_HEAL_RATIO = 0.5f, THUNDER_SET_COOLDOWN = 3f, THUNDER_SET_CLOSE_RANGE = 6f, THUNDER_SET_COUNTER_CHANCE = 1f;
        private readonly Health[] setBonusScanResults = new Health[3];
        internal readonly Queue<IEnumerator> Scheduled = new Queue<IEnumerator>();
        internal Health Target = new Health();
        internal int Scans;
        internal int LastLimit;
        internal float LastRadius;
        internal bool ValidKill = true;
        internal readonly Health[] Enemies = { new Health(), new Health(), new Health(), new Health() };
        internal bool InFlight { get { return thunderChainInFlight; } }
        internal int HitHistory { get { return thunderChainHits.Count; } }
        internal void RememberTarget() { thunderChainHits.Add(Target); }
        internal void Reactivate() { BumpSetBonusGeneration(); ResetThunderChainState(); ResetFrostNovaState(); }
        private static float GetSetBonusElementDamagePortion(Health h, DamageInfo i, ElementTypes t) { return 0f; }
        private IEnumerator DelayedHeal(Health h, float amount) { yield return null; }
        internal void Kill(bool frost, bool effect = false)
        {
            DamageInfo info = new DamageInfo { isFromBuffOrEffect = effect };
            if (frost) TryScheduleFrostNova(Target, info);
            else TryScheduleThunderChain(Target, info);
        }
        internal void ChargeSpell(bool frost) { Kill(frost); Kill(frost); Kill(frost); }
        internal void Drain()
        {
            while (Scheduled.Count > 0)
            {
                IEnumerator routine = Scheduled.Dequeue();
                while (routine.MoveNext()) { }
            }
        }
        internal void Die(bool frost)
        {
            Health player = new Health { IsMainCharacterHealth = true };
            if (frost) OnFrostSetAnyDead(player, new DamageInfo());
            else OnThunderSetAnyDead(player, new DamageInfo());
        }
        private Coroutine StartCoroutine(IEnumerator routine)
        {
            if (routine.MoveNext()) Scheduled.Enqueue(routine);
            return new Coroutine();
        }
        private void StopCoroutine(Coroutine coroutine) { }
        private bool TryResolveSetBonusKillVictim(Health health, DamageInfo damage, out CharacterMainControl victim, out Vector3 position)
        { victim = null; position = new Vector3(); return ValidKill; }
        private int ScanSetBonusEnemies(Vector3 center, float radius, CharacterMainControl except, int limit, List<Health> hits = null)
        { Scans++; LastLimit = limit; LastRadius = radius; int count = Math.Min(limit, Enemies.Length); for (int i = 0; i < count; i++) setBonusScanResults[i] = Enemies[i]; return count; }
        private void SpawnSetArc(Vector3 from, Vector3 to, int color, float a, float b) { }
        private void SpawnSetBurst(Vector3 origin, int color, float a, float b, int count) { }
        private void PlaySoundEffect(string effect) { }
        public static void DevLog(string text) { }
        private bool TryApplyFrostFreeze(CharacterMainControl target) { return true; }
        private void StopAndClearFrostFallbackSlowCoroutines() { }
    }
internal static class SetBonusSfx { internal const string ThunderChain = "thunder", FrostNova = "frost", FrostCounter = "frost_counter", ThunderCounter = "thunder_counter"; }
}
internal static class Program
{
    private static int failed, checkedCount;
    private static void Check(bool condition, string name)
    {
        checkedCount++;
        Console.WriteLine((condition ? "PASS " : "FAIL ") + name);
        if (!condition) failed++;
    }
    private static int Main()
    {
        foreach (bool energy in new[] { true, false })
        {
            if (energy)
            {
                BossRush.EnergyShieldDeathProbe.SetState();
                BossRush.EnergyShieldDeathProbe.InvokeDead(new Health { IsMainCharacterHealth = true });
                Check(BossRush.EnergyShieldDeathProbe.IsReset(), "energy shield lethal OnDead resets state");
            }
            else
            {
                BossRush.ThunderRingDeathProbe.SetState();
                BossRush.ThunderRingDeathProbe.InvokeDead(new Health { IsMainCharacterHealth = true });
                Check(BossRush.ThunderRingDeathProbe.IsReset(), "thunder ring lethal OnDead resets charges and clocks");
            }
        }
        BossRush.ModBehaviour host = new BossRush.ModBehaviour();
        host.ChargeSpell(false);
        IEnumerator old = host.Scheduled.Dequeue();
        host.Reactivate();
        host.ChargeSpell(false);
        IEnumerator current = host.Scheduled.Dequeue();
        host.RememberTarget();
        Check(host.InFlight, "new chain owns in-flight gate before stale callback");
        Check(!old.MoveNext(), "old generation exits");
        Check(host.Scans == 0, "old generation cannot damage");
        Check(host.InFlight, "old finally cannot release current chain gate");
        Check(host.HitHistory == 1, "old finally cannot clear current chain deduplication");
        Check(!current.MoveNext() && !host.InFlight && host.HitHistory == 0, "current chain still releases its own state");

        foreach (bool frost in new[] { false, true })
        {
            host = new BossRush.ModBehaviour();
            host.ChargeSpell(frost);
            IEnumerator pending = host.Scheduled.Dequeue();
            host.Die(frost);
            Check(!pending.MoveNext(), (frost ? "frost" : "thunder") + " pending spell terminates after player death");
            Check(host.Scans == 0 && host.Target.Hits == 0,
                (frost ? "frost" : "thunder") + " player death cancels delayed damage");
        }
        foreach (bool frost in new[] { false, true })
        {
            string label = frost ? "frost" : "thunder";
            Time.time = 20f;
            host = new BossRush.ModBehaviour();
            host.Kill(frost); host.Kill(frost);
            Check(host.Scheduled.Count == 0, label + " first two direct kills do not cast");
            host.Kill(frost, true);
            Check(host.Scheduled.Count == 0, label + " effect kill never advances charge");
            host.ValidKill = false; host.Kill(frost); host.ValidKill = true;
            Check(host.Scheduled.Count == 0, label + " unrelated victim never advances charge");
            host.Kill(frost);
            Check(host.Scheduled.Count == 1, label + " third direct kill casts once");
            host.Drain();
            Check(host.LastLimit == (frost ? 3 : 2) && host.LastRadius == (frost ? 3f : 4f), label + " target and radius budget");
            Check(host.Enemies[0].TotalDamage == (frost ? 8f : 12f) && host.Enemies[3].Hits == 0, label + " damage budget and scan cap");
            host.ChargeSpell(frost);
            Check(host.Scheduled.Count == 0, label + " charged kills cannot bypass cooldown");
            Time.time += frost ? 6f : 5f;
            host.Kill(frost);
            Check(host.Scheduled.Count == 1, label + " charged spell becomes available after cooldown");
            host.Drain();
            host.Reactivate();
            host.Kill(frost); host.Kill(frost); host.Die(frost);
            host.Kill(frost);
            Check(host.Scheduled.Count == 0, label + " death resets partial kill charge");
        }
        Time.time = 40f;
        CharacterMainControl player = CharacterMainControl.Main;
        player.Health.IsMainCharacterHealth = true;
        player.Health.CurrentHealth = 60f;
        CharacterMainControl attacker = new CharacterMainControl();
        attacker.transform.position = new Vector3(0,0,4);
        DamageInfo frontal = new DamageInfo(attacker) { finalDamage = 100f };
        BossRush.EnergyShieldBehaviourProbe.Reset();
        BossRush.EnergyShieldBehaviourProbe.Hurt(player.Health, frontal);
        Check(player.Health.CurrentHealth == 85f && FX.PopText.Last == "+25.0", "shield actual frontal heal is capped at 25 and displayed");
        BossRush.EnergyShieldBehaviourProbe.Hurt(player.Health, frontal);
        Check(player.Health.CurrentHealth == 85f, "shield repeated same-frame hurt respects cooldown");
        Time.time += 0.5f;
        player.Health.CurrentHealth = 95f;
        BossRush.EnergyShieldBehaviourProbe.Hurt(player.Health, frontal);
        Check(player.Health.CurrentHealth == 100f && FX.PopText.Last == "+5.0", "shield pop uses actual gain after max health clamp");
        Time.time += 0.5f; player.Health.CurrentHealth = 60f;
        attacker.transform.position = new Vector3(0,0,-4);
        BossRush.EnergyShieldBehaviourProbe.Hurt(player.Health, frontal);
        Check(player.Health.CurrentHealth == 60f, "shield rear damage cannot heal");
        player.CurrentAimDirection = new Vector3(1,0,0); attacker.transform.position = new Vector3(4,0,0);
        frontal.finalDamage = 20f;
        BossRush.EnergyShieldBehaviourProbe.Hurt(player.Health, frontal);
        Check(player.Health.CurrentHealth == 66f, "shield follows aim rotation and heals 30 percent");
        Time.time += 0.5f; player.Health.IsDead = true;
        BossRush.EnergyShieldBehaviourProbe.Hurt(player.Health, frontal);
        Check(player.Health.CurrentHealth == 66f, "shield cannot heal lethal trailing OnHurt");
        player.Health.IsDead = false;
        BossRush.NewWeaponEquipState.Equipped = false;
        BossRush.EnergyShieldBehaviourProbe.Hurt(player.Health, frontal);
        Check(player.Health.CurrentHealth == 66f, "unequipped shield cannot heal");
        Time.time = 50f;
        host = new BossRush.ModBehaviour();
        foreach (Health enemy in host.Enemies) enemy.KillOnHit = true;
        host.ChargeSpell(false); host.Drain();
        Check(host.Scans == 1, "thunder lethal hit cannot continue into another hop");
        Console.WriteLine("SetBonusCoroutines: " + (checkedCount - failed) + " PASS / " + failed + " FAIL");
        return failed == 0 ? 0 : 1;
    }
}
