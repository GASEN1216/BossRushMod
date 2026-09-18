using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;

namespace UnityEngine
{
    public class Object
    {
        public string name;
        internal bool Destroyed;
        internal virtual bool IsDestroyed { get { return Destroyed; } }
        internal static readonly List<Object> Delayed = new List<Object>();
        public static T Instantiate<T>(T source) where T : Object { return (T)(Object)((CharacterRandomPreset)(Object)source).Copy(); }
        public static void Destroy(Object value, float delay = 0)
        {
            if (ReferenceEquals(value, null) || value.IsDestroyed) return;
            if (delay > 0) { Delayed.Add(value); return; }
            var go = value as GameObject;
            if (go != null)
                foreach (Component component in go.Components.ToArray())
                {
                    MethodInfo method = component.GetType().GetMethod("OnDestroy", BindingFlags.Instance | BindingFlags.NonPublic);
                    if (method != null) method.Invoke(component, null);
                }
            value.Destroyed = true;
        }
        public static bool operator ==(Object a, Object b)
        {
            bool leftNull = ReferenceEquals(a, null) || a.IsDestroyed, rightNull = ReferenceEquals(b, null) || b.IsDestroyed;
            return leftNull || rightNull ? leftNull == rightNull : ReferenceEquals(a, b);
        }
        public static bool operator !=(Object a, Object b) { return !(a == b); }
        public override bool Equals(object value) { return this == value as Object; }
        public override int GetHashCode() { return base.GetHashCode(); }
        public int GetInstanceID() { return GetHashCode(); }
    }
    public class Component : Object
    {
        public GameObject gameObject;
        public Transform transform { get { return gameObject.transform; } }
        internal override bool IsDestroyed { get { return Destroyed || (!ReferenceEquals(gameObject, null) && gameObject.Destroyed); } }
        public T[] GetComponentsInChildren<T>(bool include) where T : Component { return gameObject.GetComponentsInChildren<T>(include); }
        public T GetComponentInChildren<T>() where T : Component { return gameObject.GetComponentInChildren<T>(); }
        public T GetComponent<T>() where T : Component { return gameObject.GetComponentInChildren<T>(); }
    }
    public class MonoBehaviour : Component { }
    public class GameObject : Object
    {
        internal readonly List<Component> Components = new List<Component>();
        public Transform transform;
        public bool activeSelf = true;
        public GameObject(string name = "object") { this.name = name; transform = new Transform { gameObject = this }; }
        public T AddComponent<T>() where T : Component, new() { var value = new T { gameObject = this }; Components.Add(value); return value; }
        public void SetActive(bool value) { activeSelf = value; }
        public T[] GetComponentsInChildren<T>(bool include) where T : Component
        {
            var result = new List<T>(); foreach (Component c in Components) if (c is T) result.Add((T)c); return result.ToArray();
        }
        public T GetComponentInChildren<T>() where T : Component { foreach (Component c in Components) if (c is T) return (T)c; return null; }
    }
    public class Transform : Component
    {
        public Vector3 position;
        private Transform parent;
        private readonly List<Transform> children = new List<Transform>();
        public void SetParent(Transform next, bool keep)
        { if (parent != null) parent.children.Remove(this); parent = next; if (parent != null) parent.children.Add(this); }
        public Transform Find(string value) { foreach (Transform child in children) if (child.gameObject.name == value) return child; return null; }
        public bool IsChildOf(Transform value) { return this == value || (parent != null && parent.IsChildOf(value)); }
    }
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x=x; this.y=y; this.z=z; }
        public static Vector3 up { get { return new Vector3(0,1,0); } }
        public static Vector3 down { get { return new Vector3(0,-1,0); } }
        public static Vector3 forward { get { return new Vector3(0,0,1); } }
        public static Vector3 operator +(Vector3 a, Vector3 b) { return new Vector3(a.x+b.x,a.y+b.y,a.z+b.z); }
        public static Vector3 operator -(Vector3 a, Vector3 b) { return new Vector3(a.x-b.x,a.y-b.y,a.z-b.z); }
        public static Vector3 operator *(Vector3 a, float b) { return new Vector3(a.x*b,a.y*b,a.z*b); }
        public float sqrMagnitude { get { return x*x+y*y+z*z; } }
        public static float Distance(Vector3 a, Vector3 b) { return (float)Math.Sqrt((a-b).sqrMagnitude); }
    }
    public static class Mathf
    {
        public const float Deg2Rad = 0.0174532925f;
        public static float Cos(float a) { return (float)Math.Cos(a); }
        public static float Sin(float a) { return (float)Math.Sin(a); }
        public static float Max(float a, float b) { return Math.Max(a, b); }
        public static float Clamp(float v, float min, float max) { return Math.Max(min, Math.Min(max, v)); }
    }
    public static class Random
    {
        public static int Range(int min, int max) { return min; }
        public static float Range(float min, float max) { return min; }
    }
    public struct RaycastHit { public Vector3 point; public Transform transform; }
    public enum QueryTriggerInteraction { Ignore }
    public static class Physics
    {
        internal static Transform Ground;
        public static bool Raycast(Vector3 p, Vector3 d, out RaycastHit hit, float distance, int mask, QueryTriggerInteraction ignore)
        { hit = new RaycastHit { point = p + Vector3.down * 2, transform = Ground }; return true; }
        public static bool CheckCapsule(Vector3 a, Vector3 b, float radius, int mask, QueryTriggerInteraction ignore) { return false; }
    }
    public static class Time { public static float time; }
    public static class Resources { public static T[] FindObjectsOfTypeAll<T>() { return (T[])(object)new[] { CharacterRandomPreset.Source }; } }
    public static class Debug { public static void LogWarning(string value) { } }
}
namespace Duckov.Utilities { public static class GameplayDataSettings { public static class Layers { public static int wallLayerMask = 2; } } }
namespace Pathfinding
{
    public struct GraphMask { }
    public class Seeker : UnityEngine.Component { public GraphMask graphMask; public void CancelCurrentPathRequest() { } }
}
public enum Teams { scav, wolf, bear }
public class AICharacterController : UnityEngine.Component
{
    public float forceTracePlayerDistance;
    public bool noticed;
    public CharacterMainControl NoticeFromCharacter { get; set; }
}
public class DamageInfo { }
public class DeathEvent
{
    private event Action<DamageInfo> handlers;
    public void AddListener(Action<DamageInfo> handler) { handlers += handler; }
    public void RemoveListener(Action<DamageInfo> handler) { handlers -= handler; }
    internal void Invoke() { if (handlers != null) handlers(new DamageInfo()); }
}
public class Health : UnityEngine.Object
{
    public bool IsDead;
    public DeathEvent OnDeadEvent = new DeathEvent();
    internal void Die() { IsDead = true; OnDeadEvent.Invoke(); }
}
public class CharacterMainControl : UnityEngine.Component
{
    public Health Health;
    public Teams Team;
    public void SetTeam(Teams value) { Team = value; }
    internal static CharacterMainControl Create(bool healthy = true)
    {
        var go = new UnityEngine.GameObject("enemy"); var character = go.AddComponent<CharacterMainControl>();
        character.Health = healthy ? new Health() : null;
        go.AddComponent<Pathfinding.Seeker>(); go.AddComponent<AICharacterController>(); return character;
    }
}
public class CharacterRandomPreset : UnityEngine.Object
{
    public bool isBoss, isZombie, dropBoxOnDead, setActiveByPlayerDistance;
    public Teams team;
    internal static CharacterRandomPreset Source = new CharacterRandomPreset { name="Scav", team=Teams.scav };
    internal static readonly List<CharacterRandomPreset> Clones = new List<CharacterRandomPreset>();
    internal static readonly List<CharacterMainControl> Created = new List<CharacterMainControl>();
    internal static TaskCompletionSource<CharacterMainControl> Block;
    internal static bool MissingHealth;
    internal CharacterRandomPreset Copy()
    { var clone = (CharacterRandomPreset)MemberwiseClone(); Clones.Add(clone); return clone; }
    public Task<CharacterMainControl> CreateCharacterAsync(UnityEngine.Vector3 point, UnityEngine.Vector3 direction, int scene, object option, bool variable)
    {
        if (Block != null) return Block.Task;
        CharacterMainControl character = CharacterMainControl.Create(!MissingHealth); character.transform.position = point;
        Created.Add(character); return Task.FromResult(character);
    }
}
namespace BossRush
{
    internal static class ModBehaviour
    {
        internal static string GetModPath() { return Environment.CurrentDirectory; }
        internal static readonly List<string> Logs = new List<string>();
        internal static void DevLog(string value) { Logs.Add(value); }
        internal static void CriticalLog(string key, string value) { }
    }
    internal static class SkyIslandResidents
    {
        internal static readonly List<string> Faces = new List<string>();
        internal static void ApplyBattleFace(CharacterMainControl actor, string id) { Faces.Add(id); }
    }
    internal static class SpawnedEnemyActivationHelper { internal static void ReleaseFromPlayerDistanceSleep(CharacterMainControl character) { } }
    // 清场播报走 L10n.T（英文玩家不该看到中文提示）。L10n 依赖官方 LocalizationManager，
    // 隔离进程里返回中文分支即可：断言只看清场记账与生成顺序，不看文案。
    internal static class L10n { internal static bool IsChinese = true; internal static string T(string zh, string en) { return IsChinese ? zh : en; } }

