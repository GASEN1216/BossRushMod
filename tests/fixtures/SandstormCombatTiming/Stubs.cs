using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace UnityEngine
{
    public class Object
    {
        internal bool destroyed;
        public static bool operator ==(Object a, Object b) { return ReferenceEquals(a, b) || ((ReferenceEquals(a, null) || a.destroyed) && (ReferenceEquals(b, null) || b.destroyed)); }
        public static bool operator !=(Object a, Object b) { return !(a == b); }
        public override bool Equals(object other) { return ReferenceEquals(this, other); }
        public override int GetHashCode() { return base.GetHashCode(); }
        public static void Destroy(Object value, float delay = 0f)
        {
            if (value == null) return;
            value.destroyed = true;
            var go = value as GameObject;
            if (!ReferenceEquals(go, null))
            {
                foreach (var child in go.transform.children.ToArray()) Destroy(child.gameObject);
                foreach (var component in go.components.ToArray())
                {
                    component.destroyed = true;
                    component.GetType().GetMethod("OnDestroy", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(component, null);
                }
            }
        }
    }
    public class Component : Object
    {
        public GameObject gameObject;
        public Transform transform { get { return gameObject.transform; } }
        public T GetComponent<T>() where T : Component { return gameObject.GetComponent<T>(); }
    }
    public class MonoBehaviour : Component
    {
        public object StartCoroutine(IEnumerator routine) { return routine; }
    }
    public class GameObject : Object
    {
        internal static readonly List<GameObject> All = new List<GameObject>();
        internal readonly List<Component> components = new List<Component>();
        public readonly Transform transform;
        public GameObject(string name) { transform = new Transform { gameObject = this }; All.Add(this); }
        public T AddComponent<T>() where T : Component, new() { var c = new T { gameObject = this }; components.Add(c); return c; }
        public T GetComponent<T>() where T : Component { return components.OfType<T>().FirstOrDefault(); }
        internal static List<T> Live<T>() where T : Component { return All.Where(g => g != null).SelectMany(g => g.components).OfType<T>().Where(c => c != null).ToList(); }
        internal static void ClearScene() { foreach (var g in All.ToArray()) Destroy(g); All.Clear(); }
    }
    public class Transform : Object
    {
        public GameObject gameObject;
        public Vector3 position, localScale = Vector3.one;
        public Quaternion rotation;
        public Vector3 forward = Vector3.forward;
        internal readonly List<Transform> children = new List<Transform>();
        public void SetParent(Transform parent, bool world) { parent.children.Add(this); }
    }
    public struct Vector3
    {
        public static Vector3 Lerp(Vector3 a,Vector3 b,float t) { return a+(b-a)*Math.Max(0f,Math.Min(1f,t)); }
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 zero { get { return new Vector3(); } }
        public static Vector3 one { get { return new Vector3(1, 1, 1); } }
        public static Vector3 forward { get { return new Vector3(0, 0, 1); } }
        public static Vector3 up { get { return new Vector3(0, 1, 0); } }
        public static Vector3 down { get { return new Vector3(0, -1, 0); } }
        public float sqrMagnitude { get { return Dot(this, this); } }
        public float magnitude { get { return (float)Math.Sqrt(sqrMagnitude); } }
        public Vector3 normalized { get { return magnitude > 0 ? this / magnitude : zero; } }
        public void Normalize() { this = normalized; }
        public static Vector3 operator +(Vector3 a, Vector3 b) { return new Vector3(a.x + b.x, a.y + b.y, a.z + b.z); }
        public static Vector3 operator -(Vector3 a, Vector3 b) { return new Vector3(a.x - b.x, a.y - b.y, a.z - b.z); }
        public static Vector3 operator -(Vector3 a) { return a * -1; }
        public static Vector3 operator *(Vector3 a, float b) { return new Vector3(a.x * b, a.y * b, a.z * b); }
        public static Vector3 operator /(Vector3 a, float b) { return a * (1f / b); }
        public static float Dot(Vector3 a, Vector3 b) { return a.x * b.x + a.y * b.y + a.z * b.z; }
        public static Vector3 Cross(Vector3 a, Vector3 b) { return new Vector3(a.y*b.z-a.z*b.y, a.z*b.x-a.x*b.z, a.x*b.y-a.y*b.x); }
        public static Vector3 ClampMagnitude(Vector3 a, float max) { return a.magnitude > max ? a.normalized * max : a; }
        public static float Distance(Vector3 a, Vector3 b) { return (a-b).magnitude; }
        public static float Angle(Vector3 a, Vector3 b) { return (float)(Math.Acos(Mathf.Clamp(Dot(a.normalized, b.normalized), -1, 1)) / Mathf.Deg2Rad); }
        public static Vector3 RotateTowards(Vector3 a, Vector3 b, float radians, float maxMagnitudeDelta)
        {
            // Horizontal steering used by the production dash; capped rotation with preserved magnitude.
            float current = (float)Math.Atan2(a.x, a.z), target = (float)Math.Atan2(b.x, b.z);
            float delta = (float)Math.Atan2(Math.Sin(target-current), Math.Cos(target-current));
            float angle = current + Mathf.Clamp(delta, -radians, radians);
            return new Vector3((float)Math.Sin(angle), 0, (float)Math.Cos(angle)) * a.magnitude;
        }
    }
    public struct Vector2 { public float x, y; public Vector2(float x, float y) { this.x=x; this.y=y; } }
    public struct Quaternion { public static Quaternion LookRotation(Vector3 a, Vector3 b) { return new Quaternion(); } public static Quaternion Euler(float x,float y,float z) { return new Quaternion(); } }
    public struct Color
    {
        public float r,g,b,a;
        public Color(float r,float g,float b,float a) { this.r=r;this.g=g;this.b=b;this.a=a; }
        public static Color Lerp(Color x,Color y,float t) { return new Color(x.r+(y.r-x.r)*t,x.g+(y.g-x.g)*t,x.b+(y.b-x.b)*t,x.a+(y.a-x.a)*t); }
    }
    public struct Bounds { public void Expand(float n) { } }
    public class Mesh : Object { public string name; public Vector3[] vertices; public Vector2[] uv; public int[] triangles; public Color[] colors; public Bounds bounds; public void MarkDynamic(){} public void RecalculateBounds(){} }
    public class Material : Object { }
    public class MeshFilter : Component { public Mesh sharedMesh; }
    public class MeshRenderer : Component { public Material sharedMaterial; public Rendering.ShadowCastingMode shadowCastingMode; public bool receiveShadows; public Rendering.LightProbeUsage lightProbeUsage; public Rendering.ReflectionProbeUsage reflectionProbeUsage; public MotionVectorGenerationMode motionVectorGenerationMode; }
    public class LineRenderer : Component { public LineAlignment alignment; public int positionCount; public float widthMultiplier; public Color startColor,endColor; public void SetPositions(Vector3[] v){} }
    public enum MotionVectorGenerationMode { ForceNoMotion }
    public enum LineAlignment { TransformZ }
    public static class Time { public static float time, deltaTime, unscaledDeltaTime; }
    public static class Mathf
    {
        public const float PI=(float)Math.PI, Deg2Rad=PI/180f;
        public static float Min(float a,float b){return Math.Min(a,b);} public static float Max(float a,float b){return Math.Max(a,b);}
        public static float Abs(float a){return Math.Abs(a);} public static float Clamp(float v,float a,float b){return Min(Max(v,a),b);}
        public static float Clamp01(float a){return Clamp(a,0,1);} public static float Lerp(float a,float b,float t){return a+(b-a)*Clamp01(t);}
        public static float Sin(float a){return (float)Math.Sin(a);} public static float Cos(float a){return (float)Math.Cos(a);}
        public static float Exp(float a){return (float)Math.Exp(a);} public static float Pow(float a,float b){return (float)Math.Pow(a,b);}
    }
    public static class Random { public static float value=0f; public static float Range(float a,float b){return (a+b)/2;} }
    public static class Debug { public static void Log(string s){} public static void LogWarning(string s){throw new Exception(s);} }
    public struct RaycastHit { public Vector3 point; }
    public enum QueryTriggerInteraction { Ignore }
    public static class Physics
    {
        internal static Func<Vector3, bool> HasGround = p => true;
        internal static Func<Vector3, float> GroundHeight = p => 0f;
        internal static readonly List<Vector3> Probes = new List<Vector3>();
        internal static bool Wall;
        public static bool Raycast(Vector3 at,Vector3 direction,out RaycastHit hit,float distance,int mask,QueryTriggerInteraction mode)
        { at.y=GroundHeight(at);Probes.Add(at);hit=new RaycastHit{point=at};return HasGround(at); }
        public static bool Linecast(Vector3 a,Vector3 b,int mask,QueryTriggerInteraction mode){return Wall;}
    }
}
namespace UnityEngine.Rendering { public enum ShadowCastingMode{Off} public enum LightProbeUsage{Off} public enum ReflectionProbeUsage{Off} }
namespace Duckov.Utilities { public static class GameplayDataSettings { public static class Layers { public static int wallLayerMask=1; } } }
public class Health { public bool IsDead; }
public class CharacterMainControl : MonoBehaviour
{
    public static CharacterMainControl Main;
    public readonly Health Health=new Health();
    public readonly Movement movementControl=new Movement();
    public Vector3 ForceVelocity;
    public int PositionWrites;
    public Action PositionChanged;
    public CharacterMainControl(){gameObject=new GameObject("character");}
    public void SetPosition(Vector3 p){transform.position=p;PositionWrites++;PositionChanged?.Invoke();}
    public void SetForceMoveVelocity(Vector3 v){ForceVelocity=v;}
    public void SetAimPoint(Vector3 p){}
    public ItemAgent_MeleeWeapon GetMeleeWeapon(){return null;}
}
public class Movement{public void ForceTurnTo(Vector3 v){}}
public class ItemAgent_MeleeWeapon : Component{}
namespace BossRush
{
    internal sealed partial class SandstormChampionController : MonoBehaviour
    {
        internal readonly CharacterMainControl _boss=new CharacterMainControl();
        private readonly ModBehaviour _owner=new ModBehaviour();
        internal readonly TestBody _body=new TestBody();
        private SandstormDashWarning _warning;
        internal int _phase=3;
        private int _groundMask=1;
        internal bool _enraged=false, Alive=true;
        private bool _stopped;
        private Vector3 _lastDashDirection,_lastMotionPosition,_reportPosition;
        private Vector3 _playerVelocity=Vector3.zero;
        private Vector3 _lastPlayerPosition;
        private bool _playerVelocityPrimed;
        private CharacterMainControl _velocityPlayer;
        internal Vector3 SampledVelocity { get { return _playerVelocity; } }
        internal void Sample(float dt) { Time.deltaTime=dt;SamplePlayerVelocity(); }
        private float _blockedSeconds,_moveDistance,_moveAttemptSeconds;
        private float _stormEndsAt=0f;
        private float _nextMoveReport=float.MaxValue;
        private MonoBehaviour _ambience;
        private IEnumerator _fightRoutine;
        internal int Count=1, Settles, Hits, TrackingTicks;
        internal float Damage;
        internal bool IsFighting { get { return Alive && !_boss.Health.IsDead && !CharacterMainControl.Main.Health.IsDead; } }
        private bool HasPhaseTransition { get { return false; } }
        internal bool Finished { get { return _fightRoutine==null; } }
        internal void Begin(int phase=3,int count=1){_phase=phase;Count=count;_fightRoutine=RunFight();}
        internal void Frame(float dt,float realDt=-1f){Time.deltaTime=dt;Time.unscaledDeltaTime=realDt<0?dt:realDt;Time.time+=dt;Update();_boss.transform.position+=_boss.ForceVelocity*dt;}
        private IEnumerator Fight(){return Dashes(Count);}
        private void TrackPlayerVelocity(){TrackingTicks++;}
        private void UpdateEnrage(){} private void TickContactDamage(){}
        private void StopBattle(bool dead){_stopped=true;_fightRoutine=null;StopMotion();}
        private void StopMotion(){_boss.SetForceMoveVelocity(Vector3.zero);}
        private void CancelNavigation(){} private void Face(Vector3 dir){}
        private bool CommandMotion(Vector3 v){_boss.SetForceMoveVelocity(v);return !Physics.Wall;}
        private IEnumerator Hover(float seconds){return WaitForFightSeconds(seconds);}
        private void SettlePhaseDash(){Settles++;}
        private static void PlayDashImpact(Vector3 at,Vector3 dir){}
        internal void HurtPlayer(float damage,Vector3 at){Hits++;Damage+=damage;}
    }
    internal sealed class TestBody { internal bool Charging;internal int Starts;internal void SetCharge(bool b){Charging=b;if(b)Starts++;}internal void Flare(float a){} }
    internal sealed class SandstormDashWarning { internal static SandstormDashWarning Create(Vector3 a,Vector3 b,float c,float d){return new SandstormDashWarning();}internal void Retarget(Vector3 a,Vector3 b){}internal void Flash(){} }
    internal sealed class ModBehaviour { internal static void DevLog(string s){}internal void PlaySoundEffect(string s){} }
    internal static class DragonKingConfig { internal const string Sound_DashCharge="",Sound_DashBurst=""; }
    internal static class SandstormChampionAssetManager { internal static void PlaySandBurst(Vector3 a,float b,int c,Color d){}internal static void ShakeNear(Vector3 a,float b,float c){} }
    internal static class BossSkillDamageRules { internal static bool Dodging; internal static bool IsDodging(CharacterMainControl p){return Dodging;} }
    internal sealed class SandstormChampionMinionMarker : MonoBehaviour { internal bool IsCombatActive=true; }
    internal sealed class AstralStaffHandVisual : Component { internal void SetSurge(float a){} }
    internal static class AstralStaffSound { internal static void PostSwing(CharacterMainControl c,int tier,bool second=false){} }
    internal static class AstralStaffConfig { internal const string LogPrefix="";internal static readonly Color Gold=new Color(1,0.7f,0.3f,1),CoreWhite=new Color(1,1,1,1); }
    internal static class AstralStaffFx
    {
        internal static Transform Root;
        internal static GameObject CreateTransient(string name){var go=new GameObject(name);if(Root!=null)go.transform.SetParent(Root,false);return go;}
        internal static Material Additive(BossRushParticleShape shape,float gain){return new Material();}
        internal static LineRenderer CreateLine(Transform parent,string name,Material m,float width,Color color,bool world){var go=new GameObject(name);go.transform.SetParent(parent,false);return go.AddComponent<LineRenderer>();}
        internal static void WithRoot(Transform root,Action action){var previous=Root;Root=root;try{action();}finally{Root=previous;}}
        internal static void PlayBurst(Vector3 p,BossRushFxBurst b){}
        internal static void PlaySweepArt(Vector3 a,Vector3 b,float c,float d,Color e){}
        internal static void PlaySpinArt(Vector3 a,Vector3 b,float c,bool d,Transform e){}
        internal static void PlayStarfallImpact(Vector3 a,Vector3 b,Vector3 c,bool d,float e){}
        internal static void PlayContact(Vector3 a,Vector3 b,int tier){}
    }
    internal static class AstralStaffSigil { internal static void Play(Vector3 a,Vector3 b,float c,int d,float e,float f,Transform g=null,bool h=false){} }
    internal enum BossRushParticleShape { TrailStrip }
    internal struct BossRushFxBurst { internal Color Main; }
    internal static class BossRushFxKit { internal const float GainBright=1;internal static BossRushFxBurst Shockwave(Color c,float a,float b,float d,bool f){return new BossRushFxBurst();} }
    internal static class BossRushUI { internal static bool Paused;internal static bool IsGamePaused(){return Paused;} }
}

// In-process roster fixture: executes production geometry for arbitrary participant counts; no transport is simulated.
namespace BossRush
{
    internal sealed class BossRushCoopPlayer { internal bool IsAlive; internal CharacterMainControl Character; }
    internal static class BossRushCoop
    {
        internal static bool IsConnected;
        internal static BossRushCoopPlayer[] Players = new BossRushCoopPlayer[0];
        internal static CharacterMainControl GetCombatTarget(Vector3 origin) { return CharacterMainControl.Main; }
    }
    // Transport boundary only: real delivery/identity validation runs in DragonKingCoopWorld.
    internal static class DragonKingCoopContactRuntimeModule
    {
        internal sealed class Call
        { internal CharacterMainControl Player;internal Vector3 Velocity;internal string Id;internal float Seconds;internal bool Dodge; }
        internal static readonly List<Call> Calls=new List<Call>();
        internal static void Push(CharacterMainControl player,Vector3 velocity,string id,float seconds=0f,bool dodge=false)
        { Calls.Add(new Call{Player=player,Velocity=velocity,Id=id,Seconds=seconds,Dodge=dodge}); }
    }
    internal sealed partial class SandstormChampionController
    {
        internal readonly List<CharacterMainControl> HitTargets = new List<CharacterMainControl>();
        private CharacterMainControl CombatPlayer { get { return CharacterMainControl.Main; } }
        internal void HurtPlayer(CharacterMainControl player, float damage, Vector3 at)
        { HitTargets.Add(player); HurtPlayer(damage, at); }
    }
}
