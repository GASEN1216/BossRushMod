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
public static class Random { public static float Forced = 0f; public static float value { get { return Forced; } } public static float Range(float min, float max) { return min; } }
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
        internal bool Resolving(bool frost) { return frost ? frostBiteResolving : thunderBiteResolving; }
        internal void Reactivate() { BumpSetBonusGeneration(); ResetThunderChainState(); ResetFrostNovaState(); }
        // 故障注入：模拟同代请求被重复交付，独立验证结算入口的冷却保护。
        internal IEnumerator DuplicateBite(bool frost)
        {
            return frost ? FrostBiteStep(Target, new CharacterMainControl(), Vector3.zero, setBonusGeneration)
                : ThunderBiteStep(Vector3.zero, new CharacterMainControl(), setBonusGeneration);
        }
        private static float GetSetBonusElementDamagePortion(Health h, DamageInfo i, ElementTypes t) { return 0f; }
        private IEnumerator DelayedHeal(Health h, float amount) { yield return null; }
        // 模拟一次「主角普攻打中敌人」。effect=true 模拟 DoT / 套装自身伤害。
        internal void Hit(bool frost, bool effect = false, float damage = 10f)
        {
            DamageInfo info = new DamageInfo { isFromBuffOrEffect = effect, finalDamage = damage };
            if (frost) TryScheduleFrostBite(Target, info);
            else TryScheduleThunderBite(Target, info);
        }
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
        private bool TryResolveSetBonusEnemyTarget(Health health, DamageInfo damage, out CharacterMainControl victim, out Vector3 position)
        { victim = new CharacterMainControl(); position = new Vector3(); return ValidKill; }
        internal int ScanCap = int.MaxValue;
        private int ScanSetBonusEnemies(Vector3 center, float radius, CharacterMainControl except, int limit, List<Health> hits = null)
        { Scans++; LastLimit = limit; LastRadius = radius; int count = Math.Min(Math.Min(limit, Enemies.Length), ScanCap); for (int i = 0; i < count; i++) setBonusScanResults[i] = Enemies[i]; return count; }
        private void SpawnSetArc(Vector3 from, Vector3 to, int color, float a, float b) { }
        private void SpawnSetBurst(Vector3 origin, int color, float a, float b, int count) { }
        private void PlaySoundEffect(string effect) { LastSfx = effect; }
        public static void DevLog(string text) { }
        internal bool FreezeSucceeds = true;
        internal int FreezeAttempts;
        private bool TryApplyFrostFreeze(CharacterMainControl target) { FreezeAttempts++; return FreezeSucceeds; }
        internal string LastSfx;
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
        // 旧激活醒来后再次命中：不能清掉新激活请求的 pending 而排进第三条请求。
        BossRush.ModBehaviour host;
        foreach (bool frost in new[] { false, true })
        {
            string label = frost ? "frost" : "thunder";
            host = new BossRush.ModBehaviour();
            Time.time = 20f;
            host.Hit(frost);
            IEnumerator old = host.Scheduled.Dequeue();
            host.Reactivate();
            Time.time = 20.01f;
            host.Hit(frost);
            IEnumerator current = host.Scheduled.Dequeue();
            Time.time = 20.05f;
            Check(!old.MoveNext(), label + " old generation exits");
            Check(host.Scans == 0 && host.Target.Hits == 0, label + " old generation cannot scan or damage");
            host.Hit(frost);
            Check(host.Scheduled.Count == 0, label + " old generation cannot release current pending gate");
            Time.time = 20.06f;
            Check(!current.MoveNext() && (frost ? host.Target.Hits : host.Enemies[0].Hits) == 1,
                label + " current on-hit still resolves once after stale request exits");
            Check(!host.Resolving(frost), label + " current resolution releases resolving gate");

            // 即使上游重复交付，结算时的冷却也必须拒绝第二次伤害和扫描。
            host = new BossRush.ModBehaviour();
            Time.time = 30f;
            host.Hit(frost);
            IEnumerator duplicate = host.DuplicateBite(frost);
            Check(duplicate.MoveNext(), label + " duplicate delivery reaches the real delayed step");
            Time.time = 30.06f;
            host.Drain();
            Time.time = 30.10f;
            Check(!duplicate.MoveNext() && (frost ? host.Target.Hits : host.Enemies[0].Hits) == 1,
                label + " resolution cooldown rejects same-generation duplicate delivery");
            Check(host.Scans == (frost ? 0 : 1), label + " duplicate delivery does not rescan enemies");
            Time.time = 32f;
            host.Hit(frost); host.Drain();
            Check((frost ? host.Target.Hits : host.Enemies[0].Hits) == 2,
                label + " cooldown rejection does not leave future attacks pending");
        }

        foreach (bool frost in new[] { false, true })
        {
            string label = frost ? "frost" : "thunder";
            Time.time = 40f;
            host = new BossRush.ModBehaviour();
            host.Hit(frost);
            IEnumerator pending = host.Scheduled.Dequeue();
            host.Die(frost);
            Check(!pending.MoveNext(), label + " pending on-hit effect terminates after player death");
            Check(host.Scans == 0 && host.Target.Hits == 0, label + " player death cancels delayed damage");
        }

        foreach (bool frost in new[] { false, true })
        {
            string label = frost ? "frost" : "thunder";
            Time.time = 100f;
            host = new BossRush.ModBehaviour();

            host.Hit(frost, true);
            Check(host.Scheduled.Count == 0, label + " buff/effect damage never triggers the on-hit effect");
            host.Hit(frost, false, 0f);
            Check(host.Scheduled.Count == 0, label + " zero final damage never triggers the on-hit effect");
            host.ValidKill = false; host.Hit(frost); host.ValidKill = true;
            Check(host.Scheduled.Count == 0, label + " unrelated victim never triggers the on-hit effect");

            // 首次普攻命中即触发（不再需要攒击杀）
            host.Hit(frost);
            Check(host.Scheduled.Count == 1, label + " first normal hit triggers immediately");
            host.Drain();

            // 内置冷却：高射速的后续命中一律被挡住，与射速脱钩
            for (int i = 0; i < 20; i++) host.Hit(frost);
            Check(host.Scheduled.Count == 0, label + " internal cooldown blocks high rate of fire");
            Time.time += frost ? 1.2f : 1.5f;
            host.Hit(frost);
            Check(host.Scheduled.Count == 1, label + " becomes available again after the internal cooldown");
            host.Drain();
        }

        // 2026-09-20 第三轮：冷却必须在**效果真的落地**之后才扣，不能在排队时先扣。
        // 雷噬扫不到其它敌人 = 这一次不造成任何伤害，下一发普攻还应该能再试。
        Time.time = 500f;
        host = new BossRush.ModBehaviour();
        host.ScanCap = 0;
        host.Hit(false); host.Drain();
        Check(host.Scans == 1 && host.Enemies[0].TotalDamage == 0f, "thunder bite with nobody in range deals no damage");
        host.ScanCap = int.MaxValue;
        host.Hit(false);
        Check(host.Scheduled.Count == 1, "thunder bite empty scan does not consume the internal cooldown");
        host.Drain();
        Check(host.Enemies[0].TotalDamage == 7f, "thunder bite resolves on the very next hit after an empty scan");
        host.Hit(false);
        Check(host.Scheduled.Count == 0, "thunder bite consumes the cooldown once it actually arcs");

        // 霜噬：目标在延迟窗口内死掉 = 这一次不造成任何伤害，冷却同样不能被吃掉
        Time.time = 600f;
        host = new BossRush.ModBehaviour();
        host.Hit(true);
        host.Target.IsDead = true;
        host.Drain();
        Check(host.Target.Hits == 0, "frost bite skips a target that died inside the delay window");
        host.Target = new Health();
        host.Hit(true);
        Check(host.Scheduled.Count == 1, "frost bite dead-target abort does not consume the internal cooldown");
        host.Drain();
        Check(host.Target.Hits == 1, "frost bite resolves on the very next hit after an aborted one");

        // 冻结失败（抗冻目标）不得播放冻结音效：TryApplyFrostFreeze 现在回报真实结果
        Time.time = 700f;
        host = new BossRush.ModBehaviour();
        UnityEngine.Random.Forced = 0f;                 // 必定进入冻结分支
        host.FreezeSucceeds = false;
        host.Hit(true); host.Drain();
        Check(host.FreezeAttempts == 1 && host.LastSfx == null, "failed freeze plays no freeze sfx");
        Time.time += 2f;
        host.FreezeSucceeds = true;
        host.Hit(true); host.Drain();
        Check(host.LastSfx == BossRush.SetBonusSfx.FrostNova, "successful freeze plays the freeze sfx");
        UnityEngine.Random.Forced = 0f;

        // 冰霜：单目标、常数伤害、不扫描
        Time.time = 200f;
        host = new BossRush.ModBehaviour();
        UnityEngine.Random.Forced = 1f;                 // 高于冻结概率 -> 本次不冻结
        host.Hit(true); host.Drain();
        Check(host.Scans == 0, "frost bite never scans for extra targets");
        Check(host.Target.Hits == 1 && host.Target.TotalDamage == 5f, "frost bite deals one constant-damage tick to the struck target");

        // 雷霆：只打命中目标之外的敌人，常数伤害，目标上限与半径
        Time.time = 300f;
        host = new BossRush.ModBehaviour();
        host.Hit(false); host.Drain();
        Check(host.LastLimit == 2 && host.LastRadius == 4f, "thunder bite target and radius budget");
        Check(host.Enemies[0].TotalDamage == 7f && host.Enemies[2].Hits == 0, "thunder bite constant damage and scan cap");
        Check(host.Target.Hits == 0, "thunder bite never stacks extra damage on the struck target");
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
        Time.time = 400f;
        host = new BossRush.ModBehaviour();
        foreach (Health enemy in host.Enemies) enemy.KillOnHit = true;
        host.Hit(false); host.Drain();
        Check(host.Scans == 1, "thunder bite lethal hit cannot continue into another hop");
        Console.WriteLine("SetBonusCoroutines: " + (checkedCount - failed) + " PASS / " + failed + " FAIL");
        return failed == 0 ? 0 : 1;
    }
}
