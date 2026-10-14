using System;
using System.Collections.Generic;
using HarmonyLib;
using ItemStatsSystem;
using UnityEngine;

namespace BossRush
{
    /// <summary>临时操控仍由房主模拟 AI。远端只送输入，原生 AI 同步负责移动、命中和动画。</summary>
    internal sealed class BossRushCoopControl : IDisposable
    {
        [Serializable] private sealed class LeaseState { public string player, lease, entity, asset, ended; public int generation; public long revision; }
        [Serializable] private sealed class InputState
        {
            public string lease;
            public int generation;
            public long sequence;
            public Vector3 move, aim;
            public bool fire, pressed, released, dash, reload, run, ads;
            public int weapon;
        }
        private sealed class Lease
        {
            internal LeaseState State;
            internal CharacterMainControl Character;
            internal AICharacterController Ai;
            internal bool AiEnabled;
            internal float LastInput;
            internal long Sequence;
            internal bool Confirmed;
            internal float NextState;
        }
        private readonly Dictionary<string, Lease> leases = new Dictionary<string, Lease>(StringComparer.Ordinal);
        private readonly IDisposable stateSubscription, inputSubscription;
        private readonly Harmony harmony = new Harmony("BossRush.Coop.Control");
        private static BossRushCoopControl active;
        private LeaseState local;
        private CharacterMainControl localTarget, originalControl;
        private readonly InputState input = new InputState();
        private bool applying;
        private float nextSend;
        private float lastLocalState;
        private long stateRevision, appliedStateRevision;