    /// 档次装饰的替身：只记录「谁被判成了哪一档」，让夹具能断言逐位分配。
    internal static class SkyIslandEnemyTiers
    {
        internal static readonly List<SkyIslandEnemyTier> Applied = new List<SkyIslandEnemyTier>();
        internal static readonly List<string> Champions = new List<string>();
        internal static readonly List<SkyIslandEnemyTier> AiTiers = new List<SkyIslandEnemyTier>();
        internal static void Apply(CharacterMainControl character, SkyIslandEnemyTier tier) { Applied.Add(tier); }
        internal static void ApplyAi(AICharacterController ai, SkyIslandEnemyTier tier) { AiTiers.Add(tier); }
        internal static void ApplyStoryChampion(CharacterMainControl character, string id, string cn, string en)
        { Champions.Add(id); }
        internal static void Reset() { Applied.Clear(); Champions.Clear(); AiTiers.Clear(); }
    }

    internal sealed class SkyIslandStormBoss : UnityEngine.MonoBehaviour
    {
        internal static int Bound, EchoBound;
        internal static Action LastDefeated;
        internal void Bind(CharacterMainControl character, Func<bool> valid,
            Action<string, bool> report, Action defeated, bool echo)
        { Bound++; if (echo) EchoBound++; LastDefeated = defeated; }
    }

