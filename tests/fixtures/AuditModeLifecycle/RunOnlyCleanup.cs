using System;
using System.Collections.Generic;
using UnityEngine;

namespace BossRush
{
    internal enum ZombieModeFailureReason
    {
        PlayerDeath,
        ManualExit,
        SceneSwitched,
        UnexpectedSceneUnload,
        SuccessfulExtraction,
        Unknown
    }

    internal sealed class ZombieModeEnemyRuntimeMarker : UnityEngine.Object
    {
        internal int RunId;
        internal bool DeathSettled;
        internal bool RemovedFromRuntime;
    }

    internal sealed class ZombieModeRunState
    {
        internal int RunId;
        internal bool IsCleaningUp;
        internal readonly List<ZombieModeRunOnlyRecord> RunOnlyObjects = new List<ZombieModeRunOnlyRecord>();
    }

    public partial class ModBehaviour
    {
        private ZombieModeRuntimeModule zombieModeRuntimeModule;
        internal readonly List<string> Trace = new List<string>();
        internal void AttachTestModule(ZombieModeRuntimeModule module) { zombieModeRuntimeModule = module; }

        private void SettleZombieModeFailureInsuranceShell(int runId)
        {
            Trace.Add("settle:" + runId + ":run=" + (runStateForTest != null ? runStateForTest.RunId : 0));
        }

        private ZombieModeRunState runStateForTest;
        internal void AttachTestRun(ZombieModeRunState run) { runStateForTest = run; }

        private void RemoveZombieModeAttributeModifiers() { Trace.Add("remove-attributes"); }
        private void RemoveZombieModeOptionRuntimeEffects() { Trace.Add("remove-option-effects"); }
        private void CleanupZombieModeFortificationInteractionState() { Trace.Add("clear-fortification"); }
        private void ClearZombieModeSupportSpawnQueue() { Trace.Add("clear-support"); }
        private void ClearZombieModeEnemyInstanceIds() { Trace.Add("clear-enemy-ids"); }
        private void ClearZombieModeRewardShell() { Trace.Add("clear-reward"); }
        private void RestoreZombieModeMapIsolationShell() { Trace.Add("restore-isolation"); }

        internal static void DevLog(string message) { }

        internal void AddTestRecord(ZombieModeRunOnlyObjectKind kind, GameObject gameObject, UnityEngine.Object target, Action cleanup)
        {
            AddTestRecord(runStateForTest.RunId, kind, gameObject, target, cleanup);
        }

        internal void AddTestRecord(int runId, ZombieModeRunOnlyObjectKind kind, GameObject gameObject, UnityEngine.Object target, Action cleanup)
        {
            zombieModeRuntimeModule.RegisterZombieModeRunOnlyObject(runId, kind, gameObject, target, cleanup);
        }

        internal void PruneTestEnemyRecords(int runId) { PruneZombieModeRunOnlyEnemyRecords(runId); }
        internal void PruneTestUnknownRecords() { PruneZombieModeUnknownRunOnlyRecords(); }
        internal void RemoveTestRecord(UnityEngine.Object target) { RemoveZombieModeRunOnlyObjectRecord(target); }
        internal void CleanupTestRunOnlyState(ZombieModeFailureReason reason, bool destroyGameObjects)
        {
            CleanupZombieModeRunOnlyState(reason, destroyGameObjects);
        }
        internal bool ShouldSettleTestInsurance(ZombieModeFailureReason reason) { return ShouldSettleZombieModeFailureInsurance(reason); }
    }

    internal static class RunOnlyCleanupRegression
    {
        private static int checks;

        private static void Check(bool value, string message)
        {
            checks++;
            if (!value) throw new Exception("FAIL " + message);
            Console.WriteLine("PASS " + message);
        }

        private static ZombieModeRunOnlyRecord Record(int runId, ZombieModeRunOnlyObjectKind kind,
            GameObject gameObject, UnityEngine.Object target, Action cleanup)
        {
            return new ZombieModeRunOnlyRecord
            {
                RunId = runId,
                Kind = kind,
                GameObject = gameObject,
                Target = target,
                CleanupAction = cleanup
            };
        }

