using System;
using System.Collections.Generic;
using System.Reflection;
using BossRush;
using HarmonyLib;
using UnityEngine;

public partial class CharacterMainControl
{
    public static CharacterMainControl Main;
    public Health Health;
}
public partial class Health
{
    public T GetComponent<T>() where T:class { return null; }
    public T GetComponentInParent<T>() where T:class { return null; }
}
public static class LocalizationStub { public static string ToPlainText(this string s) { return s; } }
public struct ItemMetaData { public int id; public string DisplayName; }
public static class ItemAssetsCollection { public static ItemMetaData GetMetaData(int id) { return new ItemMetaData(); } }
namespace Duckov.Scenes { internal class FixtureNamespace { } }
namespace UnityEngine { public static class Time { public static int frameCount; public static float unscaledTime=100, realtimeSinceStartup=100,deltaTime=.05f,unscaledDeltaTime=.05f; } }
namespace BossRush
{
    internal sealed class TidegateWorld { }
    internal sealed class TidegateTide { }
    internal static class F3GameplayValidationRunner { public static bool IsRunning; }
    internal static class ModBehaviour { public static void DevLog(string s) {} }
    internal static class L10n { public static string T(string zh,string en){return en;} }
    internal static class TidegateRaidScene { public const string LogTag="[Tidegate]"; }
    internal static class ModeGRuntimeGates
    {
        public static bool IsModeGStagingBarrierActive { get { return false; } }
        public static bool IsModeGStagingHealthBlocked(Health h){return false;}
    }
    internal static class ReverseScaleAbilityManager
    {
        public static readonly HashSet<Health> Invul = new HashSet<Health>(), Clamp = new HashSet<Health>();
        public static bool IsPostTriggerInvincible(Health h){return Invul.Contains(h);}
        public static bool TryPrepareLethalProtectionDuringHurt(Health h){return Clamp.Contains(h);}
    }
    internal class ReverseScaleConfig { public static ReverseScaleConfig Instance=new ReverseScaleConfig(); public float TriggerHealthThreshold=1f; }
    internal class DragonKingAbilityController { public bool ShouldClampLethalHealthDuringHurt(){return false;} }
    internal static class DragonKingConfig {public const float ChildProtectionHealthThreshold=1;}
    internal class DragonDescendantAbilityController {public bool ShouldClampLethalHealthDuringHurt(){return false;}}
    internal static class PetNestCompanionAgent
    {
        public static bool IsCompanionArmed {get{return false;}}
        public static bool IsCompanionHealth(Health h){return false;}
        public static void NotifyLethalClamped(Health h){}
    }
    internal static class PetNestDownedHandler {public static void NotifyLethalClamped(Health h){}}
    internal sealed class TidegateSluiceDanger : IDisposable
    {
        public static bool Hit;
        public static int Ticks;
        public TidegateSluiceDanger(TidegateWorld w,TidegateTide t,Action<string,bool> a){}
        public bool Tick(Vector3 at,float dt){Ticks++;return Hit;}
        public void Dispose(){}
    }
    internal sealed class TidegateCraneDanger : IDisposable
    {
        public static bool Hit; public static int Ticks;
        public TidegateCraneDanger(TidegateWorld w,TidegateTide t,Action<string,bool> a){}
        public bool Tick(Vector3 at,float dt){Ticks++;return Hit;}
        public void Dispose(){}
    }
    internal static class View {public static object ActiveView;}
    internal sealed class Hud { public void Tick(float t,bool hide){} }
    internal partial class SessionGate
    {
        public bool closed,ready=true,returnRequested,deathPending=false;
        public Hud hud=null;
        public CharacterMainControl player;
        public TidegateHazards hazards;
        public int Returns;
        private void DispatchReturnIfReady(){Returns++;}
        private void TickFault(string name,Exception e){throw e;}
    }
}
internal static class Program
{
    static void Must(bool b,string s){if(!b)throw new InvalidOperationException(s);}
    static DamageInfo Shot(float amount){return new DamageInfo(null){damageValue=amount};}
    sealed class Case : IDisposable
    {
        internal readonly CharacterMainControl Player, Other;
        internal readonly List<string> Notices=new List<string>();
        internal readonly TidegateHazards Hazards;
        internal readonly SessionGate Session;
        internal int DeadEvents;
        internal DamageInfo Last;
        static CharacterMainControl Build(bool main)
        {
            var c=new CharacterMainControl{IsMainCharacter=main};
            c.Health=new Health{Owner=c,team=main?Teams.player:Teams.scav};
            c.Health.CurrentHealth=50;
            return c;
        }
        internal Case()
        {
            Health.ClearStaticSubscribers();
            ReverseScaleAbilityManager.Invul.Clear();ReverseScaleAbilityManager.Clamp.Clear();
            TidegateSluiceDanger.Hit=TidegateCraneDanger.Hit=false; TidegateSluiceDanger.Ticks=TidegateCraneDanger.Ticks=0;
            F3GameplayValidationRunner.IsRunning=false; View.ActiveView=null; MultiSceneCore.Instance=new MultiSceneCore(); LevelManager.Instance=new LevelManager();
            Player=Build(true);Other=Build(false);CharacterMainControl.Main=LevelManager.Instance.MainCharacter=Player;
            Player.Health.OnDeadEvent.AddListener(d=>{DeadEvents++;Last=d;});
            Hazards=new TidegateHazards(new TidegateWorld(),new TidegateTide(),(s,w)=>Notices.Add(s));
            Session=new SessionGate{player=Player,hazards=Hazards};
        }
        public void Dispose(){Hazards.Dispose();Health.ClearStaticSubscribers();}
    }

