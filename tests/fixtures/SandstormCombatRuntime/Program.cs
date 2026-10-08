using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BossRush;
using UnityEngine;

public static class Program
{
    static int checks;
    internal static void Check(bool ok, string name) { checks++; if (!ok) throw new Exception(name); }
    static void Main()
    {
        // Test real production density with Unity's from/to/t semantics, including the old regression's midpoint.
        Check(SandstormChampionVolume.SampleHeightDensity(0f) == 0f, "sand base fades in");
        Check(SandstormChampionVolume.SampleHeightDensity(1f) < 0.0001f, "sand crown fades out");
        for (int i = 10; i <= 83; i++)
            Check(SandstormChampionVolume.SampleHeightDensity(i / 100f) > 0.999f, "sand interior must retain full density");
        Check(Math.Abs(SandstormChampionVolume.SampleHeightDensity(0.915f) - 0.5f) < 0.001f,
            "top fade is confined to the final 17 percent of height");
        foreach (float dt in new[] { 1f / 120f, 1f / 30f, 0.7f, 5f })
        {
            var orbit = new SandstormChampionController();
            Time.deltaTime = dt;
            orbit.TestOrbit();
            Check(orbit.Orbs == 31, "full and catch-up spiral casts retain all 31 bubbles");
        }
        Time.deltaTime = 1f / 60f;
        var controller = new SandstormChampionController();
        controller.Begin();
        controller.HealthRatio = 0.1f;
        for (int frame = 0; frame < 900; frame++)
        {
            Time.time = frame / 60f;
            controller.StopAllCoroutines(); // Host coroutine cancellation at every frame cannot drop fight work.
            controller.Frame();
        }
        Check(controller.DashCounts.Count >= 12, "phase three must keep attacking after its first teleport");
        for (int i = 0; i < controller.DashCounts.Count; i++)
            Check(controller.DashCounts[i] == i % 3 + 1, "production Fight consumes repeated 1/2/3 dash groups");
        Check(controller.Teleports == controller.DashCounts.Count || controller.Teleports == controller.DashCounts.Count + 1,
            "each dash group receives one completed nested reposition");
        Check(controller.Teleports < 60, "nested timed waits must not collapse into an instantaneous loop");
        int count = controller.DashCounts.Count;
        controller.Alive = false;
        for (int frame = 900; frame < 960; frame++) { Time.time = frame / 60f; controller.Frame(); }
        Check(controller.DashCounts.Count == count, "death cancels remaining phase work");

        foreach (bool bubble in new[] { true, false })
        {
            var orb = SandstormOrb.Spawn(controller, new Vector3(), new Vector3(), 1f, bubble);
            var receiver = orb.gameObject.GetComponent<DamageReceiver>();
            var health = orb.gameObject.GetComponent<HealthSimpleBase>();
            Check(receiver.useSimpleHealth && receiver.simpleHealth == health && health.dmgReceiver == receiver,
                "production wiring connects both receiver and official simple health");
            Check(health.HealthValue == (bubble ? 1f : SandstormChampionConfig.SharkHealth),
                "official Awake sees configured health only after production activation");
            Check(receiver.Team == Teams.wolf && !orb.gameObject.GetComponent<SphereCollider>().isTrigger
                && orb.gameObject.layer == LayerMask.NameToLayer("DamageReceiver"),
                "weapon-query prerequisites: enemy damage layer and non-trigger collider");
            if (!bubble)
            {
                receiver.Hurt(new DamageInfo { fromCharacter = CharacterMainControl.Main, damageValue = 1f, critRate = -1f });
                Check(orb.Pops == 0 && health.HealthValue > 0f, "nonlethal shark damage does not pop prematurely");
            }
            receiver.Hurt(new DamageInfo { fromCharacter = CharacterMainControl.Main, damageValue = 100f, critRate = -1f });
            Check(orb.Pops == 1 && !orb.gameObject.activeSelf, "official Hurt -> simple health -> Dead executes production OnShotDown");
        }
        Console.WriteLine("PASS SandstormCombatRuntime: " + checks + " checks");
    }
}

