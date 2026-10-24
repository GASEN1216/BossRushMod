using System;
using BossRush;
using ItemStatsSystem;
using ItemStatsSystem.Stats;
using UnityEngine;

internal static class Program
{
    private static int checks;
    private static void Check(bool value,string message) { checks++; if(!value) throw new Exception(message); }
    private static Item Fresh(int id=DragonBreathConfig.WEAPON_TYPE_ID)
    {
        return new Item {TypeID=id,Value=33862,Stats=new StatCollection(),Gun=new ItemSetting_Gun(),Inventory=new Inventory()};
    }
    private static Item Complete()
    {
        Item item=Fresh();DragonBreathWeaponConfig.ConfigureWeapon(item);return item;
    }
    private static void RemoveFactor(Item item) { var old=item.Stats;item.Stats=new StatCollection();foreach(var stat in old) if(stat.Key!="DamageFactorToZombie") item.Stats.Add(stat); }
    private static void Recovery()
    {
        var host=new ModBehaviour();host.Register();host.Register();
        var prefab=Complete();ItemAssetsCollection.Prefabs[prefab.TypeID]=prefab;
        Check(prefab.GetStatValue("DamageFactorToZombie".GetHashCode())==1,"fresh production configuration gives neutral zombie damage");
        foreach(float wear in new[]{0f,17f,99f})
        {
            var old=Complete();RemoveFactor(old);old.Durability=wear;old.DurabilityLoss=0.3f;
            old.Stats.GetStat("Damage").BaseValue=38;old.Stats.GetStat("Damage").RuntimeBonus=7;
            old.Variables.Add(new CustomData{Key="RF_STAT_Damage",Value=15});ReforgeDataPersistence.MarkAsRestored(old);
            Check(old.GetStatValue("DamageFactorToZombie".GetHashCode())==0,"old official hash cache records the missing factor before repair");
            Check(DragonBreathWeaponConfig.NeedsRuntimeConfig(old),"otherwise complete old gun is detected");
            ItemInitializeRestorePatch.Postfix(old);
            Check(old.GetStatValue("DamageFactorToZombie".GetHashCode())==1,"actual Item.Initialize postfix repairs old gun");
            Check(old.Durability==wear && old.DurabilityLoss==0.3f,"repair retains worn and broken durability");
            Check(old.Stats.GetStat("Damage").BaseValue==38 && old.Stats.GetStat("Damage").RuntimeBonus==7,"repair retains real RF restore and runtime modifier");
            Check(!DragonBreathWeaponConfig.NeedsRuntimeConfig(old),"repair completes the dedicated predicate");
            Check(!CustomItemRuntimeStateHelper.RestoreRuntimeState(old,"repeat"),"repeat restoration neither configures nor reapplies RF");
        }
        foreach(float factor in new[]{0.25f,1f,2.5f})
        {
            var gun=Complete();gun.Stats.GetStat("DamageFactorToZombie").BaseValue=factor;
            gun.Stats.GetStat("DamageFactorToZombie").RuntimeBonus=0.5f;
            Check(!DragonBreathWeaponConfig.NeedsRuntimeConfig(gun),"existing zombie stat is not a missing configuration");
            DragonBreathWeaponConfig.ConfigureWeapon(gun);
            Check(gun.Stats.GetStat("DamageFactorToZombie").BaseValue==factor && gun.Stats.GetStat("DamageFactorToZombie").RuntimeBonus==0.5f,"normal full configuration keeps an existing legal factor and modifier");
        }
        var stale=Fresh();stale.Stats.Add(new Stat("DamageFactorToZombie",2.5f,false));
        Check(stale.GetStatValue("Damage".GetHashCode())==0,"initial lazy dictionary lacks weapon profile stats");
        DragonBreathWeaponConfig.ConfigureWeapon(stale);
        Check(stale.GetStatValue("Damage".GetHashCode())==23 && stale.GetStatValue("DamageFactorToZombie".GetHashCode())==2.5f,"full configuration refreshes hash cache even with an existing legal zombie factor");
        var partial=Fresh();partial.Durability=12;partial.DurabilityLoss=0.2f;
        partial.Variables.Add(new CustomData{Key="RF_STAT_Damage",Value=15});
        ItemInitializeRestorePatch.Postfix(partial);
        Check(partial.Stats.GetStat("Damage").BaseValue==38 && partial.Durability==12 && partial.DurabilityLoss==0.2f,"missing basic stats take the full configuration path and restore RF/wear");
        var unknown=Fresh(123);ItemInitializeRestorePatch.Postfix(unknown);
        Check(unknown.Stats.Count==0,"unregistered official weapon is untouched");
        var player=new CharacterMainControl{IsMainCharacter=true};CharacterMainControl.Main=player;LevelManager.Instance.MainCharacter=player;
        var held=Complete();RemoveFactor(held);held.Stats.GetStat("Damage").BaseValue=61;
        var agent=new ItemAgent_Gun{Item=held,Holder=player};player.CurrentHoldItemAgent=agent;
        host.bossRushIntegrationRuntime.SubscribeDragonBreathEffectEvent();
        Check(held.Stats.GetStat("DamageFactorToZombie")!=null && held.Stats.GetStat("Damage").BaseValue==61,"scene subscription repairs already-held gun without resetting base damage");
        var late=Complete();RemoveFactor(late);player.Hold(new ItemAgent_Gun{Item=late,Holder=player});
        Check(late.Stats.GetStat("DamageFactorToZombie")!=null,"real hold-change subscription reaches dedicated repair");
        host.bossRushIntegrationRuntime.UnsubscribeDragonBreathEffectEvent();
    }
    private static float Shoot(Item item,bool zombie)
    {
        var player=CharacterMainControl.Main;
        var target=new CharacterMainControl{IsMainCharacter=false,Team=Teams.wolf};
        var health=new Health{Owner=target,isZombie=zombie};
        var go=new GameObject{layer=0};var receiver=new DamageReceiver{health=health};go.Put(receiver);var collider=new Collider();go.Put(collider);
        Physics.Hits=new[]{new RaycastHit{collider=collider,distance=1,point=new Vector3(0,0,1),normal=new Vector3(0,0,-1)}};
        var gun=new ItemAgent_Gun{Item=item,Holder=player};gun.Fire();
        Check(LevelManager.Instance.BulletPool.Last.context.fromWeaponItemID==DragonBreathConfig.WEAPON_TYPE_ID,"official gun creates a real weapon-identified projectile context");
        LevelManager.Instance.BulletPool.Last.Hit();
        return 1000-health.CurrentHealth;
    }
    private static void OfficialDamageChain()
    {
        var legacy=Complete();RemoveFactor(legacy);
        Check(Shoot(legacy,true)==0,"unfixed legacy gun reproduces zero damage through official gun, projectile, receiver and zombie Health");
        Check(Shoot(legacy,false)==23,"same gun still damages a non-zombie target");
        ItemInitializeRestorePatch.Postfix(legacy);
        Check(Shoot(legacy,true)==23,"production old-instance repair restores direct zombie damage through the official full chain");
        legacy.Stats.GetStat("DamageFactorToZombie").BaseValue=2.5f;
        Check(Shoot(legacy,true)==57.5f,"existing legal factor is carried through official gun and projectile");
    }
    private static int Main()
    {
        Recovery();OfficialDamageChain();Console.WriteLine("DragonBreathZombieRuntime PASS "+checks);return 0;
    }
}
