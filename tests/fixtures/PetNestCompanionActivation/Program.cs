using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using BossRush;

namespace UnityEngine
{
    class Object
    {
        static int next; readonly int id = ++next; internal bool Destroyed;
        public int GetInstanceID() { return id; }
        public static bool operator ==(Object a, Object b) { bool an=ReferenceEquals(a,null)||a.Destroyed,bn=ReferenceEquals(b,null)||b.Destroyed; return an||bn ? an==bn : ReferenceEquals(a,b); }
        public static bool operator !=(Object a, Object b) { return !(a==b); }
        public override bool Equals(object other) { return ReferenceEquals(this,other); }
        public override int GetHashCode() { return id; }
        public static void Destroy(Object value)
        {
            if (value == null) return;
            GameObject go = value as GameObject;
            if (go != null)
            {
                foreach (Transform child in go.transform.Children.ToArray()) Destroy(child.gameObject);
                foreach (Component component in go.Components.ToArray()) Destroy(component);
            }
            else value.GetType().GetMethod("OnDestroy", BindingFlags.Instance|BindingFlags.NonPublic)?.Invoke(value,null);
            value.Destroyed = true;
        }
    }
    class Component : Object
    {
        public GameObject gameObject;
        public Transform transform { get { return gameObject.transform; } }
        public T GetComponent<T>() where T : Component { return gameObject.GetComponent<T>(); }
        public T GetComponentInChildren<T>(bool inactive=false) where T : Component { return gameObject.GetComponentInChildren<T>(inactive); }
    }
    class MonoBehaviour : Component { public bool enabled=true; }
    class GameObject : Object
    {
        public readonly List<Component> Components=new List<Component>();
        public readonly Transform transform;
        public bool activeSelf=true;
        public bool activeInHierarchy { get { return activeSelf && (transform.Parent==null || transform.Parent.gameObject.activeInHierarchy); } }
        public GameObject() { transform=new Transform { gameObject=this }; }
        public void SetActive(bool value) { activeSelf=value; }
        public T AddComponent<T>() where T : Component { var c=(T)Activator.CreateInstance(typeof(T),true); c.gameObject=this; Components.Add(c); return c; }
        public T GetComponent<T>() where T : Component { return Components.OfType<T>().FirstOrDefault(c=>c!=null); }
        public T GetComponentInChildren<T>(bool inactive=false) where T : Component
        {
            T own=GetComponent<T>(); if (own!=null) return own;
            foreach(var child in transform.Children)
            {
                if (!inactive && !child.gameObject.activeInHierarchy) continue;
                var value=child.gameObject.GetComponentInChildren<T>(inactive); if(value!=null)return value;
            }
            return null;
        }
    }
    class Transform : Object { public GameObject gameObject; public Transform Parent; public readonly List<Transform> Children=new List<Transform>(); public Vector3 position; public void SetParent(Transform parent) { Parent=parent;parent.Children.Add(this); } }
    struct Vector3
    {
        public float x,y,z; public Vector3(float x,float y,float z) { this.x=x;this.y=y;this.z=z; }
        public static Vector3 zero { get { return new Vector3(); } } public static Vector3 up { get { return new Vector3(0,1,0); } } public static Vector3 forward { get { return new Vector3(0,0,1); } }
        public float sqrMagnitude { get { return x*x+y*y+z*z; } }
        public static Vector3 operator +(Vector3 a,Vector3 b) { return new Vector3(a.x+b.x,a.y+b.y,a.z+b.z); }
        public static Vector3 operator -(Vector3 a,Vector3 b) { return new Vector3(a.x-b.x,a.y-b.y,a.z-b.z); }
        public static Vector3 operator *(Vector3 a,float b) { return new Vector3(a.x*b,a.y*b,a.z*b); }
    }
    static class Time { public static float deltaTime; }
    static class Mathf { public static bool Approximately(float a,float b) { return Math.Abs(a-b)<.0001f; } public static float Clamp01(float x) { return Math.Max(0,Math.Min(1,x)); } }
}
namespace ItemStatsSystem
{
    class Stat { public float BaseValue; }
    class Item { public readonly Dictionary<string,Stat> Stats=new Dictionary<string,Stat>(); public Stat GetStat(string key) { return Stats[key]; } }
}
class Health : Component { public float CurrentHealth=1,MaxHealth=100; public bool Invincible; public void SetInvincible(bool b) { Invincible=b; } public void SetHealth(float hp) { CurrentHealth=Math.Min(hp,MaxHealth); } }
class DamageReceiver { public Teams Team; }
class CharacterRandomPreset : UnityEngine.Object { }
class CharacterMainControl : Component
{
    public static CharacterMainControl Main;
    public Teams Team; public Health Health; public ItemStatsSystem.Item CharacterItem=new ItemStatsSystem.Item();
    public event Action<Teams> OnTeamChanged;
    public int TeamSubscribers { get { return OnTeamChanged==null?0:OnTeamChanged.GetInvocationList().Length; } }
    public void SetTeam(Teams team) { Team=team;OnTeamChanged?.Invoke(team); }
    public void SetPosition(Vector3 position) { transform.position=position; }
}
class AICharacterController : MonoBehaviour
{
    public CharacterMainControl leader; public DamageReceiver searchedEnemy; public bool noticed;
    public float forceTracePlayerDistance=9999,sightDistance=20,traceTargetChance=.5f;
}
namespace BossRush
{
    class ModBehaviour { public static void DevLog(string s) { } public void SanitizeBossRushZombieSpawn(CharacterMainControl c,string s) { } }
    class PetNestPetRecord { public int level=1; }
    class PetNestPersonalityProfile { public float FollowTeleportDistance=40,SightDistanceMultiplier=1,TraceTargetChance=.5f; }
    static class PetNestPersonality { public static PetNestPersonalityProfile Resolve(PetNestPetRecord p) { return new PetNestPersonalityProfile(); } }
    static class PetNestGrowth { public static float ModelScale(float f,int level) { return f; } }
    static class PetNestTuning { public const float CompanionDpsShareTarget=.28f; }
    class PetNestAuraEffect : Component { }
    class PetNestBackpack : Component { public static void Attach(CharacterMainControl c,PetNestPetRecord p) { } public void Dispose(bool value) { } }
    class DuckNpcMovement : Component { public Transform Follow; public bool Bind(CharacterMainControl c,Vector3 p,float radius) { return true; } public void EnablePlayerFollow(Transform target) { Follow=target; } }
    internal static partial class PetNestCompanionSpawner
    {
        private static void ApplyModelScale(PetNestCompanionHandle h) { }
        private static void ApplyPetModifiers(CharacterMainControl c,PetNestPetRecord p) { }
        private static void AttachChromaAura(PetNestCompanionHandle h,PetNestPetRecord p) { }
        private static void DetachChromaAura(PetNestCompanionHandle h) { }
    }
}
static class Program
{
    static int checks;
    static void Check(bool value,string label) { if(!value)throw new Exception(label);checks++; }
    static CharacterMainControl Character(Teams team)
    {
        var go=new GameObject();var c=go.AddComponent<CharacterMainControl>();c.SetTeam(team);c.Health=go.AddComponent<Health>();
        c.CharacterItem.Stats["GunDamageMultiplier"]=new ItemStatsSystem.Stat { BaseValue=1 };
        c.CharacterItem.Stats["MeleeDamageMultiplier"]=new ItemStatsSystem.Stat { BaseValue=1 };
        return c;
    }
    static PetNestCompanionHandle Staged()
    {
        var c=Character(Teams.player);var aiGo=new GameObject();aiGo.transform.SetParent(c.transform);
        aiGo.AddComponent<AICharacterController>().searchedEnemy=new DamageReceiver { Team=Teams.wolf };
        c.gameObject.SetActive(false);
        return new PetNestCompanionHandle { Character=c,Health=c.Health,ClonePreset=new CharacterRandomPreset() };
    }
    static void Main()
    {
        var master=CharacterMainControl.Main=Character(Teams.player);string reason;
        var idle=Staged();var idleAi=idle.Character.GetComponentInChildren<AICharacterController>(true);
        Check(idle.Character.GetComponentInChildren<AICharacterController>()==null,"staging faithfully hides inactive AI from ordinary lookup");
        Check(PetNestCompanionSpawner.TryActivate(idle,Vector3.zero,master,new ModBehaviour(),new PetNestPetRecord(),out reason,false),"base activates");
        Check(idle.Character.gameObject.activeInHierarchy && !idleAi.gameObject.activeSelf,"base body visible while entire combat AI subtree is disabled before display");
        Check(idleAi.searchedEnemy==null && idleAi.forceTracePlayerDistance==0,"base activation clears inherited hostility");
        Check(idle.Character.GetComponent<DuckNpcMovement>().Follow==master.transform,"base preserves existing passive follow");
        Check(master.TeamSubscribers==1,"one owner subscription per entity");
        PetNestCompanionSpawner.CleanupOnce(idle);PetNestCompanionSpawner.CleanupOnce(idle);
        Check(idleAi==null && master.TeamSubscribers==0 && !PetNestCompanionAgent.IsCompanionArmed,"base cleanup releases subtree, identity and team subscription once");
        var fighter=Staged();var ai=fighter.Character.GetComponentInChildren<AICharacterController>(true);
        Check(PetNestCompanionSpawner.TryActivate(fighter,Vector3.zero,master,new ModBehaviour(),new PetNestPetRecord(),out reason),"new raid entity activates");
        Check(ai.gameObject.activeInHierarchy && ai.enabled && ai.leader==master,"raid AI is live and follows owner despite earlier base disable");
        Check(ai.searchedEnemy==null && ai.forceTracePlayerDistance==0,"raid clears inherited target and forced player pursuit");
        Check(fighter.Character.GetComponent<DuckNpcMovement>()==null,"raid uses combat AI without passive movement override");
        Check(Math.Abs(fighter.Character.CharacterItem.GetStat("GunDamageMultiplier").BaseValue-.28f)<.0001f
            && Math.Abs(fighter.Character.CharacterItem.GetStat("MeleeDamageMultiplier").BaseValue-.28f)<.0001f,"production gun and melee damage remain nonzero at companion balance");
        Check(fighter.Health.CurrentHealth==fighter.Health.MaxHealth && !fighter.Health.Invincible,"combat health is full and damageable");
        ai.searchedEnemy=new DamageReceiver { Team=Teams.bear };ai.noticed=true;master.SetTeam(Teams.bear);
        Check(fighter.Character.Team==Teams.bear && ai.searchedEnemy==null && !ai.noticed,"owner faction change immediately clears newly friendly target");
        ai.searchedEnemy=new DamageReceiver { Team=Teams.wolf };master.SetTeam(Teams.bear);
        Check(ai.searchedEnemy!=null && Team.IsEnemy(fighter.Character.Team,ai.searchedEnemy.Team),"valid enemy target survives unrelated team event");
        fighter.Agent.Bind(fighter.Character,master,true);Check(master.TeamSubscribers==1,"rebind is idempotent");
        var nextMaster=Character(Teams.scav);fighter.Agent.Bind(fighter.Character,nextMaster,true);
        Check(master.TeamSubscribers==0 && nextMaster.TeamSubscribers==1 && fighter.Character.Team==Teams.scav,"new player owns subscription and faction exclusively");
        PetNestCompanionSpawner.CleanupOnce(fighter);
        Check(ai==null && nextMaster.TeamSubscribers==0 && !PetNestCompanionAgent.IsCompanionArmed,"raid cleanup removes AI and all owned hooks");
        Console.WriteLine("PASS PetNestCompanionActivation "+checks+" assertions");
    }
}
