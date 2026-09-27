using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BossRush
{
    internal static class Program
    {
        private static int assertions;
        private static void Check(bool condition, string id)
        {
            assertions++;
            if (!condition) throw new Exception("ASSERT " + id);
        }
        private static void Same(object expected, object actual, string id) { Check(ReferenceEquals(expected, actual), id); }
        private static void Position(Vector3 expected, Vector3 actual, string id) { Check(expected == actual, id); }
        private static void Events(string expected, string id) { Check(string.Join(",", Probe.Events) == expected, id); Probe.Events.Clear(); }

        private static void MapQueries(string path)
        {
            BossRushMapRuntime.Initialize(path);
            var maps = BossRushMapRuntime.GetAllMapConfigs();
            Check(string.Join(",", maps.Select(x => x.sceneName)) == "Gamma,Alpha,Beta,Omega", "map-order-sort-and-tie");
            var beta = BossRushMapRuntime.GetMapConfigBySceneName("bEtA");
            Same(maps[2], beta, "map-name-case-insensitive");
            Same(maps[1], BossRushMapRuntime.GetMapConfigBySceneID("shared-id"), "scene-id-first-ordered-match");
            Check(BossRushMapRuntime.GetMapConfigBySceneID("SHARED-ID") == null, "scene-id-case-sensitive");
            foreach (var name in new[] { null, "", "unknown" })
            {
                Check(BossRushMapRuntime.GetMapConfigBySceneName(name) == null, "map-name-missing");
                Check(BossRushMapRuntime.GetMapConfigBySceneID(name) == null, "scene-id-missing");
            }
            maps[0] = null;
            Check(BossRushMapRuntime.GetAllMapConfigs()[0].sceneName == "Gamma", "enumeration-array-independent");
            Same(beta.spawnPoints, BossRushMapRuntime.GetSpawnPointsForScene("Beta"), "map-spawn-array-identity");
            var fallback = new Vector3(235.48f, -7.99f, 202.41f);
            foreach (var name in new[] { "Beta", "Gamma", "Alpha", "unknown" })
            {
                SceneManager.Name = name;
                var expected = name == "Beta" ? new Vector3(11, 12, 13) : name == "Gamma" ? new Vector3(31, 32, 33) : fallback;
                Position(expected, BossRushMapRuntime.GetCurrentSceneDefaultPosition(), "current-default-" + name);
                Position(expected, BossRushMapRuntime.GetDefaultPositionForScene(name), "named-default-" + name);
            }
            Position(new Vector3(21, 22, 23), BossRushMapRuntime.ResolveArenaSignPosition(beta, new Vector3(70, 80, 90)), "sign-config-precedence");
            Position(new Vector3(68, 80, 91), BossRushMapRuntime.ResolveArenaSignPosition(null, new Vector3(70, 80, 90)), "sign-player-offset");
            Position(new Vector3(68, 80, 91), BossRushMapRuntime.ResolveArenaSignPosition(BossRushMapRuntime.GetMapConfigBySceneName("Alpha"), new Vector3(70, 80, 90)), "sign-missing-offset");

            var first = new BossRushMapRuntime();
            var second = new BossRushMapRuntime();
            var dynamic = new[] { new Vector3(90, 91, 92) };
            SceneManager.Name = "Beta";
            Same(beta, BossRushMapRuntime.GetCurrentMapConfig(), "active-scene-config");
            Check(first.IsValidBossRushArenaScene("Beta") && first.IsCurrentSceneValidBossRushArena(), "arena-valid-map");
            first.SetCurrentMapSpawnPoints(dynamic);
            Same(dynamic, first.GetCurrentSceneSpawnPoints(), "dynamic-array-identity");
            Same(beta.spawnPoints, second.GetCurrentSceneSpawnPoints(), "map-owner-isolation");
            dynamic[0] = new Vector3(94, 95, 96);
            Position(dynamic[0], first.GetCurrentSceneSpawnPoints()[0], "dynamic-mutation-observed");
            SceneManager.Name = "Gamma";
            Same(dynamic, first.GetCurrentSceneSpawnPoints(), "dynamic-priority-across-scene");
            Same(BossRushMapRuntime.GetSpawnPointsForScene("Gamma"), second.GetCurrentSceneSpawnPoints(), "unoverridden-owner-live-scene");
            first.SetCurrentMapSpawnPoints(new Vector3[0]);
            Same(second.GetCurrentSceneSpawnPoints(), first.GetCurrentSceneSpawnPoints(), "empty-dynamic-falls-back");
            first.SetCurrentMapSpawnPoints(null);
            Same(second.GetCurrentSceneSpawnPoints(), first.GetCurrentSceneSpawnPoints(), "null-dynamic-falls-back");
            SceneManager.Name = "unknown";
            Check(!first.IsCurrentSceneValidBossRushArena() && !first.IsValidBossRushArenaScene("unknown"), "unknown-arena-invalid");
            Check(first.GetCurrentSceneSpawnPoints() == null && BossRushMapRuntime.GetSpawnPointsForScene("unknown") == null, "unknown-spawns-null");
        }

        private static void ArenaState()
        {
            var first = new WavesArenaRuntimeModule(new ModBehaviour());
            var second = new WavesArenaRuntimeModule(new ModBehaviour());
            int? configured = null;
            int configReads = 0;
            BossRushSignInteractable sign = null;
            int signReads = 0;
            first.BindHostStateServices(() => { configReads++; return configured; },
                () => { signReads++; return sign; }, () => Probe.Record("clear:" + first.IsActive));
            second.BindHostStateServices(() => 17, () => null, () => Probe.Record("other:" + second.IsActive));
            Check(!first.IsActive && !second.IsActive && first.PlayerCharacter == null, "arena-initial-owner-state");
            first.SetBossRushRuntimeActive(true);
            Events("clear:True,dragon", "arena-activate-state-before-clear-before-subscribe");
            Check(first.IsActive && !second.IsActive, "arena-active-owner-isolation");
            first.SetBossRushRuntimeActive(false);
            Events("clear:False,mutators,ui,cash", "arena-disable-cleanup-order");
            first.SetBossRushRuntimeActive(false);
            Events("clear:False,mutators,ui,cash", "arena-repeat-disable-retains-cleanup");
            second.SetBossRushRuntimeActive(true);
            Events("other:True,dragon", "arena-second-callback-owner");
            Check(!first.IsActive && second.IsActive, "arena-second-owner-independent");

            first.InfiniteHellWaveIndex = 8; first.InfiniteHellCashPool = 9000000000L; first.InfiniteHellMilestoneRewardTier = 4;
            configured = 19;
            first.ConfigureBossRushMode(-4, false);
            Check(first.BossesPerWave == 1 && !first.InfiniteHellMode && configReads == 0 && signReads == 0, "normal-mode-clamp-and-query-short-circuit");
            Check(first.InfiniteHellWaveIndex == 8 && first.InfiniteHellCashPool == 9000000000L && first.InfiniteHellMilestoneRewardTier == 4, "normal-mode-retains-infinite-progress");
            first.ConfigureBossRushMode(6, false);
            Check(first.BossesPerWave == 6 && configReads == 0, "normal-mode-uses-argument");
            configured = null;
            first.ConfigureBossRushMode(5, true);
            Check(first.BossesPerWave == 5 && first.InfiniteHellMode, "infinite-null-config-fallback");
            Check(first.InfiniteHellWaveIndex == 0 && first.InfiniteHellCashPool == 0L && first.InfiniteHellMilestoneRewardTier == 0, "infinite-progress-reset");
            Check(signReads == 1, "infinite-null-sign-single-read");
            configured = 0;
            first.ConfigureBossRushMode(4, true);
            Check(first.BossesPerWave == 4, "infinite-zero-config-fallback");
            configured = -2;
            first.ConfigureBossRushMode(0, true);
            Check(first.BossesPerWave == 1, "infinite-negative-config-clamped-fallback");
            configured = 23;
            sign = new BossRushSignInteractable();
            first.ConfigureBossRushMode(2, true);
            Check(first.BossesPerWave == 23 && sign.AmmoOptions == 1, "infinite-positive-config-and-ammo");
            Events("ammo", "infinite-ammo-single-action");
            configured = 31;
            var replacement = new BossRushSignInteractable { ThrowOnRefill = true };
            sign = replacement;
            first.InfiniteHellWaveIndex = 9; first.InfiniteHellCashPool = 99; first.InfiniteHellMilestoneRewardTier = 2;
            first.ConfigureBossRushMode(2, true);
            Check(first.BossesPerWave == 31 && replacement.AmmoOptions == 1 && first.InfiniteHellWaveIndex == 0 && first.InfiniteHellCashPool == 0 && first.InfiniteHellMilestoneRewardTier == 0, "infinite-live-config-sign-and-failure-containment");
            Events("ammo", "infinite-refill-failure-contained");
            second.ConfigureBossRushMode(2, true);
            Check(second.BossesPerWave == 17 && first.BossesPerWave == 31, "arena-config-owner-isolation");
            WavesArenaRuntimeModule.ArenaPlanned = true;
            WavesArenaRuntimeModule.ArenaActive = false;
            Check(WavesArenaRuntimeModule.ArenaPlanned && !WavesArenaRuntimeModule.ArenaActive, "arena-static-flags-distinct");
            WavesArenaRuntimeModule.ArenaPlanned = false;
            WavesArenaRuntimeModule.ArenaActive = true;
            Check(!WavesArenaRuntimeModule.ArenaPlanned && WavesArenaRuntimeModule.ArenaActive, "arena-static-flags-writeback");
            WavesArenaRuntimeModule.ArenaActive = false;
        }

        private static void ReturnToStart()
        {
            Probe.Events.Clear();
            var host = new ModBehaviour();
            var arena = new WavesArenaRuntimeModule(host);
            var cached = new CharacterMainControl();
            var main = new CharacterMainControl();
            arena.PlayerCharacter = cached;
            arena.DemoChallengeStartPosition = new Vector3(45, 46, 47);
            CharacterMainControl.Current = main;
            arena.ReturnToBossRushStart();
            Position(new Vector3(45, 46, 47), main.transform.position, "return-main-priority");
            Check(main.SetPositionCalls == 1 && cached.SetPositionCalls == 0 && host.Messages.Count == 1, "return-main-not-cached-and-notice");
            Events("position,message", "return-position-before-message");
            UnityEngine.Object.Destroy(main.gameObject);
            Check(main == null && main.gameObject.Destroyed, "return-scene-destroy-cascades-to-character");
            arena.ReturnToBossRushStart();
            Position(arena.DemoChallengeStartPosition, cached.transform.position, "return-destroyed-main-cache-fallback");
            Check(cached.SetPositionCalls == 1, "return-cache-used-after-scene-destroy");
            Events("position,message", "return-cache-notice-order");
            CharacterMainControl.ThrowMain = true;
            cached.ThrowSetPosition = true;
            arena.DemoChallengeStartPosition = new Vector3(55, 56, 57);
            arena.ReturnToBossRushStart();
            Position(arena.DemoChallengeStartPosition, cached.transform.position, "return-getter-and-setter-fallback");
            Events("position,message", "return-setter-failure-still-notifies");
            CharacterMainControl.ThrowMain = false;
            CharacterMainControl.Current = null;
            cached.ThrowSetPosition = false;
            cached.transform.position = new Vector3(65, 66, 67);
            arena.DemoChallengeStartPosition = Vector3.zero;
            arena.ReturnToBossRushStart();
            Position(new Vector3(65, 66, 67), cached.transform.position, "return-zero-start-preserves-current-position");
            Events("position,message", "return-zero-start-setposition-path");
            int messages = host.Messages.Count;
            UnityEngine.Object.Destroy(cached.gameObject);
            arena.ReturnToBossRushStart();
            Check(host.Messages.Count == messages, "return-no-live-player-no-success-message");
            Events("", "return-no-player-no-actions");
        }

        private static void NpcPlacement()
        {
            var owner = new CommonNpcRuntimeModule();
            var other = new CommonNpcRuntimeModule();
            bool support = true, active = false, modeD = false, arena = false;
            var queries = new List<string>();
            owner.BindSpawnPointQueries(() => { queries.Add("support"); return support; },
                () => { queries.Add("active"); return active; },
                () => { queries.Add("modeD"); return modeD; },
                () => { queries.Add("arena"); return arena; });
            other.BindSpawnPointQueries(() => false, () => true, () => false, () => false);
            Check(!owner.ShouldUseBossRushCommonNPCSpawnPoints("Beta") && string.Join(",", queries) == "support", "npc-support-first-short-circuit");
            queries.Clear(); support = false;
            Check(!owner.ShouldUseBossRushCommonNPCSpawnPoints("Beta") && string.Join(",", queries) == "support,active,modeD,arena", "npc-all-inactive-four-live-queries");
            queries.Clear(); active = true;
            Check(owner.ShouldUseBossRushCommonNPCSpawnPoints("Beta") && string.Join(",", queries) == "support,active", "npc-active-short-circuit");
            queries.Clear(); active = false; modeD = true;
            Check(owner.ShouldUseBossRushCommonNPCSpawnPoints("Beta") && string.Join(",", queries) == "support,active,modeD", "npc-modeD-live-query");
            queries.Clear(); modeD = false; arena = true;
            Check(owner.ShouldUseBossRushCommonNPCSpawnPoints("Beta") && string.Join(",", queries) == "support,active,modeD,arena", "npc-arena-live-query");
            SceneManager.Name = "Beta";
            Check(owner.ShouldUseBossRushCommonNPCSpawnPoints(null) && owner.ShouldUseBossRushCommonNPCSpawnPoints(""), "npc-empty-scene-active-fallback");
            SceneManager.Name = "unknown";
            Check(!owner.ShouldUseBossRushCommonNPCSpawnPoints(null) && owner.ShouldUseBossRushCommonNPCSpawnPoints("Beta"), "npc-explicit-scene-priority");
            var normal = new[] { new Vector3(300, 301, 302) };
            NPCSpawnConfig.Points["Beta"] = normal;
            NPCSpawnConfig.Calls.Clear();
            Same(BossRushMapRuntime.GetSpawnPointsForScene("Beta"), CommonNpcRuntimeModule.GetSharedCommonNPCSpawnPointsForScene("Beta", owner), "npc-boss-map-precedes-normal");
            Check(NPCSpawnConfig.Calls.Count == 0, "npc-boss-map-short-circuits-normal-provider");
            support = true;
            Same(normal, CommonNpcRuntimeModule.GetSharedCommonNPCSpawnPointsForScene("Beta", owner), "npc-support-uses-normal-points");
            Check(NPCSpawnConfig.Calls.SequenceEqual(new[] { "Beta" }), "npc-normal-provider-receives-original-scene");
            Same(normal, CommonNpcRuntimeModule.GetSharedCommonNPCSpawnPointsForScene("Beta", null), "npc-null-owner-normal-points");
            NPCSpawnConfig.Points["Beta"] = new Vector3[0];
            Same(BossRushMapRuntime.GetSpawnPointsForScene("Beta"), CommonNpcRuntimeModule.GetSharedCommonNPCSpawnPointsForScene("Beta", owner), "npc-empty-normal-map-fallback");
            NPCSpawnConfig.Points["Beta"] = null;
            Same(BossRushMapRuntime.GetSpawnPointsForScene("Beta"), CommonNpcRuntimeModule.GetSharedCommonNPCSpawnPointsForScene("Beta", owner), "npc-null-normal-map-fallback");
            Check(CommonNpcRuntimeModule.GetSharedCommonNPCSpawnPointsForScene("unknown", owner) == null, "npc-missing-map-and-normal-null");
            Check(other.ShouldUseBossRushCommonNPCSpawnPoints("Beta") && !owner.ShouldUseBossRushCommonNPCSpawnPoints("Beta"), "npc-owner-query-isolation");
        }

        private static int Main(string[] args)
        {
            try
            {
                MapQueries(args[0]);
                ArenaState();
                ReturnToStart();
                NpcPlacement();
                Console.WriteLine("HostMapArenaOwners: PASS (" + assertions + " assertions)");
                return 0;
            }
            catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        }
    }
}
