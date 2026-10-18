using System;
using System.Collections.Generic;
using System.Reflection;
using Duckov.Economy;
using Duckov.Scenes;
using Duckov.UI;
using HarmonyLib;
using UnityEngine.SceneManagement;

namespace BossRush
{
    // COOP's OnPointerClick vote skips the official LoadTask, including Cost.Pay.
    // Preserve that vote protocol; pay only the exact host-owned BossRush selection,
    // before its first BeginLoad RPC. A failed/throwing payment is never retried.
    internal sealed class BossRushCoopMapPaymentCompatibility : IDisposable
    {
        private const string PatchOwner = "BossRush.Coop.MapPayment.v1";
        private const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        private enum Phase { Captured, Checking, Attempted, Committed, Cancelled, Unknown }
        private sealed class Payment
        {
            internal BossRushMapSelectionHelper.CoopMapPaymentReceipt Receipt;
            internal object Native, Service;
            internal CharacterMainControl Body;
            internal MultiSceneCore Core;
            internal ItemStatsSystem.Item BodyItem;
            internal string Room, Token;
            internal int Generation, Handle;
            internal Phase State;
            internal bool VoteBound, CancelRequested, ReceiptSeen, HostLoadClaimed, Refunding, SourceRetired;
        }

        private static BossRushCoopMapPaymentCompatibility active;
        private static Payment interrupted;
        // Unknown consumption and known, unpublished refunds survive transport rebinds.
        // Only the module's final shutdown retires this process-local evidence.
        private static readonly List<Payment> unresolved = new List<Payment>();
        internal static bool PublishingReceipt { get; private set; }
        private readonly Type nativeType;
        private readonly Func<bool> live, host;
        private readonly Func<object> service;
        private readonly FieldInfo instance, vote, target, source;
        private readonly MethodInfo cancel;
        private FieldInfo requestTarget, requestSource;
        private Harmony harmony;
        private Payment selection;
        private string refusedScene;
        private bool subscribed, cancelling, ownClick;

