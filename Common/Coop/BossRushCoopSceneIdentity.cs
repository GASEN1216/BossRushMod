using System;
using System.Collections.Generic;
using Duckov.Scenes;
using UnityEngine.SceneManagement;

namespace BossRush
{
    // Only our channel carries this marker. The native scene RPC remains unchanged.
    [Serializable]
    internal sealed class BossRushCoopSceneMarker
    {
        public int version = 1;
        public string session;
        public long serial;
        public string token;
        public string sceneId;
        public string curtainGuid;
        public bool notifyEvac;
        public bool saveToFile;
        public bool useLocation;
        public string locationName;

        internal bool SameDestination(BossRushCoopSceneMarker other)
        {
            return other != null && sceneId == other.sceneId && (curtainGuid ?? "") == (other.curtainGuid ?? "")
                && notifyEvac == other.notifyEvac && saveToFile == other.saveToFile
                && useLocation == other.useLocation && (locationName ?? "") == (other.locationName ?? "");
        }

        internal bool Valid()
        {
            Guid parsed;
            return version == 1 && serial > 0 && Guid.TryParseExact(session, "N", out parsed)
                && Guid.TryParseExact(token, "N", out parsed) && !string.IsNullOrEmpty(sceneId) && sceneId.Length <= 256
                && (curtainGuid == null || curtainGuid.Length <= 256) && (locationName == null || locationName.Length <= 256);
        }
    }

    /// <summary>
    /// Binds a host travel marker to an actual local load, never to arrival order of rosters.
    /// Native reflection and Harmony remain in the Transport boundary.
    /// </summary>
    internal sealed class BossRushCoopSceneIdentity : IDisposable
    {
        private sealed class Approval
        {
            internal BossRushCoopSceneMarker Marker;
            internal object Service, Peer;
            internal MultiSceneCore Source;
            internal int SourceHandle;
        }

        private readonly Func<bool> live, host;
        private readonly Func<object> service, peer;
        private readonly Func<string> session;
        private object contextService, contextPeer;
        private bool contextHost, subscribed, travelSeen, loadSeen, everBound;
        private MultiSceneCore observedCore, initialCore;
        private int observedHandle = int.MinValue;
        private string observedId, localToken, firstOfferedToken;
        private Approval staged, approved, loading;
        private readonly Dictionary<string, long> markerSerials = new Dictionary<string, long>();
        private readonly HashSet<string> retiredSessions = new HashSet<string>();
        private string acceptedRoom;
        internal long Revision { get; private set; }

        internal BossRushCoopSceneIdentity(Func<bool> live, Func<bool> host, Func<object> service,
            Func<object> peer, Func<string> session)
        {
            this.live = live; this.host = host; this.service = service; this.peer = peer; this.session = session;
        }

        internal void Install()
        {
            if (subscribed) return;
            SceneLoader.onStartedLoadingScene += OnStartedLoading;
            SceneManager.sceneUnloaded += OnSceneUnloaded;
            subscribed = true;
        }

        internal string LocalToken
        {
            get { return CurrentCore() ? localToken : null; }
        }

        internal bool Matches(string token)
        {
            return !string.IsNullOrEmpty(token) && token == LocalToken;
        }

        private bool Context()
        {
            object currentService = service();
            bool currentHost = host();
            object currentPeer = currentHost ? null : peer();
            if (!live() || currentService == null || (!currentHost && currentPeer == null)) return false;
            if (!ReferenceEquals(contextService, currentService) || !ReferenceEquals(contextPeer, currentPeer) || contextHost != currentHost)
            {
                ResetSession();
                markerSerials.Clear(); retiredSessions.Clear();
                contextService = currentService; contextPeer = currentPeer; contextHost = currentHost;
                acceptedRoom = session();
            }
            return true;
        }

        private bool SameContext(Approval value)
        {
            return value != null && ReferenceEquals(value.Service, contextService) && ReferenceEquals(value.Peer, contextPeer);
        }

        private bool Stable(MultiSceneCore core)
        {
            if (core == null || core.SceneInfo == null || core.IsLoading || SceneLoader.IsSceneLoading) return false;
            Scene scene = core.gameObject.scene;
            return scene.IsValid() && scene.isLoaded && !string.IsNullOrEmpty(core.SceneInfo.ID);
        }

        private bool CurrentCore()
        {
            return Context() && approved == null && loading == null && Stable(observedCore)
                && ReferenceEquals(observedCore, MultiSceneCore.Instance) && observedCore.gameObject.scene.handle == observedHandle
                && observedCore.SceneInfo.ID == observedId;
        }

