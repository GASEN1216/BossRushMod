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

    private static void Begin(Subject subject, Vector3 player)
    {
        subject.Player.transform.position = player;
        Invoke(subject.Encounter, "BeginCombat"); Set(subject.Encounter, "cooldown", 0f);
        subject.Encounter.Tick(0.01f, subject.Player);
    }

    private static void TongueAndPounce()
    {
        foreach (float distance in new[] { 3f, 8.99f })
            using (var subject = new Subject(false))
            {
                Begin(subject, new Vector3(0, 0, distance));
                Check(Get<GreedyMimicAttack>(subject.Encounter, "attack") == GreedyMimicAttack.Bite && Get<float>(subject.Encounter, "warningLeft") > 0f
                    && subject.Visual.LaneShows == 1, "close bite telegraphs a locked tongue lane at " + distance);
                subject.Encounter.Tick(0.8f, subject.Player);
                Check(subject.Player.Health.DamageCalls == 1 && subject.Visual.TongueStrikes == 1, "tongue lash lands inside its lane at " + distance);
                Near(subject.Visual.TongueTarget.z, distance, "tongue strikes the locked endpoint at " + distance);
                Near(subject.Player.Health.LastDamage, 20f, "tongue keeps the bite damage budget at " + distance);
            }
        using (var subject = new Subject(false))
        {
            Begin(subject, new Vector3(0, 0, 6f));
            subject.Player.transform.position = new Vector3(1.5f, 0, 6f);
            subject.Encounter.Tick(0.8f, subject.Player);
            Check(subject.Player.Health.DamageCalls == 0 && subject.Visual.TongueStrikes == 1, "stepping out of the locked lane dodges the tongue");
        }
        using (var subject = new Subject(false))
        {
            Begin(subject, new Vector3(0, 0, 12f));
            Check(Get<GreedyMimicAttack>(subject.Encounter, "attack") == GreedyMimicAttack.Pounce && subject.Visual.CircleShows == 1,
                "a far bite turn becomes a telegraphed pounce");
            subject.Encounter.Tick(1.06f, subject.Player);
            Check(Get<float>(subject.Encounter, "leapLeft") > 0f && subject.Player.Health.DamageCalls == 0, "airborne pounce deals no damage before landing");
            for (int i = 0; i < 6; i++) subject.Encounter.Tick(0.1f, subject.Player);
            Near(subject.Visual.Root.transform.position.z, 12f, "pounce lands on the locked landing point");
            Check(subject.Visual.LeapFrames > 0 && subject.Visual.Slams == 1 && subject.Player.Health.DamageCalls == 1, "landing slam hits a player inside the ring");
            Near(subject.Player.Health.LastDamage, 26f, "unfed pounce damage budget");
        }
        using (var subject = new Subject(false))
        {
            Begin(subject, new Vector3(0, 0, 30f));
            subject.Encounter.Tick(1.06f, subject.Player);
            for (int i = 0; i < 6; i++) subject.Encounter.Tick(0.1f, subject.Player);
            Near(subject.Visual.Root.transform.position.z, GreedyMimicRules.MaximumLeap, "a long pounce is capped at the maximum leap");
            Check(subject.Player.Health.DamageCalls == 0, "a landing ring that misses the player deals no damage");
        }
        using (var subject = new Subject(false))
        {
            GreedyMimicTerrain.RejectLanding = true;
            try
            {
                Begin(subject, new Vector3(0, 0, 20f));
                Check(Get<float>(subject.Encounter, "warningLeft") == 0f && subject.Visual.CircleShows == 0 && subject.Player.Health.DamageCalls == 0,
                    "no valid landing produces no phantom attack");
            }
            finally { GreedyMimicTerrain.RejectLanding = false; }
        }
        using (var subject = new Subject(false))
        {
            Begin(subject, new Vector3(0, 0, 6f));
            subject.Player.transform.position = new Vector3(0, 0, 45.01f);
            subject.Encounter.Tick(0.8f, subject.Player);
            Check(subject.Player.Health.DamageCalls == 0 && subject.Visual.TongueStrikes == 0, "leaving combat range cancels a locked tongue");
        }
    }

    private static void ShellArmor()
    {
        DamageReceiver receiver;
        using (var subject = new Subject(false))
        {
            receiver = subject.Receiver;
            var closed = new DamageInfo(subject.Player) { damageValue = 100f };
            subject.Visual.MouthOpen = false; ReceiverDamageScaling.Apply(receiver, ref closed);
            Near(closed.damageValue, 100f * GreedyMimicRules.ShellArmor, "a closed iron shell only takes the armored share");
            var open = new DamageInfo(subject.Player) { damageValue = 100f };
            subject.Visual.MouthOpen = true; ReceiverDamageScaling.Apply(receiver, ref open);
            Near(open.damageValue, 100f, "an open mouth takes full damage");
            var heal = new DamageInfo(subject.Player) { damageValue = -50f };
            subject.Visual.MouthOpen = false; ReceiverDamageScaling.Apply(receiver, ref heal);
            Near(heal.damageValue, -50f, "armor never scales healing");
        }
        var after = new DamageInfo(null) { damageValue = 100f };
        ReceiverDamageScaling.Apply(receiver, ref after);
        Near(after.damageValue, 100f, "disposing the encounter unregisters its armor");
        Check(!ReceiverDamageScaling.IsArmed, "no stale armor registrations remain after dispose");
    }

    private static void FlyingVolley()
    {
        using (var subject = new Subject(false))
        {
            subject.Encounter.Rules.Feed(GreedyMimicFood.Ammunition); subject.Encounter.Rules.Feed(GreedyMimicFood.Ammunition);
            Invoke(subject.Encounter, "CreateReceiver");
            subject.Player.transform.position = new Vector3(0, 0, 10f);
            Invoke(subject.Encounter, "BeginCombat"); Set(subject.Encounter, "attackTurn", 1); Set(subject.Encounter, "cooldown", 0f);
            subject.Encounter.Tick(0.01f, subject.Player);
            Check(Get<GreedyMimicAttack>(subject.Encounter, "attack") == GreedyMimicAttack.Volley && subject.Visual.LaneShows == 5, "learned volley telegraphs five lanes");
            subject.Encounter.Tick(1.35f, subject.Player);
            Check(subject.Visual.Muzzles == 1 && subject.Player.Health.DamageCalls == 0, "shells leave the muzzle without instant lane damage");
            Check(Get<float>(subject.Encounter, "warningLeft") > 0f && subject.Visual.LaneShows == 10, "two ammunition meals re-aim a second wave");
            for (int i = 0; i < 40; i++) subject.Encounter.Tick(0.05f, subject.Player);
            Check(subject.Visual.Muzzles == 2 && subject.Player.Health.DamageCalls == 2, "each flying wave can hit a standing player once");
            Near(subject.Player.Health.LastDamage, 26f, "two ammunition meals volley damage");
            Check(subject.Visual.ShellPops >= 2, "shells burst on hit or at range");
        }
        using (var subject = new Subject(false))
        {
            subject.Encounter.Rules.Feed(GreedyMimicFood.Ammunition); Invoke(subject.Encounter, "CreateReceiver");
            subject.Player.transform.position = new Vector3(0, 0, 10f);
            Invoke(subject.Encounter, "BeginCombat"); Set(subject.Encounter, "attackTurn", 1); Set(subject.Encounter, "cooldown", 0f);
            subject.Encounter.Tick(0.01f, subject.Player); subject.Encounter.Tick(1.35f, subject.Player);
            subject.Player.transform.position = new Vector3(1.05f, 0, 10f);
            for (int i = 0; i < 30; i++) subject.Encounter.Tick(0.05f, subject.Player);
            Check(subject.Player.Health.DamageCalls == 0, "sidestepping after release dodges the flying shells");
        }
    }

    private static void Main()
    {
        RecoveryThresholds(); FilterAndAccumulation(); TongueAndPounce(); FlyingVolley(); ShellArmor();
        Console.WriteLine("PASS FieldAdventureMimicRuntime: " + assertions + " assertions; real Encounter, Rules, official HealthSimpleBase and DamageReceiver; host/visual are stubs.");
    }
}
