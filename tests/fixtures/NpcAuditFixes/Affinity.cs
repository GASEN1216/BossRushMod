using System;
using System.Collections.Generic;
using System.Text.Json;
using BossRush;
namespace UnityEngine { public static class Time { public static float realtimeSinceStartup=1f; } }
public static class GameClock { public static int Day=100; }
namespace BossRush { public static class ModBehaviour { public static void DevLog(string s) {} } }
namespace Saves {
 public static class SavesSystem {
  public static event Action OnSetFile,OnCollectSaveData;
  public static string Raw;
  public static bool FailLoad;
  public static int Writes,PhysicalWrites;
  public static bool KeyExisits(string key) { return Raw!=null; }
  public static T Load<T>(string key) { if(FailLoad) {FailLoad=false;throw new InvalidOperationException("controlled read failure");} return (T)(object)Raw; }
  public static void Save<T>(string key,T data) { Raw=(string)(object)data;Writes++; }
  public static void SaveFile(bool flag) { PhysicalWrites++; }
  public static void Collect() { if(OnCollectSaveData!=null)OnCollectSaveData(); }
  public static void Reset(string raw,bool failLoad=false) { Raw=raw;FailLoad=failLoad;Writes=PhysicalWrites=0;OnSetFile=null;OnCollectSaveData=null;AffinityManager.ResetStaticCaches(); }
 }
}
class Program {
 static int checks;
 static void Check(bool ok,string name){checks++;if(!ok)throw new Exception(name);}
 static void Main(){
  const string legacy="{\"npcDataList\":[{\"npcId\":\"npc\",\"points\":2300,\"lastGiftDay\":99,\"lastChatDay\":99,\"isMarriedToPlayer\":true}]}";
  foreach(string raw in new[]{legacy,"{bad}","{\"npcDataList\":[{\"npcId\":\"npc\",\"points\":\"bad\"}]}"}){
   Saves.SavesSystem.Reset(raw,raw==legacy);AffinityManager.Initialize();
   AffinityManager.SetPoints("npc",10);AffinityManager.Save();Saves.SavesSystem.Collect();AffinityManager.Shutdown();
   Check(Saves.SavesSystem.Raw==raw && Saves.SavesSystem.Writes==0 && Saves.SavesSystem.PhysicalWrites==0,"read failure must preserve raw snapshot and block every flush");
  }
  Saves.SavesSystem.Reset(legacy);AffinityManager.Initialize();
  Check(AffinityManager.CheckAndApplyDailyDecay("npc")==0 && AffinityManager.GetPoints("npc")==2300,"legacy missing decay day initializes without retroactive loss");
  Check(AffinityManager.IsMarriedToPlayer("npc"),"legacy spouse survives");
  AffinityManager.AddPoints("npc",-10);Saves.SavesSystem.Collect();Check(Saves.SavesSystem.Writes==1,"valid snapshot remains writable");Check(Saves.SavesSystem.PhysicalWrites==0,"collect only fills the official cache; the official SaveFile after collection writes the disk");
  Saves.SavesSystem.Reset(null);AffinityManager.Initialize();AffinityManager.SetPoints("new",50);Saves.SavesSystem.Collect();Check(Saves.SavesSystem.Writes==1,"missing key is a fresh writable slot");
  Saves.SavesSystem.Reset("bad");AffinityManager.Initialize();Saves.SavesSystem.Raw=legacy;AffinityManager.Load();Check(AffinityManager.CanWrite && AffinityManager.GetPoints("npc")==2300,"successful reload lifts barrier");
  DecayFloor();
  ShopStock();
  Console.WriteLine("Affinity "+checks+" PASS");
 }
 static string Npc(string id,int points,string extra){return "{\"npcId\":\""+id+"\",\"points\":"+points+",\"lastChatDay\":80,\"lastDecayCheckDay\":90"+extra+"}";}
 // P1-2（2026-10-17）：衰减不跌破已看过的最高剧情里程碑；配偶保住 Lv.10；扣分只提示一次。
 static void DecayFloor(){
  GameClock.Day=100;
  string raw="{\"npcDataList\":["+Npc("plain",300,"")+","+Npc("story5",560,",\"triggeredEventKeys\":\"story_5\"")+","+Npc("legacy5",510,",\"hasTriggeredStory5\":true")+","
   +Npc("spouse",2300,",\"isMarriedToPlayer\":true")+","+Npc("hurt",2000,",\"isMarriedToPlayer\":true")+"]}";
  Saves.SavesSystem.Reset(raw);AffinityManager.Initialize();
  Check(AffinityConfig.DAILY_DECAY_AMOUNT==10,"daily decay is 10 per idle day");
  Check(AffinityManager.CheckAndApplyDailyDecay("plain")==100 && AffinityManager.GetPoints("plain")==200,"no milestone: ten idle days cost 100");
  AffinityManager.DecayNotice notice;
  Check(AffinityManager.TryConsumeDecayNotice("plain",out notice) && notice.Days==10 && notice.Points==100,"decay leaves one notice with days and points");
  Check(!AffinityManager.TryConsumeDecayNotice("plain",out notice),"notice is consumed once");
  Check(AffinityManager.CheckAndApplyDailyDecay("story5")==60 && AffinityManager.GetLevel("story5")==5 && AffinityManager.GetPoints("story5")==500,"seen Lv.5 story floors decay at the Lv.5 threshold");
  Check(AffinityManager.CheckAndApplyDailyDecay("legacy5")==10 && AffinityManager.GetPoints("legacy5")==500,"legacy hasTriggeredStory5 flag also sets the floor");
  Check(AffinityManager.CheckAndApplyDailyDecay("spouse")==0 && AffinityManager.GetPoints("spouse")==2300 && !AffinityManager.TryConsumeDecayNotice("spouse",out notice),"spouse at Lv.10 does not decay or notify");
  Check(AffinityManager.CheckAndApplyDailyDecay("hurt")==0 && AffinityManager.GetPoints("hurt")==2000,"points already below the floor are neither decayed nor raised");
  Check(AffinityManager.CheckAndApplyDailyDecay("plain")==0,"same-day recheck is idempotent");
  // CR-2026-10-18-011：玩家没来聊天又衰减一轮时提示要累加，不能被后一次覆盖。
  GameClock.Day=103;Check(AffinityManager.CheckAndApplyDailyDecay("plain")==30,"second idle stretch decays again");
  GameClock.Day=105;Check(AffinityManager.CheckAndApplyDailyDecay("plain")==20,"third idle stretch decays again");
  Check(AffinityManager.TryConsumeDecayNotice("plain",out notice) && notice.Days==5 && notice.Points==50,"unread decay notices accumulate days and points");
  GameClock.Day=100;
  AffinityManager.Load();Check(!AffinityManager.TryConsumeDecayNotice("story5",out notice),"reload clears pending notices");
  GameClock.Day=100;
 }
 // P1-1（2026-10-17）：库存跟着好感存档；按游戏日补货，0 = 终身限量。
 static void ShopStock(){
  Func<float> never=()=>{throw new Exception("roll must not happen without refill");};
  Func<float> high=()=>0.9f;
  NPCShopStockSlot none=default(NPCShopStockSlot);
  NPCShopStockSlot fresh=NPCShopStockLedger.Resolve(false,none,7,1,0,1f,100,never);
  Check(fresh.Remaining==1 && fresh.RefillDay==100 && fresh.Shown,"missing record is a full shelf (old saves unchanged)");
  NPCShopStockSlot sold=fresh;sold.Remaining=0;
  Check(NPCShopStockLedger.Resolve(true,sold,7,1,0,1f,500,never).Remaining==0,"lifetime-limited item never restocks");
  Check(NPCShopStockLedger.Resolve(true,sold,7,1,1,1f,100,never).Remaining==0,"daily item stays sold out the same day");
  Check(NPCShopStockLedger.Resolve(true,sold,7,1,1,1f,101,()=>0f).Remaining==1,"daily item refills next game day");
  Check(NPCShopStockLedger.Resolve(true,sold,7,1,1,1f,99,()=>0f).Remaining==1,"game day going backwards refills instead of locking");
  // CR-2026-10-18-004：钻石戒指已婚不补；离婚后距上次补货满间隔就补，能再求婚。
  int ringMarried=NPCShopStockLedger.EffectiveRestockDays(3,true,true), ringSingle=NPCShopStockLedger.EffectiveRestockDays(3,true,false);
  Check(NPCShopStockLedger.Resolve(true,sold,7,1,ringMarried,1f,500,never).Remaining==0,"single-only item stays sold out while married");
  Check(NPCShopStockLedger.Resolve(true,sold,7,1,ringSingle,1f,500,()=>0f).Remaining==1,"single-only item restocks once the player is single again");
  Check(NPCShopStockLedger.Resolve(true,sold,7,1,ringSingle,1f,102,never).Remaining==0,"single-only item still waits its restock interval");
  Check(NPCShopStockLedger.EffectiveRestockDays(1,false,true)==1,"ordinary items ignore marriage");
  NPCShopStockSlot hidden=NPCShopStockLedger.Resolve(false,none,8,3,1,0.5f,100,high);
  Check(!hidden.Shown && hidden.Remaining==0,"possibility roll above p hides the item for the day");
  Check(!NPCShopStockLedger.Resolve(true,hidden,8,3,1,0.5f,100,never).Shown,"reopening the same day keeps the roll (no reroll by reopening)");
  NPCShopStockSlot many=fresh;many.Remaining=5;
  Check(NPCShopStockLedger.Resolve(true,many,7,2,1,1f,100,never).Remaining==2,"lowered MaxStock clamps the remaining stock");
  var parsed=NPCShopStockLedger.Parse("7:0:100:1,bad,8:x:1:1,9:2:100:3,500001:3:100:1");
  Check(parsed.Count==2 && parsed[7].Remaining==0 && parsed[500001].Remaining==3,"bad tokens are skipped one by one");
  Check(NPCShopStockLedger.Serialize(parsed)=="7:0:100:1,500001:3:100:1","tokens serialize sorted and stable");
  Saves.SavesSystem.Reset(null);AffinityManager.Initialize();
  AffinityManager.SetShopStockTokens("shop","7:0:100:1");Saves.SavesSystem.Collect();
  Check(Saves.SavesSystem.Raw.Contains("\"shopStockTokens\":\"7:0:100:1\""),"stock tokens ride the affinity save");
  AffinityManager.Load();Check(AffinityManager.GetShopStockTokens("shop")=="7:0:100:1","stock tokens survive reload");
  string barrier="{bad}";Saves.SavesSystem.Reset(barrier);AffinityManager.Initialize();
  AffinityManager.SetShopStockTokens("shop","7:1:100:1");Check(AffinityManager.GetShopStockTokens("shop")=="","write barrier blocks stock writes");
 }
}
