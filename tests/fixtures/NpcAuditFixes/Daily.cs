using System; using System.Collections.Generic; using System.Text.Json; using BossRush;
namespace UnityEngine { static class Time { public static float realtimeSinceStartup; public static int frameCount=1; } }
class GameClock { public static GameClock Instance=new GameClock(); public float clockTimeScale=60; }
class LevelManager { public static LevelManager Instance=new LevelManager(); public bool IsBaseLevel=true; }
namespace Saves { static class SavesSystem {
 public static event Action OnCollectSaveData,OnSetFile,OnSaveDeleted;
 public static int CurrentSlot=1; public static bool IsSaving,FailSave,FailPhysical;
 public static Dictionary<string,object> Cache=new Dictionary<string,object>(),Disk=new Dictionary<string,object>();
 public static int Saves,Physical;
 public static bool KeyExisits(string k){return Cache.ContainsKey(k);}
 public static T Load<T>(string k){return (T)Cache[k];}
 public static void Save<T>(string k,T v){Saves++;if(FailSave)throw new InvalidOperationException("injected key write failure");Cache[k]=v;}
 public static void SaveFile(bool timestamp){Physical++;if(FailPhysical)throw new InvalidOperationException("injected disk write failure");Disk=new Dictionary<string,object>(Cache);}
 public static void Reset(string raw){Cache=new Dictionary<string,object>();Disk=new Dictionary<string,object>();Cache[DailyReportTuning.StorageKey]=raw;Disk[DailyReportTuning.StorageKey]=raw;FailSave=FailPhysical=IsSaving=false;Saves=Physical=0;}
} }
namespace Duckov.Economy { class EconomyManager { public class SaveData {} public static EconomyManager Instance=new EconomyManager(); public object GenerateSaveData(){return new SaveData();} } }
namespace BossRush {
 class ModBehaviour {public static bool DevModeEnabled=false; public static void DevLog(string s){} }
 static class L10n {public static string T(string cn,string en){return cn;} }
 static class DailyReportRewards {public static int Grants;public static bool TryGrantMilestone(int q,long seed,int day,int slot,out string reason){Grants++;reason=null;return true;}public static bool TryGrantBountyCash(long amount,out string reason){reason=null;return true;} }
}
class Program {
 static int checks;static void Check(bool ok,string name){checks++;if(!ok)throw new Exception(name);}
 static void Reset(string raw){DailyReportService.ResetStaticCaches();DailyReportSaveCoordinator.ResetStaticCaches();BossRushSaveFileThrottle.ResetStaticCaches();DailyReportRewards.Grants=0;Saves.SavesSystem.Reset(raw);UnityEngine.Time.frameCount++;}
 static void Main(){
  foreach(bool keyFailure in new[]{true,false}){var d=DailyReportCodec.CreateDefault();d.BountySeed=99;string raw=DailyReportCodec.Encode(d);Reset(raw);Saves.SavesSystem.FailSave=keyFailure;Saves.SavesSystem.FailPhysical=!keyFailure;
   var result=DailyReportService.SignInAndClaim();Check(result.Outcome==DailyReportSignInOutcome.PersistBlocked,"known write failure reported blocked");Check(DailyReportRewards.Grants==0,"failure cannot grant rewards");}
  foreach(string key in new[]{"bountyCompleted","bountyRewardClaimed"}){var d=DailyReportCodec.CreateDefault();d.BountyCompleted=true;d.BountyRewardClaimed=true;d.BountyCashReward=800;string raw=DailyReportCodec.Encode(d).Replace("\""+key+"\":true","\""+key+"\":\"bad\"");Reset(raw);var loaded=DailyReportPersistence.Current;Check(DailyReportPersistence.HasWriteBarrier,"invalid declared flag creates barrier");Check(!DailyReportPersistence.Store(d),"broken snapshot cannot be rewritten");Check((string)Saves.SavesSystem.Cache[DailyReportTuning.StorageKey]==raw,"raw invalid snapshot preserved");}
  var fresh=DailyReportCodec.CreateDefault();fresh.BountySeed=99;Reset(DailyReportCodec.Encode(fresh));Saves.SavesSystem.IsSaving=true;DailyReportSignInResult deferred;Check(DailyReportService.TrySignInToday(out deferred)&&deferred.Outcome==DailyReportSignInOutcome.Success,"normal deferred write remains accepted");
  Check(DailyReportCodec.Decode("{\"schemaVersion\":1}")!=null,"missing optional old bounty flags accepted");Console.WriteLine("Daily "+checks+" PASS");
 }
}
