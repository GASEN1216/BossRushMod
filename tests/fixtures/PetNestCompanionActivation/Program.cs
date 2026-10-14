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
    static class Time { public static float deltaTime, time; }
    struct Vector2 { public float x,y; public static Vector2 zero { get { return new Vector2(); } } }
    static class Mathf { public static bool Approximately(float a,float b) { return Math.Abs(a-b)<.0001f; } public static float Clamp01(float x) { return Math.Max(0,Math.Min(1,x)); } }
}
namespace ItemStatsSystem
{
    class Stat { public float BaseValue; }
    class Item { public readonly Dictionary<string,Stat> Stats=new Dictionary<string,Stat>(); public Stat GetStat(string key) { return Stats[key]; } }
}
class Health : Component { public float CurrentHealth=1,MaxHealth=100; public bool Invincible; public void SetInvincible(bool b) { Invincible=b; } public void SetHealth(float hp) { CurrentHealth=Math.Min(hp,MaxHealth); } }
namespace Duckov.Buffs
{
    class Buff { public enum BuffExclusiveTags { NotExclusive, Bleeding, Poison, Nauseous, Pain, Burning, Electric, Freeze, Stun } }
}
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
    public DamageReceiver mainDamageReceiver=new DamageReceiver();
    public readonly List<Duckov.Buffs.Buff.BuffExclusiveTags> RemovedTags=new List<Duckov.Buffs.Buff.BuffExclusiveTags>();
    public void Trigger(bool a,bool b,bool c) { }
    public void SetMoveInput(Vector2 v) { }
    public void RemoveBuffsByTag(Duckov.Buffs.Buff.BuffExclusiveTags tag,bool one) { RemovedTags.Add(tag); }
}
class AICharacterController : MonoBehaviour
{
    public CharacterMainControl leader; public DamageReceiver searchedEnemy; public bool noticed;
    public float forceTracePlayerDistance=9999,sightDistance=20,traceTargetChance=.5f;
}
namespace BossRush
{
    class ModBehaviour { public static void DevLog(string s) { } public void SanitizeBossRushZombieSpawn(CharacterMainControl c,string s) { } }
    class PetNestPetRecord { public int level=1; public string id="pet"; public int behaviour; }
    // 生产枚举在 PetNestCompanionCare.cs（依赖官方 UI，夹具不链接）；数值必须一致，见下方断言。
    enum PetNestBehaviour { Follow=0, Active=1, Avoid=2 }
    static class PetNestCareService
    {
        public static readonly Duckov.Buffs.Buff.BuffExclusiveTags[] AvoidCleansedTags={ Duckov.Buffs.Buff.BuffExclusiveTags.Bleeding, Duckov.Buffs.Buff.BuffExclusiveTags.Poison };
        public static PetNestBehaviour Resolve(PetNestPetRecord p) { return (PetNestBehaviour)p.behaviour; }
        public static void InjectOptionLocalization(PetNestBehaviour b) { }
        public static void AttachOptions(PetNestBackpack host,string id,CharacterMainControl companion) { }
    }
    class PetNestPersonalityProfile { public float FollowTeleportDistance=40,SightDistanceMultiplier=1,TraceTargetChance=.5f; }
    static class PetNestPersonality { public static PetNestPersonalityProfile Resolve(PetNestPetRecord p) { return new PetNestPersonalityProfile(); } }
    static class PetNestGrowth { public static float ModelScale(float f,int level) { return f; } }
    static class PetNestTuning { public const float CompanionDpsShareTarget=.28f, BehaviourEngageWindowSeconds=10f, AvoidCleanseIntervalSeconds=5f; }
    class PetNestAuraEffect : Component { }
    class PetNestBackpack : Component { public static PetNestBackpack Attach(CharacterMainControl c,PetNestPetRecord p) { return null; } public void Dispose(bool value) { } }
    class DuckNpcMovement : Component { public bool enabled=true; public Transform Follow; public bool Bind(CharacterMainControl c,Vector3 p,float radius) { return true; } public void EnablePlayerFollow(Transform target) { Follow=target; } public void DisablePlayerFollow() { Follow=null; } }
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
        // 2026-10-12 行动方式：旧档 / 新崽缺省是「跟随」——不主动开打，AI 子树收起、寻路跟随主人。
        var follower=Staged();var followerAi=follower.Character.GetComponentInChildren<AICharacterController>(true);
        Check(PetNestCompanionSpawner.TryActivate(follower,Vector3.zero,master,new ModBehaviour(),new PetNestPetRecord(),out reason),"default-follow raid entity activates");
        Check(!followerAi.gameObject.activeSelf && followerAi.searchedEnemy==null,"follow mode keeps combat AI parked until the owner fights");
        var followMove=follower.Character.GetComponent<DuckNpcMovement>();
        Check(followMove!=null && followMove.enabled && followMove.Follow==master.transform,"follow mode walks after the owner");
        var foe=Character(Teams.wolf);Time.time=100f;
        follower.Agent.NotifyEngaged(foe);
        Check(followerAi.gameObject.activeSelf && followerAi.searchedEnemy==foe.mainDamageReceiver && !followMove.enabled,"owner combat wakes the companion onto that enemy");
        Time.time=111f;follower.Agent.SetBehaviour(PetNestBehaviour.Follow);
        Check(!followerAi.gameObject.activeSelf && followMove.enabled,"after the engage window it disengages and follows again");
        follower.Agent.SetBehaviour(PetNestBehaviour.Avoid);
        Check(!followerAi.gameObject.activeSelf && follower.Health.Invincible && follower.Character.RemovedTags.Count==2,"avoid mode never fights, is invincible and cleanses listed debuffs");
        follower.Agent.NotifyEngaged(foe);
        Check(!followerAi.gameObject.activeSelf,"avoid mode ignores owner combat");
        follower.Agent.SetBehaviour(PetNestBehaviour.Active);
        Check(followerAi.gameObject.activeSelf && !followMove.enabled && !follower.Health.Invincible,"active mode restores combat AI and drops only its own invincibility");
        Check((int)PetNestBehaviour.Follow==0 && (int)PetNestBehaviour.Active==1 && (int)PetNestBehaviour.Avoid==2,"saved behaviour values stable");
        PetNestCompanionSpawner.CleanupOnce(follower);
        var fighter=Staged();var ai=fighter.Character.GetComponentInChildren<AICharacterController>(true);
        Check(PetNestCompanionSpawner.TryActivate(fighter,Vector3.zero,master,new ModBehaviour(),new PetNestPetRecord { behaviour=1 },out reason),"new raid entity activates");
        Check(ai.gameObject.activeInHierarchy && ai.enabled && ai.leader==master,"raid AI is live and follows owner despite earlier base disable");
        Check(ai.searchedEnemy==null && ai.forceTracePlayerDistance==0,"raid clears inherited target and forced player pursuit");
        Check(fighter.Character.GetComponent<DuckNpcMovement>()==null,"active mode uses combat AI without passive movement override");
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

// Offline facade: this fixture exercises single-player production paths only.
namespace BossRush { internal static class BossRushCoop { internal const bool IsClient=false, IsConnected=false; } }