    /// 头目 / 岛主（2026-09-14 R1，2026-09-15 R2–R4）的替身：遭遇 owner 只负责「这一位先交给 Forge」、夜限定带队等不等夜、
    /// 整组换不换阵营与叫帮手，配装、掉落与招式在 Unity 侧（SkyIslandBossForge.cs）。这里记录被问到的「遭遇 id#位次」，
    /// 接不接手、夜限定与换阵营都问**真实档案表**（SkyIslandBossRules.cs 直接链进夹具）；夜里没有由夹具翻 <see cref="Night"/>。
    internal sealed class SkyIslandBossContext
    {
        internal UnityEngine.Transform Root;
        internal int GroundMask;
        internal Func<bool> Valid;
        internal Action<string, bool> Report;
        internal Func<string, UnityEngine.Vector3, int> CallGroup;
        internal SkyIslandBarkDelegate Bark;
    }
    internal static class SkyIslandBossForge
    {
        internal static void BindVoice(CharacterMainControl created, SkyIslandBossProfile profile,
            string championId, SkyIslandBossContext context)
        { created.gameObject.AddComponent<SkyIslandBossVoice>(); }
        internal static readonly List<string> Applied = new List<string>();
        internal static SkyIslandBossContext LastContext;
        internal static bool Night;
        internal static bool TryApply(CharacterMainControl created, string encounterId, int index, SkyIslandBossContext context)
        {
            Applied.Add(encounterId + "#" + index);
            LastContext = context;
            return SkyIslandBossRules.Find(encounterId, index) != null;
        }
        internal static bool IsNightLead(string encounterId) { return SkyIslandBossRules.LeadIsNightOnly(encounterId); }
        internal static bool LeadWaitsForNight(string encounterId) { return SkyIslandBossRules.LeadIsNightOnly(encounterId) && !Night; }
        internal static bool IsRivalFaction(string encounterId) { return SkyIslandBossRules.IsRivalFaction(encounterId); }
    }
    /// 穗镰叫帮手用到的落位与挪人：替身只把人放到给定点并记账（落地检测与寻路在 Unity 侧）。
    internal static class SkyIslandBossProps
    {
        internal static readonly List<UnityEngine.Vector3> Teleported = new List<UnityEngine.Vector3>();
        internal static int Noticed;
        internal static bool SnapNear(UnityEngine.Vector3 point, SkyIslandBossContext context, float clearance, float jitter, out UnityEngine.Vector3 ground)
        { ground = point; return true; }
        internal static bool Teleport(CharacterMainControl character, UnityEngine.Vector3 target, object pause)
        { character.transform.position = target; Teleported.Add(target); return true; }
        internal static void NoticePlayer(CharacterMainControl character) { Noticed++; }
    }
}

namespace BossRush
{
    // Quest table text injection is outside this fixture; story objectives use the real table.
    internal static class LocalizationHelper
    {
        internal static void InjectLocalization(string key, string value) { }
    }
    // Voice component is configured by the Forge (also a host substitute in this fixture).
    internal sealed class SkyIslandBossVoice : UnityEngine.MonoBehaviour { }
    internal static class BossRushUI { internal static bool Paused; internal static bool IsGamePaused() { return Paused; } }
    internal static class DialogueManager { internal static bool IsDialogueActive; }
}
namespace Cysharp.Threading.Tasks
{
    public static class UniTaskExtensions
    {
        // 与官方 Forget 一样不阻塞调用者，异常交给观察回调；不能靠同步 GetResult 模拟。
        public static async void Forget(Task task, Action<Exception> onError)
        {
            try { await task; }
            catch (Exception error) { onError(error); }
        }
    }
}
namespace Duckov.UI.DialogueBubbles
{
    public sealed class DialogueBubblesManager : UnityEngine.MonoBehaviour
    {
        public static DialogueBubblesManager Instance;
        public bool isActiveAndEnabled;
        internal static int Shown;
        internal static bool Fail, ThrowSynchronously;
        internal static Task ImmediateResult;
        internal static TaskCompletionSource<bool> LastRequest;
        public static Task Show(string line, UnityEngine.Transform speaker, float height, bool a, bool b, float speed, float duration)
        {
            // 官方缺 manager / prefab 是静默完成；正常展示是跨帧任务。
            if (Instance == null || Fail) return Task.CompletedTask;
            if (ThrowSynchronously) throw new InvalidOperationException("bubble sync failure");
            if (ImmediateResult != null) return ImmediateResult;
            Shown++;
            LastRequest = new TaskCompletionSource<bool>();
            return LastRequest.Task;
        }
    }
}
