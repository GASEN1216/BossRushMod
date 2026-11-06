using System;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace UnityEngine
{
    public partial class Object
    {
        public static T Instantiate<T>(T prefab,Vector3 pos,Quaternion rot) where T : Component,new()
        { var go = new GameObject("thrown"); go.transform.position=pos; return go.AddComponent<T>(); }
    }
    public partial class GameObject { public int layer; }
    public partial class Component
    {
        public T GetComponent<T>() where T : Component { return gameObject.components.OfType<T>().FirstOrDefault(); }
        public T GetComponentInChildren<T>() where T : Component { return GetComponentsInChildren<T>(true).FirstOrDefault(); }
    }
    public partial class Transform
    {
        public bool IsChildOf(Transform value) { return this == value || (parent != null && parent.IsChildOf(value)); }
    }
    public partial struct Vector3
    {
        public static Vector3 up { get { return new Vector3(0,1,0); } }
        public static Vector3 down { get { return new Vector3(0,-1,0); } }
        public Vector3 normalized { get { return sqrMagnitude > 0 ? this * (float)(1/Math.Sqrt(sqrMagnitude)) : this; } }
        public static Vector3 operator +(Vector3 a,Vector3 b) { return new Vector3(a.x+b.x,a.y+b.y,a.z+b.z); }
        public static Vector3 operator *(Vector3 a,float s) { return new Vector3(a.x*s,a.y*s,a.z*s); }
    }
    public class MonoBehaviour : Component
    {
        private bool on = true;
        public bool enabled { get { return on; } set { if (on && !value) Call("OnDisable"); on = value; } }
        public bool isActiveAndEnabled { get { return on && gameObject.activeSelf; } }
        public void Call(string name, params object[] args)
        { GetType().GetMethod(name,BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(this,args); }
    }
    public sealed class LineRenderer : MonoBehaviour { }
    public sealed class Collider : Component { public bool enabled = true, isTrigger = false; }
    public struct Quaternion { public static Quaternion identity { get { return new Quaternion(); } } }
    public struct LayerMask { public int value; public static implicit operator int(LayerMask m){return m.value;} }
    public class Collision { public GameObject gameObject; public Transform transform { get { return gameObject.transform; } } }
    public enum QueryTriggerInteraction { Ignore }
    public struct RaycastHit { public Transform transform; public Vector3 point; }
    public static class Physics
    {
        public static Transform Ground; public static bool Wall;
        public static Collider IgnoredBody;
        public static void IgnoreCollision(Collider a, Collider b, bool ignore) { if (ignore) IgnoredBody=b; }
        public static bool Linecast(Vector3 a,Vector3 b,int mask,QueryTriggerInteraction t) { return Wall; }
        public static bool Raycast(Vector3 p,Vector3 dir,out RaycastHit h,float dist,int mask,QueryTriggerInteraction t)
        { p.y=0; h=new RaycastHit { transform=Ground,point=p }; return Ground!=null; }
    }
    public static class Mathf { public static float Clamp01(float f){return Math.Clamp(f,0,1);} public static float Min(float a,float b){return Math.Min(a,b);} }
}
namespace Duckov.Utilities
{
    internal static class GameplayDataSettings { public static class Layers { public static LayerMask groundLayerMask=new LayerMask{value=2},wallLayerMask=new LayerMask{value=4}; } }
}
namespace Cysharp.Threading.Tasks { public static class TaskExt { public static void Forget(this int job) { } } }
namespace Duckov.UI.DialogueBubbles
{
    public static class DialogueBubblesManager { public static int Show(string text,Transform t,float y,bool req,bool skip,float speed,float secs){return 1;} }
}
namespace ItemStatsSystem
{
    internal static class ItemAssetsCollection { public static BossRush.Item Prefab; public static BossRush.Item GetPrefab(int id){return Prefab;} }
}
namespace BossRush
{
    internal sealed class Item : Component { public string DisplayNameRaw; public int TypeID; }
    internal sealed class ItemSetting_Skill : Component { public Skill_Grenade Skill; }
    internal sealed class Health { public bool IsDead; }
    internal sealed class CharacterMainControl : Component { public static CharacterMainControl Main; public Health Health=new Health(); public object mainDamageReceiver=new object(); }
    internal sealed class AICharacterController : Component { public object searchedEnemy; }
    internal struct DamageInfo { public float damageValue; public DamageInfo(CharacterMainControl owner){damageValue=0;} }
    internal sealed class Skill_Grenade : Component
    {
        public bool createExplosion,isLandmine=false; public Grenade grenadePfb; public DamageInfo damageInfo; public float explosionShakeStrength=.5f;
        public Vector3 CalculateVelocity(Vector3 start,Vector3 dest,float speed) { Grenade.Intended=dest; return dest-start; }
    }
    internal sealed class Grenade : Component
    {
        public static Grenade Last; public static Vector3 Intended;
        public float damageRange,delayTime,explosionShakeStrength; public bool createExplosion,delayFromCollide,isLandmine,Stuck;
        public DamageInfo damageInfo;
        public void SetWeaponIdInfo(int id) { if (id!=67) throw new Exception("weapon identity"); }
        public void Launch(Vector3 origin,Vector3 velocity,CharacterMainControl owner,bool hurtSelf) { gameObject.AddComponent<Collider>(); Last=this; if(hurtSelf) throw new Exception("friendly fire"); }
        public void StickTo(Transform parent) { Stuck=true; }
    }
    internal sealed class CharacterRandomPreset : Component { }
    internal static class TidegateRaidScene { internal const string LogTag="[Tidegate]"; }
    internal sealed class TidegateTide { public bool Deep; public bool IsDeepAt(Vector3 p) { return Deep; } }
    internal static class ModBehaviour { public static void DevLog(string s){Console.WriteLine(s);} }
    internal static class L10n { public static string T(string zh,string en){return en;} }
    internal static class SkyIslandBossForge
    {
        public static bool CanDraw=true; public static LineRenderer Ring; public static float ShapeRadius;
        public static LineRenderer CreateGroundRing(Transform root,Vector3 mark)
        { if(!CanDraw) throw new InvalidOperationException("ring unavailable"); var go=new GameObject("warning"); go.transform.position=mark; go.transform.SetParent(root,false); return Ring=go.AddComponent<LineRenderer>(); }
        public static void SetRing(LineRenderer line,float rad,float charge,Color tint) { ShapeRadius=rad; }
        public static void ReleaseRing(LineRenderer line) { UnityEngine.Object.Destroy(line.gameObject); }
    }
}
