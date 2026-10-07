using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using Cysharp.Threading.Tasks;

namespace BossRush
{
    internal static class Program
    {
        private sealed class Owner
        {
            internal readonly Dictionary<string, CharacterRandomPreset> Presets = new Dictionary<string, CharacterRandomPreset>();
            internal readonly HashSet<CharacterMainControl> Owned = new HashSet<CharacterMainControl>();
            internal readonly Queue<EnemyPresetInfo> Random = new Queue<EnemyPresetInfo>();
            internal readonly ModeEFSpawnPostprocessScheduler Scheduler = new ModeEFSpawnPostprocessScheduler();
            internal readonly EnemySpawnRuntime Runtime;
            internal bool Active = true, DescendantSpawned, KingSpawned;
            internal int RandomCalls, SpecialCalls;
            internal CharacterMainControl LastCreated;
            internal Owner()
            {
                Scheduler.BindServices((c, p) => { Probe.Events.Add("materialize"); return true; },
                    (c, boss) => Probe.Events.Add("multiplier"), (c, n) => Probe.Events.Add("loot:" + n),
                    p => Probe.Events.Add("cleanup-plan"), c => Probe.Events.Add("untrack"));
                Runtime = new EnemySpawnRuntime(Scheduler);
                Runtime.BindPresetQueries(() => Presets, Roll, Roll, () => DescendantSpawned, () => KingSpawned);
                Runtime.BindSpecialBossServices(p => p.name == "descendant", p => p.name == "king", p => p.name == "witch",
                    p => p.name == "descendant" || p.name == "king" || p.name == "witch",
                    (pos, child, notify, defer, nonWave, active) =>
                    {
                        Check(!child && !notify, "descendant legacy flags");
                        return SpawnSpecial("descendant", defer, nonWave, active);
                    },
                    (pos, notify, defer, nonWave, active) => { Check(!notify, "king legacy flag"); return SpawnSpecial("king", defer, nonWave, active); },
                    (pos, notify, defer, nonWave, active) => { Check(!notify, "witch legacy flag"); return SpawnSpecial("witch", defer, nonWave, active); },
                    c => Cleanup(c, "descendant"), c => Cleanup(c, "king"), c => Cleanup(c, "witch"));
                Runtime.BindEquipmentServices(c => Probe.Events.Add("normalize"),
                    (c, wave, health, boss) => { Check(wave == 4 && health == 120 && boss, "equipment receives original spawn parameters"); Probe.Events.Add("equip"); },
                    (c, wave, health, boss) => { Probe.Events.Add("plan"); return new ModeDItemPool.SharedModeEnemyEquipmentMaterializationPlan(); },
                    (c, boss) => Probe.Events.Add("multiplier"), (c, count) => Probe.Events.Add("loot:" + count));
                Runtime.BindOwnedEnemyTracking(p => p.name == "ape", () => Owned);
            }
            private EnemyPresetInfo Roll() { RandomCalls++; return Random.Count == 0 ? null : Random.Dequeue(); }
            private UniTask<CharacterMainControl> SpawnSpecial(string kind, bool defer, bool nonWave, Func<bool> active)
            {
                SpecialCalls++; Probe.Events.Add("special:" + kind + ":" + defer + ":" + nonWave);
                Check(active != null, "special spawn receives validity callback");
                LastCreated = new CharacterMainControl(kind);
                return new UniTask<CharacterMainControl>(Task.FromResult(LastCreated));
            }
            private void Cleanup(CharacterMainControl c, string kind) { Probe.Events.Add("cleanup:" + kind); UnityEngine.Object.Destroy(c.gameObject); }
            internal EnemyPresetInfo Add(string name)
            {
                Presets[name] = new CharacterRandomPreset { name = name, Create = () => LastCreated = new CharacterMainControl(name) };
                return new EnemyPresetInfo { name = name, displayName = name };
            }
            internal Task<EnemySpawnCoreResult> Spawn(EnemyPresetInfo preset, EnemySpawnCoreOptions options = null,
                bool deferred = false, Func<EnemySpawnContext, bool> commit = null, bool loot = true)
            {
                return Runtime.SpawnEnemyCoreInternalAsync(preset, new Vector3(), true, () => Active,
                    waveIndex: 4, deferActivationUntilNextFrame: deferred, onCommit: commit,
                    skipBossRushLootTracking: !loot, options: options).AsTask();
            }
        }

