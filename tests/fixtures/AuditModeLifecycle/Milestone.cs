using System;
namespace UnityEngine {
 public struct Vector3 { public Vector3 normalized {get{return this;}} }
 public static class Random { public static Vector3 insideUnitSphere; public static float Range(float a,float b){return a;} }
 public static class Time { public static float unscaledTime; }
}
namespace ItemStatsSystem {
 public class Item {
  public int TypeID, StackCount=1, MaxStackCount=1000000;
  public int GetTotalRawValue(){return 5000;}
  public void DestroyTree(){}
  public void Drop(UnityEngine.Vector3 p,bool a,UnityEngine.Vector3 d,float f){ if(StackCount<1||StackCount>MaxStackCount)throw new Exception("illegal stack"); if(TypeID==1254) ItemAssetsCollection.Crowns+=StackCount;else ItemAssetsCollection.Cash+=StackCount;ItemAssetsCollection.Drops++; }
 }
 public static class ItemAssetsCollection {
  public static long Crowns,Cash; public static int Drops,FailCount;
  public static Item GetPrefab(int id){return new Item{TypeID=id,MaxStackCount=id==1254?1:1000000};}
  public static Item InstantiateSync(int id){if(FailCount>0){FailCount--;throw new Exception("injected create failure");}return GetPrefab(id);}
 }
}
namespace Duckov.Economy {
 public class EconomyManager {
  public const int CashItemID=451;public static EconomyManager Instance=new EconomyManager(); public static long Money;public static bool ThrowAfterAdd;
  public static bool Add(long value){Money+=value;if(ThrowAfterAdd){ThrowAfterAdd=false;throw new Exception("listener fault");}return true;}
 }
}
namespace BossRush {
 public class ModBehaviour {public bool IsActive=true;public void ShowMessage(string s){}public static void DevLog(string s){} }
 public static class BossRushUI {public static bool IsGamePaused(){return false;} }
 public static class L10n {public static string T(string c,string e){return e;} }
 static class Program {
  static void Check(bool value,string name){if(!value)throw new Exception(name);Console.WriteLine("PASS "+name);}
  static void Main(){
   foreach(int tier in new[]{15,16,32,33}) {
    long expected=10000000L*(1L<<(tier-1));Check(InfiniteHellMilestoneDelivery.SaturatingReward(tier,10000000)==expected,"no overflow tier "+tier);
   }
   Check(InfiniteHellMilestoneDelivery.SaturatingReward(int.MaxValue,10000000)==long.MaxValue,"host range saturates without shift wrap");
   var service=new InfiniteHellMilestoneDelivery();var owner=new ModBehaviour();
   long expectedCash=0,expectedCrowns=0;
   for(int tier=1;tier<=16;tier++) {
    expectedCash+=10000000L*(1L<<(tier-1)); expectedCrowns+=1L<<(tier-1);
    service.Enqueue(tier,new UnityEngine.Vector3());int previous=ItemStatsSystem.ItemAssetsCollection.Drops;
    if(tier==8){ItemStatsSystem.ItemAssetsCollection.FailCount=1;Duckov.Economy.EconomyManager.ThrowAfterAdd=true;}
    for(int frame=0;frame<80;frame++){
     int before=ItemStatsSystem.ItemAssetsCollection.Drops;UnityEngine.Time.unscaledTime+=1;service.Tick(owner);
     if(ItemStatsSystem.ItemAssetsCollection.Drops-before>8)throw new Exception("frame budget exceeded");
    }
    long actual=ItemStatsSystem.ItemAssetsCollection.Cash+Duckov.Economy.EconomyManager.Money+ItemStatsSystem.ItemAssetsCollection.Crowns*5000;
    Check(actual==expectedCash+expectedCrowns*5000,"reward value conserved through tier "+tier);
    Check(ItemStatsSystem.ItemAssetsCollection.Drops-previous<=200,"physical item budget tier "+tier);
   }
  }
 }
}
