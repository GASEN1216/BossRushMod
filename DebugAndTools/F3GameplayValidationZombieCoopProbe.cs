#if BOSSRUSH_DEV
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Duckov.Economy;
using ItemStatsSystem;
using Saves;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BossRush
{
    // On-demand observation for the real three-process bridge. No timer, UI, RPC or state mutation.
    // The caller persists the returned text; sampling never marks an acceptance case PASS.
    public static class F3ZombieCoopProbe
    {
        public static string Capture(string stepId)
        {
            if (!ModBehaviour.DevModeEnabled) throw new InvalidOperationException("dev_build_required");
            const string marker = "BossRush_Validation_DedicatedSlot_v1";
            if (!SavesSystem.KeyExisits(marker) || SavesSystem.Load<string>(marker) != "v1:slot:" + SavesSystem.CurrentSlot)
                throw new InvalidOperationException("dedicated_test_slot_required");
            if (string.IsNullOrEmpty(stepId) || stepId.Length > 100) throw new ArgumentException("step_id_required");
            return ZombieModeRuntimeModule.CaptureCoopValidationSnapshot(stepId);
        }
    }

    [Serializable]
    internal sealed class F3ZombieCoopSnapshot
    {
        public string Step, Utc, Mvid, Scene, Room, SceneToken, LocalPlayer, Asset, LocalRunAsset, Lifecycle, Combat, Starter;
        public int Frame, Slot, Generation, Run, Wave, Kills, KillTarget, Living, Normal, PendingSpawns, Bosses, Points;
        public int LocalRun, RewardWave, CompletedRewardWave, ModalLeases, RunOnlyObjects, PendingStars, DropsAwaitingResult, RemainingSelections;
        public long Money, Investment, WithheldCash, KillSequence, ZoneSequence;
        public float Realtime, TimeScale, Preparation, SafeRadius, PortableRadius, InsuranceRatio;
        public Vector3 SafeCenter, PortableCenter, ExtractionPosition;
        public bool Connected, Host, BoundScene, SessionReady, WorldAuthority, FullSuiteRunning, LeaseCurrent, LeaseRevoked, EconomyReady, RuntimePresent;
        public bool Cleaning, AdmissionPending, StarterChosen, StarterGranted, PersonalDeath, Extracting, Extracted, HasExtractionArea;
        public bool SafeActive, PortableActive, InvitationHeld, CashHeld, ResourcesFinalized, RewardUiToken;
        public F3ZombieCoopActor Local;
        public F3ZombieCoopActor[] Players, Enemies;
        public F3ZombieCoopItem[] Inventory, Drops;
        public string[] StarterReady, Skipped, RewardSelections, RewardOptions, DropRequests, DropResults, Refunds, Wallets, KillOptions, ZoneFacts, ZonePending, Deliveries, Npcs;
        public int LifestealChance, LifestealHeal, Siphon, SecondWind, DoomPulse, DoomKills;
    }

    [Serializable]
    internal sealed class F3ZombieCoopActor
    {
        public string Id, Asset, FrozenAsset, Name, Team, TargetPlayer, Kind, HostAliveAsset;
        public int Instance, Run, HostAliveGeneration;
        public bool Local, Connected, Body, Active, Dead, Invincible, StarterReady, HostAliveFact, Settled, HealthPresent, InventoryPresent;
        // Remote replica HP is diagnostic only. Actual damage/heal assertions use each process's Local.
        public float Health, WalkSpeed, RunSpeed, Slow;
        public Vector3 Position, TargetPosition;
    }

    [Serializable]
    internal sealed class F3ZombieCoopItem
    {
        public string Path, Key, Room, SceneToken, Holder, Nonce, Slot;
        public int Instance, Type, Count, Run, Generation, GrantedCount, Finalized;
        public bool Body, GroundAgent, InInventory, Equipped, Consumed, NativeLease;
        public Vector3 Position;
    }

    internal sealed partial class ZombieModeRuntimeModule
    {
        internal static string CaptureCoopValidationSnapshot(string stepId)
        {
            var data = new F3ZombieCoopSnapshot { Step = stepId, Utc = DateTime.UtcNow.ToString("O"),
                Mvid = typeof(ModBehaviour).Module.ModuleVersionId.ToString(), Frame = Time.frameCount,
                Realtime = Time.realtimeSinceStartup, TimeScale = Time.timeScale, Slot = SavesSystem.CurrentSlot,
                Scene = SceneManager.GetActiveScene().name, Room = BossRushCoop.RunId,
                SceneToken = BossRushCoop.SceneToken, Generation = BossRushCoop.SceneGeneration,
                // LocalAssetId's getter can refresh identity and send a hello. Observe its published cache instead.
                LocalPlayer = BossRushCoop.LocalPlayerId, Asset = BossRushCoop.Runtime != null
                    ? (string)typeof(BossRushCoopRuntimeModule).GetField("localAssetId", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(BossRushCoop.Runtime) : null,
                Connected = BossRushCoop.IsConnected, Host = BossRushCoop.IsHost,
                BoundScene = BossRushCoop.HasBoundScene, SessionReady = BossRushCoop.SessionReady,
                WorldAuthority = BossRushCoop.HasWorldAuthority, Money = EconomyManager.Money, EconomyReady = EconomyManager.Instance != null,
                FullSuiteRunning = F3GameplayValidationRunner.IsRunning, ModalLeases = ZombieModeUIHelper.ModalInputLeaseCount,
                Local = CaptureCoopValidationActor(CharacterMainControl.Main) };
            ZombieModeRuntimeModule module = coopZombieOwner;
            data.RuntimePresent = module != null;
            if (module != null) module.FillCoopValidationSnapshot(data);
            return BossRushCoopJson.ToJson(data);
        }

        private void FillCoopValidationSnapshot(F3ZombieCoopSnapshot data)
        {
            // Do not call IsZombieModeRunValid/IsCoopZombieRunLeaseCurrent: their revocation is a mutation.
            data.LeaseRevoked = coopZombieLeaseRevoked;
            data.LeaseCurrent = !coopZombieLeaseRevoked && (!coopZombieLeaseCaptured
                || coopZombieLeaseConnected == BossRushCoop.IsConnected && (!coopZombieLeaseConnected
                    || coopZombieLeaseHost == BossRushCoop.IsHost && coopZombieLeaseRoom == BossRushCoop.RunId));
            data.LocalRun = coopZombieLocalRun; data.LocalRunAsset = coopZombieLocalAsset;
            data.AdmissionPending = coopZombieAdmissionPending; data.StarterChosen = coopZombieStarterChosen;
            data.StarterGranted = coopZombieStarterGranted; data.PersonalDeath = coopZombiePersonalDeath;
            data.RewardWave = coopZombieLocalRewardWave; data.CompletedRewardWave = coopZombieCompletedRewardWave;
            data.RewardUiToken = zombieModeRewardUiToken != null;
            data.StarterReady = new List<string>(coopZombieStarterReady).ToArray();
            data.Skipped = new List<string>(coopZombieSkipped).ToArray();
            data.RewardSelections = new List<string>(coopZombieWorldSelections).ToArray();
            var players = new List<F3ZombieCoopActor>();
            foreach (BossRushCoopPlayer player in BossRushCoop.Players)
            {
                var actor = CaptureCoopValidationActor(player.Character);
                actor.Id = player.Id; actor.Asset = player.AssetId; actor.Local = player.IsLocal;
                actor.Connected = player.IsConnected; actor.HostAliveFact = player.HostAlive;
                actor.HostAliveAsset = player.HostAliveAssetId; actor.HostAliveGeneration = player.HostAliveGeneration;
                string frozen; if (coopZombieParticipants.TryGetValue(player.Id, out frozen)) actor.FrozenAsset = frozen;
                actor.StarterReady = coopZombieStarterReady.Contains(player.Id + ":" + player.AssetId);
                players.Add(actor);
            }
            data.Players = players.ToArray();
            data.Inventory = CaptureCoopValidationInventory(CharacterMainControl.Main != null ? CharacterMainControl.Main.CharacterItem : null);
            var enemies = new List<F3ZombieCoopActor>();
            foreach (ZombieModeEnemyRuntimeMarker marker in zombieModeEnemyMarkersByInstanceId.Values)
            {
                if (marker == null) continue;
                var actor = CaptureCoopValidationActor(marker.Owner);
                actor.Run = marker.RunId; actor.Settled = marker.DeathSettled;
                actor.Kind = marker.IsBoss ? marker.BossKind.ToString() : marker.EnemyKind + ":" + marker.SpecialKind;
                AICharacterController ai = marker.CachedAI;
                if (ai != null && ai.aimTarget != null)
                {
                    actor.TargetPosition = ai.aimTarget.position;
                    CharacterMainControl target = ai.aimTarget.GetComponentInParent<CharacterMainControl>();
                    actor.TargetPlayer = BossRushCoop.GetPlayerId(target);
                }
                enemies.Add(actor);
            }
            data.Enemies = enemies.ToArray();
            var drops = new List<F3ZombieCoopItem>();
            foreach (var pair in coopZombieDropMarkers)
            {
                var drop = CaptureCoopValidationItem(pair.Key, "shared-ground");
                CoopZombieDropClaim marker = pair.Value;
                drop.Key = marker.Key; drop.Run = marker.Run; drop.Room = marker.Room;
                drop.Generation = marker.Generation; drop.SceneToken = marker.SceneToken;
                ZombieDropRecord record;
                if (coopZombieDrops.TryGetValue(marker.Key, out record))
                {
                    drop.Holder = record.Holder; drop.Consumed = record.Consumed; drop.NativeLease = record.NativeDrop != null;
                    drop.Finalized = record.Finalized.Count;
                    if (record.Grant != null) { drop.Nonce = record.Grant.Nonce; drop.GrantedCount = record.Grant.Count; }
                }
                drops.Add(drop);
            }
            data.Drops = drops.ToArray(); data.DropsAwaitingResult = coopZombieDropResults.Count;
            var values = new List<string>();
            foreach (var pair in coopZombieDropRequests) values.Add(pair.Key + ":nonce=" + pair.Value.Nonce + ":asset=" + pair.Value.Asset);
            data.DropRequests = values.ToArray(); values.Clear();
            foreach (var pair in coopZombieDropOutcomes)
                values.Add(pair.Key + ":key=" + pair.Value.Key + ":outcome=" + pair.Value.Outcome + ":remaining=" + pair.Value.Remaining);
            data.DropResults = values.ToArray(); values.Clear();
            foreach (var pair in coopZombieRefunds)
                values.Add(pair.Key + ":asset=" + pair.Value.AssetId + ":cash=" + pair.Value.Cash + ":invitation=" + pair.Value.Invitation);
            data.Refunds = values.ToArray(); values.Clear();
            foreach (var pair in coopZombieWallets)
                values.Add(pair.Key + ":points=" + pair.Value.Points + ":received=" + pair.Value.Received + ":wave=" + pair.Value.MaxWave + ":extracted=" + pair.Value.Extracted);
            data.Wallets = values.ToArray(); values.Clear();
            foreach (var pair in coopZombieKillOptions)
                values.Add(pair.Key + ":siphon=" + pair.Value.Siphon + ":second_wind=" + pair.Value.SecondWind + ":doom=" + pair.Value.DoomPulse + ":kills=" + pair.Value.Kills);
            data.KillOptions = values.ToArray(); values.Clear();
            data.KillSequence = coopZombieKillSequence; data.ZoneSequence = coopZombieZoneSequence;
            foreach (var pair in coopZombieZoneFacts)
                values.Add(pair.Key + ":player=" + pair.Value.Player + ":asset=" + pair.Value.Asset + ":wave=" + pair.Value.Effect.Wave
                    + ":persistent=" + pair.Value.Persistent + ":area_alive=" + (pair.Value.Area != null) + ":sent=" + pair.Value.Sent);
            data.ZoneFacts = values.ToArray(); values.Clear();
            foreach (var pair in coopZombieZonePending)
                values.Add(pair.Key + ":nonce=" + pair.Value.Nonce + ":requested=" + pair.Value.Requested
                    + ":requested_at=" + pair.Value.RequestedAt + ":last_request=" + pair.Value.LastRequestedAt);
            data.ZonePending = values.ToArray(); values.Clear();
            CaptureCoopValidationDelivery("personal", coopZombiePersonal, values);
            CaptureCoopValidationDelivery("kills", coopZombieKillDelivery, values);
            CaptureCoopValidationDelivery("zones", coopZombieZoneDelivery, values);
            CaptureCoopValidationDelivery("drops", coopZombieDropDelivery, values);
            data.Deliveries = values.ToArray();
            if (entryTransaction != null)
            {
                data.InvitationHeld = entryTransaction.InvitationTemporarilyHeld;
                data.CashHeld = entryTransaction.CashTemporarilyHeld; data.WithheldCash = entryTransaction.CashWithheldAmount;
                data.ResourcesFinalized = entryTransaction.EntryResourcesFinalized;
            }
            if (runState == null) return;
            data.Run = runState.RunId; data.Lifecycle = runState.LifecyclePhase.ToString(); data.Combat = runState.CombatPhase.ToString();
            data.Wave = runState.CurrentWave; data.Kills = runState.CurrentWaveKills; data.KillTarget = runState.CurrentWaveKillTarget;
            data.Living = runState.LivingZombieCount; data.Normal = runState.LivingNormalZombieCount;
            data.PendingSpawns = runState.PendingNormalZombieSpawns; data.Bosses = runState.CurrentWaveBossesRemaining;
            data.Points = runState.PurificationPoints; data.Investment = runState.ConfirmedCashInvested;
            var rewardOptions = new List<string>();
            if (runState.CurrentRewardNode != null)
            {
                data.RemainingSelections = runState.CurrentRewardNode.RemainingSelections;
                foreach (ZombieModeRewardType reward in runState.CurrentRewardNode.Options) rewardOptions.Add(reward.ToString());
            }
            data.RewardOptions = rewardOptions.ToArray();
            data.Cleaning = runState.IsCleaningUp; data.Starter = runState.StarterLoadout.ToString();
            data.Preparation = runState.PreparationTimer; data.Extracting = runState.ExtractionChanneling;
            data.Extracted = runState.ExtractionSuccessHandled; data.HasExtractionArea = runState.ActiveExtractionArea != null;
            if (runState.ActiveExtractionArea != null) data.ExtractionPosition = runState.ActiveExtractionArea.transform.position;
            data.SafeActive = runState.ActiveSafeZoneActive; data.SafeCenter = runState.ActiveSafeZoneCenter; data.SafeRadius = runState.ActiveSafeZoneRadius;
            data.PortableActive = runState.PortableSafeZoneActive; data.PortableCenter = runState.PortableSafeZoneCenter; data.PortableRadius = runState.PortableSafeZoneRadius;
            data.RunOnlyObjects = runState.RunOnlyObjects.Count; data.PendingStars = runState.PendingPurificationStars.Count;
            data.InsuranceRatio = runState.InsuranceState.RandomKeepRatio;
            var options = runState.OptionRuntime;
            data.LifestealChance = options.TriggerLifestealChancePercent; data.LifestealHeal = options.TriggerLifestealHealAmount;
            data.Siphon = options.TriggerPurificationSiphonStacks; data.SecondWind = options.TriggerSecondWindStacks;
            data.DoomPulse = options.TriggerDoomPulseStacks; data.DoomKills = options.DoomPulseKillCounter;
            values.Clear();
            foreach (ZombieModeTemporaryNpc npc in runState.TemporaryNpcs)
                if (npc != null && npc.GameObject != null) values.Add(npc.ServiceType + ":wave=" + npc.SpawnWave + ":position=" + npc.GameObject.transform.position);
            foreach (ZombieModeTemporaryRealNpcRecord npc in runState.TemporaryRealNpcs)
                if (npc != null && npc.GameObject != null) values.Add(npc.NpcType + ":wave=" + npc.SpawnWave + ":position=" + npc.GameObject.transform.position);
            data.Npcs = values.ToArray();
        }

        private static F3ZombieCoopActor CaptureCoopValidationActor(CharacterMainControl actor)
        {
            var result = new F3ZombieCoopActor { Body = actor != null };
            if (actor == null) return result;
            result.Id = BossRushCoop.GetEntityId(actor);
            result.Instance = actor.GetInstanceID(); result.Name = actor.name; result.Position = actor.transform.position;
            result.Active = actor.gameObject.activeInHierarchy; result.Team = actor.Team.ToString();
            result.HealthPresent = actor.Health != null; result.InventoryPresent = actor.CharacterItem != null && actor.CharacterItem.Inventory != null;
            if (actor.Health != null) { result.Health = actor.Health.CurrentHealth; result.Dead = actor.Health.IsDead; result.Invincible = actor.Health.Invincible; }
            if (actor.CharacterItem != null) { result.WalkSpeed = actor.CharacterWalkSpeed; result.RunSpeed = actor.CharacterRunSpeed; }
            ZombieModePlayerSlowRuntime slow = actor.GetComponent<ZombieModePlayerSlowRuntime>();
            if (slow != null) result.Slow = slow.GetCurrentSlowPercent();
            return result;
        }

        private static F3ZombieCoopItem CaptureCoopValidationItem(Item item, string path)
        {
            var result = new F3ZombieCoopItem { Path = path, Body = item != null };
            if (item == null) return result;
            result.Instance = item.GetInstanceID(); result.Type = item.TypeID; result.Count = item.StackCount;
            result.InInventory = item.InInventory != null; result.Equipped = item.PluggedIntoSlot != null;
            result.Slot = item.PluggedIntoSlot != null ? item.PluggedIntoSlot.Key : null;
            result.GroundAgent = item.ActiveAgent != null;
            if (item.ActiveAgent != null) result.Position = item.ActiveAgent.transform.position;
            return result;
        }

        private static F3ZombieCoopItem[] CaptureCoopValidationInventory(Item root)
        {
            var results = new List<F3ZombieCoopItem>();
            var visited = new HashSet<Item>();
            CaptureCoopValidationInventoryTree(root, "character", 0, visited, results);
            return results.ToArray();
        }

        private static void CaptureCoopValidationInventoryTree(Item root, string path, int depth, HashSet<Item> visited, List<F3ZombieCoopItem> results)
        {
            if (root == null || depth > 8 || !visited.Add(root)) return;
            results.Add(CaptureCoopValidationItem(root, path));
            if (root.Inventory != null)
                for (int i = 0; i < root.Inventory.Content.Count; i++)
                    CaptureCoopValidationInventoryTree(root.Inventory.Content[i], path + "/inventory/" + i, depth + 1, visited, results);
            if (root.Slots != null)
                foreach (var slot in root.Slots)
                    if (slot != null) CaptureCoopValidationInventoryTree(slot.Content, path + "/slot/" + slot.Key, depth + 1, visited, results);
        }

        private static void CaptureCoopValidationDelivery<T>(string label, CoopPersonalDelivery<T> delivery, List<string> values) where T : class
        {
            if (delivery == null) { values.Add(label + ":disposed"); return; }
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            // Read existing receipt bookkeeping only; no Tick/RefreshSession/Deliver call is made.
            Type type = delivery.GetType();
            var pending = (IDictionary)type.GetField("pending", flags).GetValue(delivery);
            foreach (DictionaryEntry pair in pending) values.Add(label + ":pending=" + pair.Key);
            var delivered = (HashSet<string>)type.GetField("delivered", flags).GetValue(delivery);
            values.Add(label + ":delivered_count=" + delivered.Count);
        }
    }
}
#endif