namespace BossRush
{
    partial class SandstormChampionController : MonoBehaviour
    {
        IEnumerator _fightRoutine;
        readonly SandstormChampionAttackPattern _attackPattern = new SandstormChampionAttackPattern();
        internal bool Alive = true;
        internal bool IsFighting { get { return Alive; } }
        readonly CharacterMainControl _boss = new CharacterMainControl();
        internal float HealthRatio { set { _boss.Health.CurrentHealth = value; } }
        int _phase = 1, _nextMinionCycle = 2;
        bool _enraged, _minionSummoning;
        readonly Minions _minions = new Minions();
        internal readonly List<int> DashCounts = new List<int>();
        internal int Teleports;
        internal int Orbs;
        bool HasPhaseTransition { get { return false; } }
        internal void TestOrbit()
        {
            _boss.transform.position = new Vector3(7f, 0f, -3f);
            IEnumerator orbit = OrbitAndOrbs(31);
            int frame = 0;
            while (orbit.MoveNext())
            {
                // Moving target, with an intentionally obstructed orbit: actual boss location differs from ideal radial.
                CharacterMainControl.Main.transform.position = new Vector3(frame % 7 - 4, 0f, frame % 5 + 2);
                if (++frame > 1000) throw new Exception("spiral never finishes");
            }
        }
        void MoveToward(Vector3 desired, float speed) { _boss.transform.position += new Vector3(0.013f, 0f, -0.007f); }
        void FacePlayer() { }
        void StopMotion() { }
        void SpawnOrb(Vector3 position, Vector3 direction, float speed)
        {
            Vector3 inward = CharacterMainControl.Main.transform.position - _boss.transform.position;
            Program.Check(Vector3.Dot(direction, inward) > 0f, "every spiral bubble must launch inward from actual position");
            Orbs++;
        }
        internal Teams HazardTeam { get { return Teams.wolf; } }
        internal void ReportOrbShotDown(bool bubble) { }
        internal void Begin() { _fightRoutine = RunFight(); }
        internal void Frame() { TickFight(); }
        void StopBattle(bool death) { Alive = false; _fightRoutine = null; }
        IEnumerator Transition() { yield return WaitForFightSeconds(1f); }
        IEnumerator Hover(float seconds) { yield return WaitForFightSeconds(seconds); }
        IEnumerator Dashes(int count) { DashCounts.Add(count); yield return WaitForFightSeconds(0.12f * count); }
        IEnumerator Reposition() { yield return WaitForFightSeconds(0.22f); Teleports++; yield return WaitForFightSeconds(0.08f); }
        IEnumerator BubbleBelch(int count) { yield return null; }
        IEnumerator SummonTornadoes(bool cyclone) { yield return null; }
        IEnumerator SummonMinions() { yield return null; }
    }
    public class Minions { internal bool CanSummon { get { return false; } } }
    partial class SandstormOrb : MonoBehaviour
    {
        SandstormChampionController _owner;
        bool _bubble, _dead, _subscribed;
        HealthSimpleBase _health;
        Collider _hitCollider;
        internal int Pops;
        void IgnoreCharacterContact(CharacterMainControl c) { }
        void Pop(bool hit) { if (!_dead) { _dead = true; Pops++; } }
    }
    public static class SandstormChampionConfig
    {
        internal const string LogPrefix = "[Sandstorm] ";
        internal const float HoverSeconds = 0.2f, OrbHitRadius = 0.6f, SharkHealth = 24f;
        internal const float SpiralBubbleSeconds = 2.2f, SpiralOrbitRadius = 7f, SpiralOrbitSpeed = 10f;
    }
    public static class ModBehaviour { internal static void DevLog(string s) { } }
}

