
using System;using System.Collections.Generic;using System.Linq;
namespace UnityEngine {
 public struct Vector3 {public float x,y,z;public Vector3(float a,float b,float c){x=a;y=b;z=c;}public static Vector3 up=>new Vector3(0,1,0);public Vector3 normalized=>this;public static Vector3 operator +(Vector3 a,Vector3 b)=>new Vector3(a.x+b.x,a.y+b.y,a.z+b.z);public static Vector3 operator *(Vector3 a,float b)=>new Vector3(a.x*b,a.y*b,a.z*b);}
 public class Transform{public Vector3 position;}public class GameObject{public Transform transform=new Transform();}
 public static class Debug{public static void LogWarning(string s){}}
 public static class Random{public static int Calls;public static bool MoveLastToTwenty=true;public static int Range(int a,int b){return MoveLastToTwenty && Calls++==0?20:b-1;}public static Vector3 insideUnitSphere=>new Vector3(1,0,0);}
}
class CharacterMainControl {public static CharacterMainControl Main;public UnityEngine.Transform transform=new UnityEngine.Transform();}
namespace BossRush {
 class EnemyPresetInfo{public string name;public bool Enabled=true;}
 partial class BossFilterRuntimeModule{internal ModBehaviour owner;private bool _filteredPresetsCacheDirty=true;private List<EnemyPresetInfo> _filteredPresetsCache;private bool IsBossEnabled(string name)=>owner.BossFilterEnemyPresets.First(x=>x.name==name).Enabled;}
 class RuntimeCache{public void NotifyEnemyPresetsRefreshed(){}public void InvalidateBossCatalogCache(){}public void InvalidateLineageCatalogCache(){}} class ModBehaviour{public RuntimeCache PetNestRuntime, CodexRuntime;public List<EnemyPresetInfo> BossFilterEnemyPresets=new List<EnemyPresetInfo>();public BossFilterRuntimeModule Filter;public bool IsActive;public UnityEngine.GameObject ArenaRewardSignGameObject;public ModBehaviour(){Filter=new BossFilterRuntimeModule{owner=this};}public List<EnemyPresetInfo> GetFilteredEnemyPresets()=>Filter.GetFilteredEnemyPresets();public void InvalidateFilteredPresetsCache()=>Filter.InvalidateFilteredPresetsCache();public static void DevLog(string s){}public static UnityEngine.Vector3 GetCurrentSceneDefaultPosition()=>new UnityEngine.Vector3();public void ShowMessage(string s){}public void ClearEnemiesForBossRushForArena(){}public void BeginAchievementSessionForArena(string s){}public void SetBossRushRuntimeActiveForArena(bool value){IsActive=value;}public void TryRollMutatorsForArena(string s){}public void SubscribeArenaBossDeaths(){}public void LogLootWarningLimitedForArena(string k,string m,Exception e){}}
 static class L10n{public static string T(string cn,string en)=>cn;}
 partial class WavesArenaRuntimeModule{internal ModBehaviour owner;public WavesArenaRuntimeModule(ModBehaviour o){owner=o;}public List<EnemyPresetInfo> EnemyPresets=>owner.BossFilterEnemyPresets;public bool InfiniteHellMode;public UnityEngine.Vector3 DemoChallengeStartPosition;public int BossesPerWave=1,CurrentEnemyIndex,DefeatedEnemies,TotalEnemies,BossesInCurrentWaveTotal,BossesInCurrentWaveRemaining;public List<object> CurrentWaveBosses=new List<object>();public Dictionary<object,object> bossSpawnTimes=new Dictionary<object,object>(),bossOriginalLootCounts=new Dictionary<object,object>();public HashSet<object> CountedDeadBosses=new HashSet<object>();public EnemyPresetInfo Spawned;public static void ResetMilestones(ModBehaviour o){}public void SpawnNextEnemy(){Spawned=owner.GetFilteredEnemyPresets()[0];}}

