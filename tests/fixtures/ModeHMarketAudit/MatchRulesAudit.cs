// 直接编译 ModeHMatchRules + RuntimeStatModifierTracker；以下只模拟 Unity/官方宿主边界。
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace UnityEngine
{
    public class Object
    {
        internal bool Destroyed;
        public static bool operator ==(Object a, Object b)
        { return ReferenceEquals(a, b) || (ReferenceEquals(a, null) || a.Destroyed) && (ReferenceEquals(b, null) || b.Destroyed); }
        public static bool operator !=(Object a, Object b) { return !(a == b); }
        public override bool Equals(object other) { return ReferenceEquals(this, other); }
        public override int GetHashCode() { return base.GetHashCode(); }
        public static void Destroy(Object value)
        {
            if (ReferenceEquals(value, null)) return;
            value.Destroyed = true;
            var go = value as GameObject;
            if (!ReferenceEquals(go, null)) foreach (var child in go.Children) Destroy(child);
        }
    }
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float a, float b, float c) { x = a; y = b; z = c; }
        public static Vector3 up { get { return new Vector3(0, 1, 0); } }
        public static Vector3 operator +(Vector3 a, Vector3 b) { return new Vector3(a.x + b.x, a.y + b.y, a.z + b.z); }
        public static Vector3 operator *(Vector3 a, float b) { return new Vector3(a.x * b, a.y * b, a.z * b); }
    }
    public struct Color { public Color(float r, float g, float b, float a) { } }
    public sealed class Transform { public Vector3 position; public GameObject Owner; }
    public sealed class GameObject : Object
    {
        public readonly List<Object> Children = new List<Object>();
        public readonly Transform transform;
        public GameObject(string name) { transform = new Transform { Owner = this }; }
    }
    public sealed class Material : Object { }
    public sealed class LineRenderer : Object { public Material sharedMaterial = new Material(); }
}
namespace ItemStatsSystem.Stats
{
    public enum ModifierType { Add, PercentageAdd }
    public sealed class Modifier
    {
        internal float Value; internal ModifierType Type;
        public Modifier(ModifierType type, float value, object source) { Type = type; Value = value; }
    }
    public sealed class Stat
    {
        public float BaseValue = 1f;
        internal readonly List<Modifier> Modifiers = new List<Modifier>();
        public void AddModifier(Modifier m) { Modifiers.Add(m); }
        public void RemoveModifier(Modifier m) { Modifiers.Remove(m); }
        public float Value { get { return BaseValue * (1f + Modifiers.Where(x => x.Type == ModifierType.PercentageAdd).Sum(x => x.Value))
            + Modifiers.Where(x => x.Type == ModifierType.Add).Sum(x => x.Value); } }
    }
}
namespace ItemStatsSystem
{
    public sealed class Item
    {
        internal readonly Dictionary<string, Stats.Stat> Stats = new Dictionary<string, Stats.Stat>();
        public Stats.Stat GetStat(string name) { Stats.Stat s; return Stats.TryGetValue(name, out s) ? s : null; }
    }
}
namespace BossRush
{
    internal sealed class ZombieModeAttributeModifierRecord
    { public ItemStatsSystem.Item CharacterItem; public ItemStatsSystem.Stats.Stat Stat; public ItemStatsSystem.Stats.Modifier Modifier; public string StatName; }
    internal enum DamageTypes { normal, realDamage }
    internal enum ElementTypes { physics }
    internal struct DamageInfo
    {
        public DamageTypes damageType; public bool ignoreArmor, ignoreDifficulty, isFromBuffOrEffect;
        public float damageValue; public Vector3 damagePoint;
        public DamageInfo(CharacterMainControl c) { this = default(DamageInfo); }
        public void AddElementFactor(ElementTypes t, float factor) { }
    }
    internal sealed class HealthEvent
    {
        private readonly List<Action<Health>> _callbacks = new List<Action<Health>>();
        public int Count { get { return _callbacks.Count; } }
        public void AddListener(Action<Health> callback) { _callbacks.Add(callback); }
        public void RemoveListener(Action<Health> callback) { _callbacks.Remove(callback); }
        public void Invoke(Health h) { for (int i = 0; i < _callbacks.Count; i++) _callbacks[i](h); }
    }
    internal sealed partial class Health
    {
        public float MaxHealth = 100, CurrentHealth = 100;
        public bool IsDead;
        public int Hits;
        public HealthEvent OnHealthChange = new HealthEvent();
        public void SetHealth(float value) { float old = CurrentHealth; CurrentHealth = Math.Min(value, MaxHealth); if (old != CurrentHealth) OnHealthChange.Invoke(this); }
        public void Hurt(DamageInfo info) { Hits++; SetHealth(CurrentHealth - info.damageValue); IsDead = CurrentHealth <= 0; }
    }
    internal sealed partial class CharacterMainControl
    {
        public Health Health = new Health();
        public Transform transform;
        public ItemStatsSystem.Item CharacterItem = new ItemStatsSystem.Item();
        public CharacterMainControl()
        {
            var go = new GameObject("fighter"); go.Children.Add(this); go.Children.Add(Health); transform = go.transform;
            foreach (string name in new[] { "GunDamageMultiplier", "MeleeDamageMultiplier", "ElementFactor_Physics" })
                CharacterItem.Stats[name] = new ItemStatsSystem.Stats.Stat();
        }
    }
    internal sealed class ModeHSupportedMap { public Vector3 ArenaCenter; public float ArenaRadius = 10; }
    internal static class SkyIslandGroundRing
    {
        internal const float GroundLift = 0.16f;
        internal static LineRenderer Last; internal static bool MissingMaterial; internal static float Radius;
        internal static LineRenderer Create(Transform parent, Vector3 position)
        { Last = new LineRenderer(); if (MissingMaterial) Last.sharedMaterial = null; parent.Owner.Children.Add(Last); return Last; }
        internal static void SetShape(LineRenderer ring, float radius, float width, Color color) { Radius = radius; }
    }
    internal static class MatchRulesAudit
    {
        private static int _checks;
        private static void Check(bool value, string message) { _checks++; if (!value) throw new Exception("Match rules: " + message); }
        private static bool Near(float a, float b) { return Math.Abs(a - b) < 0.001f; }
        private static float Stat(ModeHParticipantRef p, string name) { return p.Character.CharacterItem.GetStat(name).Value; }
        private static ModeHParticipantRef Person(bool enemy, int slot)
        { return new ModeHParticipantRef { IsEnemy = enemy, PlanSlotIndex = slot, Character = new CharacterMainControl() }; }
        private static ModeHMatchPlanDto Plan(string condition)
        { return new ModeHMatchPlanDto { conditionId = condition, skeletonId = "wounded_line", enemyStableKeys = new List<string> { "Cname_Boss_Shot", "Cname_Prison_Boss" }, publicSummary = new ModeHPublicSummaryDto() }; }
        internal static void Run()
        {
            var rules = new ModeHMatchRules(); var map = new ModeHSupportedMap(); string reason;
            Check(!rules.Begin(Plan("unknown"), map, out reason), "unknown rule cannot silently do nothing");
            foreach (string condition in new[] { "narrow_cage", "open_field", "residual_might" })
            {
                Check(rules.Begin(Plan(condition), map, out reason), "begin " + condition);
                var friendly = Person(false, -1); var enemy = Person(true, 1);
                Check(rules.Enter(friendly, out reason) && rules.Enter(enemy, out reason), "both sides enter " + condition);
                Check(Near(enemy.Character.Health.CurrentHealth, 75), "actual highest threat enemy is wounded " + condition);
                Check(Near(Stat(friendly, "GunDamageMultiplier"), condition == "narrow_cage" ? 0.8f : condition == "open_field" ? 1.15f : 1.2f), "gun effect " + condition);
                Check(Near(Stat(friendly, "GunDamageMultiplier"), Stat(enemy, "GunDamageMultiplier")), "symmetric rule " + condition);
                Check(Near(Stat(friendly, "MeleeDamageMultiplier"), condition == "open_field" ? 1f : 1.2f), "melee effect " + condition);
                rules.Enter(new ModeHParticipantRef { IsEnemy = true, PlanSlotIndex = 1, Character = enemy.Character }, out reason);
                Check(Near(Stat(enemy, "GunDamageMultiplier"), Stat(friendly, "GunDamageMultiplier")), "duplicate entry cannot stack");
                if (condition == "residual_might")
                {
                    rules.Tick(7.75f, out reason); Check(Near(Stat(enemy, "GunDamageMultiplier"), 1.2f), "entry window remains before deadline");
                    var relay = Person(false, -1); rules.Enter(relay, out reason);
                    rules.Tick(0.25f, out reason);
                    Check(Near(Stat(enemy, "GunDamageMultiplier"), 1f) && Near(Stat(relay, "GunDamageMultiplier"), 1.2f), "late relay owns independent eight-second window");
                }
                rules.RestoreAll(); rules.RestoreAll();
                Check(Near(Stat(enemy, "GunDamageMultiplier"), 1f) && Near(Stat(friendly, "MeleeDamageMultiplier"), 1f), "cleanup restores modifiers " + condition);
            }
            rules.Begin(Plan("center_cover"), map, out reason);
            var center = Person(false, -1); rules.Enter(center, out reason);
            Check(Near(Stat(center, "ElementFactor_Physics"), 0.75f) && Near(SkyIslandGroundRing.Radius, 3), "cover uses visible radius and applies immediately");
            center.Character.transform.position = new Vector3(3.1f, 100f, 0);
            rules.Tick(0.25f, out reason); Check(Near(Stat(center, "ElementFactor_Physics"), 1), "leaving cover removes effect");
            center.Character.transform.position = new Vector3(2.9f, 100f, 0);
            rules.Tick(0.25f, out reason); Check(Near(Stat(center, "ElementFactor_Physics"), 0.75f), "height does not shift horizontal cover");
            rules.RestoreAll(); Check(SkyIslandGroundRing.Last == null, "cleanup destroys child ring");

            rules.Begin(Plan("danger_edge"), map, out reason);
            var edge = Person(false, -1); edge.Character.transform.position = new Vector3(7, 0, 0); rules.Enter(edge, out reason);
            Check(Near(SkyIslandGroundRing.Radius, 6.5f), "hazard shares drawn and logical boundary");
            rules.Tick(5, out reason); Check(edge.Character.Health.Hits == 0, "entry grace inflicts no damage");
            rules.Tick(1, out reason); Check(Near(edge.Character.Health.CurrentHealth, 98), "edge inflicts actual two-percent damage");
            edge.Character.transform.position = new Vector3(6.5f, 0, 0); rules.Tick(1, out reason);
            Check(edge.Character.Health.Hits == 1, "boundary belongs to safe area");
            edge.Character.transform.position = new Vector3(7, 0, 0); rules.Tick(20, out reason);
            Check(edge.Character.Health.Hits == 2, "stall cannot burst catch-up damage");
            var late = Person(true, 0); late.Character.transform.position = new Vector3(7, 0, 0); rules.Enter(late, out reason); rules.Tick(1, out reason);
            Check(late.Character.Health.Hits == 0 && Near(late.Character.Health.CurrentHealth, 100), "unwounded late reinforcement has its own grace");
            UnityEngine.Object.Destroy(edge.Character.transform.Owner); rules.Tick(1, out reason); rules.RestoreAll();

            rules.Begin(Plan("medical_limited"), map, out reason);
            var patient = Person(false, -1); patient.Character.Health.SetHealth(20); rules.Enter(patient, out reason);
            Check(patient.Character.Health.OnHealthChange.Count == 1, "medical owner subscribes once");
            rules.Enter(patient, out reason); patient.Character.Health.SetHealth(60);
            Check(Near(patient.Character.Health.CurrentHealth, 40), "healing halved without recursive reapplication");
            patient.Character.Health.SetHealth(25); Check(Near(patient.Character.Health.CurrentHealth, 25), "damage unchanged");
            patient.Character.Health.SetHealth(45); Check(Near(patient.Character.Health.CurrentHealth, 35), "successive healing uses new baseline");
            rules.RestoreAll(); patient.Character.Health.SetHealth(55);
            Check(patient.Character.Health.OnHealthChange.Count == 0 && Near(patient.Character.Health.CurrentHealth, 55), "cleanup unsubscribes and normal healing returns");

            var woundedPlan = Plan("open_field");
            Check(ModeHEncounterPlanner.GetWoundedEnemyCount(woundedPlan) == 1 && ModeHEncounterPlanner.IsWoundedEnemy(woundedPlan, 1)
                && !ModeHEncounterPlanner.IsWoundedEnemy(woundedPlan, 0) && !ModeHEncounterPlanner.IsWoundedEnemy(woundedPlan, -1), "wounds assigned by frozen core identity");
            Check(ModeHEncounterPlanner.TryApplyRecon(woundedPlan, "current_injury", out reason) && woundedPlan.publicSummary.visibleWoundedEnemyCount == 1, "scout reveals actual wound count");
            int woundedScore = ModeHOddsController.ComputeEnemyPublicScore(woundedPlan, null);
            woundedPlan.publicSummary.visibleWoundedEnemyCount = 0;
            Check(ModeHOddsController.ComputeEnemyPublicScore(woundedPlan, null) == woundedScore + 5, "only revealed actual wounds reduce enemy score");
            rules.Begin(Plan("narrow_cage"), map, out reason);
            var missing = Person(false, -1); missing.Character.CharacterItem.Stats.Remove("GunDamageMultiplier");
            Check(!rules.Enter(missing, out reason), "missing stat rejects partial effect"); rules.RestoreAll();
            Check(Near(Stat(missing, "MeleeDamageMultiplier"), 1), "failed entry partial effect rolls back");
            SkyIslandGroundRing.MissingMaterial = true;
            Check(!rules.Begin(Plan("danger_edge"), map, out reason) && SkyIslandGroundRing.Last == null, "invisible hazard rejected and cleaned");
            SkyIslandGroundRing.MissingMaterial = false;
            Console.WriteLine("Mode H match rules: " + _checks + " assertions passed (Unity/Stat host stubs).");
        }
    }
}
