using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using Duckov.Economy;

namespace BossRush
{
    [Serializable]
    internal sealed class CoopRandomEventState
    {
        public int Id, Serial, Phase, Fired, Maximum;
        public float Elapsed, Duration;
        public Vector3 Anchor;
        public string Merchant;
    }
    [Serializable]
    internal sealed class CoopRandomEventFx
    {
        public string Run, Id;
        public int Scene, Kind, Index;
        public float Duration;
        public Vector3 Position;
    }
    [Serializable]
    internal sealed class CoopRandomEventCash { public long Amount; }

    internal sealed partial class RandomEventsRuntimeModule
    {
        private static RandomEventsRuntimeModule coopOwner;
        private CoopGameplayChannel<CoopRandomEventState> coopChannel;
        private CoopPersonalDelivery<CoopRandomEventCash> coopCash;
        private IDisposable coopFxSubscription;
        private long coopCashSequence;
        private CharacterMainControl coopMerchant;
        private StockShop coopMerchantShop;
        private string coopMerchantEntity;
        private float coopMerchantRetry;
        private string coopFxRun;
        private int coopFxScene = -1;
        private readonly HashSet<string> coopReceivedFx = new HashSet<string>(StringComparer.Ordinal);
        private readonly List<GameObject> coopAirdropVisuals = new List<GameObject>();