        internal BossRushCoopMapPaymentCompatibility(Type nativeType, Func<bool> live, Func<bool> host, Func<object> service)
        {
            this.nativeType = nativeType; this.live = live; this.host = host; this.service = service;
            instance = Field("Instance", nativeType);
            vote = Field("sceneVoteActive", typeof(bool));
            target = Field("sceneTargetId", typeof(string));
            source = Field("sceneLocationName", typeof(string));
            cancel = Method("CancelVote", Type.EmptyTypes);
        }
        private FieldInfo Field(string name, Type type)
        {
            FieldInfo result = nativeType.GetField(name, Flags);
            if (result == null || result.FieldType != type) throw new MissingFieldException(nativeType.FullName, name);
            return result;
        }
        private MethodInfo Method(string name, Type[] args)
        {
            MethodInfo result = nativeType.GetMethod(name, Flags, null, args, null);
            if (result == null || result.IsStatic || result.ReturnType != typeof(void)) throw new MissingMethodException(nativeType.FullName, name);
            return result;
        }
        internal void Install()
        {
            if (harmony != null) return;
            if (active != null) active.Dispose();
            active = this; harmony = new Harmony(PatchOwner);
            try
            {
                if (interrupted != null && ReferenceEquals(interrupted.Native, instance.GetValue(null))
                    && ReferenceEquals(interrupted.Service, service())) selection = interrupted;
                Patch(Method("Host_BeginSceneVote_Simple", new[] { typeof(string), typeof(string), typeof(bool), typeof(bool), typeof(bool), typeof(string) }), "BeforeVote", "AfterVote");
                Patch(Method("Server_BroadcastBeginSceneLoad", Type.EmptyTypes), "BeforeBroadcast", null);
                Patch(Method("Host_PerformSceneLoad", Type.EmptyTypes), "BeforeHostLoad", null);
                Patch(cancel, "BeforeCancel", null);
                Type rpc = nativeType.Assembly.GetType(nativeType.Namespace + ".RPCScene", true);
                MethodInfo requested = rpc.GetMethod("HandleSceneVoteRequest", Flags);
                if (requested == null || !requested.IsStatic || requested.GetParameters().Length != 2)
                    throw new MissingMethodException(rpc.FullName, "HandleSceneVoteRequest");
                Type requestType = requested.GetParameters()[1].ParameterType;
                requestTarget = requestType.GetField("TargetSceneId", Flags);
                requestSource = requestType.GetField("LocationName", Flags);
                if (requestTarget == null || requestTarget.FieldType != typeof(string)
                    || requestSource == null || requestSource.FieldType != typeof(string))
                    throw new MissingFieldException(requestType.FullName, "TargetSceneId/LocationName");
                harmony.Patch(requested, prefix: new HarmonyMethod(typeof(BossRushCoopMapPaymentCompatibility), "BeforeRemoteVote") { priority = Priority.First });
                MethodInfo cancelled = rpc.GetMethod("HandleSceneVoteCancel", Flags);
                if (cancelled == null || !cancelled.IsStatic || cancelled.GetParameters().Length != 2)
                    throw new MissingMethodException(rpc.FullName, "HandleSceneVoteCancel");
                harmony.Patch(cancelled, postfix: new HarmonyMethod(typeof(BossRushCoopMapPaymentCompatibility), "AfterNativeCancel"));
                EconomyManager.OnCostPaid += OnCostPaid;
                BossRushCoop.SessionChanged += OnInvalidated;
                Saves.SavesSystem.OnSetFile += OnInvalidated;
                Saves.SavesSystem.OnSaveDeleted += OnInvalidated;
                SceneManager.sceneUnloaded += OnUnloaded;
                subscribed = true;
            }
            catch { Dispose(); throw; }
        }
        private void Patch(MethodInfo method, string prefix, string postfix)
        {
            Type own = typeof(BossRushCoopMapPaymentCompatibility);
            harmony.Patch(method, new HarmonyMethod(own, prefix) { priority = Priority.First },
                postfix == null ? null : new HarmonyMethod(own, postfix));
        }
        private bool HostCurrent(object native)
        {
            return live() && host() && BossRushCoop.IsConnected && BossRushCoop.IsHost
                && native != null && ReferenceEquals(instance.GetValue(null), native);
        }
        private bool Current(Payment value, bool beforeBroadcast)
        {
            if (value == null || !HostCurrent(value.Native) || !ReferenceEquals(value.Service, service())
                || value.Body == null || value.Core == null || value.BodyItem == null
                || !ReferenceEquals(value.Body, CharacterMainControl.Main) || !ReferenceEquals(value.BodyItem, value.Body.CharacterItem)
                || value.Body.Health == null || value.Body.Health.IsDead || !ReferenceEquals(value.Core, MultiSceneCore.Instance)
                || value.Room != BossRushCoop.RunId || value.Receipt.Asset != BossRushCoop.LocalAssetId
                || value.Receipt.Slot != Saves.SavesSystem.CurrentSlot
                || value.Receipt.SceneId != (string)target.GetValue(value.Native)
                || (string)source.GetValue(value.Native) != "OnPointerClick") return false;
            Scene scene = value.Core.gameObject.scene;
            if (!scene.IsValid() || !scene.isLoaded || scene.handle != value.Handle) return false;
            return !beforeBroadcast || (!SceneLoader.IsSceneLoading && value.Generation == BossRushCoop.SceneGeneration
                && value.Token == BossRushCoop.SceneToken && value.Receipt.IsCurrent() && (bool)vote.GetValue(value.Native));
        }

