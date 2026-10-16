using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace BossRush
{
    [Serializable]
    internal sealed class BossRushCoopFxArgument
    {
        public string text;
        public float[] values;
    }

    [Serializable]
    internal sealed class BossRushCoopFxPacket
    {
        public string run;
        public int scene;
        public string token;
        public string origin;
        public string method;
        public BossRushCoopFxArgument[] args;
    }

    [Serializable]
    internal sealed class BossRushCoopHazardState
    {
        public int id;
        public int kind;
        public bool variant;
        public Vector3 position;
        public Quaternion rotation;
    }
    [Serializable]
    internal sealed class BossRushCoopHazardPacket
    {
        public string run;
        public int scene;
        public string token;
        public BossRushCoopHazardState[] hazards;
        public int hit;
        public float damage;
        public BossRushCoopBossState[] bosses;
    }
    [Serializable]
    internal sealed class BossRushCoopBossState
    {
        public int ai;
        public string kind;
        public bool visible;
        public Vector3 scale;
        public bool eyesOnly;
        public bool charging;
        public float intensity;
        public float visibility;
    }

    /// <summary>只转发白名单纯表现调用。伤害、装备行动、冷却继续由既有主人执行与底层伤害协议同步。</summary>
    internal sealed class BossRushCoopFxRuntimeModule : BossRushRuntimeModuleBase
    {
        private const string Topic = "content.fx";
        private readonly Dictionary<string, MethodInfo> methods = new Dictionary<string, MethodInfo>();
        private Harmony harmony;
        private IDisposable channel;
        private IDisposable hazardChannel;
        private IDisposable hitChannel;
        private readonly Dictionary<int, Component> hazards = new Dictionary<int, Component>();
        private readonly Dictionary<int, BossRushCoopHazardState> hazardKinds = new Dictionary<int, BossRushCoopHazardState>();
        private readonly Dictionary<int, Component> replicas = new Dictionary<int, Component>();
        private readonly Dictionary<Component, int> replicaIds = new Dictionary<Component, int>();
        private readonly Dictionary<CharacterMainControl, string> bosses = new Dictionary<CharacterMainControl, string>();
        private readonly Dictionary<int, SandstormChampionBody> sandBodies = new Dictionary<int, SandstormChampionBody>();
        private float nextHazardTick;
        private bool sentHazards;
        private Transform remoteRoot;
        private bool subscribed;
        private static BossRushCoopFxRuntimeModule instance;
        private static int depth;
        private static bool replaying;
        public override string ModuleName { get { return "CoopContentFx"; } }

        public override void OnAwake(ModBehaviour owner)
        {
            instance = this;
            if (!subscribed)
            {
                BossRushCoop.SessionChanged += OnSessionChanged;
                subscribed = true;
            }
            channel = BossRushCoop.Register(Topic, OnMessage);
            hazardChannel = BossRushCoop.Register("content.hazards", OnHazards);
            hitChannel = BossRushCoop.Register("content.hazard-hit", OnHazardHit);
            harmony = new Harmony("BossRush.Coop.ContentFx");
            Type[] types = { typeof(NewWeaponFx), typeof(FrostmourneSwingFx), typeof(PhantomWitchScytheSwingFx),
                typeof(AstralStaffFx), typeof(RollCallLedgerFx), typeof(EmptyMagazineMineFx), typeof(DragonDashAfterimageFx), typeof(DragonKingFxShared),
                typeof(BossRushFxKit), typeof(SandstormDashWarning), typeof(DragonKingShockwaveEffect), typeof(RandomEventFx) };
            foreach (Type type in types)
                foreach (MethodInfo method in type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (!IsVisualMethod(type, method) || !HasSupportedArguments(method)) continue;
                    string key = MethodKey(method);
                    methods[key] = method;
                    harmony.Patch(method, new HarmonyMethod(typeof(BossRushCoopFxRuntimeModule), nameof(Before)),
                        null, null, new HarmonyMethod(typeof(BossRushCoopFxRuntimeModule), nameof(After)));
                }
        }

        private static bool IsVisualMethod(Type type, MethodInfo method)
        {
            // 随机事件拥有带 run/scene/GUID 的独立表现消息。仅截住嵌套 FxKit，避免再次转发。
            if (type == typeof(RandomEventFx)) return method.Name == "PlayFirework" || method.Name == "PlayAirdropLanding";
            if (type == typeof(NewWeaponFx)) return method.Name == "PlayBurst" || method.Name == "PlayArc" || method.Name == "PlaySound";
            if (type == typeof(FrostmourneSwingFx) || type == typeof(PhantomWitchScytheSwingFx)) return method.Name == "PlayAt" || method.Name == "SpawnSmokeBurst";
            if (type == typeof(RollCallLedgerFx)) return method.Name == "ShowMark" || method.Name == "ShowComplete" || method.Name == "PlayStamp";
            if (type == typeof(EmptyMagazineMineFx)) return method.Name == "PlayExplosion";
            if (type == typeof(DragonDashAfterimageFx)) return method.Name == "Spawn";
            if (type == typeof(DragonKingFxShared)) return method.Name == "HitBurst";
            if (type == typeof(BossRushFxKit)) return method.Name == "PlayBurst";
            if (type == typeof(SandstormDashWarning)) return method.Name == "Create";
            if (type == typeof(DragonKingShockwaveEffect)) return method.Name == "PlayAt";
            return type == typeof(AstralStaffFx) && method.Name.StartsWith("Play", StringComparison.Ordinal);
        }

        private static bool Supported(Type type)
        {
            return type == typeof(float) || type == typeof(int) || type == typeof(bool) || type == typeof(string)
                || type == typeof(Vector3) || type == typeof(Quaternion) || type == typeof(Color)
                || type == typeof(CharacterMainControl) || type == typeof(Health) || type == typeof(Transform)
                || type == typeof(BossRushFxBurst);
        }
        private static bool HasSupportedArguments(MethodInfo method)
        {
            foreach (ParameterInfo parameter in method.GetParameters()) if (!Supported(parameter.ParameterType)) return false;
            return true;
        }
        private static string MethodKey(MethodInfo method)
        {
            string key = method.DeclaringType.FullName + "." + method.Name;
            foreach (ParameterInfo parameter in method.GetParameters()) key += "|" + parameter.ParameterType.FullName;
            return key;
        }

        private static void Before(out int __state) { __state = depth; depth++; }
        private static Exception After(Exception __exception, MethodBase __originalMethod, object[] __args, int __state)
        {
            depth = __state;
            if (__exception == null && __state == 0 && !replaying && instance != null && BossRushCoop.IsConnected
                && __originalMethod.DeclaringType != typeof(RandomEventFx))
                instance.Send((MethodInfo)__originalMethod, __args);
            return __exception;
        }

        private void Send(MethodInfo method, object[] args)
        {
            if (!BossRushCoop.HasBoundScene) return;
            try
            {
                var packet = new BossRushCoopFxPacket { run = BossRushCoop.RunId, scene = BossRushCoop.SceneGeneration,
                    token = BossRushCoop.SceneToken, origin = BossRushCoop.LocalPlayerId, method = MethodKey(method), args = new BossRushCoopFxArgument[args.Length] };
                for (int i = 0; i < args.Length; i++) packet.args[i] = Encode(args[i]);
                if (BossRushCoop.IsHost) BossRushCoop.Broadcast(Topic, packet);
                else BossRushCoop.SendToHost(Topic, packet);
            }
            catch (Exception e) { ModBehaviour.DevLog("[CoopContentFx] 表现发送失败: " + e.Message); }
        }

        private void OnMessage(BossRushCoopMessage message)
        {
            if (!BossRushCoop.IsConnected || !BossRushCoop.HasBoundScene || (!BossRushCoop.IsHost && !message.IsFromHost)) return;
            try
            {
                BossRushCoopFxPacket packet = message.Read<BossRushCoopFxPacket>();
                MethodInfo method;
                if (packet == null || packet.run != BossRushCoop.RunId || packet.scene != BossRushCoop.SceneGeneration
                    || string.IsNullOrEmpty(packet.token) || packet.token != BossRushCoop.SceneToken
                    || packet.args == null || packet.args.Length > 16 || !methods.TryGetValue(packet.method ?? string.Empty, out method)) return;
                if (BossRushCoop.IsHost && !message.IsFromHost)
                {
                    packet.origin = message.SenderId;
                    BossRushCoop.Broadcast(Topic, packet);
                }
                if (packet.origin == BossRushCoop.LocalPlayerId) return;
                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length != packet.args.Length) return;
                object[] args = new object[parameters.Length];
                for (int i = 0; i < args.Length; i++) args[i] = Decode(packet.args[i], parameters[i].ParameterType);
                if (remoteRoot == null) remoteRoot = new GameObject("BossRushCoopRemoteFx").transform;
                replaying = true;
                try { AstralStaffFx.WithRoot(remoteRoot, () => method.Invoke(null, args)); }
                finally { replaying = false; }
            }
            catch (Exception e) { ModBehaviour.DevLog("[CoopContentFx] 表现接收失败: " + e.Message); }
        }

        private static BossRushCoopFxArgument Encode(object value)
        {
            var arg = new BossRushCoopFxArgument();
            if (value == null) return arg;
            if (value is string) arg.text = (string)value;
            else if (value is BossRushFxBurst) arg.text = BossRushCoopJson.ToJson(value);
            else if (value is Vector3) { var v = (Vector3)value; arg.values = new[] { v.x, v.y, v.z }; }
            else if (value is Quaternion) { var q = (Quaternion)value; arg.values = new[] { q.x, q.y, q.z, q.w }; }
            else if (value is Color) { var c = (Color)value; arg.values = new[] { c.r, c.g, c.b, c.a }; }
            else if (value is float || value is int || value is bool) arg.values = new[] { Convert.ToSingle(value) };
            else
            {
                CharacterMainControl character = value as CharacterMainControl;
                if (value is Health) character = ((Health)value).TryGetCharacter();
                if (value is Transform) character = ((Transform)value).GetComponentInParent<CharacterMainControl>();
                arg.text = BossRushCoop.GetEntityId(character);
                if (value is Transform && character != null)
                {
                    Transform t = (Transform)value;
                    var indices = new List<float>();
                    while (t != null && t != character.transform) { indices.Insert(0, t.GetSiblingIndex()); t = t.parent; }
                    arg.values = indices.ToArray();
                }
            }
            return arg;
        }

        private static object Decode(BossRushCoopFxArgument arg, Type type)
        {
            if (arg == null) throw new FormatException("null fx argument");
            if (arg.text != null && arg.text.Length > 4096) throw new FormatException("fx text too long");
            float[] v = arg.values;
            if (v != null) foreach (float f in v) if (float.IsNaN(f) || float.IsInfinity(f) || Mathf.Abs(f) > 100000f) throw new FormatException("invalid fx value");
            if (type == typeof(string)) return arg.text;
            if (type == typeof(BossRushFxBurst))
            {
                BossRushFxBurst burst = BossRushCoopJson.FromJson<BossRushFxBurst>(arg.text);
                if (burst.Count < 0 || burst.Count > 512 || burst.LifeMax > 30f || burst.SizeMax > 200f) throw new FormatException("invalid burst budget");
                return burst;
            }
            if (type == typeof(Vector3)) { Require(v, 3); return new Vector3(v[0], v[1], v[2]); }
            if (type == typeof(Quaternion)) { Require(v, 4); return new Quaternion(v[0], v[1], v[2], v[3]); }
            if (type == typeof(Color)) { Require(v, 4); return new Color(v[0], v[1], v[2], v[3]); }
            if (type == typeof(float)) { Require(v, 1); return v[0]; }
            if (type == typeof(int)) { Require(v, 1); return (int)v[0]; }
            if (type == typeof(bool)) { Require(v, 1); return v[0] != 0f; }
            CharacterMainControl character = BossRushCoop.FindEntity(arg.text);
            if (type == typeof(CharacterMainControl)) return character;
            if (type == typeof(Health)) return character != null ? character.Health : null;
            Transform transform = character != null ? character.transform : null;
            if (v != null && v.Length > 32) throw new FormatException("fx transform path too long");
            if (v != null) foreach (float index in v)
            {
                if (transform == null || index < 0 || index >= transform.childCount) return null;
                transform = transform.GetChild((int)index);
            }
            return transform;
        }
        private static void Require(float[] values, int count) { if (values == null || values.Length != count) throw new FormatException("fx argument shape"); }

        internal static void TrackHazard(Component component, int kind, bool variant)
        {
            if (instance == null || !BossRushCoop.IsHost || component == null) return;
            int id = component.GetInstanceID();
            instance.hazards[id] = component;
            instance.hazardKinds[id] = new BossRushCoopHazardState { id = id, kind = kind, variant = variant };
        }
        internal static void TrackBoss(CharacterMainControl character, string kind)
        {
            if (instance == null || !BossRushCoop.IsHost || character == null) return;
            instance.bosses[character] = kind;
            BossRushCoop.RequestAiRegistration(character);
        }
        internal static void SubmitHazardHit(Component component, DamageInfo damage)
        {
            int id;
            if (instance == null || !BossRushCoop.IsClient || !BossRushCoop.HasBoundScene || damage.fromCharacter != CharacterMainControl.Main
                || !instance.replicaIds.TryGetValue(component, out id) || damage.damageValue <= 0f) return;
            BossRushCoop.SendToHost("content.hazard-hit", new BossRushCoopHazardPacket {
                run = BossRushCoop.RunId, scene = BossRushCoop.SceneGeneration, token = BossRushCoop.SceneToken, hit = id, damage = damage.damageValue });
        }
        private void OnHazardHit(BossRushCoopMessage message)
        {
            if (!BossRushCoop.IsHost || !BossRushCoop.HasBoundScene || message.IsFromHost) return;
            BossRushCoopHazardPacket packet = message.Read<BossRushCoopHazardPacket>();
            Component component;
            BossRushCoopPlayer player = BossRushCoop.FindPlayer(message.SenderId);
            if (packet == null || packet.run != BossRushCoop.RunId || packet.scene != BossRushCoop.SceneGeneration
                || string.IsNullOrEmpty(packet.token) || packet.token != BossRushCoop.SceneToken
                || packet.damage <= 0f || packet.damage > 100000f || float.IsNaN(packet.damage) || player == null || !player.IsAlive
                || !hazards.TryGetValue(packet.hit, out component) || component == null
                || (component.transform.position - player.Character.transform.position).sqrMagnitude > 300f * 300f) return;
            SandstormOrb orb = component as SandstormOrb;
            if (orb != null) orb.ApplyNetworkHit(player.Character, packet.damage);
        }
        public override void OnUpdate(float deltaTime, float unscaledDeltaTime)
        {
            if (!BossRushCoop.IsHost || !BossRushCoop.HasBoundScene || (hazards.Count == 0 && bosses.Count == 0 && !sentHazards) || Time.unscaledTime < nextHazardTick) return;
            nextHazardTick = Time.unscaledTime + 0.1f;
            var states = new List<BossRushCoopHazardState>();
            var remove = new List<int>();
            foreach (var pair in hazards)
            {
                Component component = pair.Value;
                bool dead = component == null || !component.gameObject.activeSelf
                    || (component is SandstormOrb && ((SandstormOrb)component).IsDead)
                    || (component is SandstormCycloneSeed && ((SandstormCycloneSeed)component).IsDead)
                    || (component is SandstormTornado && ((SandstormTornado)component).IsDead);
                if (dead) { remove.Add(pair.Key); continue; }
                BossRushCoopHazardState state = hazardKinds[pair.Key];
                state.position = component.transform.position; state.rotation = component.transform.rotation;
                states.Add(state);
            }
            foreach (int id in remove) { hazards.Remove(id); hazardKinds.Remove(id); }
            var bossStates = new List<BossRushCoopBossState>();
            var gone = new List<CharacterMainControl>();
            foreach (var pair in bosses)
            {
                CharacterMainControl character = pair.Key;
                if (character == null || character.Health == null || character.Health.IsDead) { gone.Add(character); continue; }
                int ai = BossRushCoop.GetAiId(character);
                if (ai == 0) continue;
                var state = new BossRushCoopBossState { ai = ai, kind = pair.Value, visible = character.gameObject.activeSelf,
                    scale = character.transform.localScale };
                SandstormChampionBody body = character.GetComponentInChildren<SandstormChampionBody>(true);
                if (body == null && pair.Value == "sand") body = SandstormChampionBody.FindForCoop(character);
                if (body != null) body.WriteCoopState(state);
                bossStates.Add(state);
            }
            foreach (CharacterMainControl character in gone) bosses.Remove(character);
            BossRushCoop.Broadcast("content.hazards", new BossRushCoopHazardPacket {
                run = BossRushCoop.RunId, scene = BossRushCoop.SceneGeneration, token = BossRushCoop.SceneToken, hazards = states.ToArray(), bosses = bossStates.ToArray() });
            sentHazards = states.Count > 0;
        }
        private void OnHazards(BossRushCoopMessage message)
        {
            if (!BossRushCoop.IsClient || !BossRushCoop.HasBoundScene || !message.IsFromHost) return;
            BossRushCoopHazardPacket packet = message.Read<BossRushCoopHazardPacket>();
            if (packet == null || packet.run != BossRushCoop.RunId || packet.scene != BossRushCoop.SceneGeneration
                || string.IsNullOrEmpty(packet.token) || packet.token != BossRushCoop.SceneToken
                || packet.hazards == null || packet.hazards.Length > 256) return;
            var present = new HashSet<int>();
            if (packet.bosses != null && packet.bosses.Length <= 128)
                foreach (BossRushCoopBossState state in packet.bosses)
                {
                    CharacterMainControl character = state != null ? BossRushCoop.FindAi(state.ai) : null;
                    if (character == null) continue;
                    CoopAiPredictionHealthProtection.MarkReplica(character);
                    character.transform.localScale = state.scale;
                    if (character.gameObject.activeSelf != state.visible) character.gameObject.SetActive(state.visible);
                    if (state.kind != "sand" && state.kind != "sand-minion") continue;
                    SandstormChampionBody body;
                    if (!sandBodies.TryGetValue(state.ai, out body) || body == null)
                    {
                        body = SandstormChampionBody.Attach(character, state.kind == "sand-minion" ? 0.7f : 1f, state.kind == "sand-minion");
                        sandBodies[state.ai] = body;
                    }
                    body.ReadCoopState(state);
                }
            foreach (BossRushCoopHazardState state in packet.hazards)
            {
                if (state == null || state.id == 0 || !present.Add(state.id) || state.kind < 1 || state.kind > 3) continue;
                Component component;
                if (!replicas.TryGetValue(state.id, out component) || component == null)
                {
                    if (state.kind == 1) component = SandstormOrb.Spawn(null, state.position, Vector3.forward, 0f, state.variant, true);
                    else if (state.kind == 2) component = SandstormCycloneSeed.Spawn(null, state.position, state.position, state.variant, true);
                    else component = SandstormTornado.Spawn(null, state.position, state.variant, true);
                    if (component == null) continue;
                    replicas[state.id] = component; replicaIds[component] = state.id;
                }
                component.transform.SetPositionAndRotation(state.position, state.rotation);
            }
            var remove = new List<int>();
            foreach (var pair in replicas) if (!present.Contains(pair.Key)) remove.Add(pair.Key);
            foreach (int id in remove)
            {
                Component component = replicas[id];
                if (!ReferenceEquals(component, null)) replicaIds.Remove(component);
                if (component != null) UnityEngine.Object.Destroy(component.gameObject);
                replicas.Remove(id);
            }
        }

        private void OnSessionChanged() { OnSceneLoaded(default(SceneRuntimeContext)); }
        public override void OnSceneLoaded(SceneRuntimeContext context)
        {
            if (remoteRoot != null) UnityEngine.Object.Destroy(remoteRoot.gameObject);
            remoteRoot = null;
            depth = 0;
            foreach (Component component in replicas.Values) if (component != null) UnityEngine.Object.Destroy(component.gameObject);
            replicas.Clear(); replicaIds.Clear(); hazards.Clear(); hazardKinds.Clear(); sentHazards = false;
            foreach (SandstormChampionBody body in sandBodies.Values) if (body != null) UnityEngine.Object.Destroy(body.gameObject);
            sandBodies.Clear(); bosses.Clear();
            SandstormChampionBody.ResetStaticCaches();
        }
        public override void OnDestroy()
        {
            if (subscribed)
            {
                BossRushCoop.SessionChanged -= OnSessionChanged;
                subscribed = false;
            }
            if (channel != null) channel.Dispose();
            channel = null;
            if (hazardChannel != null) hazardChannel.Dispose();
            if (hitChannel != null) hitChannel.Dispose();
            hazardChannel = null; hitChannel = null;
            if (harmony != null) harmony.UnpatchAll(harmony.Id);
            harmony = null;
            OnSceneLoaded(default(SceneRuntimeContext));
            methods.Clear();
            if (instance == this) instance = null;
            replaying = false;
        }
    }
}