        private void InitializeCoop()
        {
            if (coopChannel != null) return;
            coopOwner = this;
            coopChannel = new CoopGameplayChannel<CoopRandomEventState>("RandomEvents",
                () => _director != null ? _director.CaptureCoop() : new CoopRandomEventState(), ApplyCoop, null);
            coopCash = new CoopPersonalDelivery<CoopRandomEventCash>("RandomEvents", receipt =>
                receipt != null && receipt.Amount == RandomEventsTuning.BloodMoonCashPerKill && EconomyManager.Add(receipt.Amount));
            coopFxSubscription = BossRushCoop.Register("RandomEvents.fx", ReceiveCoopFx);
            BossRushCoop.SessionChanged += ResetCoop;
        }
        private void ResetCoop()
        {
            if (_director != null && _director.ValidateCoopRun()) return;
            ClearCoopMerchant();
            ClearCoopAirdropVisuals();
            coopReceivedFx.Clear();
            coopFxRun = BossRushCoop.RunId;
            coopFxScene = BossRushCoop.SceneGeneration;
        }
        private void TickCoop(float delta)
        {
            if (coopChannel == null) return;
            RefreshCoopScope();
            coopChannel.Tick(delta);
            coopCash.Tick(delta);
            for (int i = coopAirdropVisuals.Count - 1; i >= 0; i--)
                if (coopAirdropVisuals[i] == null) coopAirdropVisuals.RemoveAt(i);
            if (!BossRushCoop.IsClient) return;
            if (coopMerchant != null && coopMerchantShop != null) return;
            coopMerchantRetry -= Mathf.Max(0f, delta);
            if (coopMerchantRetry > 0f) return;
            CharacterMainControl character = BossRushCoop.FindEntity(coopMerchantEntity);
            if (character == null) return;
            CoopAiPredictionHealthProtection.MarkReplica(character);
            coopMerchantRetry = 0.5f;
            coopMerchant = character;
            coopMerchantShop = BuildRandomEventMerchantShop(character.gameObject);
        }
        private void RefreshCoopScope()
        {
            if (coopFxScene != BossRushCoop.SceneGeneration || !string.Equals(coopFxRun, BossRushCoop.RunId, StringComparison.Ordinal))
            {
                ClearCoopMerchant();
                ClearCoopAirdropVisuals();
                coopReceivedFx.Clear();
                coopFxRun = BossRushCoop.RunId;
                coopFxScene = BossRushCoop.SceneGeneration;
            }
        }
        private void ApplyCoop(CoopRandomEventState state)
        {
            if (state == null || !BossRushCoop.IsClient || CharacterMainControl.Main == null) return;
            EnsureBootstrapped();
            if (_director == null) return;
            _director.ApplyCoop(state);
            if (!string.Equals(coopMerchantEntity, state.Merchant, StringComparison.Ordinal))
            {
                ClearCoopMerchant();
                coopMerchantEntity = state.Merchant;
            }
        }
        private void ClearCoopMerchant()
        {
            // 只释放本机店铺，角色由原生 AI 生命周期拥有。
            if (coopMerchantShop != null) DespawnRandomEventMerchant(null, coopMerchantShop);
            coopMerchantShop = null; coopMerchant = null; coopMerchantEntity = null;
            coopMerchantRetry = 0f;
        }
        private void ClearCoopAirdropVisuals()
        {
            foreach (GameObject visual in coopAirdropVisuals) if (visual != null) UnityEngine.Object.Destroy(visual);
            coopAirdropVisuals.Clear();
        }
        private void ShutdownCoop()
        {
            BossRushCoop.SessionChanged -= ResetCoop;
            if (coopChannel != null) coopChannel.Dispose();
            if (coopCash != null) coopCash.Dispose();
            if (coopFxSubscription != null) coopFxSubscription.Dispose();
            coopChannel = null; coopCash = null; coopFxSubscription = null;
            ClearCoopMerchant();
            ClearCoopAirdropVisuals();
            coopReceivedFx.Clear();
            if (coopOwner == this) coopOwner = null;
        }
        internal static bool DeliverBloodMoonCash(long amount)
        {
            if (amount != RandomEventsTuning.BloodMoonCashPerKill) return false;
            if (!BossRushCoop.IsConnected) return EconomyManager.Add(amount);
            if (!BossRushCoop.IsHost || coopOwner == null || coopOwner.coopCash == null) return false;
            string receipt = "bloodmoon:" + (++coopOwner.coopCashSequence);
            bool queued = false;
            foreach (BossRushCoopPlayer player in BossRushCoop.Players)
                if (player.IsConnected) queued = coopOwner.coopCash.Deliver(player.Id, receipt, new CoopRandomEventCash { Amount = amount }) || queued;
            return queued;
        }
        internal static void PublishCoopFx(int kind, Vector3 position, int index, float duration = 0f)
        {
            if (BossRushCoop.IsHost) BossRushCoop.Broadcast("RandomEvents.fx", new CoopRandomEventFx
            { Run = BossRushCoop.RunId, Id = Guid.NewGuid().ToString("N"), Scene = BossRushCoop.SceneGeneration, Kind = kind, Position = position, Index = index, Duration = duration });
        }
        private void ReceiveCoopFx(BossRushCoopMessage message)
        {
            if (!BossRushCoop.IsClient || message == null || !message.IsFromHost) return;
            CoopRandomEventFx fx = message.Read<CoopRandomEventFx>();
            if (fx == null || fx.Scene != BossRushCoop.SceneGeneration || fx.Run != BossRushCoop.RunId
                || string.IsNullOrEmpty(fx.Id) || fx.Id.Length > 64 || fx.Kind < 1 || fx.Kind > 4
                || (fx.Kind == 4 && (float.IsNaN(fx.Duration) || float.IsInfinity(fx.Duration) || fx.Duration < 0.05f || fx.Duration > 60f))
                || float.IsNaN(fx.Position.x) || float.IsInfinity(fx.Position.x)
                || float.IsNaN(fx.Position.y) || float.IsInfinity(fx.Position.y)
                || float.IsNaN(fx.Position.z) || float.IsInfinity(fx.Position.z)) return;
            RefreshCoopScope();
            if (!coopReceivedFx.Add(fx.Id)) return;
            if (fx.Kind == 1) RandomEventFx.PlayFirework(fx.Position, fx.Index);
            else if (fx.Kind == 2) PopRandomEventText(L10n.T("咕嘎?", "Quack?"), fx.Position + Vector3.up * 1.2f, BossRushUIColors.Accent, 1f);
            else if (fx.Kind == 3) { RandomEventFx.PlayAirdropLanding(fx.Position); PlayRandomEventModSound("lottery/special.mp3"); }
            else if (fx.Kind == 4)
            {
                // 客端只持有定位圈的短时视觉 owner，箱子与库存由原生 world loot 同步。
                GameObject visual = new GameObject("BossRushCoopAirdropVisual");
                visual.transform.position = fx.Position;
                coopAirdropVisuals.Add(visual);
                RandomEventFx.SpawnAirdropMarker(fx.Position, visual, fx.Duration, visual.transform);
                UnityEngine.Object.Destroy(visual, fx.Duration + 0.5f);
            }
        }
    }

    internal sealed partial class RandomEventDirector
    {
        private bool coopRunBound, coopRunConnected, coopRunClient, coopRunRevoked;
        private string coopRunRoom;
        private int coopRunScene;

        private void CaptureCoopRun()
        {
            coopRunBound = true; coopRunRevoked = false;
            coopRunConnected = BossRushCoop.IsConnected; coopRunClient = BossRushCoop.IsClient;
            coopRunRoom = BossRushCoop.RunId;
            coopRunScene = SceneManager.GetActiveScene().handle;
        }
        internal bool ValidateCoopRun()
        {
            if (coopRunRevoked)
            {
                // 同一个旧局仍在旧地图时永久失效；实际模式结束或真正换图后才准许新局。
                if (RandomEventModeGate.ComputeRunSignature(_owner) != 0 && SceneManager.GetActiveScene().handle == coopRunScene) return false;
                coopRunBound = coopRunRevoked = false;
            }
            if (!coopRunBound && _runSignature != 0) CaptureCoopRun();
            if (!coopRunBound) return true;
            bool lost = coopRunConnected != BossRushCoop.IsConnected
                || coopRunConnected && coopRunClient != BossRushCoop.IsClient
                || coopRunConnected && !string.IsNullOrEmpty(coopRunRoom) && !string.IsNullOrEmpty(BossRushCoop.RunId)
                    && !string.Equals(coopRunRoom, BossRushCoop.RunId, StringComparison.Ordinal);
            if (!lost)
            {
                if (coopRunConnected && string.IsNullOrEmpty(coopRunRoom) && !string.IsNullOrEmpty(BossRushCoop.RunId)) coopRunRoom = BossRushCoop.RunId;
                return true;
            }
            coopRunRevoked = true;
            OnSceneChanged();
            return false;
        }
        private RandomEventContext coopPublishedContext;
        private int coopSerial;
        private int coopAppliedSerial = -1;
        private bool coopRunsLocalEffect;

