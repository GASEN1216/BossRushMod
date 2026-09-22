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
  AffinityManager.AddPoints("npc",-10);Saves.SavesSystem.Collect();Check(Saves.SavesSystem.Writes==1,"valid snapshot remains writable");
  Saves.SavesSystem.Reset(null);AffinityManager.Initialize();AffinityManager.SetPoints("new",50);Saves.SavesSystem.Collect();Check(Saves.SavesSystem.Writes==1,"missing key is a fresh writable slot");
  Saves.SavesSystem.Reset("bad");AffinityManager.Initialize();Saves.SavesSystem.Raw=legacy;AffinityManager.Load();Check(AffinityManager.CanWrite && AffinityManager.GetPoints("npc")==2300,"successful reload lifts barrier");
  Console.WriteLine("Affinity "+checks+" PASS");
 }
}
