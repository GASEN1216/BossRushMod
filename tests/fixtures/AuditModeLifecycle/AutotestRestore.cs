using System;
using System.Collections.Generic;
namespace Saves { public static class SavesSystem {public static int CurrentSlot=1;public static bool IsSaving, FailPhysical, FailKey;public static string Key="snapshot";public static T Load<T>(string key){return (T)(object)Key;} public static void Save<T>(string k,T v){if(FailKey)throw new Exception("write failure");Key=(string)(object)v;}public static void SaveFile(bool flag){if(FailPhysical){FailKey=true;throw new Exception("physical failure");}}} }
public class Item {public void Save(string key){BossRush.RestoreProbe.Saved++;}}
public class Inventory {public void Save(string key){BossRush.RestoreProbe.Saved++;}}
public class CharacterMainControl {public static CharacterMainControl Main=new CharacterMainControl();public Item CharacterItem=new Item();}
public class PlayerStorage {public static Inventory Inventory=new Inventory();}
public class PlayerStorageBuffer {public static PlayerStorageBuffer Instance=new PlayerStorageBuffer();public static void SaveBuffer(){BossRush.RestoreProbe.Saved++;}}
namespace BossRush {
 public static class ModBehaviour {public static void DevLog(string s){}}
 public partial class RestoreProbe {
  private const string AutotestSnapshotKey="snapshot";
  private class AutotestSnapshot {public int Slot=1;public bool BufferCountsIncluded=true;public Dictionary<int,int> Items=new Dictionary<int,int>{{42,2}};}
  private static readonly int[] AutotestLedgerTypeIds={42};
  public static int Reclaimed,Saved,Count=2;public static bool Gate=true, CountFailed;
  private static bool AutotestWriteAllowed(out string reason){reason=null;return Gate;}
  private static string ReclaimAutotestItems(AutotestSnapshot s){Reclaimed++;return "ledger";}
  private static int CountOwnedItems(int type,out string where){where=CountFailed?"pack:error/storage:0":"pack:0/storage:0";return Count;}
  private static int CountBufferedItems(int type){return 0;}
  public bool Reclaim(int slot){string detail;return TryReclaimAutotestItems(new AutotestSnapshot{Slot=slot},out detail);}
  public bool Clear(){return ClearAutotestSnapshotKey();}
 }
 static class Program {
  static void Check(bool b,string s){if(!b)throw new Exception(s);Console.WriteLine("PASS "+s);}
  static void Main(){var p=new RestoreProbe();
   Check(!p.Reclaim(2)&&RestoreProbe.Reclaimed==0,"wrong slot rejects before destructive reclaim");
   Saves.SavesSystem.IsSaving=true;Check(!p.Reclaim(1)&&RestoreProbe.Reclaimed==0,"saving rejects before reclaim");Saves.SavesSystem.IsSaving=false;
   RestoreProbe.Count=1;Check(p.Reclaim(1)&&RestoreProbe.Saved==3,"consumed original items keep fewer-not-refilled contract");
   RestoreProbe.Saved=0;RestoreProbe.Count=3;Check(!p.Reclaim(1)&&RestoreProbe.Saved==0,"remaining excess keeps recovery key");
   RestoreProbe.Count=0;RestoreProbe.CountFailed=true;Check(!p.Reclaim(1)&&RestoreProbe.Saved==0,"failed count cannot masquerade as shortage");
   Saves.SavesSystem.FailPhysical=true;Check(!p.Clear(),"physical and compensating key write failures do not escape");
  }
 }
}
