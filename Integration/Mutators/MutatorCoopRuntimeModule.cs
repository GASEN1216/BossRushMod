using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace BossRush
{
    [Serializable] internal sealed class CoopMutatorScale { public string Entity; public Vector3 Scale; }
    [Serializable] internal sealed class CoopMutatorWorld
    {
        public bool Active;
        public string Epoch, Mode;
        public string[] Ids;
        public CoopMutatorScale[] Enemies;
    }
    [Serializable] internal sealed class CoopMutatorKill { public string Epoch; public int Scene; public Vector3 Position; }

    /// <summary>共享词条由房主抽取；个人数值在本人角色应用，世界伤害只由房主产生。</summary>
    internal sealed class MutatorCoopRuntimeModule : BossRushRuntimeModuleBase
    {
        private static MutatorCoopRuntimeModule active;
        private ModBehaviour owner;
        private CoopGameplayChannel<CoopMutatorWorld> channel;
        private CoopPersonalDelivery<CoopMutatorKill> kills;
        private readonly HashSet<int> seenDeaths = new HashSet<int>();
        private readonly HashSet<CharacterMainControl> enemies = new HashSet<CharacterMainControl>();
        private string epoch, mode, session, appliedEpoch;
        private CoopMutatorWorld view;
        private bool wasClient, dispatching;
        public override string ModuleName { get { return "CoopMutators"; } }

        public override void OnAwake(ModBehaviour owner)
        {
            this.owner = owner; active = this;
            channel = new CoopGameplayChannel<CoopMutatorWorld>("Mutators", Capture, Apply, null);
            kills = new CoopPersonalDelivery<CoopMutatorKill>("Mutators", ApplyKill);
        }
        internal static void NotifyRolled(string mode)
        {
            if (active == null) return;
            active.mode = mode; active.epoch = Guid.NewGuid().ToString("N");
            active.seenDeaths.Clear(); active.enemies.Clear();
            active.channel.Publish();
        }
        internal static void TrackEnemy(CharacterMainControl enemy)
        { if (active != null && BossRushCoop.IsHost && enemy != null) active.enemies.Add(enemy); }

        public override void OnUpdate(float deltaTime, float unscaledDeltaTime)
        {
            kills.Tick(unscaledDeltaTime);
            if (!BossRushCoop.IsConnected)
            {
                if (wasClient) { MutatorManager.RemoveAll(); MutatorUI.HideAll(); }
                wasClient = false; view = null; appliedEpoch = null; session = null;
                return;
            }
            wasClient = BossRushCoop.IsClient;
            if (session != BossRushCoop.RunId)
            {
                session = BossRushCoop.RunId; seenDeaths.Clear(); appliedEpoch = null;
                if (BossRushCoop.IsHost) epoch = Guid.NewGuid().ToString("N");
            }
            CharacterMainControl player = CharacterMainControl.Main;
            if (player != null && player.CharacterItem != null)
            {
                if (BossRushCoop.IsHost && MutatorManager.IsActive) MutatorManager.RebindCoopLocalPlayer(player);
                else if (BossRushCoop.IsClient && view != null
                    && (!MutatorManager.IsActive || !MutatorManager.CoopLocalPlayerMatches(player))) Apply(view);
            }
            channel.Tick(unscaledDeltaTime);
        }
        private CoopMutatorWorld Capture()
        {
            if (!MutatorManager.IsActive) return new CoopMutatorWorld();
            if (string.IsNullOrEmpty(epoch)) epoch = Guid.NewGuid().ToString("N");
            var scales = new List<CoopMutatorScale>();
            enemies.RemoveWhere(x => x == null || x.Health == null || x.Health.IsDead);
            foreach (CharacterMainControl enemy in enemies)
            {
                string entity = BossRushCoop.GetEntityId(enemy);
                if (!string.IsNullOrEmpty(entity)) scales.Add(new CoopMutatorScale { Entity = entity, Scale = enemy.transform.localScale });
            }
            return new CoopMutatorWorld { Active = true, Epoch = epoch, Mode = mode, Ids = MutatorManager.CopyCoopIds(), Enemies = scales.ToArray() };
        }
        private void Apply(CoopMutatorWorld state)
        {
            if (!BossRushCoop.IsClient || state == null) return;
            view = state;
            if (!state.Active)
            {
                if (appliedEpoch != null) { MutatorManager.RemoveAll(); MutatorUI.HideAll(); }
                appliedEpoch = null; return;
            }
            CharacterMainControl player = CharacterMainControl.Main;
            if (player == null || player.CharacterItem == null || string.IsNullOrEmpty(state.Epoch) || state.Epoch.Length > 80
                || state.Ids == null || state.Ids.Length > 28) return;
            var unique = new HashSet<string>(StringComparer.Ordinal);
            foreach (string id in state.Ids)
                if (string.IsNullOrEmpty(id) || !unique.Add(id) || MutatorManager.FindCoopDefinition(id) == null) return;
            if (appliedEpoch != state.Epoch || !MutatorManager.IsActive)
            {
                MutatorManager.ApplyCoopPersonalIds(player, state.Ids, state.Mode);
                appliedEpoch = state.Epoch;
                MutatorUI.ShowBanner();
            }
            else MutatorManager.RebindCoopLocalPlayer(player);
            if (state.Enemies != null && state.Enemies.Length <= 2048)
                foreach (CoopMutatorScale scale in state.Enemies)
                {
                    CharacterMainControl enemy = scale != null ? BossRushCoop.FindEntity(scale.Entity) : null;
                    if (enemy != null && !BossRushCoop.IsPlayer(enemy))
                    {
                        CoopAiPredictionHealthProtection.MarkReplica(enemy);
                        enemy.transform.localScale = scale.Scale;
                    }
                }
        }
        internal static bool TryHandleEnemyKilled(Health health, DamageInfo damage)
        {
            if (!BossRushCoop.IsConnected) return false;
            if (active == null || !BossRushCoop.IsHost || !MutatorManager.IsActive || health == null || damage.isFromBuffOrEffect || active.dispatching) return true;
            CharacterMainControl victim = health.TryGetCharacter();
            if (BossRushCoop.IsPlayer(victim)) return true;
            string playerId = BossRushCoop.GetPlayerId(damage.fromCharacter);
            BossRushCoopPlayer player = BossRushCoop.FindPlayer(playerId);
            if (player == null || !player.IsAlive || !active.seenDeaths.Add(health.GetInstanceID())) return true;
            Vector3 position = victim != null ? victim.transform.position : damage.damagePoint;
            active.owner.StartCoroutine(active.DispatchKill(playerId, damage.fromCharacter, position, active.epoch, BossRushCoop.SceneGeneration, health.GetInstanceID()));
            return true;
        }
        private IEnumerator DispatchKill(string playerId, CharacterMainControl source, Vector3 position, string expectedEpoch, int scene, int victim)
        {
            yield return null;
            while (BossRushUI.IsGamePaused())
            {
                if (!IsDispatchValid(playerId, source, expectedEpoch, scene)) yield break;
                yield return null;
            }
            if (!IsDispatchValid(playerId, source, expectedEpoch, scene)) yield break;
            kills.Deliver(playerId, expectedEpoch + ":kill:" + victim, new CoopMutatorKill { Epoch = expectedEpoch, Scene = scene, Position = position });
            if (!MutatorManager.HasActiveMutator("explode_on_death")) yield break;
            MutatorDefinition definition = MutatorManager.FindCoopDefinition("explode_on_death");
            if (definition == null) yield break;
            dispatching = true;
            try
            {
                var context = new MutatorContext { Player = source };
                definition.OnApply?.Invoke(context);
                foreach (var callback in context.EnemyKilledCallbacks) callback(null, position);
            }
            finally { dispatching = false; }
        }
        private bool IsDispatchValid(string playerId, CharacterMainControl source, string expectedEpoch, int scene)
        {
            BossRushCoopPlayer player = BossRushCoop.FindPlayer(playerId);
            return owner != null && BossRushCoop.IsHost && MutatorManager.IsActive && epoch == expectedEpoch
                && BossRushCoop.SceneGeneration == scene && source != null && player != null && player.IsAlive && player.Character == source;
        }
        private bool ApplyKill(CoopMutatorKill kill)
        {
            if (kill == null) return false;
            // 生命回复属于本局当前场景的即时效果；过图后确认取消，不带进另一局。
            string currentEpoch = BossRushCoop.IsHost ? epoch : appliedEpoch;
            if (kill.Scene != BossRushCoop.SceneGeneration) return true;
            if (BossRushCoop.IsClient && (view == null || (view.Active && view.Epoch == kill.Epoch && currentEpoch != kill.Epoch))) return false;
            if (kill.Epoch != currentEpoch) return true;
            return MutatorManager.ApplyCoopPersonalKill(kill.Position);
        }
        public override void OnDestroy()
        {
            channel?.Dispose(); kills?.Dispose(); enemies.Clear(); seenDeaths.Clear(); view = null; owner = null;
            if (active == this) active = null;
        }
    }
}
