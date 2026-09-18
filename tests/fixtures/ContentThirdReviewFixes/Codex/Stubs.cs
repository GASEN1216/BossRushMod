using System;
using System.Collections.Generic;
using BossRush;
namespace UnityEngine { struct Color { public Color(float r,float g,float b,float a){} } static class Time { public static float time; } }
enum Teams { player,wolf }
class LevelManager { public static LevelManager Instance=new LevelManager(); public bool IsBaseLevel; }
class CharacterRandomPreset { public string nameKey; }
class CharacterMainControl { public bool IsMainCharacter,isBossCharacter;public Teams Team;public CharacterRandomPreset characterPreset; public object Component;public T GetComponent<T>() where T:class {return Component as T;} public int GetInstanceID(){return GetHashCode();} }
class Health { public bool IsDead,IsMainCharacterHealth,IsCompanion;public CharacterMainControl Character;public CharacterMainControl TryGetCharacter(){return Character;}public int GetInstanceID(){return GetHashCode();} }
struct DamageInfo { public CharacterMainControl fromCharacter;public float finalDamage; }
namespace BossRush {
 enum ZombieModeBossKind { Titan,Hunter,Splitter,Shielder,Corruptor }
 class ZombieModeEnemyRuntimeMarker {public bool IsBoss;public ZombieModeBossKind BossKind;}
 class EnemyPresetInfo {public string name,displayName;}
 class ModBehaviour {public static ModBehaviour Instance=new ModBehaviour();public bool IsZombieModeActive,IsModeDActive,IsModeEActive,IsModeFActive,IsActive,IsBossRushArenaActive;internal static void DevLog(string s){}internal bool IsCodexConfiguredEnabled(){return true;}internal bool IsCodexInfiniteHellActive(){return false;}internal static bool ModeHRunning;internal static bool IsModeHRunInProgressSafe(){return ModeHRunning;}internal static bool IsModeGRunInProgressSafe(){return false;}internal List<EnemyPresetInfo> Pool=new List<EnemyPresetInfo>();internal List<EnemyPresetInfo> GetFilteredEnemyPresets(){return Pool;}}
 static class DragonDescendantConfig { internal const string BOSS_NAME_KEY="descendant",BOSS_NAME_CN="descendant",BOSS_NAME_EN="descendant"; }
 static class DragonKingConfig {internal const string BossNameKey="king",BossNameCN="king",BossNameEN="king";}
 static class PhantomWitchConfig {internal const string BossNameKey="witch",BossNameCN="witch",BossNameEN="witch";}
 static class L10n {internal static bool IsChinese=true;internal static string T(string a,string b=null){return IsChinese || b==null ? a:b;}}
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