        internal static bool CanOpenMapSelection()
        {
            TickRefunds();
            foreach (Payment value in unresolved)
                if (value.Receipt.Slot == Saves.SavesSystem.CurrentSlot && value.Receipt.Asset == BossRushCoop.LocalAssetId) return false;
            var owner = active;
            return owner == null || owner.selection == null || owner.selection.State == Phase.Cancelled;
        }
        private static bool SourceRetired(Payment value)
        { return value.SourceRetired || value.Core == null || !ReferenceEquals(value.Core, MultiSceneCore.Instance); }
        private void RetireCompleted()
        {
            if (selection != null && selection.State == Phase.Committed && SourceRetired(selection)) selection = null;
        }
        internal static void CaptureSelected(MapSelectionEntry entry, int index)
        {
            var owner = active;
            if (owner == null || !owner.live() || !owner.host()) return;
            owner.refusedScene = entry.SceneID;
            if (!owner.HostCurrent(owner.instance.GetValue(null))) return;
            owner.RetireCompleted();
            Payment old = owner.selection;
            if (old != null && (old.State == Phase.Attempted || old.State == Phase.Committed || old.VoteBound)) return;
            // A self-owned entry without a valid intent is refused, never made free.
            owner.refusedScene = entry.SceneID;
            if (!CanOpenMapSelection()) return;
            var receipt = BossRushMapSelectionHelper.CaptureCoopMapPaymentReceipt(entry, index);
            var body = CharacterMainControl.Main; var core = MultiSceneCore.Instance;
            if (receipt == null || body == null || body.CharacterItem == null || body.Health == null || body.Health.IsDead
                || core == null || SceneLoader.IsSceneLoading || string.IsNullOrEmpty(BossRushCoop.RunId)
                || string.IsNullOrEmpty(BossRushCoop.SceneToken) || string.IsNullOrEmpty(receipt.Asset)) return;
            Scene scene = core.gameObject.scene;
            if (!scene.IsValid() || !scene.isLoaded) return;
            owner.selection = new Payment { Receipt = receipt, Native = owner.instance.GetValue(null), Service = owner.service(),
                Body = body, BodyItem = body.CharacterItem, Core = core, Room = BossRushCoop.RunId,
                Generation = BossRushCoop.SceneGeneration, Token = BossRushCoop.SceneToken, Handle = scene.handle };
            owner.refusedScene = null;
        }
        internal static bool AllowEntryClick(MapSelectionEntry entry)
        {
            if (entry == null || entry.GetComponent<BossRushMapEntryClickHandler>() == null) return true;
            var owner = active;
            bool guest = owner != null && owner.live() && !owner.host();
            if (!guest && !(BossRushCoop.IsConnected && !BossRushCoop.IsHost)) return true;
            try
            {
                var mod = ModBehaviour.Instance;
                if (mod != null) mod.ShowMessage(L10n.T("请由房主选择 BossRush 地图，再参与准备。", "Ask the host to select the BossRush map, then ready up."));
            }
            catch (Exception e) { ModBehaviour.DevLog("[BossRush Coop] Host-entry message failed: " + e.Message); }
            return false;
        }
        internal static bool CanRecordSelection()
        {
            if (active != null) active.RetireCompleted();
            Payment value = active == null ? null : active.selection;
            return value == null || (value.State == Phase.Captured && !value.VoteBound) || value.State == Phase.Cancelled;
        }
        internal static void RejectSelected(MapSelectionEntry entry)
        {
            var owner = active;
            if (owner == null || !owner.live() || !owner.host() || entry == null) return;
            var marker = entry.GetComponent<BossRushMapEntryClickHandler>();
            Cost cost = entry.Cost;
            owner.ownClick = marker != null && marker.entryIndex >= 0 && cost.money == 0L
                && cost.items != null && cost.items.Length == 1
                && cost.items[0].id == BossRushMapSelectionHelper.GetBossRushTicketTypeId() && cost.items[0].amount == 1L;
            owner.refusedScene = owner.ownClick ? entry.SceneID : null;
        }
        private static bool BeforeRemoteVote(object __1)
        {
            var owner = active;
            if (owner == null || !owner.HostCurrent(owner.instance.GetValue(null))) return true;
            string id = (string)owner.requestTarget.GetValue(__1);
            if ((string)owner.requestSource.GetValue(__1) != "OnPointerClick"
                || !BossRushMapSelectionHelper.HasCoopOwnedMapTarget(id)) return true;
            Payment value = owner.selection;
            // The native packet has no entry identity. Only the host's actual
            // owned UI selection and already active vote may authorize this target.
            return value != null && value.VoteBound && value.State == Phase.Captured
                && value.Receipt.SceneId == id && owner.Current(value, true);
        }
        private static bool BeforeVote(object __instance, string __0, string __5)
        {
            var owner = active;
            if (owner == null) return true;
            if (!owner.HostCurrent(__instance)) return __5 != "OnPointerClick" || owner.refusedScene != __0;
            if ((bool)owner.vote.GetValue(__instance)) return true;
            Payment value = owner.selection;
            if (__5 != "OnPointerClick" || !owner.ownClick || (value == null || value.Receipt.SceneId != __0))
            {
                if (__5 == "OnPointerClick" && owner.refusedScene == __0) return false;
                if (value != null && value.State == Phase.Committed && !SourceRetired(value)) Retain(value);
                owner.CancelSelection(); owner.selection = null; owner.refusedScene = null;
                return true;
            }
            return value.State == Phase.Captured && value.Receipt.IsCurrent();
        }
        private static void AfterVote(object __instance)
        {
            var owner = active; Payment value = owner == null ? null : owner.selection;
            if (value != null && value.State == Phase.Captured && owner.Current(value, true)) value.VoteBound = true;
        }
        private static bool BeforeBroadcast(object __instance)
        {
            var owner = active;
            if (owner == null) return true;
            Payment value = owner.selection;
            if (!owner.HostCurrent(__instance)) return (value == null || !ReferenceEquals(value.Native, __instance))
                && (owner.refusedScene == null || owner.refusedScene != (string)owner.target.GetValue(__instance));
            if (value == null) return owner.refusedScene == null || owner.refusedScene != (string)owner.target.GetValue(__instance);
            if (!value.VoteBound) { owner.Reject(value, "unbound_selection"); return false; }
            if (value.State != Phase.Captured) return false;
            value.State = Phase.Checking;
            bool enough = false;
            try { enough = owner.Current(value, true) && value.Receipt.Cost.Enough && owner.Current(value, true); }
            catch (Exception e) { ModBehaviour.CriticalLog("coop-map-payment-preflight", "[BossRush Coop] Map payment preflight failed: " + e.Message); }
            if (!enough || value.CancelRequested)
            { owner.Reject(value, "lease_or_funds"); return false; }

            value.State = Phase.Attempted; // Before Pay, including all synchronous notifications/reentry.
            bool paid = false;
            try { paid = value.Receipt.Cost.Pay(true, true); }
            catch (Exception e) { ModBehaviour.CriticalLog("coop-map-payment", "[BossRush Coop] Map payment outcome requires confirmation: " + e.Message); }
            if (!paid && !value.ReceiptSeen)
            {
                value.State = Phase.Unknown; Retain(value);
                owner.Reject(value, "payment_unconfirmed"); return false;
            }
            if (value.CancelRequested || !owner.Current(value, true))
            {
                value.State = Phase.Cancelled; Retain(value);
                owner.Reject(value, "cancelled_after_payment"); TickRefunds(); return false;
            }
            // Publish only a real, complete payment to the existing E/F/G/H entry owners.
            PublishingReceipt = true;
            try
            {
                if (!value.Receipt.NotePaid())
                { value.State = Phase.Cancelled; Retain(value); owner.Reject(value, "receipt_lease"); return false; }
                value.State = Phase.Committed; // The original may now send; exceptions cannot justify refund/retry.
                return true;
            }
            catch (Exception e)
            {
                // Publication itself has no inventory mutation, but the native payment
                // has already completed. Keep the confirmed obligation if a provider fails.
                value.State = Phase.Cancelled; Retain(value); owner.Reject(value, "receipt_publication_exception");
                ModBehaviour.CriticalLog("coop-map-payment-receipt", "[BossRush Coop] Paid map receipt could not be published: " + e.Message);
                return false;
            }
            finally { PublishingReceipt = false; }
        }
        private void OnCostPaid(Cost cost)
        {
            Payment value = selection;
            if (value != null && value.State == Phase.Attempted && cost.money == value.Receipt.Cost.money
                && ReferenceEquals(cost.items, value.Receipt.Cost.items) && cost.items != null && cost.items.Length == 1
                && cost.items[0].id == BossRushMapSelectionHelper.GetBossRushTicketTypeId() && cost.items[0].amount == 1L)
                value.ReceiptSeen = true;
        }
        private static bool BeforeHostLoad(object __instance)
        {
            var owner = active;
            if (owner == null) return true;
            Payment value = owner.selection;
            if (!owner.HostCurrent(__instance)) return (value == null || !ReferenceEquals(value.Native, __instance))
                && (owner.refusedScene == null || owner.refusedScene != (string)owner.target.GetValue(__instance));
            if (value == null) return owner.refusedScene == null || owner.refusedScene != (string)owner.target.GetValue(__instance);
            if (!value.VoteBound) return false;
            if (value.State != Phase.Committed || value.HostLoadClaimed || !owner.Current(value, false)) return false;
            value.HostLoadClaimed = true;
            return true;
        }
        private void Reject(Payment value, string reason)
        {
            CancelSelection();
            value.CancelRequested = true;
            try
            {
                if (!cancelling && HostCurrent(value.Native) && (bool)vote.GetValue(value.Native))
                { cancelling = true; cancel.Invoke(value.Native, null); }
            }
            catch (Exception e) { ModBehaviour.CriticalLog("coop-map-payment-cancel", "[BossRush Coop] Could not cancel rejected map vote: " + e.Message); }
            finally { cancelling = false; }
            ModBehaviour.CriticalLog("coop-map-payment-rejected", "[BossRush Coop] Map departure blocked: " + reason);
        }
        private static void Retain(Payment value) { if (!unresolved.Contains(value)) unresolved.Add(value); }
        private void CancelSelection()
        {
            Payment value = selection;
            if (value == null || value.State == Phase.Committed || value.State == Phase.Unknown) return;
            value.CancelRequested = true;
            if (value.State == Phase.Captured || value.State == Phase.Checking) value.State = Phase.Cancelled;
        }
        private static void BeforeCancel(object __instance)
        { if (active != null && ReferenceEquals(active.instance.GetValue(null), __instance)) active.CancelSelection(); }
        private static void AfterNativeCancel()
        {
            var owner = active;
            if (owner != null && owner.selection != null && !(bool)owner.vote.GetValue(owner.selection.Native)) owner.CancelSelection();
        }
        internal static void OnMapClosed()
        {
            var owner = active; Payment value = owner == null ? null : owner.selection;
            if (value == null || value.State == Phase.Committed) return;
            owner.CancelSelection(); owner.Reject(value, "map_closed");
        }
        private void OnInvalidated() { CancelSelection(); }
        private void OnUnloaded(Scene scene)
        {
            if (selection == null || selection.Handle != scene.handle) return;
            selection.SourceRetired = true; CancelSelection(); RetireCompleted();
        }
        internal static void TickRefunds()
        {
            if (active != null) active.RetireCompleted();
            for (int i = unresolved.Count - 1; i >= 0; i--)
            {
                Payment value = unresolved[i];
                if (value.State == Phase.Committed && SourceRetired(value)) { unresolved.RemoveAt(i); continue; }
                if (value.State != Phase.Cancelled || value.Refunding) continue;
                value.Refunding = true;
                try { if (value.Receipt.Refund()) unresolved.RemoveAt(i); }
                finally { value.Refunding = false; }
            }
        }
        internal static void ResetStaticCaches() { unresolved.Clear(); interrupted = null; PublishingReceipt = false; }
        public void Dispose()
        {
            CancelSelection();
            interrupted = selection;
            if (selection != null && selection.State == Phase.Committed && !SourceRetired(selection)) Retain(selection);
            if (subscribed)
            {
                EconomyManager.OnCostPaid -= OnCostPaid;
                BossRushCoop.SessionChanged -= OnInvalidated;
                Saves.SavesSystem.OnSetFile -= OnInvalidated;
                Saves.SavesSystem.OnSaveDeleted -= OnInvalidated;
                SceneManager.sceneUnloaded -= OnUnloaded;
                subscribed = false;
            }
            if (harmony != null) { harmony.UnpatchAll(PatchOwner); harmony = null; }
            if (ReferenceEquals(active, this)) active = null;
        }
    }
}
