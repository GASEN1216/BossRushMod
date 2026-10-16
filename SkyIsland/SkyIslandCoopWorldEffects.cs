using System;
using System.Collections.Generic;
using UnityEngine;

namespace BossRush
{
    /// <summary>服务器招式的真实线几何、可破坏道具与减速。表现镜像不运行招式或再次造成伤害。</summary>
    internal sealed class SkyIslandCoopWorldEffects : IDisposable
    {
        [Serializable] private sealed class World { public string TravelId, SceneToken; public long Revision; public Line[] Lines; public Prop[] Props; public Lamp[] Lamps; }
        [Serializable] private sealed class Line
        {
            public int Id; public Vector3 Position, Scale; public Quaternion Rotation;
            public Vector3[] Points; public Color Start, End; public float Width;
            public bool Loop, WorldSpace, Fill, Circle; public int Alignment; public float Radius;
            public Keyframe[] WidthCurve; public GradientColorKey[] Colors; public GradientAlphaKey[] Alphas;
        }
        [Serializable] private sealed class Lamp { public int Id; public Vector3 Position; public Color Color; public float Range, Intensity; }
        [Serializable] private sealed class Prop
        {
            public string Key, Name, Source; public Vector3 Position; public Quaternion Rotation;
            public float Radius, Health; public int Team; public bool Active, Decoy; public Color Tint;
        }
        [Serializable] private sealed class Slow { public string TravelId, Id, AssetId, SceneToken; public float Percent, Seconds; public long Revision; }
        [Serializable] private sealed class PlayerEffects { public string TravelId, AssetId, Stream, SceneToken; public long Noise, Revision; public bool Earmuffs; public int Rootweave; }
        private sealed class PlayerEffectsState
        {
            internal string AssetId, Stream; internal long Noise, Revision = -1; internal bool Earmuffs; internal int Rootweave;
            internal readonly HashSet<string> RetiredStreams = new HashSet<string>(StringComparer.Ordinal);
        }
        private sealed class HostProp { internal GameObject Object; internal Prop Definition; }
        private sealed class ReplicaProp { internal GameObject Object; internal readonly List<Mesh> Baked = new List<Mesh>(); }
        private sealed class LocalSlow
        {
            internal readonly object Source = new object(); internal float Until;
            internal CharacterMainControl Target;
            internal readonly List<BossRushStatModifierRecord> Records = new List<BossRushStatModifierRecord>();
        }
        private readonly Dictionary<int, LineRenderer> lines = new Dictionary<int, LineRenderer>();
        private readonly Dictionary<int, HostProp> props = new Dictionary<int, HostProp>();
        private readonly Dictionary<int, Light> lamps = new Dictionary<int, Light>();
        private readonly Dictionary<string, ReplicaProp> replicas = new Dictionary<string, ReplicaProp>(StringComparer.Ordinal);
        private readonly Dictionary<string, LocalSlow> slows = new Dictionary<string, LocalSlow>(StringComparer.Ordinal);
        private readonly Dictionary<string, long> slowRevisions = new Dictionary<string, long>(StringComparer.Ordinal);
        private readonly Dictionary<string, PlayerEffectsState> playerEffects = new Dictionary<string, PlayerEffectsState>(StringComparer.Ordinal);
        private readonly List<string> expiredSlows = new List<string>();
        private readonly IDisposable worldSubscription, slowSubscription, playerSubscription;
        private readonly Func<Transform> root;
        private float nextPublish, nextPlayerPublish;
        private string localAsset, localStream;
        private long localNoise, localRevision;
        private bool shootSubscribed;
        private long sequence;
        private World latest;
        private static SkyIslandCoopWorldEffects current;
        internal static event Action<CharacterMainControl, long> PlayerGunNoise;
        private string TravelId { get { return SkyIslandCoopRuntime.Current != null ? SkyIslandCoopRuntime.Current.TravelId : null; } }
        internal SkyIslandCoopWorldEffects(Func<Transform> sceneRoot)
        {
            root = sceneRoot; current = this;
            worldSubscription = BossRushCoop.Register("sky.effects.world", ReceiveWorld);
            slowSubscription = BossRushCoop.Register("sky.effects.slow", ReceiveSlow);
            playerSubscription = BossRushCoop.Register("sky.effects.player", ReceivePlayerEffects);
            if (!shootSubscribed) { ItemAgent_Gun.OnMainCharacterShootEvent += OnLocalShot; shootSubscribed = true; }
        }
        internal static bool WorldEarmuffs
        {
            get
            {
                if (!BossRushCoop.IsConnected) return SkyIslandBossGearWorn.Earmuffs;
                foreach (BossRushCoopPlayer member in BossRushCoop.Players)
                {
                    if (!member.IsAlive) continue;
                    if (member.Id == BossRushCoop.LocalPlayerId) { if (SkyIslandBossGearWorn.Earmuffs) return true; continue; }
                    PlayerEffectsState value;
                    if (current != null && current.playerEffects.TryGetValue(member.Id, out value)
                        && value.AssetId == member.AssetId && value.Earmuffs) return true;
                }
                return false;
            }
        }
        internal static int WorldRootweavePieces
        {
            get
            {
                if (!BossRushCoop.IsConnected) return SkyIslandBossGearWorn.RootweavePieces;
                int pieces = 0;
                foreach (BossRushCoopPlayer member in BossRushCoop.Players)
                {
                    if (!member.IsAlive) continue;
                    if (member.Id == BossRushCoop.LocalPlayerId) { pieces = Math.Max(pieces, SkyIslandBossGearWorn.RootweavePieces); continue; }
                    PlayerEffectsState value;
                    if (current != null && current.playerEffects.TryGetValue(member.Id, out value) && value.AssetId == member.AssetId)
                        pieces = Math.Max(pieces, value.Rootweave);
                }
                return pieces;
            }
        }
        private void EnsureLocalStream()
        {
            if (localAsset == BossRushCoop.LocalAssetId) return;
            localAsset = BossRushCoop.LocalAssetId; localStream = Guid.NewGuid().ToString("N"); localNoise = localRevision = 0;
        }
        private void OnLocalShot(ItemAgent_Gun gun)
        {
            CharacterMainControl player = CharacterMainControl.Main;
            if (!BossRushCoop.IsConnected || player == null || gun == null || gun.Holder != player
                || player.Health == null || player.Health.IsDead || string.IsNullOrEmpty(TravelId)) return;
            EnsureLocalStream();
            long noise = SkyIslandBossGearWorn.Earmuffs ? 1L : 2L;
            if (!SkyIslandBossRules.InCave(player.transform.position.x, player.transform.position.z)) noise = 0;
            if (noise == 0) return;
            localNoise += noise;
            // 噪声是累加事实，下个既有 4 Hz 包完整上报；逐枪发包不增加玩法信息。
        }
        private void PublishPlayerEffects()
        {
            if (!BossRushCoop.HasBoundScene) return;
            EnsureLocalStream();
            var value = new PlayerEffects { TravelId = TravelId, SceneToken = BossRushCoop.SceneToken, AssetId = localAsset, Stream = localStream,
                Noise = localNoise, Revision = ++localRevision, Earmuffs = SkyIslandBossGearWorn.Earmuffs, Rootweave = SkyIslandBossGearWorn.RootweavePieces };
            if (BossRushCoop.IsClient) BossRushCoop.SendToHost("sky.effects.player", value);
            else if (BossRushCoop.IsHost) ApplyPlayerEffects(BossRushCoop.LocalPlayerId, value);
        }
        private void ReceivePlayerEffects(BossRushCoopMessage message)
        { if (BossRushCoop.IsHost) ApplyPlayerEffects(message.SenderId, message.Read<PlayerEffects>()); }
        private void ApplyPlayerEffects(string playerId, PlayerEffects value)
        {
            BossRushCoopPlayer member = BossRushCoop.FindPlayer(playerId);
            if (!BossRushCoop.HasBoundScene || member == null || value == null || value.TravelId != TravelId || value.AssetId != member.AssetId
                || string.IsNullOrEmpty(value.SceneToken) || value.SceneToken != BossRushCoop.SceneToken
                || string.IsNullOrEmpty(value.Stream) || value.Stream.Length > 64 || value.Noise < 0 || value.Revision < 0
                || value.Rootweave < 0 || value.Rootweave > SkyIslandBossRules.RootweaveSet.Length) return;
            PlayerEffectsState state;
            if (!playerEffects.TryGetValue(playerId, out state) || state.AssetId != value.AssetId)
            { state = new PlayerEffectsState { AssetId = value.AssetId }; playerEffects[playerId] = state; }
            if (state.Stream != value.Stream)
            {
                if (state.RetiredStreams.Contains(value.Stream)) return;
                if (state.Stream != null) state.RetiredStreams.Add(state.Stream);
                state.Stream = value.Stream; state.Noise = 0; state.Revision = -1;
            }
            if (value.Revision <= state.Revision) return;
            state.Revision = value.Revision;
            state.Earmuffs = value.Earmuffs;
            state.Rootweave = value.Rootweave;
            if (value.Noise <= state.Noise) return;
            long noise = value.Noise - state.Noise; state.Noise = value.Noise;
            if (member.IsAlive && PlayerGunNoise != null) PlayerGunNoise(member.Character, noise);
        }
        internal static void Track(LineRenderer line)
        { if (current != null && line != null && BossRushCoop.IsHost) current.lines[line.GetInstanceID()] = line; }
        internal static void Track(Light light)
        { if (current != null && light != null && BossRushCoop.IsHost) current.lamps[light.GetInstanceID()] = light; }
        internal static void TrackProp(GameObject value, CharacterMainControl source, bool decoy = false, Color tint = default(Color))
        {
            if (current == null || value == null || !BossRushCoop.IsHost) return;
            HealthSimpleBase health = value.GetComponent<HealthSimpleBase>(); SphereCollider collider = value.GetComponent<SphereCollider>();
            if (health == null || collider == null) return;
            HostProp prop;
            if (!current.props.TryGetValue(value.GetInstanceID(), out prop))
            {
                prop = new HostProp { Object = value, Definition = new Prop { Key = "sky-prop:" + BossRushCoop.RunId
                    + ":" + BossRushCoop.SceneGeneration + ":" + current.TravelId + ":" + (++current.sequence) } };
                current.props.Add(value.GetInstanceID(), prop);
            }
            prop.Definition.Name = value.name; prop.Definition.Radius = collider.radius; prop.Definition.Health = health.maxHealthValue;
            prop.Definition.Team = (int)health.team; prop.Definition.Decoy = decoy; prop.Definition.Tint = tint;
            prop.Definition.Source = BossRushCoop.GetEntityId(source);
            BossRushCoop.RegisterWorldDestructible(health, prop.Definition.Key);
        }
        internal static void SendSlow(string player, string id, float percent, float seconds)
        {
            if (current != null && BossRushCoop.IsHost && BossRushCoop.HasBoundScene && player != BossRushCoop.LocalPlayerId)
            {
                BossRushCoopPlayer target = BossRushCoop.FindPlayer(player);
                if (target == null) return;
                BossRushCoop.SendToPlayer(player, "sky.effects.slow", new Slow { TravelId = current.TravelId, SceneToken = BossRushCoop.SceneToken, AssetId = target.AssetId,
                    Id = id, Percent = percent, Seconds = seconds, Revision = ++current.sequence });
            }
        }
        private void ReceiveSlow(BossRushCoopMessage message)
        {
            if (!BossRushCoop.IsClient || !message.IsFromHost) return;
            Slow data = message.Read<Slow>();
            if (data == null || data.TravelId != TravelId || data.AssetId != BossRushCoop.LocalAssetId || string.IsNullOrEmpty(data.Id)
                || string.IsNullOrEmpty(data.SceneToken) || data.SceneToken != BossRushCoop.SceneToken
                || data.Percent > 0f || data.Percent < -1f || data.Revision <= 0) return;
            long revision;
            if (slowRevisions.TryGetValue(data.Id, out revision) && data.Revision <= revision) return;
            slowRevisions[data.Id] = data.Revision;
            LocalSlow slow;
            if (slows.TryGetValue(data.Id, out slow)) RuntimeStatModifierTracker.RemoveAll(slow.Records, "SkyIslandCoopSlow");
            if (data.Percent == 0f) { slows.Remove(data.Id); return; }
            slow = slow ?? new LocalSlow(); slow.Until = data.Seconds > 0f ? Time.time + data.Seconds : float.PositiveInfinity;
            slow.Target = CharacterMainControl.Main;
            RuntimeStatModifierTracker.TryAdd(CharacterMainControl.Main, ZombieModeStatNames.WalkSpeed, data.Percent, slow.Source, slow.Records, "SkyIslandCoopSlow");
            RuntimeStatModifierTracker.TryAdd(CharacterMainControl.Main, ZombieModeStatNames.RunSpeed, data.Percent, slow.Source, slow.Records, "SkyIslandCoopSlow");
            slows[data.Id] = slow;
        }
        private void ReceiveWorld(BossRushCoopMessage message)
        {
            if (!BossRushCoop.IsClient || !message.IsFromHost) return;
            World state = message.Read<World>();
            if (state != null && state.TravelId == TravelId && !string.IsNullOrEmpty(state.SceneToken) && state.SceneToken == BossRushCoop.SceneToken && state.Revision > 0
                && (latest == null || latest.SceneToken != state.SceneToken || state.Revision > latest.Revision)) latest = state;
        }
        internal void Tick()
        {
            if (!BossRushCoop.IsConnected) return;
            TickSlows();
            if (!BossRushCoop.HasBoundScene) return;
            if (Time.unscaledTime >= nextPlayerPublish)
            { nextPlayerPublish = Time.unscaledTime + 0.25f; PublishPlayerEffects(); }
            Transform worldRoot = root();
            if (worldRoot == null || Time.unscaledTime < nextPublish) return;
            nextPublish = Time.unscaledTime + 0.1f;
            if (BossRushCoop.IsHost) { Publish(); return; }
            ApplyWorld(worldRoot);
        }
        private void TickSlows()
        {
            expiredSlows.Clear();
            foreach (var pair in slows)
                if (Time.time >= pair.Value.Until || pair.Value.Target != CharacterMainControl.Main || CharacterMainControl.Main == null
                    || CharacterMainControl.Main.Health == null || CharacterMainControl.Main.Health.IsDead)
                { RuntimeStatModifierTracker.RemoveAll(pair.Value.Records, "SkyIslandCoopSlow"); expiredSlows.Add(pair.Key); }
            foreach (string id in expiredSlows) slows.Remove(id);
        }
        private void ApplyWorld(Transform worldRoot)
        {
            if (!IsWorldCurrent() || latest.Lines == null || latest.Props == null || latest.Lines.Length > 128 || latest.Props.Length > 64) return;
            var alive = new HashSet<int>();
            foreach (Line entry in latest.Lines)
            {
                if (entry == null || (!entry.Circle && (entry.Points == null || entry.Points.Length > 128))) continue;
                alive.Add(entry.Id); LineRenderer line;
                if (!lines.TryGetValue(entry.Id, out line) || line == null)
                {
                    line = SkyIslandGroundRing.Create(worldRoot, Vector3.zero); lines[entry.Id] = line;
                    if (entry.Fill) SkyIslandBossRingFx.Attach(line);
                }
                line.transform.SetPositionAndRotation(entry.Position, entry.Rotation); line.transform.localScale = entry.Scale;
                line.useWorldSpace = entry.WorldSpace; line.loop = entry.Loop; line.alignment = (LineAlignment)entry.Alignment;
                if (entry.Circle) SkyIslandGroundRing.SetShape(line, entry.Radius, entry.Width, entry.Start);
                else { line.positionCount = entry.Points.Length; line.SetPositions(entry.Points); }
                line.widthMultiplier = entry.Width; line.startColor = entry.Start; line.endColor = entry.End;
                if (entry.WidthCurve != null && entry.WidthCurve.Length <= 16) line.widthCurve = new AnimationCurve(entry.WidthCurve);
                if (entry.Colors != null && entry.Alphas != null && entry.Colors.Length <= 8 && entry.Alphas.Length <= 8)
                { var gradient = new Gradient(); gradient.SetKeys(entry.Colors, entry.Alphas); line.colorGradient = gradient; }
            }
            var remove = new List<int>();
            foreach (var pair in lines) if (!alive.Contains(pair.Key)) { if (pair.Value != null) UnityEngine.Object.Destroy(pair.Value.gameObject); remove.Add(pair.Key); }
            foreach (int id in remove) lines.Remove(id);
            foreach (Prop entry in latest.Props) ApplyProp(entry, worldRoot);
            var removeProps = new List<string>();
            foreach (var pair in replicas)
            {
                bool exists = false; foreach (Prop entry in latest.Props) if (entry != null && entry.Key == pair.Key) { exists = true; break; }
                if (!exists) { SkyIslandBossProps.DestroyDecoy(pair.Value.Object, pair.Value.Baked); removeProps.Add(pair.Key); }
            }
            foreach (string id in removeProps) replicas.Remove(id);
            var aliveLamps = new HashSet<int>();
            if (latest.Lamps != null && latest.Lamps.Length <= 128) foreach (Lamp entry in latest.Lamps)
            {
                if (entry == null) continue; aliveLamps.Add(entry.Id); Light lamp;
                if (!lamps.TryGetValue(entry.Id, out lamp) || lamp == null)
                { var go = new GameObject("SkyCoopBossLight"); go.transform.SetParent(worldRoot, true); lamp = go.AddComponent<Light>(); lamp.type = LightType.Point; lamp.shadows = LightShadows.None; lamps[entry.Id] = lamp; }
                lamp.transform.position = entry.Position; lamp.color = entry.Color; lamp.range = entry.Range; lamp.intensity = entry.Intensity;
            }
            var removeLamps = new List<int>();
            foreach (var pair in lamps) if (!aliveLamps.Contains(pair.Key)) { if (pair.Value != null) UnityEngine.Object.Destroy(pair.Value.gameObject); removeLamps.Add(pair.Key); }
            foreach (int id in removeLamps) lamps.Remove(id);
        }
        private bool IsWorldCurrent()
        { return BossRushCoop.HasBoundScene && latest != null && latest.SceneToken == BossRushCoop.SceneToken; }
        private void Publish()
        {
            var snapshotLines = new List<Line>();
            var staleLines = new List<int>();
            foreach (var pair in lines)
            {
                LineRenderer line = pair.Value;
                if (line == null) { staleLines.Add(pair.Key); continue; }
                if (!line.enabled || !line.gameObject.activeInHierarchy || line.positionCount > 128) continue;
                // GroundRing 的 64 点圆由同一生产函数重建，避免每个圆每拍传 64 个向量。
                bool circle = line.loop && !line.useWorldSpace && line.positionCount == SkyIslandGroundRing.Segments;
                Vector3[] points = circle ? null : new Vector3[line.positionCount]; if (!circle) line.GetPositions(points);
                snapshotLines.Add(new Line { Id = pair.Key, Position = line.transform.position, Rotation = line.transform.rotation,
                    Scale = line.transform.lossyScale, Points = points, Start = line.startColor, End = line.endColor,
                    Width = line.widthMultiplier, Loop = line.loop, WorldSpace = line.useWorldSpace, Alignment = (int)line.alignment,
                    Fill = line.GetComponent<SkyIslandBossRingFx>() != null, Circle = circle, Radius = circle ? line.GetPosition(0).x : 0f,
                    WidthCurve = line.widthCurve.keys, Colors = line.colorGradient.colorKeys, Alphas = line.colorGradient.alphaKeys });
            }
            foreach (int id in staleLines) lines.Remove(id);
            var snapshotProps = new List<Prop>();
            var staleProps = new List<int>();
            foreach (var pair in props)
            {
                HostProp source = pair.Value;
                if (source.Object == null) { staleProps.Add(pair.Key); continue; }
                Prop entry = source.Definition; entry.Position = source.Object.transform.position; entry.Rotation = source.Object.transform.rotation;
                entry.Active = source.Object.activeSelf; snapshotProps.Add(entry);
            }
            foreach (int id in staleProps) props.Remove(id);
            var snapshotLamps = new List<Lamp>(); var staleLamps = new List<int>();
            foreach (var pair in lamps)
            {
                Light light = pair.Value; if (light == null) { staleLamps.Add(pair.Key); continue; }
                if (light.enabled && light.gameObject.activeInHierarchy) snapshotLamps.Add(new Lamp { Id = pair.Key, Position = light.transform.position, Color = light.color, Range = light.range, Intensity = light.intensity });
            }
            foreach (int id in staleLamps) lamps.Remove(id);
            BossRushCoop.Broadcast("sky.effects.world", new World { TravelId = TravelId, SceneToken = BossRushCoop.SceneToken, Revision = ++sequence,
                Lines = snapshotLines.ToArray(), Props = snapshotProps.ToArray(), Lamps = snapshotLamps.ToArray() });
        }
        private void ApplyProp(Prop entry, Transform worldRoot)
        {
            if (entry == null || string.IsNullOrEmpty(entry.Key)) return;
            ReplicaProp replica;
            if (!replicas.TryGetValue(entry.Key, out replica) || replica.Object == null)
            {
                CharacterMainControl source = BossRushCoop.FindEntity(entry.Source);
                if (entry.Decoy && source == null) return;
                replica = new ReplicaProp();
                replica.Object = entry.Decoy ? SkyIslandBossProps.CreateDecoy(worldRoot, source, entry.Health, entry.Tint, replica.Baked)
                    : SkyIslandBossProps.CreateReceiver(worldRoot, entry.Name, entry.Position, entry.Radius, entry.Health, source);
                if (replica.Object == null) return;
                replica.Object.name = entry.Name; replica.Object.transform.SetPositionAndRotation(entry.Position, entry.Rotation);
                HealthSimpleBase health = replica.Object.GetComponent<HealthSimpleBase>(); health.team = (Teams)entry.Team;
                SkyIslandBossProps.Activate(replica.Object, source);
                BossRushCoop.RegisterWorldDestructible(health, entry.Key);
                replicas[entry.Key] = replica;
            }
            replica.Object.transform.SetPositionAndRotation(entry.Position, entry.Rotation);
            if (replica.Object.activeSelf != entry.Active) replica.Object.SetActive(entry.Active);
        }
        public void Dispose()
        {
            worldSubscription.Dispose(); slowSubscription.Dispose(); playerSubscription.Dispose();
            if (shootSubscribed) { ItemAgent_Gun.OnMainCharacterShootEvent -= OnLocalShot; shootSubscribed = false; }
            foreach (var slow in slows.Values) RuntimeStatModifierTracker.RemoveAll(slow.Records, "SkyIslandCoopSlow");
            foreach (var prop in replicas.Values) SkyIslandBossProps.DestroyDecoy(prop.Object, prop.Baked);
            if (BossRushCoop.IsClient) foreach (var line in lines.Values) if (line != null) UnityEngine.Object.Destroy(line.gameObject);
            if (BossRushCoop.IsClient) foreach (var lamp in lamps.Values) if (lamp != null) UnityEngine.Object.Destroy(lamp.gameObject);
            slows.Clear(); slowRevisions.Clear(); expiredSlows.Clear(); playerEffects.Clear(); replicas.Clear(); lines.Clear(); props.Clear(); lamps.Clear(); latest = null;
            if (ReferenceEquals(current, this)) current = null;
        }
    }
}