        internal CoopRandomEventState CaptureCoop()
        {
            if (!ReferenceEquals(coopPublishedContext, _activeContext)) { coopPublishedContext = _activeContext; coopSerial++; }
            RandomEventWanderingMerchant merchant = _activeEvent as RandomEventWanderingMerchant;
            return new CoopRandomEventState
            {
                Id = (int)ActiveEventId, Serial = coopSerial, Phase = (int)_phase, Fired = _eventsFiredThisRun, Maximum = _maxEventsThisRun,
                Elapsed = _activeContext != null ? _activeContext.ElapsedSeconds : 0f,
                Duration = ActiveDurationSeconds, Anchor = _activeContext != null ? _activeContext.AnchorPosition : Vector3.zero,
                Merchant = merchant != null ? merchant.CoopEntity : null
            };
        }
        internal void ApplyCoop(CoopRandomEventState state)
        {
            if (state == null || !BossRushCoop.IsClient) return;
            if (!coopRunBound && (state.Maximum > 0 || state.Id != 0)) CaptureCoopRun();
            if (!ValidateCoopRun()) return;
            RandomEventBase definition = RandomEventCatalog.Find((RandomEventId)state.Id);
            if (coopAppliedSerial != state.Serial || ActiveEventId != (RandomEventId)state.Id)
            {
                EndActiveEvent(RandomEventEndReason.RunEnded);
                coopRunsLocalEffect = false;
                if (definition != null)
                {
                    RandomEventContext context = CreateContext(definition);
                    if (context == null) return;
                    context.ElapsedSeconds = Mathf.Max(0f, state.Elapsed);
                    context.DurationSeconds = Mathf.Max(0f, state.Duration);
                    context.AnchorPosition = state.Anchor;
                    bool local = definition is RandomEventBloodMoon || definition is RandomEventTempo;
                    // 只运行本机天气、暗角与玩家属性；世界事件保留 HUD，生成/库存归房主。
                    if (local && !definition.OnTrigger(context)) { definition.OnCleanup(context, RandomEventEndReason.RunEnded); return; }
                    _activeContext = context;
                    _activeEvent = local ? definition : new CoopRandomEventProjection(definition);
                    coopRunsLocalEffect = local;
                    if (!local) _owner.ShowRandomEventDirectionalBanner(definition.DisplayName, state.Anchor);
                }
                coopAppliedSerial = state.Serial;
            }
            _phase = (RandomEventPhase)state.Phase;
            _eventsFiredThisRun = state.Fired; _maxEventsThisRun = state.Maximum;
            if (_activeContext != null) { _activeContext.ElapsedSeconds = Mathf.Max(0f, state.Elapsed); _activeContext.DurationSeconds = Mathf.Max(0f, state.Duration); }
        }
        private void TickCoopProjection(float delta)
        {
            if (_activeContext == null || _activeEvent == null) return;
            float dt = float.IsNaN(delta) || float.IsInfinity(delta) ? 0f : Mathf.Max(0f, Mathf.Min(delta, RandomEventsTuning.MaxDeltaPerFrame));
            _activeContext.ElapsedSeconds = Mathf.Min(_activeContext.DurationSeconds, _activeContext.ElapsedSeconds + dt);
            if (!coopRunsLocalEffect || _activeTickFaulted) return;
            try { _activeEvent.OnTick(_activeContext, dt); }
            catch (Exception e) { _activeTickFaulted = true; LogFailure("coop_event_tick", e); }
        }
    }
    internal sealed class CoopRandomEventProjection : RandomEventBase
    {
        private readonly RandomEventBase definition;
        internal CoopRandomEventProjection(RandomEventBase definition) { this.definition = definition; }
        internal override RandomEventId Id { get { return definition.Id; } }
        internal override string DisplayName { get { return definition.DisplayName; } }
        internal override float DurationSeconds { get { return definition.DurationSeconds; } }
        internal override float Weight { get { return 0f; } }
        internal override bool OnTrigger(RandomEventContext context) { return true; }
        internal override void OnCleanup(RandomEventContext context, RandomEventEndReason reason) { RandomEventEffectHelpers.ClearScope(context, reason); }
    }
}
