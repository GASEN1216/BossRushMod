using System;
using BossRush;
using ItemStatsSystem;
using UnityEngine;

internal static class Program
{
    static void Check(bool ok,string message){if(!ok) throw new Exception(message);}
    private sealed class Raid : IDisposable
    {
        public readonly GameObject Root = new GameObject("TidegateWorld");
        public readonly GameObject Ground = new GameObject("COL_Ground");
        public readonly CharacterMainControl Owner, Player;
        public readonly AICharacterController Ai;
        public readonly Collider PlayerShell;
        public readonly TidegateTide Tide = new TidegateTide();
        public bool Ready = true;
        public readonly TidegateGunner Gunner;
        public Raid(bool hasSkill=true)
        {
            Time.time=0; Physics.Wall=false; SkyIslandBossForge.CanDraw=true; SkyIslandBossForge.Ring=null; Grenade.Last=null;
            Ground.layer=1; Ground.transform.SetParent(Root.transform,false); Physics.Ground=Ground.transform;
            Player=new GameObject("player").AddComponent<CharacterMainControl>(); Player.transform.position=new Vector3(12,0,0); CharacterMainControl.Main=Player;
            PlayerShell=Player.gameObject.AddComponent<Collider>(); Player.gameObject.AddComponent<Collider>().isTrigger=true; Physics.IgnoredBody=null;
            Owner=new GameObject("digger").AddComponent<CharacterMainControl>(); Owner.transform.SetParent(Root.transform,false);
            Ai=Owner.gameObject.AddComponent<AICharacterController>();
            var item=new GameObject("Item_Grenade").AddComponent<Item>(); item.DisplayNameRaw="Item_Grenade"; item.TypeID=67;
            var skill=item.gameObject.AddComponent<Skill_Grenade>(); skill.createExplosion=true; skill.damageInfo.damageValue=80;
            skill.grenadePfb=new GameObject("grenade prefab").AddComponent<Grenade>();
            item.gameObject.AddComponent<ItemSetting_Skill>().Skill=skill; ItemAssetsCollection.Prefab=hasSkill?item:null;
            TidegateEnemyActors.Equip(TidegateEnemyRole.Gunner, Owner, Tide, () => Ready); Gunner=Owner.GetComponent<TidegateGunner>();
        }
        public void See(){Ai.searchedEnemy=Player.mainDamageReceiver;}
        public void Tick(float dt){Time.time+=dt; if(Gunner.enabled) Gunner.Call("Update");}
        public void Warn(){See(); Tick(4.1f); Check(Gunner.WarningActive && Gunner.Throws==0 && Grenade.Last==null,"no visible windup before throw");}
        public void Fire(){Tick(.66f); Check(Gunner.Throws>0 && Grenade.Last!=null && !Grenade.Last.createExplosion && Grenade.Last.delayTime>20f,"in-flight projectile must be inert");}
        public void Land(bool off, bool wall)
        {
            var bomb=Grenade.Last;
            bomb.transform.position=SkyIslandBossForge.Ring.transform.position+new Vector3(off?3f:.15f,.12f,0);
            var collider=wall?new GameObject("wall"):Ground;
            if(wall){ collider.layer=2;collider.transform.SetParent(Root.transform,false); }
            bomb.GetComponent<TidegateGunnerLanding>().Call("OnCollisionEnter",new Collision{gameObject=collider});
        }
        public void Dispose()
        {
            if(Gunner!=null) Gunner.enabled=false;
            UnityEngine.Object.Destroy(Root); UnityEngine.Object.Destroy(Player.gameObject);
            Check(SkyIslandBossForge.Ring==null && Grenade.Last==null,"world retained a live ring or projectile");
        }
    }
    static void Attack()
    {
        using(var run=new Raid())
        {
            run.Warn(); Vector3 mark=SkyIslandBossForge.Ring.transform.position;
            run.Player.transform.position=new Vector3(17,0,0); run.Fire();
            Check((Grenade.Intended-new Vector3(mark.x,.12f,mark.z)).sqrMagnitude<.001f,"tracked player after warning");
            Check(Physics.IgnoredBody==run.PlayerShell,"player body could turn the incoming grenade into a dud");
            run.Land(false,false); var bomb=Grenade.Last;
            Check(run.Gunner.Landed && bomb.Stuck && bomb.createExplosion && bomb.delayTime>=2f && bomb.damageInfo.damageValue<=25 &&
                bomb.damageRange==SkyIslandBossForge.ShapeRadius && (bomb.transform.position-Grenade.Intended).sqrMagnitude<.001f,"armed outside visual or not enough time to escape");
            run.Tick(1.9f); Check(run.Gunner.WarningActive && Grenade.Last!=null,"ring vanished before detonation");
            run.Tick(.7f); Check(!run.Gunner.WarningActive && Grenade.Last==null,"spent round remained");
            run.Tick(18f);Check(!run.Gunner.WarningActive,"cooldown allowed rapid volley");
            run.Tick(2.1f);Check(run.Gunner.WarningActive,"did not ready second round");run.Fire();run.Land(false,false);
            run.Tick(3f);run.Tick(25f); Check(run.Gunner.Throws==2 && !run.Gunner.enabled && !run.Gunner.WarningActive,"third throw");
        }
    }
    static void Hazards()
    {
        using(var noSkill=new Raid(false)) Check(noSkill.Gunner==null,"missing real grenade still advertised actor");
        using(var run=new Raid())
        {
            run.Tick(5f); Check(!run.Gunner.WarningActive,"spotted nobody but started attack");run.See();
            foreach(float dist in new[]{5f,22f}){run.Player.transform.position=new Vector3(dist,0,0);run.Tick(.6f);Check(!run.Gunner.WarningActive,"bad throw distance");}
            run.Player.transform.position=new Vector3(12,0,0); Physics.Wall=true;run.Tick(.6f);Check(!run.Gunner.WarningActive,"saw through wall");
            Physics.Wall=false;run.Tide.Deep=true;run.Tick(.6f);Check(!run.Gunner.WarningActive,"locked player in deep water");
        }
        foreach(int mode in new[]{0,1,2,3,4,5})
        using(var run=new Raid())
        {
            run.Warn();run.Fire();var bomb=Grenade.Last;
            if(mode==0) run.Land(true,false);
            if(mode==1) run.Land(false,true);
            if(mode==2){run.Land(false,false);run.Owner.Health.IsDead=true;run.Tick(.1f);}
            if(mode==3){run.Land(false,false);run.Ready=false;run.Tick(.1f);}
            if(mode==4) run.Tick(3.1f);
            if(mode==5){SkyIslandBossForge.Ring.enabled=false;run.Tick(.1f);}
            Check(bomb==null && !bomb.createExplosion && !run.Gunner.WarningActive,"failed safe-disarm mode "+mode);
        }
        using(var run=new Raid()){SkyIslandBossForge.CanDraw=false;run.See();run.Tick(5);Check(!run.Gunner.enabled && run.Gunner.Throws==0,"invisible blast");}
    }
    static void Main()
    {
        Attack();Hazards();Console.WriteLine("PASS: fixed target, visible lead time and radius, two rounds, cooldown; targeting/range/wall/deep gates; off-target impact, wall contact, death, exit, timeout and lost-warning disarm.");
    }
}
