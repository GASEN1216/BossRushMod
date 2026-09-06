using System;
using System.Collections.Generic;
using BossRush;
namespace UnityEngine { struct Color { public Color(float r,float g,float b,float a){} } static class Time { public static float time; } }
enum Teams { player,wolf }
class LevelManager { public static LevelManager Instance=new LevelManager(); public bool IsBaseLevel; }
class CharacterRandomPreset { public string nameKey; }
class CharacterMainControl { public bool IsMainCharacter,isBossCharacter;public Teams Team;public CharacterRandomPreset characterPreset; public T GetComponent<T>() where T:class {return null;} public int GetInstanceID(){return GetHashCode();} }
class Health { public bool IsDead,IsMainCharacterHealth;public CharacterMainControl Character;public CharacterMainControl TryGetCharacter(){return Character;}public int GetInstanceID(){return GetHashCode();} }
struct DamageInfo { public CharacterMainControl fromCharacter;public float finalDamage; }
namespace BossRush {
 enum ZombieModeBossKind { Titan,Hunter,Splitter,Shielder,Corruptor }
 class ZombieModeEnemyRuntimeMarker {public bool IsBoss;public ZombieModeBossKind BossKind;}
 class EnemyPresetInfo {public string name,displayName;}
 class ModBehaviour {public static ModBehaviour Instance=new ModBehaviour();public bool IsZombieModeActive,IsModeDActive,IsModeEActive,IsModeFActive,IsActive,IsBossRushArenaActive;internal static void DevLog(string s){}internal bool IsCodexConfiguredEnabled(){return true;}internal bool IsCodexInfiniteHellActive(){return false;}internal static bool IsModeHRunInProgressSafe(){return false;}internal static bool IsModeGRunInProgressSafe(){return false;}internal List<EnemyPresetInfo> GetFilteredEnemyPresets(){return new List<EnemyPresetInfo>();}}
 static class DragonDescendantConfig { internal const string BOSS_NAME_KEY="descendant",BOSS_NAME_CN="descendant",BOSS_NAME_EN="descendant"; }
 static class DragonKingConfig {internal const string BossNameKey="king",BossNameCN="king",BossNameEN="king";}
 static class PhantomWitchConfig {internal const string BossNameKey="witch",BossNameCN="witch",BossNameEN="witch";}
 static class L10n {internal static string T(string a,string b=null){return a;}}
 static class LocalizationHelper {internal static string GetLocalizedText(string s){return s;}}
 static class PetNestCompanionAgent {internal static bool IsCompanionHealth(Health h){return false;}}
 static class CodexPersistence {internal static CodexData Current=new CodexData();internal static bool Store(CodexData d){Current=d;return true;}}
 static class CodexSaveCoordinator {internal static void RequestFlush(){}}
 static class BossRushAchievementManager {internal static HashSet<string> Unlocked=new HashSet<string>();internal static void Initialize(){}internal static bool TryUnlock(string id){return Unlocked.Add(id);}}
}

