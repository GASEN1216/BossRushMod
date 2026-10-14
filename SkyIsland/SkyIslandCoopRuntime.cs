using System;
using UnityEngine;

namespace BossRush
{
    /// <summary>联机航行请求。每端先准备场景资源，再由房主发起官方切图投票。</summary>
    internal sealed class SkyIslandCoopRuntime : IDisposable
    {
        [Serializable] internal sealed class Travel { public int Seed, Generation; public string Id; public bool InRaid; }
        private const string RequestTopic = "sky.travel.request";
        private const string StartTopic = "sky.travel.start";
        private readonly ModBehaviour owner;
        private readonly IDisposable request, start;
        private float requestAt;
        private float publishAt;
        internal static SkyIslandCoopRuntime Current { get; private set; }
        internal int Seed { get; private set; }
        internal string TravelId { get; private set; }
        internal int DepartureGeneration { get; private set; }

        internal SkyIslandCoopRuntime(ModBehaviour host)
        {
            owner = host;
            Current = this;
            request = BossRushCoop.Register(RequestTopic, OnRequest);
            start = BossRushCoop.Register(StartTopic, OnStart);
        }

        internal void Enter(Action<string, bool> status)
        {
            if (BossRushCoop.IsClient)
            {
                if (Time.unscaledTime < requestAt) return;
                requestAt = Time.unscaledTime + 2f;
                BossRushCoop.SendToHost(RequestTopic, new Travel());
                status(L10n.T("已向房主申请群岛航行", "Sky Islands departure requested from the host"), false);
                return;
            }
            StartTravel(status);
        }

        private void OnRequest(BossRushCoopMessage message)
        {
            if (!BossRushCoop.IsHost || owner == null || BossRushCoop.FindPlayer(message.SenderId) == null) return;
            StartTravel(Report);
        }

        private void StartTravel(Action<string, bool> status)
        {
            if (!BossRushCoop.SessionReady) { status(L10n.T("正在等待同行者准备完成", "Waiting for your party to be ready"), false); return; }
            string reason;
            if (!SkyIslandSession.CanEnter(owner, out reason)) { status(reason, true); return; }
            Seed = unchecked(Environment.TickCount ^ (int)(Time.realtimeSinceStartup * 1000f));
            TravelId = Guid.NewGuid().ToString("N");
            DepartureGeneration = BossRushCoop.SceneGeneration;
            if (BossRushCoop.IsConnected) BossRushCoop.Broadcast(StartTopic, new Travel { Seed = Seed, Id = TravelId, Generation = DepartureGeneration });
            SkyIslandSession.EnterPrepared(owner, status);
        }

        private void OnStart(BossRushCoopMessage message)
        {
            if (!BossRushCoop.IsClient || !message.IsFromHost || owner == null) return;
            Travel data = message.Read<Travel>();
            if (data == null || string.IsNullOrEmpty(data.Id) || data.Id.Length > 64 || data.Generation < 0) return;
            Seed = data.Seed;
            TravelId = data.Id;
            DepartureGeneration = data.Generation;
            if (owner.GetComponent<SkyIslandSession>() != null) return;
            // 邀请者的航路决定共享地图入口；受邀者的剧情仍由自己的 StoryService 保存。
            if (data.InRaid) SkyIslandSession.EnterCoopArrival(owner, Report);
            else SkyIslandSession.EnterPrepared(owner, Report);
        }

        internal void Tick()
        {
            SkyIslandSession session = owner != null ? owner.GetComponent<SkyIslandSession>() : null;
            if (!BossRushCoop.IsHost || session == null || !session.CoopTravelActive
                || string.IsNullOrEmpty(TravelId) || Time.unscaledTime < publishAt) return;
            publishAt = Time.unscaledTime + 0.5f;
            // 原生频道只缓存最新包；抵达后换代重发，让迟加入者取得同一趟的种子与身份。
            BossRushCoop.Broadcast(StartTopic, new Travel { Seed = Seed, Id = TravelId, Generation = DepartureGeneration,
                InRaid = BossRushCoop.SceneGeneration > DepartureGeneration || SkyIslandSceneReferenceBridge.IsActiveScene });
        }

        internal void PrepareNativeArrival()
        {
            if (!BossRushCoop.IsClient || owner == null || owner.GetComponent<SkyIslandSession>() != null) return;
            // 原生 COOP 直接调用官方 LoadScene 跟图。必须在它卸载基地、读取 bundle 场景前接入租约。
            SkyIslandSession.EnterCoopArrival(owner, Report);
        }

        private void Report(string text, bool error)
        {
            if (owner != null) owner.ShowMessage(text);
            if (error) Debug.LogWarning("[SkyIslandCoop] " + text);
        }

        public void Dispose()
        {
            request.Dispose(); start.Dispose();
            if (ReferenceEquals(Current, this)) Current = null;
        }
    }
}
