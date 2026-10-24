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
            internal Vector3 ReliablePoint = new Vector3(2, 0, 0);
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
            private bool Reliable(out Vector3 position) { ReliableCalls++; position = ReliablePoint; return ReliableAvailable; }
            internal void Tick(float seconds)
            { Time.deltaTime = seconds; Time.time += seconds; Time.unscaledTime += seconds; Runtime.UpdateEnemyRecoveryMonitor(); }
        }
        private static int assertions;
        private static void Check(bool value, string message)
        { assertions++; if (!value) throw new Exception(message); }
        private static void Reset()
        {
            Time.time = 0; Time.deltaTime = 0; Time.unscaledTime = 0; Physics.Probes = 0; NavMesh.Probes = 0;
            Physics.Syncs = 0; Physics.ThrowOnSync = false; GameObject.TreeScans = 0; Debug.Warnings.Clear();
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
        private static bool Finite(Vector3 value)
        { return !float.IsNaN(value.x) && !float.IsInfinity(value.x) && !float.IsNaN(value.y) && !float.IsInfinity(value.y) && !float.IsNaN(value.z) && !float.IsInfinity(value.z); }
        private static bool Same(Vector3 a, Vector3 b)
        { return a.x == b.x && a.y == b.y && a.z == b.z; }
        private static bool Same(Quaternion a, Quaternion b)
        { return a.x == b.x && a.y == b.y && a.z == b.z && a.w == b.w; }
        private static CharacterMainControl TrackedZombie(Owner owner, string name)
        {
            var enemy = Enemy(name);
            enemy.gameObject.Add(new ZombieModeEnemyRuntimeMarker());
            owner.ZombieRun.RunOnlyObjects.Add(new ZombieModeRunOnlyRecord { Kind = ZombieModeRunOnlyObjectKind.Enemy, GameObject = enemy.gameObject });
            return enemy;
        }
        private static void InvalidTransformCases()
        {
            Action<Transform>[] faults =
            {
                t => t.position = new Vector3(float.NaN, 0, 0),
                t => t.position = new Vector3(0, float.NegativeInfinity, 0),
                t => t.rotation = new Quaternion(),
                t => t.rotation = new Quaternion(float.MaxValue, float.MaxValue, 0, 1),
                t => t.rotation = new Quaternion(float.NaN, 0, 0, 1),
                t => t.localScale = new Vector3(1, float.PositiveInfinity, 1),
                t => t.localScale = new Vector3(1e30f, 1, 1),
                t => t.localScale = new Vector3(1, float.NaN, 1)
            };
            for (int i = 0; i < faults.Length; i++)
            {
                Reset(); var owner = new Owner { ZombieActive = true }; var enemy = TrackedZombie(owner, "ZombieMode_Run13_fault" + i);
                Quaternion rotation = new Quaternion(0, .6f, 0, .8f); Vector3 scale = new Vector3(1.2f, .9f, .8f);
                enemy.transform.rotation = rotation; enemy.transform.localScale = scale;
                var dynamicBody = enemy.gameObject.Add(new Rigidbody { velocity = new Vector3(float.NaN, 0, 0), angularVelocity = new Vector3(0, float.PositiveInfinity, 0) });
                var child = new GameObject { name = "secondaryBody" }; child.transform.parent = enemy.transform;
                var childBody = child.Add(new Rigidbody { velocity = new Vector3(float.NaN, 2, 3) });
                enemy.Health.CurrentHealth = 17; enemy.HealOnTeleport = true;
                owner.Runtime.RegisterEnemyRecoveryAnchor(enemy, enemy.transform.position);
                faults[i](enemy.transform);
                enemy.movementControl.forceMove = true; enemy.movementControl.forceMoveVelocity = new Vector3(float.NaN, 0, 0);
                enemy.movementControl.Velocity = new Vector3(float.NaN, 0, 0); enemy.movementControl.MoveInput = new Vector3(float.NaN, 0, 0);
                enemy.Controller.PendingForces = new Vector3(float.NaN, 0, 0);
                enemy.Controller.PendingImpulses = new Vector3(0, float.NaN, 0);
                enemy.Controller.PendingLaunch = new Vector3(0, 0, float.NaN);
                owner.Tick(1);
                Check(enemy.Teleports == 1 && Same(enemy.transform.position, owner.ReliablePoint) && Finite(enemy.transform.position), "root fault " + i + " returns same entity to a reachable point");
                Check(Same(enemy.transform.rotation, rotation) && Same(enemy.transform.localScale, scale), "root fault " + i + " restores legal historical rotation and scale");
                Check(enemy.Health.CurrentHealth == 17 && owner.ZombieRun.RunOnlyObjects.Count == 1 && !enemy.Health.IsDead,
                    "root fault " + i + " keeps HP and registration without replacement or death");
                Check(!enemy.movementControl.forceMove && Same(enemy.movementControl.forceMoveVelocity, Vector3.zero) && Same(enemy.movementControl.Velocity, Vector3.zero) && Same(childBody.velocity, Vector3.zero) && Same(dynamicBody.angularVelocity, Vector3.zero), "root fault " + i + " clears official movement and all dynamic bodies");
                Check(enemy.Controller.ClearForces == 1 && Same(enemy.Controller.PendingForces, Vector3.zero) && Same(enemy.Controller.PendingImpulses, Vector3.zero) && Same(enemy.Controller.PendingLaunch, Vector3.zero) && enemy.PositionEvents == 0,
                    "root fault " + i + " clears accumulated forces without disable/enable or CharacterMainControl position events");
                Check(Debug.Warnings.Count == 1 && Debug.Warnings[0].Contains("node=.") && Debug.Warnings[0].Contains("instanceId=") && Debug.Warnings[0].Contains("velocityFinite=False") && Debug.Warnings[0].Contains("forceVelocityFinite=False") && Debug.Warnings[0].Contains("ZombieMode_Run13"), "root fault " + i + " records original bad state and run-bearing identity");
                enemy.movementControl.Advance(); enemy.Controller.Advance(); Check(Finite(enemy.transform.position), "cleared movement cannot reinfect the next tick");
                enemy.transform.rotation = new Quaternion(); owner.Tick(1);
                Check(enemy.Teleports == 2 && Same(enemy.transform.rotation, rotation) && Debug.Warnings.Count == 1,
                    "invalid transform bypasses ordinary four second fall cooldown and warnings stay throttled");
            }

            Reset(); var recoveryOwner = new Owner { ZombieActive = true }; var zombie = TrackedZombie(recoveryOwner, "child-chain");
            var bone = new GameObject { name = "ModelRoot" }; bone.transform.parent = zombie.transform;
            bone.transform.localPosition = new Vector3(0, 1, 0); bone.transform.localScale = new Vector3(2, 2, 2);
            var head = new GameObject { name = "HeadCollider" }; head.transform.parent = bone.transform; head.Add(new Collider()); head.Add(new HeadCollider());
            recoveryOwner.Runtime.RegisterEnemyRecoveryAnchor(zombie, zombie.transform.position); recoveryOwner.Tick(1);
            int scans = GameObject.TreeScans;
            bone.transform.localRotation = new Quaternion(float.NaN, 0, 0, 1);
            recoveryOwner.Tick(1); Check(zombie.Teleports == 0, "healthy root uses scheduled cached child-chain check");
            recoveryOwner.Tick(1);
            Check(zombie.Teleports == 1 && Same(bone.transform.localRotation, Quaternion.identity) && Same(bone.transform.localScale, new Vector3(2, 2, 2)) && Finite(head.transform.position), "bad collider ancestor is restored from a local snapshot");
            Check(GameObject.TreeScans == scans && Debug.Warnings[0].Contains("node=ModelRoot") && Debug.Warnings[0].Contains("localRotation=(NaN"), "cached child checks avoid tree scans and identify original broken ancestor");
            zombie.mainDamageReceiver.transform.localScale = new Vector3(float.NaN, 1, 1); recoveryOwner.Tick(2);
            Check(zombie.Teleports == 2 && Same(zombie.mainDamageReceiver.transform.localScale, Vector3.one), "body damage receiver has independent recovery coverage");

            Reset(); recoveryOwner = new Owner { ZombieActive = true, ReliableAvailable = false }; zombie = TrackedZombie(recoveryOwner, "pending");
            recoveryOwner.Runtime.RegisterEnemyRecoveryAnchor(zombie, zombie.transform.position);
            zombie.transform.position = new Vector3(float.NaN, 0, 0); recoveryOwner.Tick(1); recoveryOwner.Tick(1);
            Check(zombie.Teleports == 0 && float.IsNaN(zombie.transform.position.x) && Physics.Syncs == 0 && Physics.Probes == 0, "unreachable recovery cannot masquerade as an in-place repair or use ground-only fallback");
            recoveryOwner.ReliableAvailable = true; recoveryOwner.Tick(1);
            Check(zombie.Teleports == 1 && Finite(zombie.transform.position), "failed attempts cannot replace the last valid snapshot with NaN");

            Reset(); recoveryOwner = new Owner { ZombieActive = true }; zombie = TrackedZombie(recoveryOwner, "no snapshot");
            zombie.transform.position = new Vector3(float.NaN, 0, 0); recoveryOwner.Tick(1);
            Check(zombie.Teleports == 0 && recoveryOwner.ReliableCalls == 0, "enemy first seen corrupt has no invented historical pose");
            var firstSeenState = (EnemyRecoveryMonitor.EnemyRecoveryState)States(recoveryOwner.Runtime)[zombie];
            Check(!firstSeenState.invalidTransformRecoveryPending, "no-history corruption cannot latch recovery pending forever");
            zombie.transform.position = Vector3.zero; recoveryOwner.Tick(2);
            zombie.transform.rotation = new Quaternion(); recoveryOwner.Tick(1);
            Check(zombie.Teleports == 1 && Same(zombie.transform.rotation, Quaternion.identity),
                "external repair can establish a first legal snapshot for later corruption");

            for (int failure = 0; failure < 2; failure++)
            {
                Reset(); recoveryOwner = new Owner { ZombieActive = true }; zombie = TrackedZombie(recoveryOwner, "interrupted repair " + failure);
                zombie.Health.CurrentHealth = 17; zombie.HealOnTeleport = true;
                recoveryOwner.Runtime.RegisterEnemyRecoveryAnchor(zombie, zombie.transform.position);
                var interruptedState = (EnemyRecoveryMonitor.EnemyRecoveryState)States(recoveryOwner.Runtime)[zombie];
                zombie.transform.position = new Vector3(float.NaN, 0, 0);
                zombie.movementControl.Velocity = new Vector3(float.NaN, 0, 0);
                zombie.Controller.ThrowOnSafeTeleport = failure == 0; Physics.ThrowOnSync = failure == 1;
                recoveryOwner.Tick(1);
                Check(Finite(zombie.transform.position) && interruptedState.invalidTransformRecoveryPending && zombie.Health.CurrentHealth == 17,
                    "partial repair " + failure + " retains pending despite legal transforms and keeps HP");
                Check(Same(interruptedState.validPosition, Vector3.zero) && Same(zombie.movementControl.Velocity, Vector3.zero),
                    "partial repair " + failure + " retains historical snapshot and clears velocity before native teleport");
                zombie.Controller.ThrowOnSafeTeleport = false; Physics.ThrowOnSync = false;
                recoveryOwner.Tick(1);
                Check(zombie.Controller.SafeTeleports == 2 && !interruptedState.invalidTransformRecoveryPending && zombie.Health.CurrentHealth == 17 && Same(zombie.transform.position, recoveryOwner.ReliablePoint),
                    "partial repair " + failure + " retries next monitor tick and only complete sync clears pending");
            }

            Reset(); recoveryOwner = new Owner { ZombieActive = true }; zombie = TrackedZombie(recoveryOwner, "invalid candidate");
            recoveryOwner.Runtime.RegisterEnemyRecoveryAnchor(zombie, zombie.transform.position);
            recoveryOwner.ReliablePoint = new Vector3(float.NaN, 0, 0); zombie.transform.rotation = new Quaternion(); recoveryOwner.Tick(1);
            Check(zombie.Teleports == 0 && Same(zombie.transform.rotation, new Quaternion()), "even a selector success flag cannot admit a nonfinite target");

            Reset(); recoveryOwner = new Owner { DActive = true }; zombie = Enemy("tracked D"); recoveryOwner.D.Add(zombie);
            recoveryOwner.Runtime.RegisterEnemyRecoveryAnchor(zombie, zombie.transform.position);
            zombie.transform.position = new Vector3(float.PositiveInfinity, 0, 0); recoveryOwner.Tick(1);
            Check(zombie.Teleports == 1 && zombie.transform.position.x == 10, "other already tracked modes select nearest ground point using legal historical position");

            Reset(); recoveryOwner = new Owner { ZombieActive = true }; zombie = TrackedZombie(recoveryOwner, "normal");
            recoveryOwner.Runtime.RegisterEnemyRecoveryAnchor(zombie, zombie.transform.position);
            scans = GameObject.TreeScans;
            for (int i = 0; i < 8; i++) recoveryOwner.Tick(1);
            Check(zombie.Teleports == 0 && GameObject.TreeScans == scans && Debug.Warnings.Count == 0, "healthy enemies have no tree rescans, teleports, or diagnostics within cache lifetime");
            var unusedWeapon = new GameObject { name = "unused weapon collider" }; unusedWeapon.transform.parent = zombie.transform;
            unusedWeapon.Add(new Collider()); unusedWeapon.transform.localScale = new Vector3(float.NaN, 1, 1);
            var lateHead = new GameObject { name = "LateHead" }; lateHead.transform.parent = zombie.transform;
            lateHead.Add(new HeadCollider()); lateHead.Add(new Collider());
            Quaternion latestRotation = new Quaternion(.6f, 0, 0, .8f);
            zombie.transform.rotation = latestRotation; recoveryOwner.Tick(3);
            Check(zombie.Teleports == 0, "unrelated weapon colliders cannot trigger character recovery");
            lateHead.transform.localRotation = new Quaternion(); recoveryOwner.Tick(2);
            Check(zombie.Teleports == 1 && Same(lateHead.transform.localRotation, Quaternion.identity) && Same(zombie.transform.rotation, latestRotation),
                "late-created head colliders gain snapshots and current healthy root pose is retained");
            zombie.transform.localScale = new Vector3(0, -1, .00000001f); recoveryOwner.Tick(2);
            Check(zombie.Teleports == 1, "finite deliberate zero, negative, and small scale is not classified as NaN corruption");

            Reset(); recoveryOwner = new Owner { ZombieActive = true }; zombie = TrackedZombie(recoveryOwner, "late child under corrupt ancestor");
            bone = new GameObject { name = "ModelRoot" }; bone.transform.parent = zombie.transform;
            head = new GameObject { name = "KnownHead" }; head.transform.parent = bone.transform; head.Add(new HeadCollider()); head.Add(new Collider());
            recoveryOwner.Runtime.RegisterEnemyRecoveryAnchor(zombie, zombie.transform.position);
            lateHead = new GameObject { name = "UnsampledHead" }; lateHead.transform.parent = bone.transform; lateHead.Add(new HeadCollider()); lateHead.Add(new Collider());
            bone.transform.localScale = new Vector3(float.NaN, 1, 1);
            recoveryOwner.Tick(11);
            Check(zombie.Teleports == 1 && Finite(lateHead.transform.position) && Same(bone.transform.localScale, Vector3.one),
                "ancestor snapshot restores before an unsampled new head with inherited bad world transform");
            var state = (EnemyRecoveryMonitor.EnemyRecoveryState)States(recoveryOwner.Runtime)[zombie]; int cached = state.colliderTransforms.Count;
            lateHead.transform.parent = null; lateHead.transform.localRotation = new Quaternion(float.NaN, 0, 0, 1);
            recoveryOwner.Tick(10);
            Check(zombie.Teleports == 1 && state.colliderTransforms.Count == cached - 1,
                "detached corrupted collider is ignored and retired at hierarchy refresh");

            Reset(); recoveryOwner = new Owner { ZombieActive = true }; zombie = TrackedZombie(recoveryOwner, "reparented cached ancestor");
            var deepParent = zombie.transform;
            for (int i = 0; i < 3; i++)
            {
                var parent = new GameObject { name = "old-parent-" + i }; parent.transform.parent = deepParent; deepParent = parent.transform;
            }
            bone = new GameObject { name = "MovedBone" }; bone.transform.parent = deepParent;
            head = new GameObject { name = "KnownHead" }; head.transform.parent = bone.transform; head.Add(new HeadCollider()); head.Add(new Collider());
            recoveryOwner.Runtime.RegisterEnemyRecoveryAnchor(zombie, zombie.transform.position); recoveryOwner.Tick(1);
            bone.transform.parent = zombie.transform; bone.transform.localPosition = new Vector3(0, 2, 0); recoveryOwner.Tick(2);
            lateHead = new GameObject { name = "NewHeadAfterReparent" }; lateHead.transform.parent = bone.transform; lateHead.Add(new HeadCollider()); lateHead.Add(new Collider());
            bone.transform.localRotation = new Quaternion(float.NaN, 0, 0, 1); recoveryOwner.Tick(8);
            Check(zombie.Teleports == 1 && bone.transform.parent == zombie.transform && Same(bone.transform.localPosition, new Vector3(0, 2, 0)) && Finite(lateHead.transform.position),
                "current depth restores a reparented cached ancestor before its unsampled new head using the new parent snapshot");
        }
        private static void Main()
        {
            InvalidTransformCases();
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