        internal static void Run()
        {
            var owner = new ModBehaviour();
            var run = new ZombieModeRunState { RunId = 41 };
            var module = new ZombieModeRuntimeModule(owner, run);
            owner.AttachTestModule(module);
            owner.AttachTestRun(run);

            owner.AddTestRecord(40, ZombieModeRunOnlyObjectKind.Projectile, new GameObject(), null, null);
            Check(run.RunOnlyObjects.Count == 0, "stale RunId cannot register a run-only object");

            owner.AddTestRecord(ZombieModeRunOnlyObjectKind.RewardUi, null, null, null);
            var currentRewardUi = new GameObject();
            owner.AddTestRecord(ZombieModeRunOnlyObjectKind.RewardUi, currentRewardUi, currentRewardUi, null);
            Check(run.RunOnlyObjects.Count == 1 && run.RunOnlyObjects[0].GameObject == currentRewardUi,
                "new reward UI registration prunes only the destroyed empty reward UI record");
            var retainedRewardUi = new GameObject();
            owner.AddTestRecord(ZombieModeRunOnlyObjectKind.RewardUi, retainedRewardUi, retainedRewardUi, null);
            Check(run.RunOnlyObjects.Count == 2, "live reward UI records remain in the authoritative run list");

            var removedObject = new GameObject();
            owner.AddTestRecord(ZombieModeRunOnlyObjectKind.TemporaryNpc, removedObject, removedObject,
                delegate { owner.Trace.Add("remove-action"); });
            owner.RemoveTestRecord(removedObject);
            Check(owner.Trace.Contains("remove-action") && !removedObject.Destroyed &&
                run.RunOnlyObjects.Count == 2, "individual record removal runs cleanup without destroying an owned object");

            owner.AddTestRecord(ZombieModeRunOnlyObjectKind.Unknown, null, null,
                delegate { owner.Trace.Add("unknown-action"); });
            owner.PruneTestUnknownRecords();
            Check(owner.Trace.Contains("unknown-action") && run.RunOnlyObjects.Count == 2,
                "unknown empty records are pruned from the sole RunOnlyObjects registry");

            var liveEnemyObject = new GameObject();
            var liveEnemy = new ZombieModeEnemyRuntimeMarker { RunId = 41 };
            run.RunOnlyObjects.Add(Record(41, ZombieModeRunOnlyObjectKind.Enemy, liveEnemyObject, liveEnemy, null));
            var settledEnemyObject = new GameObject();
            var settledEnemy = new ZombieModeEnemyRuntimeMarker { RunId = 41, DeathSettled = true };
            run.RunOnlyObjects.Add(Record(41, ZombieModeRunOnlyObjectKind.Enemy, settledEnemyObject, settledEnemy,
                delegate { owner.Trace.Add("settled-enemy"); }));
            owner.PruneTestEnemyRecords(41);
            Check(run.RunOnlyObjects.Count == 3 && run.RunOnlyObjects.Exists(record => record.Target == liveEnemy) &&
                owner.Trace.Contains("settled-enemy") && !settledEnemyObject.Destroyed,
                "enemy pruning keeps live markers and removes settled records without destroying GameObjects");

            run.RunOnlyObjects.Clear();
            owner.Trace.Clear();
            var cleanupObjects = new[] { new GameObject(), new GameObject(), new GameObject() };
            string[] recordNames = { "record-a", "record-b", "record-c" };
            for (int i = 0; i < cleanupObjects.Length; i++)
            {
                string recordName = recordNames[i];
                GameObject cleanupObject = cleanupObjects[i];
                run.RunOnlyObjects.Add(Record(41, ZombieModeRunOnlyObjectKind.Projectile, cleanupObject, cleanupObject,
                    delegate
                    {
                        owner.Trace.Add(recordName + ":run=" + run.RunId + ":cleaning=" + run.IsCleaningUp);
                    }));
            }

            ZombieModeRuntimeModule.TestNextRunId = 9;
            owner.CleanupTestRunOnlyState(ZombieModeFailureReason.ManualExit, true);
            string actualOrder = string.Join(",", owner.Trace);
            string expectedOrder = "settle:41:run=41,remove-attributes,remove-option-effects,clear-fortification,clear-support," +
                "record-c:run=-41:cleaning=True,record-b:run=-41:cleaning=True,record-a:run=-41:cleaning=True," +
                "clear-enemy-ids,clear-reward,restore-isolation";
            Check(actualOrder == expectedOrder, "failure cleanup preserves settlement, effect, invalidation, reverse-record and shell-cleanup order");
            Check(run.RunId == -41 && run.IsCleaningUp && ZombieModeRuntimeModule.TestNextRunId == 10,
                "run identity invalidates immediately before registered object callbacks");
            Check(run.RunOnlyObjects.Count == 0 && cleanupObjects[0].Destroyed && cleanupObjects[1].Destroyed && cleanupObjects[2].Destroyed,
                "terminal cleanup clears the registry after destroying run-owned objects");
            Check(owner.ShouldSettleTestInsurance(ZombieModeFailureReason.PlayerDeath) &&
                !owner.ShouldSettleTestInsurance(ZombieModeFailureReason.SuccessfulExtraction),
                "insurance settlement gate distinguishes failure from successful extraction");

            owner.Trace.Clear();
            run.RunId = 42;
            run.IsCleaningUp = false;
            owner.CleanupTestRunOnlyState(ZombieModeFailureReason.SuccessfulExtraction, true);
            Check(!owner.Trace.Exists(entry => entry.StartsWith("settle:")) && run.RunId == -42,
                "successful extraction skips failure insurance and still invalidates the run");
            Console.WriteLine("AuditModeLifecycle RunOnly cleanup: " + checks + " PASS / 0 FAIL");
        }
    }
}

namespace UnityEngine
{
    public class Object
    {
        public bool Destroyed;
        public static void Destroy(Object target) { if (!ReferenceEquals(target, null)) target.Destroyed = true; }
        public static bool operator ==(Object left, Object right)
        {
            bool leftNull = ReferenceEquals(left, null) || left.Destroyed;
            bool rightNull = ReferenceEquals(right, null) || right.Destroyed;
            return leftNull ? rightNull : !rightNull && ReferenceEquals(left, right);
        }
        public static bool operator !=(Object left, Object right) { return !(left == right); }
        public override bool Equals(object other) { return base.Equals(other); }
        public override int GetHashCode() { return base.GetHashCode(); }
    }

    public sealed class GameObject : Object
    {
        public T GetComponent<T>() where T : Object { return null; }
    }
}

public static class Program
{
    public static void Main() { BossRush.RunOnlyCleanupRegression.Run(); }
}