    static void Fatal(bool sluice)
    {
        using(var c=new Case())
        {
            var p=c.Player.Health; var other=c.Other.Health; p.SetInvincible(true); p.ResistanceFactor=0f;
            ReverseScaleAbilityManager.Invul.Add(p); ReverseScaleAbilityManager.Clamp.Add(p);
            ReverseScaleAbilityManager.Invul.Add(other);
            int broadcasts=0,changed=0; bool nested=true,otherDamage=true;
            Health.OnDead+=(h,d)=>{if(h==p)broadcasts++;};
            p.OnHealthChange.AddListener(h=>{
                changed++;
                Must(!h.IsDead && h.CurrentHealth==0,"official first zero before death");
                Must(TidegateHazards.IsResolving(h) && !TidegateHazards.IsResolving(other),"target identity scope");
                nested=h.Hurt(Shot(6));
                otherDamage=other.Hurt(Shot(90));
            });
            TidegateSluiceDanger.Hit=sluice; TidegateCraneDanger.Hit=true;
            c.Session.Step();c.Session.Step();
            Must(c.DeadEvents==1 && broadcasts==1 && p.IsDead && !p.gameObject.activeSelf,"official death did not execute exactly once: local="+c.DeadEvents+", global="+broadcasts);
            Must(changed==1 && !nested && !otherDamage && other.CurrentHealth==50,"nested same player / protected other player damage escaped boundary");
            Must(p.Invincible && !BossRushHealthHurtContextPatch.IsInsideHurt && !TidegateHazards.IsResolving(p),"invulnerability or scope not restored");
            Must(c.Notices.Count==1 && c.Last.damageType==DamageTypes.realDamage && c.Last.ignoreArmor && c.Last.ignoreDifficulty, "not a confirmed real hit");
            Must(c.Last.GenerateDescription()==(sluice?"Swept into the sluice intake":"Crushed by the dock crane"),"reason lost after resolved scope closed");
            Must(Shot(1).GenerateDescription()=="?","hazard reason leaked to unrelated damage");
        }
    }
    static void MissAndNormal()
    {
        using(var c=new Case())
        {
            var p=c.Player.Health;
            c.Session.Step(); Must(!p.IsDead && p.CurrentHealth==50 && c.Notices.Count==0,"near miss hurt player");
            TidegateSluiceDanger.Hit=true;
            int n=TidegateSluiceDanger.Ticks;
            F3GameplayValidationRunner.IsRunning=true;c.Session.Step(); F3GameplayValidationRunner.IsRunning=false;
            View.ActiveView=new object();c.Session.Step();View.ActiveView=null;
            float dt=Time.deltaTime;Time.deltaTime=0f;c.Session.Step();Time.deltaTime=dt;
            c.Session.returnRequested=true;c.Session.Step();c.Session.returnRequested=false;
            c.Session.ready=false;c.Session.Step();c.Session.ready=true;
            c.Session.closed=true;c.Session.Step();c.Session.closed=false;
            c.Session.player=c.Other;c.Session.Step();c.Session.player=c.Player;
            LevelManager.Instance.IsRaidMap=false;c.Session.Step();LevelManager.Instance.IsRaidMap=true;
            MultiSceneCore.Instance.IsLoading=true;c.Session.Step();MultiSceneCore.Instance.IsLoading=false;
            Must(TidegateSluiceDanger.Ticks==n && c.Session.Returns==1 && c.Notices.Count==0 && p.CurrentHealth==50,"paused/return/F3/loading/wrong character touched hazard");
            p.SetInvincible(true);Must(!p.Hurt(Shot(75)) && p.CurrentHealth==50,"official ordinary invulnerability");
            p.SetInvincible(false);ReverseScaleAbilityManager.Invul.Add(p);
            Must(!p.Hurt(Shot(75)) && p.CurrentHealth==50,"ordinary reverse-scale invulnerability");
            ReverseScaleAbilityManager.Invul.Remove(p);ReverseScaleAbilityManager.Clamp.Add(p);
            Must(p.Hurt(Shot(75)) && p.CurrentHealth==1 && !p.IsDead && c.DeadEvents==0,"ordinary lethal protection not preserved");
            ReverseScaleAbilityManager.Clamp.Remove(p);p.Hurt(Shot(10));
            Must(c.DeadEvents==1 && c.Last.GenerateDescription()=="?" && c.Notices.Count==0,"ordinary lethal hit got hazard treatment");
        }
    }
    static void Fault()
    {
        using(var c=new Case())
        {
            var p=c.Player.Health;p.SetInvincible(true);TidegateCraneDanger.Hit=true;
            var thrown=new InvalidOperationException("intentional health change fault");
            UnityEngine.Events.UnityAction<Health> fail=h=>{throw thrown;};p.OnHealthChange.AddListener(fail);
            bool caught=false;try{c.Hazards.Tick(c.Player,.05f);}catch(Exception e){caught=ReferenceEquals(thrown,e);}
            Must(caught && !TidegateHazards.IsResolving(p) && !BossRushHealthHurtContextPatch.IsInsideHurt && p.Invincible &&
                 c.DeadEvents==0 && c.Notices.Count==0,"fault did not restore scoped bindings");
            p.OnHealthChange.RemoveListener(fail);p.SetHealth(50f);
            Must(!p.Hurt(Shot(5)) && p.CurrentHealth==50,"normal hurt after exception bypassed protection");
            Must(c.Hazards.Tick(c.Player,.05f) && c.DeadEvents==1,"fresh impact did not work after exception");
        }
    }
    public static int Main()
    {
        var h=new Harmony("bossrush.tests.tidegate.fatal");
        try
        {
            h.CreateClassProcessor(typeof(BossRushHealthHurtContextPatch)).Patch();
            h.CreateClassProcessor(typeof(BossRushBossLethalHealthProtectionPatch)).Patch();
            var desc=typeof(TidegateHazards).GetNestedType("DeathDescription",BindingFlags.NonPublic);
            h.CreateClassProcessor(desc).Patch();
            Fatal(true);Fatal(false);MissAndNormal();Fault();
            Console.WriteLine("PASS: real Harmony, official Hurt/current-health/death events. Sluice + crane, reentrant listeners, bypass only on impact, cause text, pause/F3/return and exception restoration.");
            return 0;
        }
        catch(Exception e){Console.Error.WriteLine("FAIL: "+e.GetType().Name+": "+e.Message);return 1;}
        finally{h.UnpatchAll(h.Id);}
    }
}