        internal void ObserveCore(MultiSceneCore core)
        {
            if (!Context()) return;
            if (!Stable(core) || !ReferenceEquals(core, MultiSceneCore.Instance)) return;
            int handle = core.gameObject.scene.handle;
            string id = core.SceneInfo.ID;
            if (ReferenceEquals(core, observedCore) && handle == observedHandle && id == observedId
                && (!host() || everBound || travelSeen || loadSeen || string.IsNullOrEmpty(session()))) return;
            localToken = null;
            if (initialCore == null && !travelSeen && !loadSeen) initialCore = core;
            if (loading != null)
            {
                Approval completed = loading;
                // Native delivery runs before ModNetworkPump. Its new-room welcome may still be queued.
                if (SameContext(completed) && completed.Marker.session != session()) return;
                loading = null;
                if (SameContext(completed) && completed.Marker.session == session()
                    && !ReferenceEquals(core, completed.Source) && handle != completed.SourceHandle && id == completed.Marker.sceneId)
                {
                    localToken = completed.Marker.token;
                    everBound = true;
                }
            }
            else if (host() && !everBound && !travelSeen && !loadSeen && !string.IsNullOrEmpty(session()))
            {
                localToken = Guid.NewGuid().ToString("N");
                everBound = true;
            }
            observedCore = core; observedHandle = handle; observedId = id;
        }

        internal bool TryAdoptInitial(string token, string sceneId)
        {
            Guid parsed;
            if (host() || !Guid.TryParseExact(token, "N", out parsed) || !Context()) return false;
            if (firstOfferedToken == null) firstOfferedToken = token;
            ObserveCore(MultiSceneCore.Instance);
            if (token != firstOfferedToken || everBound || travelSeen || loadSeen || !CurrentCore()
                || !ReferenceEquals(initialCore, observedCore) || sceneId != observedId) return false;
            localToken = token; everBound = true;
            return true;
        }

        private Approval Capture(BossRushCoopSceneMarker marker)
        {
            var source = MultiSceneCore.Instance;
            // A real native join can start from the menu, before any MultiSceneCore exists.
            if (source == null) return new Approval { Marker = marker, Service = contextService, Peer = contextPeer, SourceHandle = int.MinValue };
            if (source.SceneInfo == null) return null;
            Scene scene = source.gameObject.scene;
            if (!scene.IsValid() || !scene.isLoaded) return null;
            return new Approval { Marker = marker, Service = contextService, Peer = contextPeer, Source = source, SourceHandle = scene.handle };
        }

        internal void StageMarker(BossRushCoopSceneMarker marker)
        {
            if (!Context() || host() || marker == null || !marker.Valid()) return;
            if (retiredSessions.Contains(marker.session)) return;
            long prior;
            if (markerSerials.TryGetValue(marker.session, out prior) && marker.serial <= prior) return;
            markerSerials[marker.session] = marker.serial;
            staged = Capture(marker);
            Revision++;
        }

        internal void ApproveHost(BossRushCoopSceneMarker marker)
        {
            if (!Context() || !host() || marker == null || !marker.Valid() || marker.session != session()) return;
            travelSeen = true;
            approved = Capture(marker);
            Revision++;
        }

        internal void ObserveNativeBegin(BossRushCoopSceneMarker destination)
        {
            if (!Context() || host()) return;
            travelSeen = true;
            Approval next = staged;
            staged = null; // One ingress marker may identify one real native Begin only.
            approved = SameContext(next) && next.Marker.SameDestination(destination) ? next : null;
            Revision++;
        }

        internal void CancelPending()
        {
            staged = approved = null;
            Revision++;
        }

        private void OnStartedLoading(SceneLoadingContext context)
        {
            if (!Context()) return;
            loadSeen = true;
            localToken = null;
            Approval next = approved;
            approved = null;
            loading = null;
            if (!SameContext(next) || !ReferenceEquals(next.Source, MultiSceneCore.Instance)) return;
            if (!ReferenceEquals(next.Source, null) && (next.Source == null || next.Source.gameObject.scene.handle != next.SourceHandle)) return;
            var info = SceneInfoCollection.GetSceneInfo(next.Marker.sceneId);
            if (info == null || info.SceneReference == null || info.SceneReference.Name != context.sceneName) return;
            loading = next;
            Revision++;
        }

        private void OnSceneUnloaded(Scene scene)
        {
            if (scene.handle == observedHandle) localToken = null;
            if (approved != null && approved.SourceHandle == scene.handle) approved = null;
            if (staged != null && staged.SourceHandle == scene.handle) staged = null;
        }

        internal void ResetSession(string acceptedSession = null)
        {
            // A new host's native Begin can precede its queued welcome. Preserve only that exact new room.
            if (!string.IsNullOrEmpty(acceptedRoom) && acceptedRoom != acceptedSession) retiredSessions.Add(acceptedRoom);
            acceptedRoom = acceptedSession;
            bool current = live() && ReferenceEquals(contextService, service()) && ReferenceEquals(contextPeer, host() ? null : peer());
            staged = current && staged != null && staged.Marker.session == acceptedSession ? staged : null;
            approved = current && approved != null && approved.Marker.session == acceptedSession ? approved : null;
            loading = current && loading != null && loading.Marker.session == acceptedSession ? loading : null;
            travelSeen = staged != null || approved != null || loading != null;
            loadSeen = loading != null;
            observedCore = initialCore = null; observedHandle = int.MinValue; observedId = localToken = firstOfferedToken = null;
            everBound = false;
            Revision++;
        }

        public void Dispose()
        {
            if (subscribed)
            {
                SceneLoader.onStartedLoadingScene -= OnStartedLoading;
                SceneManager.sceneUnloaded -= OnSceneUnloaded;
                subscribed = false;
            }
            ResetSession(); contextService = contextPeer = null;
            markerSerials.Clear(); retiredSessions.Clear();
        }
    }
}
