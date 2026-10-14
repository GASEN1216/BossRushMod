using System;
using System.Collections.Generic;

namespace BossRush
{
    internal struct SceneRuntimeContext { }
    internal sealed class RecordingModule : BossRushRuntimeModuleBase
    {
        private readonly string name;
        internal RecordingModule(string name) { this.name = name; }
        public override string ModuleName { get { return name; } }
        public override void OnAwake(ModBehaviour owner) { AssemblyOrder.Awakened.Add(name); }
    }
    internal static partial class BossRushCoop
    {
        internal static BossRushCoopRuntimeModule Runtime;
    }
    internal sealed partial class ModeGRuntimeModule
    {
        public override string ModuleName { get { return "ModeG"; } }
    }
    public partial class ModBehaviour
    {
        internal static void CriticalLog(string key, string message) { AssemblyOrder.Errors.Add(message); }
    }
    internal sealed partial class BossRushCoopRuntimeModule : BossRushRuntimeModuleBase
    {
        private ModBehaviour owner;
        private bool assetEventsSubscribed;
        private BossRushCoopTransport transport;
        private BossRushCoopWorldLoot worldLoot;
        private BossRushCoopControl control;
        public override string ModuleName { get { return "Coop"; } }
        internal string GetLocalAssetId() { return BossRushCoop.LocalAssetId; }
        internal IDisposable Register(string topic, Action<BossRushCoopMessage> action)
        { return BossRushCoop.RegisterNetwork(topic, action); }
        private void OnSlotSelected() { }
        private void OnSlotDeleted() { }
        private void OnPacket() { }
        private void OnPeerDisconnected() { }
        private void OnNativePlayerSpawned() { }
        private void OnAiSpawned() { }
        public override void OnUpdate(float deltaTime, float unscaledDeltaTime)
        { AssemblyOrder.Awakened.Add("Coop"); }
    }
    internal sealed class BossRushCoopTransport
    {
        internal BossRushCoopTransport(Action packet, Action disconnected, Action playerSpawned, Action aiSpawned) { }
    }
    internal sealed class BossRushCoopWorldLoot { internal BossRushCoopWorldLoot(BossRushCoopTransport transport) { } }
    internal sealed class BossRushCoopControl { }
    internal static partial class AssemblyOrder
    {
        internal static readonly List<string> Awakened = new List<string>();
        internal static readonly List<string> Errors = new List<string>();
        internal static void Run(Action<bool, string> check)
        {
            Harness.Reset();
            BossRushCoop.Runtime = null;
            Awakened.Clear(); Errors.Clear();
            var owner = new ModBehaviour();
            var host = new BossRushRuntimeModuleHost();
            RegisterProductionModuleList(host, owner);
            check(BossRushCoop.Runtime == null, "Collecting the complete production module list must not install Coop or register consumers");
            check(BossRushCoop.Current.Handlers.Count == 0, "Registration must keep idle gameplay channels uncreated until OnAwake");
            host.OnAwake(owner);
            check(Errors.Count == 0, "Actual ordered module OnAwake must not hide a consumer-before-Coop error: " + string.Join(";", Errors));
            check(BossRushCoop.Runtime != null && Awakened.IndexOf("Coop") >= 0, "Production Coop.OnAwake installs the facade before G consumes it");
            check(BossRushCoop.Current.Handlers.ContainsKey("BossRush.Gameplay.G.snapshot"), "The registered G shell OnAwake creates the real idle snapshot consumer");
            check(Awakened.Count == ProductionModuleCount - 1, "Every production module except G must execute its recorded native boundary callback");
            check(!owner.Active && owner.Consumed.Count == 0 && ModeGRunContext.Current == null,
                "Whole module assembly cannot start G or consume a ticket/relic");
            owner.Shutdown();
        }
    }
}
namespace Saves
{
    internal static class SavesSystem
    {
        internal static event Action OnSetFile;
        internal static event Action OnSaveDeleted;
    }
}