        private static int assertions;
        private static void Check(bool value, string message)
        { assertions++; if (!value) throw new Exception(message + " / " + string.Join(",", Probe.Events)); }
        private static void Reset()
        { Probe.Events.Clear(); Probe.YieldGate = null; EnemySpawnRuntime.ManagedBossSpawnDispatcher = null; }
        private static void Main()
        {
            Reset(); var owner = new Owner(); var preset = owner.Add("ape");
            var result = owner.Spawn(preset, commit: c => { Probe.Events.Add("commit"); return true; }).Result;
            Check(result.success && result.actualPreset == preset && owner.Owned.Contains(result.context.character), "successful spawn returns identity and tracks owned ape");
            Check(Probe.Events.SequenceEqual(new[] { "create:ape", "yield", "normalize", "equip", "multiplier", "active:True", "wake", "mutator", "loot:3", "commit" }),
                "normal spawn preserves yield, activation, wake, mutator, loot and commit ordering");

            Reset(); var gate = new TaskCompletionSource<bool>(); Probe.YieldGate = gate;
            owner = new Owner(); preset = owner.Add("late"); var pending = owner.Spawn(preset);
            Check(!pending.IsCompleted && Probe.Events.SequenceEqual(new[] { "create:late", "yield" }), "ordinary spawn waits before touching equipment");
            owner.Active = false; gate.SetResult(true); result = pending.Result;
            Check(!result.success && result.failureReason == "模式结束" && owner.LastCreated == null,
                "invalidated spawn after yield destroys character and returns observable failure");
            Check(!Probe.Events.Contains("equip") && !Probe.Events.Contains("mutator"), "late result cannot configure or mutate ended run");

            foreach (bool modeF in new[] { false, true })
            {
                Reset(); owner = new Owner(); preset = owner.Add("disposed-" + modeF);
                var host = new ModBehaviour(); var e = new ModeERuntimeModule(); var f = new ModeFRuntimeModule();
                e.OnAwake(host); f.OnAwake(host); host.F = f;
                int token = modeF ? f.Begin() : e.Begin();
                Func<bool> productionGate = () => e.IsModeEOrModeFSpawnSessionStillValid(modeF ? token : 0, 17, modeF ? 0 : token, 17);
                var creation = new TaskCompletionSource<CharacterMainControl>(); owner.Presets[preset.name].PendingCreation = creation.Task;
                int commits = 0;
                pending = owner.Runtime.SpawnEnemyCoreInternalAsync(preset, new Vector3(), true, productionGate,
                    waveIndex: 4, onCommit: c => { commits++; return true; }).AsTask();
                Check(!pending.IsCompleted && productionGate(), "production E/F gate is live while real spawn core awaits factory F=" + modeF);
                if (modeF) f.OnDestroy(); else e.OnDestroy();
                Check(!productionGate(), "production OnDestroy closes shared spawn gate F=" + modeF);
                var lateCharacter = new CharacterMainControl(preset.name); creation.SetResult(lateCharacter); result = pending.Result;
                Check(!result.success && result.failureReason == "模式结束" && lateCharacter == null,
                    "shared production spawn core recycles factory completion after module destruction F=" + modeF);
                Check(commits == 0 && owner.RandomCalls == 0 && !Probe.Events.Contains("normalize") && !Probe.Events.Contains("equip")
                    && !Probe.Events.Contains("mutator") && !Probe.Events.Contains("loot:3"),
                    "disposed real gate prevents event commit, loot, mutation and retry random roll F=" + modeF);
            }

            Reset(); owner = new Owner(); preset = owner.Add("deferred");
            pending = owner.Spawn(preset, deferred: true, commit: c => { Probe.Events.Add("commit"); return true; });
            Check(!pending.IsCompleted && Probe.Events.SequenceEqual(new[] { "create:deferred", "yield", "normalize", "plan" }), "deferred spawn waits on real scheduler");
            owner.Scheduler.TickModeEFSpawnPostprocessScheduler(); result = pending.Result;
            Check(result.success && Probe.Events.IndexOf("materialize") < Probe.Events.IndexOf("multiplier")
                && Probe.Events.IndexOf("loot:3") < Probe.Events.IndexOf("commit"), "deferred completion flows through production postprocessing");

            Reset(); owner = new Owner(); preset = owner.Add("deferred-ended"); pending = owner.Spawn(preset, deferred: true);
            owner.Active = false; owner.Scheduler.TickModeEFSpawnPostprocessScheduler(); result = pending.Result;
            Check(!result.success && owner.LastCreated == null && Probe.Events.Skip(4).SequenceEqual(new[] { "cleanup-plan", "untrack", "destroy:deferred-ended" }),
                "scheduler rejects ended run and releases materialization before destruction");

            Reset(); owner = new Owner(); preset = owner.Add("held");
            result = owner.Spawn(preset, new EnemySpawnCoreOptions { HoldForExternalCommit = true, ApplySharedMutators = false },
                commit: c => { throw new Exception("external hold called legacy commit"); }, loot: false).Result;
            Check(result.success && result.context.character.Health.Invincible && !result.context.character.gameObject.Active,
                "external hold returns frozen character");
            Check(!Probe.Events.Contains("wake") && !Probe.Events.Contains("mutator") && !Probe.Events.Contains("loot:3"), "external hold respects caller mutator and loot policy");

            foreach (string kind in new[] { "descendant", "king", "witch" })
            {
                Reset(); owner = new Owner(); preset = new EnemyPresetInfo { name = kind, displayName = kind };
                result = owner.Spawn(preset, new EnemySpawnCoreOptions { SuppressWaveBossRegistration = true }, deferred: true).Result;
                Check(result.success && owner.SpecialCalls == 1 && Probe.Events.Contains("special:" + kind + ":True:True"), "special boss preserves delayed activation and wave isolation options");
                Check(!Probe.Events.Contains("yield") && !Probe.Events.Contains("equip") && !Probe.Events.Contains("multiplier"), "special boss avoids duplicate generic preparation");
                Reset(); owner = new Owner(); owner.Active = false; result = owner.Spawn(preset).Result;
                Check(!result.success && owner.LastCreated == null && Probe.Events.Contains("cleanup:" + kind), "special boss invalidation uses its original cleanup owner");
            }

            Reset(); owner = new Owner(); owner.DescendantSpawned = true; owner.KingSpawned = true;
            owner.Random.Enqueue(new EnemyPresetInfo { name = "descendant" }); owner.Random.Enqueue(new EnemyPresetInfo { name = "king" });
            owner.Random.Enqueue(new EnemyPresetInfo { name = "witch" }); owner.Random.Enqueue(null); owner.Random.Enqueue(null);
            result = owner.Spawn(null).Result;
            Check(!result.success && owner.RandomCalls == 5 && owner.SpecialCalls == 0, "retry limits preserve five random rolls and singleton rejection");
            Reset(); owner = new Owner(); result = owner.Spawn(null, new EnemySpawnCoreOptions { AllowRandomRetryFallback = false }).Result;
            Check(!result.success && owner.RandomCalls == 0 && owner.SpecialCalls == 0, "disabled retry never samples fallback preset");

            Reset(); owner = new Owner(); preset = new EnemyPresetInfo { name = "king" };
            var options = new EnemySpawnCoreOptions { ManagedBossContext = new object(), AllowRandomRetryFallback = false };
            result = owner.Spawn(preset, options).Result;
            Check(!result.success && owner.SpecialCalls == 0 && owner.RandomCalls == 0, "missing managed dispatcher fails closed without legacy spawn");
            var managedCharacter = new CharacterMainControl("managed"); var handle = new ManagedBossRuntimeHandle();
            EnemySpawnRuntime.ManagedBossSpawnDispatcher = (p, position, context, defer) =>
            {
                Check(ReferenceEquals(context, options.ManagedBossContext) && defer, "managed dispatcher receives original context and activation gate");
                return new UniTask<ManagedBossPrepareResult>(Task.FromResult(new ManagedBossPrepareResult { Character = managedCharacter, Handle = handle }));
            };
            options.HoldForExternalCommit = true; options.ApplySharedMutators = false;
            result = owner.Spawn(preset, options, deferred: true, commit: c => { throw new Exception("managed hold called legacy commit"); }).Result;
            Check(result.success && result.context.managedBossHandle == handle && result.context.character == managedCharacter,
                "managed success retains prepared handle and character");

            Reset(); owner = new Owner(); preset = owner.Add("callback"); int success = 0, failure = 0;
            owner.Runtime.SpawnEnemyCore(preset, new Vector3(), true, () => true,
                c => { success++; throw new Exception("consumer failed"); }, () => failure++, waveIndex: 4);
            Check(success == 1 && failure == 1, "legacy callback exception invokes failure exactly once");
            Reset(); owner = new Owner(); var other = new Owner(); preset = owner.Add("one"); var otherPreset = other.Add("two");
            Check(owner.Spawn(preset).Result.success && other.Spawn(otherPreset).Result.success
                && owner.Presets["one"].Calls == 1 && other.Presets["two"].Calls == 1, "separate runtime owners use their own preset providers");
            Console.WriteLine("EnemySpawnRuntime: PASS " + assertions + " assertions");
        }
    }
}
