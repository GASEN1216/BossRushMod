using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.AI;

namespace BossRush
{
    internal static class Program
    {
        private sealed class Owner
        {
            internal readonly EnemyRecoveryMonitor Runtime = new EnemyRecoveryMonitor();
            internal readonly List<CharacterMainControl> D = new List<CharacterMainControl>();
            internal readonly List<CharacterMainControl> E = new List<CharacterMainControl>();
            internal readonly List<CharacterMainControl> F = new List<CharacterMainControl>();
            internal readonly List<CharacterMainControl> G = new List<CharacterMainControl>();
            internal readonly ZombieModeRunState ZombieRun = new ZombieModeRunState();
            internal readonly ZombieModeRuntimeModule Zombie;
            internal readonly WavesArenaRuntimeModule Arena = new WavesArenaRuntimeModule();
            internal bool DActive, EActive, FActive, GActive, ZombieActive, ArenaActive;
            internal bool ThrowGQuery;
            internal bool ReliableAvailable = true;
            internal int GReads, MarkerAIReads, Pressure, ReliableCalls;
            internal Vector3[] Points = { new Vector3(10, 0, 0), new Vector3(20, 0, 0) };
            internal readonly Dictionary<Teams, List<Vector3>> Allocation = new Dictionary<Teams, List<Vector3>>();
            internal Owner()
            {
                Zombie = new ZombieModeRuntimeModule(ZombieRun);
                Zombie.BindEnemyRecoveryMonitor(Runtime); Arena.BindEnemyRecoveryMonitor(Runtime);
                Runtime.BindModeQueries(() => DActive, () => EActive, () => FActive, () => ArenaActive, () => ZombieActive,
                    () => { GReads++; if (ThrowGQuery) throw new Exception("G unavailable"); return GActive; });
                Runtime.BindTrackedEnemies(() => D, () => E, () => F, () => G,
                    Zombie.MonitorZombieModeEnemyRecovery, Arena.MonitorNormalBossRushRecovery);
                Runtime.BindSpawnPositions(() => Allocation, () => Points, () => Points, position => Points,
                    Zombie.AppendZombieModeRecoverySpawnCandidates, Reliable);
                Runtime.BindRecoveryPolicies(marker => ((ZombieModeEnemyRuntimeMarker)marker).IsBoss,
                    () => 100f, () => 5f,
                    (go, marker) => { MarkerAIReads++; return go.GetComponent<AICharacterController>(); },
                    enemy => Pressure++);
                Runtime.ClearEnemyRecoveryMonitorState();
            }
            private bool Reliable(out Vector3 position) { ReliableCalls++; position = new Vector3(2, 0, 0); return ReliableAvailable; }
            internal void Tick(float seconds)
            { Time.deltaTime = seconds; Time.time += seconds; Runtime.UpdateEnemyRecoveryMonitor(); }
        }
        private static int assertions;
        private static void Check(bool value, string message)
        { assertions++; if (!value) throw new Exception(message); }
        private static void Reset()
        {
            Time.time = 0; Time.deltaTime = 0; Physics.Probes = 0; NavMesh.Probes = 0;
            Physics.Ground = pos => pos.x >= 5 ? (Vector3?)new Vector3(pos.x, 0, pos.z) : null;
            NavMesh.Ground = pos => null;
            CharacterMainControl.Main = new CharacterMainControl("player", new Vector3());
        }
        private static IDictionary States(EnemyRecoveryMonitor runtime)
        { return (IDictionary)typeof(EnemyRecoveryMonitor).GetField("enemyRecoveryStates", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(runtime); }
        private static void AppendE(Owner owner)
        { typeof(EnemyRecoveryMonitor).GetMethod("AppendModeERecoverySpawnCandidates", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(owner.Runtime, null); }
        private static CharacterMainControl Enemy(string name, float y = 0)
        { return new CharacterMainControl(name, new Vector3(0, y, 0)); }
        private static void Main()
        {
            Reset(); var owner = new Owner(); var enemy = Enemy("inactive");
            owner.Runtime.RegisterEnemyRecoveryAnchor(enemy, enemy.transform.position); owner.Tick(1);
            Check(States(owner.Runtime).Count == 0 && Physics.Probes == 0 && owner.GReads == 1, "inactive recovery clears state before geometry work");
            owner.DActive = true; owner.D.Add(enemy); owner.ThrowGQuery = true; owner.Tick(.5f);
            Check(States(owner.Runtime).Count == 0, "subsecond tick returns before enumerating enemies");
            owner.Tick(.5f); Check(States(owner.Runtime).Count == 1, "one second sampling survives unavailable G query");

            Reset(); owner = new Owner { DActive = true, EActive = true }; var d = Enemy("D"); var e = Enemy("E"); owner.D.Add(d); owner.E.Add(e);
            owner.Tick(1); Check(States(owner.Runtime).Contains(d) && !States(owner.Runtime).Contains(e), "D retains priority over E during overlap");
            UnityEngine.Object.Destroy(d.gameObject); owner.Tick(1); Check(States(owner.Runtime).Count == 0, "destroyed components are removed from recovery state");

            Reset(); owner = new Owner { DActive = true }; enemy = Enemy("falling"); owner.D.Add(enemy);
            enemy.Health.CurrentHealth = 23; enemy.HealOnTeleport = true;
            var body = enemy.gameObject.Add(new Rigidbody { velocity = new Vector3(2, 3, 4), angularVelocity = new Vector3(5, 6, 7) });
            var ai = enemy.gameObject.Add(new AICharacterController());
            owner.Runtime.RegisterEnemyRecoveryAnchor(enemy, enemy.transform.position);
            for (int i = 1; i <= 3; i++) { enemy.transform.position = new Vector3(0, -3 * i, 0); owner.Tick(1); }
            Check(enemy.Teleports == 1 && enemy.transform.position.x == 10 && Math.Abs(enemy.transform.position.y - .15f) < .001f,
                "three continuous falling samples recover to nearest alternate grounded point");
            Check(enemy.Health.CurrentHealth == 23 && body.velocity.x == 0 && body.angularVelocity.y == 0,
                "recovery preserves damaged health and clears dynamic velocity");
            Check(ai.noticed && ai.Targets == 1 && ai.Notices == 1 && ai.forceTracePlayerDistance == 500,
                "recovered ordinary enemy regains player aggro");
            for (int i = 1; i <= 3; i++) { enemy.transform.position = new Vector3(0, -3 * i, 0); owner.Tick(1); }
            Check(enemy.Teleports == 1, "four second recovery cooldown blocks immediate repeated falls");
            enemy.transform.position = new Vector3(0, -12, 0);
            owner.Tick(1); Check(enemy.Teleports == 2, "recovery becomes eligible at exact cooldown boundary");

            Reset(); owner = new Owner { DActive = true }; enemy = Enemy("stationary"); owner.D.Add(enemy);
            CharacterMainControl.Main.transform.position = new Vector3(0, 100, 0);
            owner.Runtime.RegisterEnemyRecoveryAnchor(enemy, enemy.transform.position);
            owner.Tick(10); Check(enemy.Teleports == 0, "stationary enemy without ground is not recovered from player height alone");
            Physics.Ground = pos => pos.x == 0 ? (Vector3?)new Vector3(0, 1, 0) : new Vector3(pos.x, 0, 0);
            owner.Tick(1); Check(enemy.Teleports == 1, "stationary enemy below sampled ground recovers after delay");

            Reset(); owner = new Owner { ZombieActive = true }; enemy = new CharacterMainControl("distant", new Vector3(101, 0, 0));
            var marker = enemy.gameObject.Add(new ZombieModeEnemyRuntimeMarker());
            ai = enemy.gameObject.Add(new AICharacterController());
            owner.ZombieRun.RunOnlyObjects.Add(new ZombieModeRunOnlyRecord { Kind = ZombieModeRunOnlyObjectKind.Enemy, GameObject = enemy.gameObject });
            owner.Tick(1); Check(marker.Owner == enemy && owner.ZombieRun.RunOnlyObjects[0].Target == marker, "zombie enumeration backfills marker and owner once found");
            owner.Tick(4); Check(owner.ReliableCalls == 0, "distant recovery waits full five seconds");
            owner.Tick(1); Check(owner.ReliableCalls == 1 && enemy.transform.position.x == 2 && owner.MarkerAIReads == 1 && ai.noticed,
                "eligible distant zombie uses reliable near-player point and marker AI");
            marker.IsBoss = true; enemy.transform.position = new Vector3(101, 0, 0); owner.Tick(6); owner.Tick(6);
            Check(owner.ReliableCalls == 1, "boss marker is excluded from distant recovery");

            Reset(); owner = new Owner { EActive = true }; owner.Allocation[Teams.wolf] = new List<Vector3>();
            AppendE(owner); int probes = Physics.Probes; AppendE(owner);
            Check(probes == 2 && Physics.Probes == probes, "same E source array reuses validated geometry cache");
            owner.Points = new[] { new Vector3(30, 0, 0) }; AppendE(owner);
            Check(Physics.Probes == probes + 1, "replaced source array rebuilds geometry cache");
            owner.Runtime.ClearEnemyRecoveryMonitorState(); AppendE(owner);
            Check(Physics.Probes == probes + 2, "clear invalidates source identity cache");

            Reset(); owner = new Owner { ZombieActive = true }; enemy = Enemy("falling zombie");
            enemy.gameObject.Add(new ZombieModeEnemyRuntimeMarker());
            owner.ZombieRun.RunOnlyObjects.Add(new ZombieModeRunOnlyRecord { Kind = ZombieModeRunOnlyObjectKind.Enemy, GameObject = enemy.gameObject });
            owner.Tick(1);
            for (int i = 1; i <= 3; i++) { enemy.transform.position = new Vector3(0, -3 * i, 0); owner.Tick(1); }
            Check(owner.ReliableCalls == 1 && enemy.Teleports == 1 && enemy.transform.position.x == 2,
                "Falling zombie must use the same validated reachable selector as distant recovery");
            Reset(); owner = new Owner { ZombieActive = true, ReliableAvailable = false }; enemy = Enemy("unreachable zombie");
            enemy.gameObject.Add(new ZombieModeEnemyRuntimeMarker());
            owner.ZombieRun.RunOnlyObjects.Add(new ZombieModeRunOnlyRecord { Kind = ZombieModeRunOnlyObjectKind.Enemy, GameObject = enemy.gameObject });
            owner.Tick(1);
            for (int i = 1; i <= 3; i++) { enemy.transform.position = new Vector3(0, -3 * i, 0); owner.Tick(1); }
            Check(owner.ReliableCalls == 1 && enemy.Teleports == 0,
                "No reachable Zombie point must not fall through to generic raycast-only map points");

            Reset(); owner = new Owner { ArenaActive = true }; var a = Enemy("single"); var b = Enemy("multi");
            owner.Arena.CurrentBoss = a; owner.Arena.BossesPerWave = 1; owner.Tick(1);
            Check(States(owner.Runtime).Contains(a), "arena adapter monitors current single boss");
            owner.Arena.BossesPerWave = 2; owner.Arena.CurrentWaveBosses.Add(b); owner.Tick(1);
            Check(States(owner.Runtime).Contains(b) && !States(owner.Runtime).Contains(a), "arena adapter changes to wave list and retires unseen boss state");

            Reset(); owner = new Owner(); var candidates = new List<Vector3>(); bool validated = true;
            owner.ZombieRun.EffectiveSpawnPoints = new List<ZombieModeSpawnPoint> { new ZombieModeSpawnPoint { Position = new Vector3(8, 0, 0) } };
            owner.ZombieRun.SpawnPoints = new List<ZombieModeSpawnPoint> { new ZombieModeSpawnPoint { Position = new Vector3(9, 0, 0) } };
            owner.Zombie.AppendZombieModeRecoverySpawnCandidates(candidates, ref validated);
            Check(candidates.Count == 1 && candidates[0].x == 8 && !validated, "zombie effective candidates retain priority and reset prevalidation");
            candidates.Clear(); owner.ZombieRun.EffectiveSpawnPoints.Clear(); owner.Zombie.AppendZombieModeRecoverySpawnCandidates(candidates, ref validated);
            Check(candidates.Count == 1 && candidates[0].x == 9, "empty effective candidates fall back to original zombie spawn points");

            Reset(); owner = new Owner { FActive = true }; enemy = Enemy("spawn", -5);
            owner.Runtime.RegisterEnemyRecoveryAnchor(enemy, new Vector3(0, 5, 0)); enemy.Health.CurrentHealth = 31; enemy.HealOnTeleport = true;
            owner.Runtime.ValidateAndFixBossPosition(enemy);
            Check(enemy.Teleports == 1 && enemy.Health.CurrentHealth == 31 && owner.Pressure == 1, "spawn void validation shares recovery state and F pressure policy");
            owner.FActive = false; owner.EActive = true; enemy.transform.position = new Vector3(0, -10, 0);
            ai = enemy.gameObject.Add(new AICharacterController()); owner.Runtime.ValidateAndFixBossPosition(enemy);
            Check(!ai.noticed && owner.Pressure == 1, "E recovery preserves faction aggro policy");
            var delayed = owner.Runtime.DelayedBossPositionValidation(enemy, 2.5f);
            Check(delayed.MoveNext() && ((WaitForSeconds)delayed.Current).Seconds == 2.5f, "delayed validation yields original duration");
            UnityEngine.Object.Destroy(enemy.gameObject); int before = Physics.Probes;
            Check(!delayed.MoveNext() && Physics.Probes == before, "destroyed delayed-validation target exits without probing");
            Console.WriteLine("EnemyRecoveryRuntime: PASS " + assertions + " assertions");
        }
    }
}