 internal static class Program {
  private static int checks;
  private static void Check(bool value,string reason){checks++;if(!value)throw new Exception(reason);}
  private static ModBehaviour Mixed(bool infinite=false){
   var host=new ModBehaviour();
   for(int i=0;i<20;i++)host.BossFilterEnemyPresets.Add(new EnemyPresetInfo{name="disabled"+i,Enabled=false});
   for(int i=0;i<22;i++)host.BossFilterEnemyPresets.Add(new EnemyPresetInfo{name="normal"+i});
   host.BossFilterEnemyPresets.Add(new EnemyPresetInfo{name="boss_dragonking"});
   return host;
  }
  private static void Main(){
   UnityEngine.Random.Calls=0;
   var host=Mixed();var original=host.BossFilterEnemyPresets.ToArray();
   var originalCache=host.GetFilteredEnemyPresets();
   var arena=new WavesArenaRuntimeModule(host);arena.StartFirstWave();
   var pool=host.GetFilteredEnemyPresets();
   Check(pool.Take(20).All(p=>!WavesArenaRuntimeModule.EarlyWaveExcludedBosses.Contains(p.name)),"disabled entries must not let dragon enter first 20 actual spawns");
   Check(arena.Spawned.name!="boss_dragonking","first wave must use the reordered enabled pool");
   Check(pool.Count==23&&pool.Count(p=>p.name=="boss_dragonking")==1,"enabled pool contents must be conserved");
   Check(!Object.ReferenceEquals(pool,originalCache),"shuffle must invalidate the preloaded filter cache");
   Check(host.BossFilterEnemyPresets.Count==original.Length&&original.All(p=>host.BossFilterEnemyPresets.Count(q=>Object.ReferenceEquals(p,q))==1),"raw pool references must not duplicate or disappear");
   Check(Enumerable.Range(0,20).All(i=>Object.ReferenceEquals(original[i],host.BossFilterEnemyPresets[i])),"disabled slots must not be used for replacement");
   UnityEngine.Random.Calls=0;host=Mixed();arena=new WavesArenaRuntimeModule(host){InfiniteHellMode=true};arena.StartFirstWave();
   Check(arena.Spawned.name=="boss_dragonking","infinite mode must retain unrestricted shuffled order");
   UnityEngine.Random.MoveLastToTwenty=false;
   host=new ModBehaviour();host.BossFilterEnemyPresets.Add(new EnemyPresetInfo{name="DragonDescendant"});
   host.BossFilterEnemyPresets.Add(new EnemyPresetInfo{name="boss_dragonking"});
   host.BossFilterEnemyPresets.Add(new EnemyPresetInfo{name="Cname_StormBoss1"});
   for(int i=0;i<25;i++)host.BossFilterEnemyPresets.Add(new EnemyPresetInfo{name="normal"+i});
   arena=new WavesArenaRuntimeModule(host);arena.StartFirstWave();pool=host.GetFilteredEnemyPresets();
   Check(pool.Take(20).All(p=>!WavesArenaRuntimeModule.EarlyWaveExcludedBosses.Contains(p.name)),"all strong categories must move behind 20 ordinary enabled bosses");
   Check(pool.Count==28&&pool.Count(p=>WavesArenaRuntimeModule.EarlyWaveExcludedBosses.Contains(p.name))==3,"multiple swaps must preserve every strong boss");
   var order=host.BossFilterEnemyPresets.ToArray();arena.StartFirstWave();
   Check(order.SequenceEqual(host.BossFilterEnemyPresets),"repeated start during active run must not reshuffle");
   host=new ModBehaviour();host.BossFilterEnemyPresets.Add(new EnemyPresetInfo{name="boss_dragonking"});for(int i=0;i<21;i++)host.BossFilterEnemyPresets.Add(new EnemyPresetInfo{name="DragonDescendant"});
   arena=new WavesArenaRuntimeModule(host);arena.StartFirstWave();
   Check(host.GetFilteredEnemyPresets().Count==22,"no replacement available must preserve the full pool");
   // 玩家反馈 2026-10-15：弹指可灭首波出幽灵女巫；Boss 池不足 20 个时也要把强力 Boss 压到最后。
   Check(WavesArenaRuntimeModule.EarlyWaveExcludedBosses.Contains("boss_phantomwitch"),"phantom witch must count as an early-wave strong boss");
   host=new ModBehaviour();host.BossFilterEnemyPresets.Add(new EnemyPresetInfo{name="boss_phantomwitch"});host.BossFilterEnemyPresets.Add(new EnemyPresetInfo{name="boss_dragonking"});
   for(int i=0;i<10;i++)host.BossFilterEnemyPresets.Add(new EnemyPresetInfo{name="normal"+i});
   arena=new WavesArenaRuntimeModule(host);arena.StartFirstWave();pool=host.GetFilteredEnemyPresets();
   Check(pool.Count==12&&pool.Take(10).All(p=>!WavesArenaRuntimeModule.EarlyWaveExcludedBosses.Contains(p.name))&&arena.Spawned.name.StartsWith("normal"),"small pool must still keep strong bosses behind every ordinary boss");
   host=new ModBehaviour();host.BossFilterEnemyPresets.Add(new EnemyPresetInfo{name=SandstormChampionBoss.NameKey});
   for(int i=0;i<5;i++)host.BossFilterEnemyPresets.Add(new EnemyPresetInfo{name="normal"+i});
   arena=new WavesArenaRuntimeModule(host);arena.StartFirstWave();pool=host.GetFilteredEnemyPresets();
   Check(pool.Take(5).All(p=>p.name!=SandstormChampionBoss.NameKey)&&pool.Last().name==SandstormChampionBoss.NameKey,
       "champion follows ordinary enabled bosses even in a small pool");
   Console.WriteLine("WaveOrdering: "+checks+" assertions PASS");
  }
 }
}

namespace BossRush
{
 internal static class SandstormChampionBoss { internal const string NameKey = "BossRush_Campaign_FinalBoss_Name"; }
}