namespace UnityEngine
{
    public class Object
    {
        internal bool destroyed;
        public static implicit operator bool(Object o) { return o != null && !o.destroyed; }
        public static void Destroy(Object o) { if (o != null) { o.destroyed = true; var go = o as GameObject; if (go != null) foreach (var c in go.components) c.destroyed = true; } }
    }
    public class Component : Object { public GameObject gameObject; public Transform transform { get { return gameObject.transform; } } public T GetComponent<T>() where T : Component { return gameObject == null ? null : gameObject.GetComponent<T>(); } }
    public class MonoBehaviour : Component { public void StopAllCoroutines() { } public object StartCoroutine(IEnumerator routine) { return null; } }
    public class GameObject : Object
    {
        public bool activeSelf = true;
        public int layer;
        public readonly Transform transform = new Transform();
        internal readonly List<Component> components = new List<Component>();
        readonly HashSet<Component> awake = new HashSet<Component>();
        public GameObject(string name) { }
        public T AddComponent<T>() where T : Component, new() { var value = new T { gameObject = this }; components.Add(value); if (activeSelf) Awake(value); return value; }
        void Awake(Component c) { if (awake.Add(c)) c.GetType().GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(c, null); }
        public void SetActive(bool active) { activeSelf = active; if (active) foreach (var c in components) Awake(c); }
        public T GetComponent<T>() where T : Component { foreach (var c in components) if (c is T) return (T)c; return null; }
    }
    public class Transform { public Vector3 position; }
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 up { get { return new Vector3(0f, 1f, 0f); } }
        public static Vector3 forward { get { return new Vector3(0f, 0f, 1f); } }
        public float sqrMagnitude { get { return Dot(this, this); } }
        public void Normalize() { this = this * (1f / (float)Math.Sqrt(sqrMagnitude)); }
        public static float Dot(Vector3 a, Vector3 b) { return a.x * b.x + a.y * b.y + a.z * b.z; }
        public static Vector3 operator +(Vector3 a, Vector3 b) { return new Vector3(a.x + b.x, a.y + b.y, a.z + b.z); }
        public static Vector3 operator -(Vector3 a, Vector3 b) { return new Vector3(a.x - b.x, a.y - b.y, a.z - b.z); }
        public static Vector3 operator *(Vector3 a, float b) { return new Vector3(a.x * b, a.y * b, a.z * b); }
    }
    public struct Quaternion
    {
        float angle;
        public static Quaternion Euler(float x, float y, float z) { return new Quaternion { angle = y * (float)Math.PI / 180f }; }
        public static Vector3 operator *(Quaternion q, Vector3 v)
        { return new Vector3(v.x * (float)Math.Cos(q.angle) + v.z * (float)Math.Sin(q.angle), v.y, -v.x * (float)Math.Sin(q.angle) + v.z * (float)Math.Cos(q.angle)); }
    }
    public class Collider : Component { public bool isTrigger; }
    public class SphereCollider : Collider { public float radius; }
    public class Rigidbody : Component { public bool isKinematic, useGravity; }
    public static class LayerMask { public static int NameToLayer(string name) { return name == "DamageReceiver" ? 7 : -1; } }
    public static class Time { public static float time, deltaTime; }
    public static class Mathf
    {
        public static float Max(float a, float b) { return Math.Max(a, b); }
        public static float SmoothStep(float from, float to, float t)
        { t = Math.Max(0f, Math.Min(1f, t)); return from + (to - from) * (t * t * (3f - 2f * t)); }
    }
    public static class Random { public static float Range(float a, float b) { return 0.5f; } }
    public static class Debug { public static void Log(string s) { } public static void LogWarning(string s) { throw new Exception(s); } }
}
namespace UnityEngine.Events
{
    public delegate void UnityAction<T>(T value);
    public class UnityEvent<T> { event UnityAction<T> listeners; public void AddListener(UnityAction<T> l) { listeners += l; } public void RemoveListener(UnityAction<T> l) { listeners -= l; } public void Invoke(T value) { listeners?.Invoke(value); } }
}
namespace Duckov.Buffs { public class Buff { } }
public enum Teams { all, player, wolf }
public class CharacterMainControl : MonoBehaviour
{
    public CharacterMainControl() { gameObject = new GameObject("character"); }
    public static CharacterMainControl Main = new CharacterMainControl { IsMainCharacter = true };
    public bool IsMainCharacter, isVehicle, protectVehicleDriver;
    public Health Health = new Health();
    public void AddBuff(Duckov.Buffs.Buff buff, CharacterMainControl who, int n) { }
}
public class Health : UnityEngine.Object
{
    public bool IsDead, IsMainCharacterHealth;
    public float CurrentHealth = 1f, MaxHealth = 1f;
    public Teams team;
    public UnityEngine.Events.UnityEvent<DamageInfo> OnDeadEvent = new UnityEngine.Events.UnityEvent<DamageInfo>();
    public void SetInvincible(bool value) { }
    public CharacterMainControl TryGetCharacter() { return null; }
    public void Hurt(DamageInfo info) { }
}
public class DamageInfo { public bool isExplosion; public float damageValue, critRate, critDamageFactor = 1f; public int crit; public CharacterMainControl fromCharacter; public DamageReceiver toDamageReceiver; }
public class LevelManager { public static LevelManager Instance = new LevelManager(); public CharacterMainControl ControllingCharacter; }
