using System;
using System.Reflection;
using BossRush;
using UnityEngine;
using UnityEngine.SceneManagement;

internal static class Program
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    private static int assertions;
    private static void Check(bool condition, string message)
    { assertions++; if (!condition) throw new InvalidOperationException(message); }
    private static void Near(float actual, float expected, string message)
    { Check(Math.Abs(actual - expected) <= 0.001f, message + ": expected " + expected + ", got " + actual); }
    private static T Get<T>(object target, string field) { return (T)target.GetType().GetField(field, Flags).GetValue(target); }
    private static void Set(object target, string field, object value) { target.GetType().GetField(field, Flags).SetValue(target, value); }
    private static void Invoke(object target, string method) { target.GetType().GetMethod(method, Flags).Invoke(target, null); }

    private sealed class Subject : IDisposable
    {
        internal readonly CharacterMainControl Player, Npc;
        internal readonly GreedyMimicEncounter Encounter;
        internal HealthSimpleBase Health { get { return Get<HealthSimpleBase>(Encounter, "health"); } }
        internal DamageReceiver Receiver { get { return Get<DamageReceiver>(Encounter, "receiver"); } }
        internal GreedyMimicVisual Visual { get { return Get<GreedyMimicVisual>(Encounter, "visual"); } }
        internal Subject(bool medicine)
        {
            FieldAdventureSupport.Notices.Clear(); Physics.Blocked = false;
            Player = new GameObject("MainPlayer").AddComponent<CharacterMainControl>(); Player.IsMainCharacter = true;
            Player.Health = Player.gameObject.AddComponent<Health>();
            Player.transform.position = new Vector3(0, 0, 5);
            Npc = new GameObject("Npc").AddComponent<CharacterMainControl>();
            CharacterMainControl.Main = Player;
            Encounter = new GreedyMimicEncounter(new ModBehaviour(), default(Scene), Vector3.zero, Player);
            if (medicine)
            {
                Encounter.Rules.Feed(GreedyMimicFood.Medicine);
                Invoke(Encounter, "CreateReceiver");
            }
            Near(Health.HealthValue, Encounter.Rules.MaximumHealth, "production receiver activates only after official health assembly");
            Check(Receiver.simpleHealth == Health && Health.dmgReceiver == Receiver, "production simple receiver is wired in both directions");
        }
        internal void Hit(float raw, float factor = 1, bool critical = false, CharacterMainControl source = null, bool fromMain = true)
        {
            Receiver.Hurt(new DamageInfo(fromMain ? Player : source)
            { damageValue = raw, critRate = critical ? 1 : -1, critDamageFactor = factor });
        }
        internal void BeginRecovery()
        {
            Hit(200); // 真实官方扣血唤醒，再经生产 BeginAttack 进入已学习的回血轮次。
            Set(Encounter, "attackTurn", 1); Set(Encounter, "cooldown", 0f);
            Encounter.Tick(0.01f, Player);
            Check(Get<GreedyMimicAttack>(Encounter, "attack") == GreedyMimicAttack.Recovery && Get<float>(Encounter, "warningLeft") > 0f,
                "production learned attack enters recovery charge");
            Near(Get<float>(Encounter, "recoveryDamage"), 0, "recovery begins with empty damage credit");
        }
        public void Dispose()
        {
            HealthSimpleBase previous = Health; DamageReceiver receiver = Receiver; GameObject root = Visual.Root;
            Encounter.Dispose(); Encounter.Dispose();
            Check(previous == null && receiver == null && root == null, "dispose destroys receiver components and visual subtree using Unity null semantics");
            Check(Get<HealthSimpleBase>(Encounter, "health") == null && !Get<bool>(Encounter, "subscribed"), "dispose releases event owner");
            UnityEngine.Object.Destroy(Player.gameObject); UnityEngine.Object.Destroy(Npc.gameObject);
            CharacterMainControl.Main = null;
        }
    }

    private static void RecoveryCase(string name, float raw, float factor, bool critical, float expectedDamage, bool interrupt)
    {
        using (var subject = new Subject(true))
        {
            subject.BeginRecovery();
            float initial = subject.Health.HealthValue;
            subject.Hit(raw, factor, critical);
            float afterHit = subject.Health.HealthValue;
            Near(initial - afterHit, expectedDamage, name + ": official health deduction");
            Near(Get<float>(subject.Encounter, "recoveryDamage"), Math.Max(0f, expectedDamage), name + ": interrupt credits real positive health loss");
            subject.Encounter.Tick(2.8f, subject.Player);
            Near(subject.Health.HealthValue, interrupt ? afterHit : afterHit + subject.Encounter.Rules.Recovery(afterHit), name + ": recovery result");
            bool notice = FieldAdventureSupport.Notices.Contains("The bottle jams! Healing interrupted.");
            Check(notice == interrupt, name + ": interrupt notice matches damage threshold");
            Console.WriteLine("PASS " + name + " damage=" + expectedDamage + " interrupted=" + notice);
        }
    }

    private static void RecoveryThresholds()
    {
        RecoveryCase("ordinary below", 44.99f, 1, false, 44.99f, false);
        RecoveryCase("ordinary exact", 45f, 1, false, 45f, true);
        RecoveryCase("ordinary above", 45.01f, 1, false, 45.01f, true);
        RecoveryCase("critical exact", 22.5f, 2, true, 45f, true);
        RecoveryCase("critical below", 22.49f, 2, true, 44.98f, false);
        RecoveryCase("critical above", 22.51f, 2, true, 45.02f, true);
        RecoveryCase("critical sixty", 30f, 2, true, 60f, true);
        RecoveryCase("unrolled factor ignored", 30f, 2, false, 30f, false);
        RecoveryCase("zero critical factor", 50f, 0, true, 0f, false);
        RecoveryCase("negative critical factor heals", 50f, -1, true, -50f, false);
        RecoveryCase("negative damage heals", -10f, 2, true, -20f, false);
        RecoveryCase("zero raw damage", 0f, 2, true, 0f, false);
    }

    private static void FilterAndAccumulation()
    {
        using (var subject = new Subject(true))
        {
            subject.BeginRecovery(); float initial = subject.Health.HealthValue;
            subject.Hit(1000f, 2, true, subject.Npc, false);
            subject.Hit(1000f, 2, true, null, false);
            Near(subject.Health.HealthValue, initial, "non-main and missing sources have official zero damage multiplier");
            Near(Get<float>(subject.Encounter, "recoveryDamage"), 0, "non-main and missing sources cannot interrupt recovery");
            subject.Hit(20f, 2, true); subject.Hit(4.99f);
            Near(Get<float>(subject.Encounter, "recoveryDamage"), 44.99f, "mixed actual damage accumulates just below threshold");
            subject.Encounter.Tick(0.1f, subject.Player);
            Check(!FieldAdventureSupport.Notices.Contains("The bottle jams! Healing interrupted."), "subthreshold accumulated damage keeps charging");
            subject.Hit(0.02f);
            Near(Get<float>(subject.Encounter, "recoveryDamage"), 45.01f, "mixed actual damage crosses threshold");
            subject.Encounter.Tick(0.1f, subject.Player);
            Check(FieldAdventureSupport.Notices.Contains("The bottle jams! Healing interrupted."), "accumulated critical and normal hits interrupt");
            float credit = Get<float>(subject.Encounter, "recoveryDamage");
            subject.Hit(10f);
            Near(Get<float>(subject.Encounter, "recoveryDamage"), credit, "damage outside charge does not enter recovery credit");
        }
        using (var subject = new Subject(true))
        {
            subject.BeginRecovery(); float initial = subject.Health.HealthValue;
            subject.Health.onlyReceiveExplosion = true; subject.Hit(100f, 2, true);
            Near(subject.Health.HealthValue, initial, "official rejected damage never reaches owner");
            Near(Get<float>(subject.Encounter, "recoveryDamage"), 0, "official rejected damage earns no interrupt credit");
        }
        using (var subject = new Subject(false))
        {
            subject.Hit(10f, 0, true);
            Check(!Get<bool>(subject.Encounter, "fighting"), "zero applied damage cannot wake the encounter");
            subject.Hit(-10f);
            Check(!Get<bool>(subject.Encounter, "fighting"), "healing cannot wake the encounter");
            subject.Hit(10f);
            Check(Get<bool>(subject.Encounter, "fighting"), "ordinary positive damage wakes the encounter");
        }
    }

    private static void BiteRange()
    {
        foreach (float distance in new[] { 31.99f, 32f, 32.01f, 40f, 45f, 45.01f })
            using (var subject = new Subject(false))
            {
                subject.Player.transform.position = new Vector3(0, 0, distance);
                Invoke(subject.Encounter, "BeginCombat"); Set(subject.Encounter, "cooldown", 0f);
                subject.Encounter.Tick(0.01f, subject.Player); subject.Encounter.Tick(1.4f, subject.Player);
                bool inRange = distance <= 45f;
                Check(subject.Player.Health.DamageCalls == (inRange ? 1 : 0), "production bite range at " + distance);
                Check(subject.Visual.TongueStrikes == (inRange ? 1 : 0), "production tongue invocation shares bite gate at " + distance);
                if (inRange)
                {
                    Near(subject.Visual.TongueTarget.z, distance, "production tongue receives full locked bite endpoint at " + distance);
                    Near(subject.Player.Health.LastDamage, 20f, "production bite preserves damage at " + distance);
                }
            }
        using (var subject = new Subject(false))
        {
            subject.Player.transform.position = new Vector3(0, 0, 40);
            Invoke(subject.Encounter, "BeginCombat"); Set(subject.Encounter, "cooldown", 0f);
            subject.Encounter.Tick(0.01f, subject.Player);
            subject.Player.transform.position = new Vector3(0, 0, 45.01f);
            subject.Encounter.Tick(1.4f, subject.Player);
            Check(subject.Player.Health.DamageCalls == 0 && subject.Visual.TongueStrikes == 0, "leaving combat range cancels locked bite");
        }
    }

    private static void Main()
    {
        RecoveryThresholds(); FilterAndAccumulation(); BiteRange();
        Console.WriteLine("PASS FieldAdventureMimicRuntime: " + assertions + " assertions; real Encounter, Rules, official HealthSimpleBase and DamageReceiver; host/visual are stubs.");
    }
}
