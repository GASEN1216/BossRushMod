using System;
using BossRush;
using UnityEngine;

internal static class Program
{
    private static int assertions;
    private static void Check(bool value, string message)
    { assertions++; if (!value) { Console.Error.WriteLine("FAIL assertion: " + message); throw new InvalidOperationException(message); } }
    private static int Successes
    { get { return FieldAdventureSupport.Messages.FindAll(s => s.StartsWith("Test passed!")).Count; } }
    private sealed class Subject : IDisposable
    {
        internal readonly CharacterMainControl Player;
        internal readonly ItemAgent_Gun Gun;
        internal readonly DangerousPrototypeRuntimeModule Runtime;
        internal Subject(DangerousPrototypeKind kind)
        {
            Check(CharacterMainControl.HoldSubscribers == 0 && ItemAgent_Gun.ShootSubscribers == 0, "previous lease leaves no subscriptions");
            Physics.Reset(); ModBehaviour.Errors.Clear(); FieldAdventureSupport.Messages.Clear();
            FieldAdventureSupport.Allowed = true; BossRushUI.Paused = false; SceneLoader.IsSceneLoading = false;
            DangerousPrototypeVisuals.MainBursts = DangerousPrototypeVisuals.BombletBursts = DangerousPrototypeVisuals.Disposals = 0;
            Player = new CharacterMainControl { Aim = new Vector3(0f, 0f, 10f), Team = Teams.player };
            Gun = new ItemAgent_Gun { Holder = Player };
            Gun.muzzle.position = new Vector3(0f, 0.7f, 1f);
            Runtime = new DangerousPrototypeRuntimeModule(Player, Gun, kind);
            Check(Runtime.Active && Runtime.LeaseSubscribed && Runtime.RigVisible, "real BeginLease owns an active rig");
            Check(CharacterMainControl.HoldSubscribers == 1 && ItemAgent_Gun.ShootSubscribers == 1, "real BeginLease subscribes both named handlers once");
        }
        internal void Tick(float dt) { Runtime.OnUpdate(dt, dt); }
        public void Dispose()
        {
            Runtime.Cleanup(); Runtime.Cleanup();
            Check(CharacterMainControl.HoldSubscribers == 0 && ItemAgent_Gun.ShootSubscribers == 0, "repeated cleanup releases all lease handlers");
            Check(!Runtime.HasBorrower && !Runtime.HasGunOrItem && !Runtime.HasVisuals && Runtime.Shells == 0 && Runtime.Bomblets == 0,
                "repeated cleanup releases all lease resources");
            Check(ModBehaviour.Errors.Count == 0, "production handlers ran without swallowed errors: " + string.Join(";", ModBehaviour.Errors));
        }
    }
    private static Health Target(Vector3 point)
    {
        var health = new Health();
        Physics.Targets.Add(new Collider { bounds = new Bounds { center = point }, Receiver = new DamageReceiver { Team = Teams.wolf, health = health } });
        return health;
    }
    private static void MainTargets(int count)
    { for (int i = 0; i < count; i++) Target(new Vector3(0f, 0f, 10f)); }
    private static void Completed(Subject subject, string context)
    {
        Check(subject.Runtime.Completed && !subject.Runtime.Active, context + ": actual damage reaches completion");
        Check(!subject.Runtime.LeaseSubscribed && CharacterMainControl.HoldSubscribers == 0 && ItemAgent_Gun.ShootSubscribers == 0,
            context + ": completion releases both event subscriptions");
        Check(!subject.Runtime.HasBorrower && !subject.Runtime.HasGunOrItem && !subject.Runtime.RigVisible,
            context + ": completion releases borrower, gun, item and hides the rig");
        Check(Successes == 1, context + ": completion announces success once");
    }
    private static void SynchronousCompletion()
    {
        using (var subject = new Subject(DangerousPrototypeKind.JetCannon))
        {
            MainTargets(6); subject.Gun.Fire();
            Completed(subject, "synchronous jet");
            subject.Gun.Fire(); subject.Tick(0.1f); subject.Tick(0.1f);
            Check(Successes == 1, "later updates do not repeat synchronous completion");
            Check(subject.Runtime.HasVisuals, "synchronous completion keeps the short visual tail");
            subject.Tick(0.5f);
            Check(!subject.Runtime.HasVisuals && DangerousPrototypeVisuals.Disposals == 1, "synchronous tail is disposed once");
            Check(subject.Runtime.Claim() && !subject.Runtime.Claim(), "synchronous completion remains claimable exactly once");
        }
    }
    private static void AsynchronousMainCompletion()
    {
        using (var subject = new Subject(DangerousPrototypeKind.GrandFinale))
        {
            MainTargets(10); subject.Gun.Fire();
            Check(subject.Runtime.Shells == 1 && !subject.Runtime.Completed, "a fired shell does not finish before landing");
            subject.Tick(0.84f);
            Check(subject.Runtime.Progress == 0 && DangerousPrototypeVisuals.MainBursts == 0, "airborne shell has not damaged or completed");
            subject.Tick(0.02f);
            Completed(subject, "asynchronous main explosion");
            Check(subject.Runtime.Shells == 0 && subject.Runtime.Bomblets == 12 && subject.Runtime.HasVisuals,
                "completion preserves all launched bomblets and the short visual tail");
            Physics.Targets.Clear();
            Health tailTarget = Target(new Vector3(2.2f, 0.2f, 10f));
            subject.Gun.Fire(); subject.Tick(0.8f); subject.Tick(0.1f);
            Check(DangerousPrototypeVisuals.MainBursts == 1 && DangerousPrototypeVisuals.BombletBursts == 12 && tailTarget.CurrentHealth < 1000f,
                "already launched fireworks finish and damage after lease completion");
            Check(subject.Runtime.Shells == 0 && subject.Runtime.Bomblets == 0 && !subject.Runtime.HasVisuals,
                "firework tail drains without launching another shell");
            Check(Successes == 1 && subject.Runtime.Progress == 10, "later explosions and updates cannot repeat completion");
            Check(subject.Runtime.Claim() && !subject.Runtime.Claim(), "asynchronous completion remains claimable exactly once");
        }
    }
    private static void AsynchronousBombletCompletion()
    {
        using (var subject = new Subject(DangerousPrototypeKind.GrandFinale))
        {
            MainTargets(9); subject.Gun.Fire(); subject.Tick(0.84f); subject.Tick(0.02f);
            Check(subject.Runtime.Active && subject.Runtime.Progress == 9 && Successes == 0, "nine main hits keep the trial active");
            Physics.Targets.Clear(); Target(new Vector3(2.2f, 0.2f, 10f));
            subject.Tick(0.2f);
            Completed(subject, "asynchronous bomblet explosion");
            Check(subject.Runtime.Bomblets > 0 && subject.Runtime.Bomblets < 12, "remaining bomblets survive a sub-explosion completion");
            subject.Tick(1f); subject.Tick(0.1f);
            Check(subject.Runtime.Bomblets == 0 && Successes == 1, "sub-explosion completion stays idempotent after the tail");
        }
    }
    private static void Boundaries()
    {
        using (var subject = new Subject(DangerousPrototypeKind.GrandFinale))
        {
            MainTargets(10); subject.Gun.Fire();
            BossRushUI.Paused = true; subject.Tick(1f);
            Check(subject.Runtime.Progress == 0 && subject.Runtime.Shells == 1 && Successes == 0, "pause freezes pending firework completion");
            BossRushUI.Paused = false;
            subject.Player.Hold(null); subject.Tick(0.84f); subject.Tick(0.02f);
            Completed(subject, "completion while holstered");
        }
        using (var subject = new Subject(DangerousPrototypeKind.GrandFinale))
        {
            MainTargets(10); subject.Gun.Fire(); subject.Runtime.Cancel(); subject.Tick(1f);
            Check(!subject.Runtime.Active && !subject.Runtime.Completed && Successes == 0,
                "cancelled trial cannot complete from an already fired shell");
            Check(DangerousPrototypeVisuals.MainBursts == 1 && DangerousPrototypeVisuals.BombletBursts == 12,
                "returning the rig preserves already fired fireworks");
        }
        using (var subject = new Subject(DangerousPrototypeKind.GrandFinale))
        {
            MainTargets(10); subject.Gun.Fire(); GameObject pendingShell = subject.Runtime.ShellObject;
            Check(pendingShell != null, "scene cleanup begins with a real pending shell object");
            FieldAdventureSupport.Allowed = false; subject.Tick(0.9f);
            Check(!subject.Runtime.Active && !subject.Runtime.Completed && Successes == 0 && subject.Runtime.Shells == 0,
                "raid cleanup cancels the shell before it can announce completion");
            Check(pendingShell == null, "scene cleanup destroys the shell using Unity destroyed-object null semantics");
            FieldAdventureSupport.Allowed = true; subject.Tick(1f);
            Check(DangerousPrototypeVisuals.MainBursts == 0, "cleared shells never explode after scene cleanup");
        }
        using (var subject = new Subject(DangerousPrototypeKind.JetCannon))
        {
            subject.Tick(DangerousPrototypeRules.TrialSeconds);
            Check(!subject.Runtime.Active && !subject.Runtime.Completed && !subject.Runtime.LeaseSubscribed && Successes == 0,
                "timeout releases the lease without success");
            Check(FieldAdventureSupport.Messages.FindAll(s => s.StartsWith("Prototype recalled.")).Count == 1,
                "timeout announces recall once");
        }
    }
    private static void Main()
    {
        // 先执行异步场景，确保旧生产代码在真实达标后的退订断言转红。
        AsynchronousMainCompletion(); AsynchronousBombletCompletion(); SynchronousCompletion(); Boundaries();
        Console.WriteLine("PASS FieldAdventurePrototypeCompletion: " + assertions + " assertions; real lease, shot, firework, completion, tail and cleanup paths; Unity/host stubs.");
    }
}
