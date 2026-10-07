using System;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using Cysharp.Threading.Tasks;

namespace BossRush
{
    internal static class Program
    {
        private static int assertions;
        private static void Check(bool value,string message) { assertions++;if(!value)throw new Exception(message); }
        private sealed class Owner
        {
            internal readonly ModBehaviour Host=new ModBehaviour();
            internal readonly WavesArenaRuntimeModule Runtime;
            internal CharacterMainControl Special;
            internal bool HasConfig=true,BlocksBullets;
            internal Owner()
            {
                Runtime=new WavesArenaRuntimeModule(Host);
                Runtime.BindLegacySpawnServices(p=>p.name=="descendant",p=>p.name=="king",p=>p.name=="witch",
                    (position,child,notify,active)=>{Check(!child&&!notify,"descendant options remain false");return SpecialSpawn(active);},
                    (position,notify,active)=>{Check(!notify,"king notification remains false");return SpecialSpawn(active);},
                    (position,notify,active)=>{Check(!notify,"witch notification remains false");return SpecialSpawn(active);},
                    c=>Probe.Events.Add("multiplier"));
                Runtime.BindLootBoxPolicies(()=>HasConfig,()=>BlocksBullets);
            }
            private UniTask<CharacterMainControl> SpecialSpawn(Func<bool> active)
            { Probe.Events.Add("special");return new UniTask<CharacterMainControl>(Task.FromResult(Special)); }
        }
        private static void Reset()
        {
            UnityEngine.Object.All.Clear();UnityEngine.Object.SceneScans=0;UnityEngine.Object.ResourceScans=0;Probe.Events.Clear();
            SceneManager.Name="arena";ModBehaviour.Map=null;
            WavesArenaRuntimeModule.CharacterCache.Clear();WavesArenaRuntimeModule.PrepareSceneCharacterCacheForLoad();
            WavesArenaRuntimeModule.CachedLootBoxTemplateWithLoader=null;WavesArenaRuntimeModule.CachedDifficultyRewardLootBoxTemplate=null;
            CharacterMainControl.Main=new CharacterMainControl("player");
        }
        private static CharacterRandomPreset Preset(string key,CharacterMainControl result)
        { return new CharacterRandomPreset { name=key,nameKey=key,DisplayName=key,showName=true,team=Teams.wolf,Factory=()=>Task.FromResult(result) }; }
        private static EnemyPresetInfo Info(string key) { return new EnemyPresetInfo{name=key,displayName=key,team=(int)Teams.wolf}; }
        private static async Task Run()
        {
            Reset();var owner=new Owner();var enemy=new CharacterMainControl("enemy");var preset=Preset("daxing",enemy);
            enemy.characterPreset=preset;var ai=enemy.gameObject.Add(new AICharacterController());ObjectCache.Presets=new[]{preset};
            owner.Runtime.InfiniteHellMode=true;owner.Runtime.InfiniteHellWaveIndex=5;owner.Runtime.BossesPerWave=2;
            var actual=await owner.Runtime.SpawnEnemyAtPositionAsync(Info("daxing"),Vector3.zero,()=>true);
            Check(actual==enemy&&preset.Calls==1&&enemy.Team==Teams.wolf,"exact preset factory and neutral-team safety net");
            Check(owner.Runtime.CurrentBoss==enemy&&owner.Runtime.CurrentWaveBosses.Single()==enemy&&owner.Runtime.OwnedDaXingXing.Contains(enemy),"ordinary spawn records current, wave and owned identities");
            Check(Math.Abs(enemy.Health.CurrentHealth-110)<.001f&&Math.Abs(enemy.CharacterItem.GetStat("GunDamageMultiplier").BaseValue-110)<.001f&&Math.Abs(enemy.CharacterItem.GetStat("MeleeDamageMultiplier").BaseValue-110)<.001f,"infinite scaling preserves health and both damage stats");
            Check(string.Join(",",Probe.Events)=="factory,team,multiplier,active,wake,mutator,loot,target,notice,coroutine,anchor","ordinary spawn preserves activation mutator loot aggro and recovery order");
            Check(ai.noticed&&ai.forceTracePlayerDistance==500&&ai.searchedEnemy==CharacterMainControl.Main.mainDamageReceiver,"ordinary spawn restores full player aggro");

            foreach(var key in new[]{"descendant","king","witch"})
            {
                Reset();owner=new Owner {Special=new CharacterMainControl(key)};
                var special=await owner.Runtime.SpawnEnemyAtPositionAsync(Info(key),Vector3.zero,()=>true);
                Check(special==owner.Special&&string.Join(",",Probe.Events)=="special,mutator"&&owner.Runtime.CurrentBoss==null,"special branch applies mutator before early return: "+key);
            }

            Reset();owner=new Owner();enemy=new CharacterMainControl("companion"){Companion=true};preset=Preset("companion",enemy);ObjectCache.Presets=new[]{preset};
            await owner.Runtime.SpawnEnemyAtPositionAsync(Info("companion"),Vector3.zero);
            Check(enemy.Team==Teams.middle,"companion remains exempt from hostile team rewrite");

            Reset();owner=new Owner();enemy=new CharacterMainControl("fallback");var wrong=Preset("wrong",new CharacterMainControl("wrong"));wrong.showName=false;
            preset=Preset("fallback",enemy);ObjectCache.Presets=new[]{wrong,preset};
            Check(await owner.Runtime.SpawnEnemyAtPositionAsync(Info("missing"),Vector3.zero)==enemy&&wrong.Calls==0,"missing exact preset falls back to named same-team preset");

            Reset();owner=new Owner();enemy=new CharacterMainControl("late");preset=Preset("late",enemy);var gate=new TaskCompletionSource<CharacterMainControl>();preset.Factory=()=>gate.Task;ObjectCache.Presets=new[]{preset};
            bool active=true;var pending=owner.Runtime.SpawnEnemyAtPositionAsync(Info("late"),Vector3.zero,()=>active).AsTask();
            Check(!pending.IsCompleted&&preset.Calls==1,"official factory remains asynchronously awaited");
            active=false;owner.Runtime.OnDestroy();UnityEngine.Object.Destroy(owner.Host.gameObject);gate.SetResult(enemy);
            Check(await pending==null&&enemy==null&&!Probe.Events.Contains("active"),"late factory result after real module teardown is destroyed before postprocessing");
            Check(await owner.Runtime.SpawnEnemyAtPositionAsync(Info("late"),Vector3.zero,()=>false)==null&&preset.Calls==1,"inactive request returns before factory work");

            Reset();owner=new Owner();enemy=new CharacterMainControl("fault");preset=Preset("fault",enemy);preset.Factory=()=>throw new Exception("factory");ObjectCache.Presets=new[]{preset};
            Check(await owner.Runtime.SpawnEnemyAtPositionAsync(Info("fault"),Vector3.zero)==null,"factory exceptions retain null failure behavior");

            Reset();owner=new Owner();var own=new CharacterMainControl("own");var companion=new CharacterMainControl("pet"){Companion=true};var modeG=new CharacterMainControl("G"){ModeGOwned=true};
            var foreign=new CharacterMainControl("foreign");var distant=new CharacterMainControl("outside");distant.transform.position=new Vector3(501,0,0);
            foreach(var c in new[]{own,companion,modeG,foreign,distant,CharacterMainControl.Main}) c.characterPreset=Preset("daxing",c);
            owner.Runtime.OwnedDaXingXing.Add(own);WavesArenaRuntimeModule.SetArenaCenterFromSign(Vector3.zero);
            owner.Runtime.TryCleanNonBossRushDaXingXing();
            Check(foreign==null&&own!=null&&companion!=null&&modeG!=null&&distant!=null&&CharacterMainControl.Main!=null,"cleanup preserves owned companion ModeG player and out-of-range identities");
            Check(UnityEngine.Object.SceneScans==1,"first cleanup refreshes dirty character cache once");
            owner.Runtime.TryCleanNonBossRushDaXingXing();Check(UnityEngine.Object.SceneScans==1,"non-demo cleanup reuses cache");
            SceneManager.Name="Level_DemoChallenge_1";WavesArenaRuntimeModule.PrepareSceneCharacterCacheForLoad();owner.Runtime.TryCleanNonBossRushDaXingXing();int scans=UnityEngine.Object.SceneScans;
            for(int i=0;i<19;i++)owner.Runtime.TryCleanNonBossRushDaXingXing();Check(UnityEngine.Object.SceneScans==scans,"demo refresh waits full ten seconds");
            owner.Runtime.TryCleanNonBossRushDaXingXing();Check(UnityEngine.Object.SceneScans==scans+1,"demo refresh occurs at ten-second boundary");
            var other=new Owner();Check(!other.Runtime.OwnedDaXingXing.Contains(own)&&WavesArenaRuntimeModule.CharacterCache.Contains(own),"owned registry is per module while scene cache retains original static lifetime");
            WavesArenaRuntimeModule.PrepareSceneCharacterCacheForLoad();Check(WavesArenaRuntimeModule.CharacterCacheNeedsRefresh&&!WavesArenaRuntimeModule.ArenaCenterSet,"scene preparation invalidates cache and arena center");
            ModBehaviour.Map=new BossRushMapConfig {defaultSignPos=new Vector3(1,0,0),customSpawnPos=new Vector3(2,0,0),spawnPoints=new[]{new Vector3(3,0,0)}};
            owner.Runtime.SetArenaCenterFromMapConfig("arena");Check(WavesArenaRuntimeModule.ArenaCenter.x==1,"map center keeps sign priority");
            ModBehaviour.Map.defaultSignPos=null;owner.Runtime.SetArenaCenterFromMapConfig("arena");Check(WavesArenaRuntimeModule.ArenaCenter.x==2,"map center falls back to custom spawn");
            ModBehaviour.Map.customSpawnPos=null;ModBehaviour.Map.spawnPoints=new[]{new Vector3(4,0,0),new Vector3(8,0,0)};owner.Runtime.SetArenaCenterFromMapConfig("arena");Check(WavesArenaRuntimeModule.ArenaCenter.x==6,"map center finally averages spawn points");

            Reset();owner=new Owner();var fallback=new InteractableLootbox("fallback");var loader=new InteractableLootbox("loader");loader.gameObject.Add(new Duckov.Utilities.LootBoxLoader());
            Check(WavesArenaRuntimeModule.GetLootBoxTemplateWithLoader()==loader&&WavesArenaRuntimeModule.GetDifficultyRewardLootBoxTemplate()==loader,"loot templates prefer original loader-backed box");
            scans=UnityEngine.Object.ResourceScans;WavesArenaRuntimeModule.GetLootBoxTemplateWithLoader();WavesArenaRuntimeModule.GetDifficultyRewardLootBoxTemplate();Check(UnityEngine.Object.ResourceScans==scans,"live templates reuse cached identities");
            UnityEngine.Object.Destroy(loader.gameObject);Check(WavesArenaRuntimeModule.GetDifficultyRewardLootBoxTemplate()==fallback,"destroyed template falls back to remaining box");
            var col=fallback.gameObject.Add(new Collider());var child=fallback.gameObject.Add(new Collider());
            owner.HasConfig=false;owner.Runtime.ApplyLootBoxCoverSetting(fallback);Check(!col.isTrigger&&!child.isTrigger,"missing configuration preserves cover colliders");
            owner.HasConfig=true;owner.BlocksBullets=true;owner.Runtime.ApplyLootBoxCoverSetting(fallback);Check(!col.isTrigger,"enabled cover preserves colliders");
            owner.Runtime.ApplyLootBoxCoverSetting(fallback,true);Check(col.isTrigger&&child.isTrigger,"forced cover policy updates self and child colliders");
            TestCampaignFinalBossPreclear();
            TestRegeneration();
            Console.WriteLine("ArenaHostRemainder: PASS "+assertions+" assertions");
        }
        private static CharacterMainControl SceneEnemy(string name,Teams team=Teams.wolf)
        {
            var actor=new CharacterMainControl(name);
            actor.characterPreset=Preset(name,actor);
            actor.characterPreset.team=team;
            actor.Team=team;
            return actor;
        }
        private static void TestCampaignFinalBossPreclear()
        {
            Reset();var owner=new Owner();owner.Host.IsBossRushArenaActive=true;
            WavesArenaRuntimeModule.SetArenaCenterFromSign(Vector3.zero);
            CharacterMainControl.Main.characterPreset=Preset("player-hostile-preset",CharacterMainControl.Main);
            var companion=SceneEnemy("companion-with-hostile-preset");companion.Companion=true;
            var officialPet=SceneEnemy("official-pet",Teams.player);officialPet.gameObject.Add(new PetAI());
            var neutral=SceneEnemy("neutral",Teams.middle);
            var outside=SceneEnemy("outside");outside.transform.position=new Vector3(501,0,0);
            var ordinary=SceneEnemy("ordinary-before-stone");
            var ordinaryAi=ordinary.gameObject.Add(new AICharacterController());
            var routine=owner.Runtime.ContinuousClearEnemiesUntilWaveStart();
            Check(routine.MoveNext()&&ReferenceEquals(routine.Current,owner.Host.ArenaSharedWait05s),"ordinary arena preclear remains a yielding live coroutine");
            Check(ordinary==null&&ordinary.gameObject==null&&ordinaryAi==null,"ordinary preclear destroys the enemy game object and its character and AI components");
            Check(companion!=null&&officialPet!=null&&CharacterMainControl.Main!=null&&neutral!=null&&outside!=null,"ordinary cleanup preserves companion player neutral and distant identities");

            // 石头启动后即由终章持有场地，先于异步工厂返回和角色认领。
            // 角色没有 CurrentBoss、wave 或 owned 登记，不能靠特定角色身份白名单蒙混通过。
            owner.Host.IsCampaignFinalBossActive=true;
            var ghost=SceneEnemy("Cname_Ghost-unclaimed-staging");
            Check(owner.Runtime.CurrentBoss==null&&owner.Runtime.CurrentWaveBosses.Count==0&&!owner.Runtime.OwnedDaXingXing.Contains(ghost),"terminal factory candidate starts without an arena ownership registration");
            int scans=UnityEngine.Object.SceneScans,disables=owner.Runtime.SpawnerDisableCalls;
            Check(routine.MoveNext(),"preclear stays suspended rather than terminating when terminal dialogue starts");
            Check(ghost!=null&&ghost.gameObject!=null&&!ghost.Health.IsDead,"terminal dialogue/spawn protects an unclaimed hostile ghost from preclear");
            Check(UnityEngine.Object.SceneScans==scans&&owner.Runtime.SpawnerDisableCalls==disables,"terminal dialogue pauses scanning and spawner mutation before any maintenance work");

            var battleEnemy=SceneEnemy("terminal-battle-enemy");
            // 超过普通清场的 600 次上限，暂停帧不能耗尽恢复清场的预算。
            for(int i=0;i<605;i++)Check(routine.MoveNext(),"terminal battle keeps the same suspended routine alive: "+i);
            Check(ghost!=null&&battleEnemy!=null&&UnityEngine.Object.SceneScans==scans&&owner.Runtime.SpawnerDisableCalls==disables,"terminal battle neither scans nor deletes newly spawned enemies");
            WavesArenaRuntimeModule.CharacterCacheNeedsRefresh=true;
            owner.Runtime.ClearEnemiesForBossRush();
            Check(ghost!=null&&battleEnemy!=null&&UnityEngine.Object.SceneScans==scans,"direct clear honors terminal ownership before even refreshing a dirty character cache");

            // 成功或失败均释放同一个场地 owner；原有预清场协程随后继续工作。
            owner.Host.IsCampaignFinalBossActive=false;
            Check(routine.MoveNext()&&ghost==null&&battleEnemy==null,"releasing terminal ownership resumes the original preclear and removes stale hostile actors");
            Check(companion!=null&&officialPet!=null&&CharacterMainControl.Main!=null,"resumed preclear still exempts player and pets");
            var failureEnemy=SceneEnemy("failed-terminal-actor");
            owner.Host.IsCampaignFinalBossActive=true;scans=UnityEngine.Object.SceneScans;
            Check(routine.MoveNext()&&failureEnemy!=null&&UnityEngine.Object.SceneScans==scans,"a subsequent terminal attempt pauses maintenance again");
            owner.Host.IsCampaignFinalBossActive=false;
            Check(routine.MoveNext()&&failureEnemy==null,"terminal failure releases ownership and cleanup resumes again");
            var directEnemy=SceneEnemy("ordinary-direct-clear");WavesArenaRuntimeModule.CharacterCacheNeedsRefresh=true;
            owner.Runtime.ClearEnemiesForBossRush();
            Check(directEnemy==null&&companion!=null&&CharacterMainControl.Main!=null,"direct clear resumes its ordinary cleanup semantics after the terminal attempt");
            owner.Host.IsActive=true;
            Check(!routine.MoveNext(),"ordinary wave start still terminates preclear after campaign ownership has ended");
            owner.Host.IsActive=false;owner.Host.IsCampaignFinalBossActive=true;
            var alreadyActiveGhost=SceneEnemy("terminal-before-preclear-start");
            scans=UnityEngine.Object.SceneScans;disables=owner.Runtime.SpawnerDisableCalls;
            var delayedRoutine=owner.Runtime.ContinuousClearEnemiesUntilWaveStart();
            Check(delayedRoutine.MoveNext()&&alreadyActiveGhost!=null&&UnityEngine.Object.SceneScans==scans&&owner.Runtime.SpawnerDisableCalls==disables,"preclear first entered during terminal ownership also skips its initial scene scan");
            owner.Host.IsCampaignFinalBossActive=false;
            Check(delayedRoutine.MoveNext()&&alreadyActiveGhost==null,"a preclear first entered during terminal ownership can resume after release");
        }
        private static void TestRegeneration()
        {
            var regen=new MutatorBossRegenRuntime();MutatorManager.BossRegenEnabled=false;
            regen.Tick();Check(true,"disabled regeneration returns without reading unbound queries");
            bool arena=true,d=true,e=true,f=true;int arenaCount=1;
            var single=new CharacterMainControl("single");var dBoss=new CharacterMainControl("D");var eBoss=new CharacterMainControl("E");var fBoss=new CharacterMainControl("F");
            var wave=new System.Collections.Generic.List<MonoBehaviour>{single,eBoss};
            var dList=new System.Collections.Generic.List<CharacterMainControl>{null,dBoss};
            var eList=new System.Collections.Generic.List<CharacterMainControl>{eBoss};
            var fSet=new System.Collections.Generic.HashSet<CharacterMainControl>{fBoss};
            var eCache=new System.Collections.Generic.List<MonoBehaviour>{eBoss};var fCache=new System.Collections.Generic.List<MonoBehaviour>{fBoss};
            regen.BindArenaQueries(()=>arena,()=>arenaCount,()=>single,()=>wave);
            regen.BindModeDQueries(()=>d,()=>dList);regen.BindModeEQueries(()=>e,()=>eList,()=>eCache);regen.BindModeFQueries(()=>f,()=>fSet,()=>fCache);
            MutatorManager.BossRegenEnabled=true;Time.deltaTime=.25f;MutatorManager.RegenCalls.Clear();regen.Tick();
            Check(MutatorManager.RegenCalls.Count==2&&MutatorManager.RegenCalls[0].Single()==single&&MutatorManager.RegenCalls[1].Single()==dBoss,"arena runs before D and D filters null entries without entering E/F");
            arena=false;dList.Clear();MutatorManager.RegenCalls.Clear();regen.Tick();Check(MutatorManager.RegenCalls.Single().Single()==eBoss,"empty D list retains original fall-through to E");
            e=false;MutatorManager.RegenCalls.Clear();regen.Tick();Check(MutatorManager.RegenCalls.Single().Single()==fBoss,"F regeneration follows inactive E");
            f=false;arena=true;arenaCount=2;MutatorManager.RegenCalls.Clear();regen.Tick();Check(MutatorManager.RegenCalls.Single().SequenceEqual(wave),"multi-boss arena uses the complete wave list");
            arenaCount=1;UnityEngine.Object.Destroy(single.gameObject);MutatorManager.RegenCalls.Clear();regen.Tick();Check(MutatorManager.RegenCalls.Count==0,"destroyed single boss is skipped with Unity null semantics");
            MutatorManager.BossRegenEnabled=false;
        }
        private static void Main() { Run().GetAwaiter().GetResult(); }
    }
}