        internal BossRushCoopControl()
        {
            active = this;
            stateSubscription = BossRushCoop.Register("control-state", OnState);
            inputSubscription = BossRushCoop.Register("control-input", OnInput);
            BossRushCoop.PlayerDisconnected += OnDisconnected;
            Patch("SetMoveInput", "MovePrefix"); Patch("SetAimPoint", "AimPrefix");
            Patch("Trigger", "TriggerPrefix"); Patch("Dash", "DashPrefix");
            Patch("TryToReload", "ReloadPrefix"); Patch("SwitchWeapon", "WeaponPrefix");
            Patch("SetRunInput", "RunPrefix"); Patch("SetAdsInput", "AdsPrefix");
        }
        private void Patch(string method, string prefix)
        {
            var target = AccessTools.Method(typeof(CharacterMainControl), method);
            if (target == null) throw new MissingMethodException(typeof(CharacterMainControl).FullName, method);
            harmony.Patch(target, prefix: new HarmonyMethod(typeof(BossRushCoopControl), prefix));
        }
        internal string Begin(string playerId, string entityId)
        {
            if (!BossRushCoop.IsHost || !BossRushCoop.SessionReady || string.IsNullOrEmpty(entityId) || !entityId.StartsWith("a:", StringComparison.Ordinal)) return null;
            var player = BossRushCoop.FindPlayer(playerId);
            CharacterMainControl character = BossRushCoop.FindEntity(entityId);
            if (player == null || !player.IsAlive || character == null || character.Health == null || character.Health.IsDead) return null;
            foreach (Lease lease in leases.Values) if (lease.State.player == playerId || lease.State.entity == entityId) return null;
            var state = new LeaseState { player = playerId, entity = entityId, lease = Guid.NewGuid().ToString("N"), generation = BossRushCoop.SceneGeneration, asset = player.AssetId, revision = ++stateRevision };
            var ai = character.GetComponentInChildren<AICharacterController>(true);
            var created = new Lease { State = state, Character = character, Ai = ai, AiEnabled = ai != null && ai.enabled, LastInput = Time.unscaledTime };
            if (ai != null) ai.enabled = false;
            leases.Add(state.lease, created);
            BossRushCoop.SendToPlayer(playerId, "control-state", state);
            if (!player.IsLocal) BossRushCoop.RaiseControlLeaseChanged(playerId, state.lease, entityId);
            return state.lease;
        }
        internal bool Contains(string lease)
        {
            Lease value;
            return !string.IsNullOrEmpty(lease) && ((leases.TryGetValue(lease, out value) && value.Confirmed)
                || (local != null && local.lease == lease && localTarget != null && local.asset == BossRushCoop.LocalAssetId));
        }
        internal void End(string leaseId)
        {
            if (string.IsNullOrEmpty(leaseId)) return;
            Lease lease;
            if (leases.TryGetValue(leaseId, out lease))
            {
                leases.Remove(leaseId);
                applying = true;
                try
                {
                    if (lease.Character != null) { lease.Character.SetMoveInput(Vector3.zero); lease.Character.Trigger(false, false, true); lease.Character.SetRunInput(false); lease.Character.SetAdsInput(false); }
                    if (lease.Ai != null) lease.Ai.enabled = lease.AiEnabled;
                }
                finally { applying = false; }
                var released = new LeaseState { player = lease.State.player, lease = string.Empty, ended = leaseId, entity = lease.State.entity, generation = BossRushCoop.SceneGeneration, asset = lease.State.asset, revision = ++stateRevision };
                BossRushCoop.SendToPlayer(lease.State.player, "control-state", released);
                if (released.player != BossRushCoop.LocalPlayerId) BossRushCoop.RaiseControlLeaseChanged(released.player, string.Empty, released.entity);
            }
            if (local != null && local.lease == leaseId) RestoreLocal();
        }
        private void OnState(BossRushCoopMessage message)
        {
            if (!message.IsFromHost) return;
            var state = message.Read<LeaseState>();
            if (state == null || state.generation != BossRushCoop.SceneGeneration || state.player != BossRushCoop.LocalPlayerId || state.asset != BossRushCoop.LocalAssetId) return;
            // 释放也必须留下顺序屏障。晚到的赋权不能夺回镜头，旧释放不能结束新租约。
            if (state.revision <= 0 || state.revision < appliedStateRevision) return;
            if (state.revision == appliedStateRevision && (local == null || local.lease != state.lease)) return;
            appliedStateRevision = state.revision;
            if (string.IsNullOrEmpty(state.lease))
            { if (local == null || local.lease != state.ended) return; RestoreLocal(); }
            else { SetLocal(state); lastLocalState = Time.unscaledTime; }
            BossRushCoop.RaiseControlLeaseChanged(state.player, state.lease, state.entity);
        }
        private void SetLocal(LeaseState state)
        {
            if (local != null && local.lease == state.lease) return;
            RestoreLocal(); local = state;
            originalControl = LevelManager.Instance != null ? LevelManager.Instance.ControllingCharacter : CharacterMainControl.Main;
            input.sequence = 0; input.fire = input.pressed = input.released = input.dash = input.reload = input.run = input.ads = false;
            input.move = Vector3.zero; input.weapon = 0; nextSend = 0f;
        }
        private void RestoreLocal()
        {
            if (local == null) return;
            local = null;
            try
            {
                if (LevelManager.Instance != null && localTarget != null && LevelManager.Instance.ControllingCharacter == localTarget)
                    LevelManager.Instance.SetControllingCharacter(originalControl != null ? originalControl : CharacterMainControl.Main);
                if (GameCamera.Instance != null && originalControl != null) GameCamera.Instance.SetTarget(originalControl);
            }
            finally { localTarget = null; originalControl = null; }
        }
        internal bool Submit(string lease, Vector3 move, Vector3 aim, bool fire, bool dash, bool reload, int weapon)
        {
            if (local == null || local.lease != lease || localTarget == null) return false;
            input.move = move; input.aim = aim; input.pressed |= fire && !input.fire; input.released |= !fire && input.fire;
            input.fire = fire; input.dash |= dash; input.reload |= reload; input.weapon = weapon;
            return SendInput();
        }
        private bool SendInput()
        {
            if (local == null) return false;
            input.lease = local.lease; input.generation = local.generation; input.sequence++;
            bool sent = BossRushCoop.SendToHost("control-input", input);
            if (sent) { input.pressed = input.released = input.dash = input.reload = false; input.weapon = 0; }
            return sent;
        }
        private void OnInput(BossRushCoopMessage message)
        {
            if (!BossRushCoop.IsHost) return;
            InputState value = message.Read<InputState>();
            Lease lease;
            if (value == null || value.generation != BossRushCoop.SceneGeneration || string.IsNullOrEmpty(value.lease)
                || !leases.TryGetValue(value.lease, out lease) || lease.State.player != message.SenderId
                || value.sequence <= lease.Sequence || !Finite(value.move) || !Finite(value.aim)) return;
            BossRushCoopPlayer player = BossRushCoop.FindPlayer(lease.State.player);
            if (lease.Character == null || lease.Character.Health == null || lease.Character.Health.IsDead || player == null || player.AssetId != lease.State.asset) { End(value.lease); return; }
            lease.Sequence = value.sequence; lease.LastInput = Time.unscaledTime;
            lease.Confirmed = true;
            applying = true;
            try
            {
                lease.Character.SetMoveInput(Vector3.ClampMagnitude(value.move, 1f));
                Vector3 from = lease.Character.transform.position;
                lease.Character.SetAimPoint(from + Vector3.ClampMagnitude(value.aim - from, 1000f));
                lease.Character.SetRunInput(value.run); lease.Character.SetAdsInput(value.ads);
                lease.Character.Trigger(value.fire || value.pressed, value.pressed, value.released);
                if (value.dash) lease.Character.Dash();
                if (value.reload) lease.Character.TryToReload();
                if (value.weapon != 0) lease.Character.SwitchWeapon(value.weapon > 0 ? 1 : -1);
            }
            finally { applying = false; }
        }
        private static bool Finite(Vector3 value)
        { return !float.IsNaN(value.x) && !float.IsNaN(value.y) && !float.IsNaN(value.z) && !float.IsInfinity(value.x) && !float.IsInfinity(value.y) && !float.IsInfinity(value.z); }
        internal void Tick()
        {
            if (BossRushCoop.IsHost)
            {
                var ended = new List<string>();
                foreach (var pair in new List<KeyValuePair<string, Lease>>(leases))
                {
                    BossRushCoopPlayer player = BossRushCoop.FindPlayer(pair.Value.State.player);
                    if (Time.unscaledTime - pair.Value.LastInput > 3f || pair.Value.Character == null || pair.Value.Character.Health == null || pair.Value.Character.Health.IsDead
                        || player == null || !player.IsConnected || player.AssetId != pair.Value.State.asset) { ended.Add(pair.Key); continue; }
                    // 赋权消息可能先于 AI 镜像抵达；直到实际输入确认前，H 保持 AwaitingControl。
                    if (Time.unscaledTime >= pair.Value.NextState)
                    { pair.Value.NextState = Time.unscaledTime + 0.5f; BossRushCoop.SendToPlayer(pair.Value.State.player, "control-state", pair.Value.State); }
                }
                foreach (string id in ended) End(id);
            }
            if (local == null) return;
            if (local.generation != BossRushCoop.SceneGeneration || !BossRushCoop.IsConnected || local.asset != BossRushCoop.LocalAssetId
                || Time.unscaledTime - lastLocalState > 3f) { RestoreLocal(); return; }
            if (localTarget == null)
            {
                localTarget = BossRushCoop.FindEntity(local.entity);
                if (localTarget == null || LevelManager.Instance == null) return;
                LevelManager.Instance.SetControllingCharacter(localTarget);
                if (GameCamera.Instance != null) GameCamera.Instance.SetTarget(localTarget);
            }
            if (Time.unscaledTime >= nextSend) { nextSend = Time.unscaledTime + 0.05f; SendInput(); }
        }
        private void OnDisconnected(string player)
        {
            var ended = new List<string>();
            foreach (var pair in leases) if (pair.Value.State.player == player) ended.Add(pair.Key);
            foreach (string id in ended) End(id);
        }
        internal void Reset() { foreach (string id in new List<string>(leases.Keys)) End(id); RestoreLocal(); stateRevision = appliedStateRevision = 0; }
        public void Dispose()
        {
            Reset(); stateSubscription.Dispose(); inputSubscription.Dispose();
            BossRushCoop.PlayerDisconnected -= OnDisconnected;
            harmony.UnpatchAll(harmony.Id); if (active == this) active = null;
        }
        private static bool Capture(CharacterMainControl character) { return active != null && !active.applying && active.local != null && active.localTarget == character; }
        private static bool MovePrefix(CharacterMainControl __instance, Vector3 __0) { if (!Capture(__instance)) return true; active.input.move = __0; return false; }
        private static bool AimPrefix(CharacterMainControl __instance, Vector3 __0) { if (!Capture(__instance)) return true; active.input.aim = __0; return false; }
        private static bool TriggerPrefix(CharacterMainControl __instance, bool __0, bool __1, bool __2)
        { if (!Capture(__instance)) return true; active.input.fire = __0; active.input.pressed |= __1; active.input.released |= __2; return false; }
        private static bool DashPrefix(CharacterMainControl __instance) { if (!Capture(__instance)) return true; active.input.dash = true; return false; }
        private static bool ReloadPrefix(CharacterMainControl __instance, ref bool __result) { if (!Capture(__instance)) return true; active.input.reload = true; __result = true; return false; }
        private static bool WeaponPrefix(CharacterMainControl __instance, int __0) { if (!Capture(__instance)) return true; active.input.weapon = __0; return false; }
        private static bool RunPrefix(CharacterMainControl __instance, bool __0) { if (!Capture(__instance)) return true; active.input.run = __0; return false; }
        private static bool AdsPrefix(CharacterMainControl __instance, bool __0) { if (!Capture(__instance)) return true; active.input.ads = __0; return false; }
    }
}
