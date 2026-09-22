using System;
using System.Collections.Generic;
using BossRush;
namespace UnityEngine { struct Color { public Color(float r,float g,float b,float a){} } static class Time { public static float time; } }
enum Teams { player,wolf,scav,usec,bear,lab,middle,all }
// 与官方 Team.IsEnemy 的真实阵营判据一致。
static class Team { public static bool IsEnemy(Teams selfTeam, Teams targetTeam) { return selfTeam != Teams.middle && (selfTeam == Teams.all || (targetTeam != Teams.middle && selfTeam != targetTeam)); } }
class LevelManager { public static LevelManager Instance=new LevelManager(); public bool IsBaseLevel; }
class CharacterRandomPreset { public string nameKey; }
class CharacterMainControl { public bool IsMainCharacter,isBossCharacter;public Teams Team;public CharacterRandomPreset characterPreset; public object Component;public T GetComponent<T>() where T:class {return Component as T;} public int GetInstanceID(){return GetHashCode();} }
class Health { public bool IsDead,IsMainCharacterHealth,IsCompanion;public CharacterMainControl Character;public CharacterMainControl TryGetCharacter(){return Character;}public int GetInstanceID(){return GetHashCode();} }
struct DamageInfo { public CharacterMainControl fromCharacter;public float finalDamage; }
namespace BossRush {
 enum ZombieModeBossKind { Titan,Hunter,Splitter,Shielder,Corruptor }
 class ZombieModeEnemyRuntimeMarker {public bool IsBoss;public ZombieModeBossKind BossKind;}
 class EnemyPresetInfo {public string name,displayName;}
 class ModBehaviour {public static ModBehaviour Instance=new ModBehaviour();public bool IsZombieModeActive,IsModeDActive,IsModeEActive,IsModeFActive,IsActive,IsBossRushArenaActive;internal static void DevLog(string s){}internal static void CriticalLog(string id,string s){CriticalLogs.Add(id);}internal static readonly List<string> CriticalLogs=new List<string>();internal bool IsCodexConfiguredEnabled(){return true;}internal bool IsCodexInfiniteHellActive(){return false;}internal static bool ModeHRunning;internal static bool IsModeHRunInProgressSafe(){return ModeHRunning;}internal static bool IsModeGRunInProgressSafe(){return false;}internal List<EnemyPresetInfo> Pool=new List<EnemyPresetInfo>();internal List<EnemyPresetInfo> GetFilteredEnemyPresets(){return Pool;}}
 static class DragonDescendantConfig { internal const string BOSS_NAME_KEY="descendant",BOSS_NAME_CN="descendant",BOSS_NAME_EN="descendant"; }
 static class DragonKingConfig {internal const string BossNameKey="king",BossNameCN="king",BossNameEN="king";}
 static class PhantomWitchConfig {internal const string BossNameKey="witch",BossNameCN="witch",BossNameEN="witch";}
 static class L10n {internal static bool IsChinese=true;internal static string T(string a,string b=null){return IsChinese || b==null ? a:b;}}
 // 2026-09-20：初见场景记录经它取场景 id。夹具只验采集链路，场景解析是 Unity 侧行为。
 static class CodexSceneNames {internal static string Captured="Level_GroundZero_Main";internal static string Capture(){return Captured;}internal static string Resolve(string id){return string.IsNullOrEmpty(id)?null:("map:"+id);}}
 // 2026-09-20 第三轮：官方 Boss 名单补目录。读**仓库里那份真实 JSON**，
 // 让夹具验证的是生产数据而不是另抄一份常量表。
 static class JsonDataRegistry
 {
     // 从可执行目录逐级上溯找仓库根（带 Assets/Data/CodexOfficialBosses.json 的那一层），
     // 不写死相对层数：dotnet 的输出目录层级随 SDK 版本变动过。
     internal static readonly string Root = Locate();
     static string Locate()
     {
         var dir = new System.IO.DirectoryInfo(System.AppContext.BaseDirectory);
         while (dir != null)
         {
             string candidate = System.IO.Path.Combine(dir.FullName, "Assets", "Data");
             if (System.IO.File.Exists(System.IO.Path.Combine(candidate, "CodexOfficialBosses.json"))) return candidate;
             dir = dir.Parent;
         }
         return null;
     }
     public static bool TryReadDataFile(string fileName, out string json)
     {
         json = null;
         if (Root == null) return false;
         string path = System.IO.Path.Combine(Root, fileName);
         if (!System.IO.File.Exists(path)) return false;
         json = System.IO.File.ReadAllText(path);
         return true;
     }
 }
 static class LocalizationHelper {internal static readonly Dictionary<string,string> Text=new Dictionary<string,string>();internal static string GetLocalizedText(string s){string value;return Text.TryGetValue(s,out value)?value:s;}}
 static class PetNestCompanionAgent {internal static bool IsCompanionHealth(Health h){return h.IsCompanion;}}
 static class CodexPersistence
 {
     internal static CodexData Current=new CodexData();
     internal static bool HasWriteBarrier,IsStoreFaulted,RejectStore;
     internal static int StoreCalls;
     internal static string SavedJson;
     internal static bool Store(CodexData d)
     {
         StoreCalls++;
         if(HasWriteBarrier||IsStoreFaulted||RejectStore)return false;
         string json=CodexCodec.Encode(d);
         if(CodexCodec.Decode(json)==null)throw new Exception("Codec rejected production candidate");
         Current=d;SavedJson=json;return true;
     }
 }
 static class CodexSaveCoordinator {internal static void RequestFlush(){}}
 static class BossRushAchievementManager {internal static HashSet<string> Unlocked=new HashSet<string>();internal static void Initialize(){}internal static bool TryUnlock(string id){return Unlocked.Add(id);}}
}
