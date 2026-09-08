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
        public Vector3(float x, float y, float z) { }
        public static Vector3 up { get { return new Vector3(); } }
        public static Vector3 operator +(Vector3 a, Vector3 b) { return a; }
        public static Vector3 operator -(Vector3 a, Vector3 b) { return a; }
        public static Vector3 operator *(Vector3 a, float b) { return a; }
    }
    public class Transform { public Vector3 position, right; }
    public static class Time { public static float time = 10f; }
    public static class Random { public static float Range(float min, float max) { return min; } }
    public static class Mathf { public static float Pow(float a, float b) { return (float)Math.Pow(a, b); } }
}
public enum DamageTypes { normal }
public enum ElementTypes { electricity, ice }
public struct DamageInfo
{
    public float damageValue;
    public bool isFromBuffOrEffect;
    public int fromWeaponItemID;
    public Vector3 damagePoint;
    public DamageTypes damageType;
    public DamageInfo(CharacterMainControl player) { this = default(DamageInfo); }
    public void AddElementFactor(ElementTypes type, float amount) { }
}
public sealed class CharacterMainControl
{
    public static CharacterMainControl Main = new CharacterMainControl();
    public Transform transform = new Transform();
}
public sealed class Health
{
    public bool IsDead, IsMainCharacterHealth;
    public int Hits;
    public Transform transform = new Transform();
    public void Hurt(DamageInfo info) { Hits++; }
    public CharacterMainControl TryGetCharacter() { return new CharacterMainControl(); }
}
namespace BossRush
{
    public partial class ModBehaviour
    {
        private bool thunderSetActive = true, frostSetActive = true;
        private int setBonusGeneration;
        private float lastThunderTriggerTime, lastFrostTriggerTime;
        private const int THUNDER_SET_ARC_COLOR = 0, THUNDER_SET_BURST_COLOR = 0, FROST_SET_BURST_COLOR = 0;
        private readonly Health[] setBonusScanResults = new Health[1];
        internal readonly Queue<IEnumerator> Scheduled = new Queue<IEnumerator>();
        internal Health Target = new Health();
        internal int Scans;
        internal bool InFlight { get { return thunderChainInFlight; } }
        internal int HitHistory { get { return thunderChainHits.Count; } }
        internal void RememberTarget() { thunderChainHits.Add(Target); }
        internal void Reactivate() { BumpSetBonusGeneration(); ResetThunderChainState(); ResetFrostNovaState(); }
        internal void Kill(bool frost)
        {
            if (frost) TryScheduleFrostNova(Target, new DamageInfo());
            else TryScheduleThunderChain(Target, new DamageInfo());
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
        { victim = null; position = new Vector3(); return true; }
        private int ScanSetBonusEnemies(Vector3 center, float radius, CharacterMainControl except, int limit, List<Health> hits = null)
        { Scans++; setBonusScanResults[0] = Target; return 1; }
        private void SpawnSetArc(Vector3 from, Vector3 to, int color, float a, float b) { }
        private void SpawnSetBurst(Vector3 origin, int color, float a, float b, int count) { }
        private void PlaySoundEffect(string effect) { }
        public static void DevLog(string text) { }
        private bool TryApplyFrostFreeze(CharacterMainControl target) { return true; }
        private void StopAndClearFrostFallbackSlowCoroutines() { }
    }
    internal static class SetBonusSfx { internal const string ThunderChain = "thunder", FrostNova = "frost"; }
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
        BossRush.ModBehaviour host = new BossRush.ModBehaviour();
        host.Kill(false);
        IEnumerator old = host.Scheduled.Dequeue();
        host.Reactivate();
        host.Kill(false);
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
            host.Kill(frost);
            IEnumerator pending = host.Scheduled.Dequeue();
            host.Die(frost);
            Check(!pending.MoveNext(), (frost ? "frost" : "thunder") + " pending spell terminates after player death");
            Check(host.Scans == 0 && host.Target.Hits == 0,
                (frost ? "frost" : "thunder") + " player death cancels delayed damage");
        }
        Console.WriteLine("SetBonusCoroutines: " + (checkedCount - failed) + " PASS / " + failed + " FAIL");
        return failed == 0 ? 0 : 1;
    }
}
