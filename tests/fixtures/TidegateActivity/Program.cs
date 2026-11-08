using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using BossRush;

namespace UnityEngine
{
    public class Object
    {
        public bool Destroyed;
        public static bool operator ==(Object a, Object b) { return ReferenceEquals(a,b) || ((ReferenceEquals(a,null) || a.Destroyed) && (ReferenceEquals(b,null) || b.Destroyed)); }
        public static bool operator !=(Object a, Object b) { return !(a == b); }
        public override bool Equals(object obj) { return ReferenceEquals(this,obj); }
        public override int GetHashCode() { return base.GetHashCode(); }
        public static void Destroy(GameObject go) { go.Destroyed=true; foreach(var c in go.Components) c.Destroyed=true; go.activeSelf=false; }
    }
    public class Component : Object { public GameObject gameObject; public Transform transform { get { return gameObject.transform; } } }
    public class Transform : Object
    {
        public Vector3 position; public Transform parent;
        public bool IsChildOf(Transform root) { for(var t=this;t!=null;t=t.parent) if(t==root)return true;return false; }
    }
    public class GameObject : Object
    {
        public Transform transform=new Transform(); public bool activeSelf=true; public int Changes;
        public readonly List<Component> Components=new List<Component>();
        public void SetActive(bool value) { activeSelf=value; Changes++; }
    }
    public struct Vector3
    {
        public float x,y,z; public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}
        public static Vector3 zero { get { return new Vector3(); } }
        public float sqrMagnitude { get { return x*x+y*y+z*z; } }
        public static Vector3 operator -(Vector3 a,Vector3 b){return new Vector3(a.x-b.x,a.y-b.y,a.z-b.z);}
    }
    public static class Time { public static float time; }
}
namespace Pathfinding { public class Seeker : Component { public int graphMask,Cancelled; public void CancelCurrentPathRequest(){Cancelled++;} } }
namespace HarmonyLib
{
    public class HarmonyPatch : Attribute { public HarmonyPatch(Type type,string name){} public HarmonyPatch(Type type,string name,Type[] args){} }
}
public class AI_PathControl : Component { public int Stopped; public void StopMove(){Stopped++;} }
public class AICharacterController { public Transform aimTarget; public object searchedEnemy; public bool Noticing; public bool isNoticing(float seconds){return Noticing;} }
public class Health { public bool IsDead; public int Value=73; }
public class CharacterActionBase { }
public class CA_Dash : CharacterActionBase
{
    public CharacterMainControl Owner; public bool StopAction(){Owner.Dashing=false;Owner.Velocity=new Vector3(9,0,0);return true;}
}
public class CharacterMainControl : Component
{
    public static CharacterMainControl Main;
    public Health Health=new Health(); public bool Swimming,Dashing; public CA_Dash dashAction;
    public Vector3 Velocity; public object Equipment=new object(); public int Released;
    public CharacterMainControl(){gameObject=new GameObject();gameObject.Components.Add(this);dashAction=new CA_Dash{Owner=this};}
    public T[] GetComponentsInChildren<T>(bool includeInactive) { return gameObject.Components.OfType<T>().ToArray(); }
    public void SetForceMoveVelocity(Vector3 velocity) { Velocity=velocity; }
}
namespace BossRush
{
    internal static class TidegateRaidScene { public static bool IsActiveScene=true; }
    internal static class BossRushUI { public static bool Paused; public static bool IsGamePaused(){return Paused;} }
    internal static class SpawnedEnemyActivationHelper { public static void ReleaseFromPlayerDistanceSleep(CharacterMainControl c){c.Released++;} }
    internal sealed partial class TidegateEnemies
    {
        private sealed class Slot { internal CharacterMainControl Character; internal AICharacterController Ai=new AICharacterController(); internal string Group="town",Kind="scav"; }
        private sealed class Navigation { internal int Mask=16; }
        private readonly Navigation navigation=new Navigation();
        private readonly CharacterMainControl player=new CharacterMainControl();
        private readonly List<Slot> slots=new List<Slot>();
        private float revealUntil=-1;
        internal CharacterMainControl Add(float distance,string group="town",string kind="scav")
        {
            var c=new CharacterMainControl();c.transform.position=new Vector3(distance,0,0);
            c.gameObject.Components.Add(new Pathfinding.Seeker{gameObject=c.gameObject});
            c.gameObject.Components.Add(new AI_PathControl{gameObject=c.gameObject});
            slots.Add(new Slot{Character=c,Group=group,Kind=kind});return c;
        }
        internal void Run(){Time.time+=.4f;TickActivity();}
        internal int Sleeping { get { return activitySuspended.Count; } }
        internal void Beacon(){revealUntil=Time.time+40;WakeActivityForBeacon();}
        internal void EndBeacon(){revealUntil=-1;}
        internal void AimPlayer(CharacterMainControl c){slots.Find(s=>s.Character==c).Ai.aimTarget=player.transform;}
        internal void Notice(CharacterMainControl c){slots.Find(s=>s.Character==c).Ai.Noticing=true;}
        internal void Search(CharacterMainControl c){slots.Find(s=>s.Character==c).Ai.searchedEnemy=new object();}
        internal void Clear(){ClearActivity();}
    }
}
internal static class Program
{
    private static int assertions;
    private static void Check(bool value,string message){assertions++;if(!value)throw new Exception(message);}
    private static bool Start(CharacterMainControl c, CharacterActionBase action,out bool result)
    {
        object[] args={c,action,true};
        bool run=(bool)typeof(TidegateSwimDashPatch).GetMethod("Prefix",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,args);
        result=(bool)args[2];return run;
    }
    private static void Stop(CharacterMainControl c){typeof(TidegateSwimDashStopPatch).GetMethod("Postfix",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{c});}
    private static void Main()
    {
        var owner=new TidegateEnemies(); var near=owner.Add(65);var far=owner.Add(66);object kit=far.Equipment;
        owner.Run();Check(near.gameObject.activeSelf,"sleep boundary inclusive");Check(!far.gameObject.activeSelf&&owner.Sleeping==1,"far whole actor suspended");
        var seeker=far.GetComponentsInChildren<Pathfinding.Seeker>(true)[0];var path=far.GetComponentsInChildren<AI_PathControl>(true)[0];
        Check(seeker.Cancelled==1&&path.Stopped==1,"suspend cancels path and callback");
        far.transform.position=new Vector3(60,0,0);owner.Run();Check(!far.gameObject.activeSelf,"hysteresis prevents flicker");
        far.transform.position=new Vector3(55,0,0);owner.Run();Check(far.gameObject.activeSelf&&owner.Sleeping==0,"wake boundary restores entity");
        Check(ReferenceEquals(kit,far.Equipment)&&far.Health.Value==73,"same equipment and health preserved");
        Check(seeker.Cancelled==2&&path.Stopped==2&&seeker.graphMask==16&&far.Released==1,"wake clears old callback and restores graph");
        int transitions=far.gameObject.Changes;owner.Run();Check(far.gameObject.Changes==transitions,"stable state does not reconfigure actor");
        var dig=owner.Add(130,"dig");var vault=owner.Add(130,"vault");var boss=owner.Add(130,"ship","boss");
        var aimed=owner.Add(90);owner.AimPlayer(aimed);owner.Run();Check(dig.gameObject.activeSelf&&vault.gameObject.activeSelf&&boss.gameObject.activeSelf,"tide actors and boss keep moving");Check(aimed.gameObject.activeSelf,"player engagement remains awake");
        var noticed=owner.Add(90);var searching=owner.Add(90);owner.Notice(noticed);owner.Search(searching);owner.Run();Check(noticed.gameObject.activeSelf&&searching.gameObject.activeSelf,"hurt investigation and acquired targets remain awake before aim");
        var dead=owner.Add(90);owner.Run();dead.Health.IsDead=true;dead.transform.position=Vector3.zero;owner.Run();Check(!dead.gameObject.activeSelf,"dead actor never resurrected");
        var destroyed=owner.Add(90);owner.Run();UnityEngine.Object.Destroy(destroyed.gameObject);owner.Run();Check(destroyed==null,"destroyed component behaves as Unity null");
        var external=owner.Add(10);external.gameObject.SetActive(false);owner.Run();Check(!external.gameObject.activeSelf,"does not seize externally disabled actor");
        var signal=owner.Add(120);owner.Run();Check(!signal.gameObject.activeSelf,"beacon subject starts suspended");owner.Beacon();Check(signal.gameObject.activeSelf,"beacon restores listeners before sound and map");
        owner.EndBeacon();owner.Run();Check(!signal.gameObject.activeSelf,"beacon expiry permits suspension");
        signal.transform.position=Vector3.zero;owner.DevActivityFrozen=true;owner.Run();Check(!signal.gameObject.activeSelf,"ablation frozen");owner.DevActivityFrozen=false;
        BossRushUI.Paused=true;owner.Run();Check(!signal.gameObject.activeSelf,"paused does not activate");BossRushUI.Paused=false;owner.Run();Check(signal.gameObject.activeSelf,"resumes after pause");
        owner.Clear();Check(owner.Sleeping==0,"cleanup clears ownership");

        var player=new CharacterMainControl{Swimming=true};CharacterMainControl.Main=player;bool result;
        Check(!Start(player,player.dashAction,out result)&&!result,"swimming refuses dash before original");
        Check(Start(player,new CharacterActionBase(),out result),"other actions remain allowed");
        var npc=new CharacterMainControl{Swimming=true};Check(Start(npc,npc.dashAction,out result),"NPC unaffected");
        TidegateRaidScene.IsActiveScene=false;Check(Start(player,player.dashAction,out result),"other maps unaffected");
        TidegateRaidScene.IsActiveScene=true;player.Swimming=false;Check(Start(player,player.dashAction,out result),"ashore dash restored");
        player.Swimming=true;player.Dashing=true;player.Velocity=new Vector3(12,0,0);Stop(player);Check(!player.Dashing&&player.Velocity.sqrMagnitude==0,"rolling into water cancels post-stop impulse");
        npc.Dashing=true;Stop(npc);Check(npc.Dashing,"NPC roll not cancelled");
        Console.WriteLine("TidegateActivity PASS assertions="+assertions);
    }
}
