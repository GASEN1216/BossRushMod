using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BossRush;
using UnityEngine;
using Duckov.ItemUsage;
using Duckov.Economy;
using Duckov.Economy.UI;

internal static class Program
{
 private static int checks;
 private static void Check(bool value,string message){checks++;if(!value)throw new Exception(message);}
 private static void Ordered(params string[] names){int p=0;foreach(var name in names){int i=Probe.Events.FindIndex(p,e=>e==name);Check(i>=0,"missing/order: "+name+" "+string.Join("|",Probe.Events));p=i+1;}}
 private static void Reset(){Probe.Events.Clear();Probe.LastTask=null;Resources.Values=null;Resources.Calls=0;UnityEngine.SceneManagement.SceneManager.Index=1;BossRushAudioManager.Instance=new BossRushAudioManager();CharacterMainControl.Main=new CharacterMainControl("player");StockShopView.Instance=null;RandomEventsRuntimeModule.ShopSuccess=true;}
 private static async Task Intruder()
 {
  Reset();var owner=new ModBehaviour();int success=0,failed=0;bool valid=true;
  owner.SpawnRandomEventIntruderBoss(null,Vector3.zero,()=>valid,c=>success++,()=>failed++);Check(failed==1&&Probe.LastTask==null,"null intruder preset must synchronously fail once");
  failed=0;var preset=new EnemyPresetInfo();owner.SpawnRandomEventIntruderBoss(preset,Vector3.zero,()=>valid,c=>success++,()=>failed++);
  Check(!Probe.LastTask.IsCompleted&&owner.Options.SuppressWaveBossRegistration,"intruder lost observable core await/nonwave option");
  var character=new CharacterMainControl();var context=new EnemySpawnContext{character=character,preset=preset};
  Check(owner.Commit(context)&&character.Team==Teams.wolf&&character.gameObject.name=="RndEvt_Intruder_enemy","intruder commit lost hostile/name setup");
  owner.SpawnPending.SetResult(new EnemySpawnCoreResult{success=true,context=context});await Probe.LastTask;
  Check(success==1&&failed==0,"committed intruder completion changed");
  Probe.Events.Clear();owner.DespawnRandomEventIntruderBoss(character);Ordered("unregister-anchor","bgm:descendant","bgm:witch","bgm:king","destroy:RndEvt_Intruder_enemy");
  Check(character==null&&character.Health==null,"intruder cleanup must destroy all components");
  Probe.Events.Clear();owner.DespawnRandomEventIntruderBoss(character);Check(Probe.Events.Count==0,"duplicate intruder cleanup was not a no-op");
  owner.SpawnRandomEventIntruderBoss(preset,Vector3.zero,()=>valid,c=>success++,()=>failed++);character=new CharacterMainControl();context=new EnemySpawnContext{character=character,preset=preset};valid=false;
  owner.SpawnPending.SetResult(new EnemySpawnCoreResult{success=true,context=context});await Probe.LastTask;
  Check(character==null&&success==1&&failed==0,"late intruder result escaped cleanup or notified twice");
  valid=true;owner.SpawnRandomEventIntruderBoss(preset,Vector3.zero,()=>valid,c=>success++,()=>failed++);
  owner.SpawnPending.SetException(new InvalidOperationException());await Probe.LastTask;Check(failed==1,"intruder core exception must notify failure once");
  owner.SpawnRandomEventIntruderBoss(preset,Vector3.zero,()=>valid,c=>success++,()=>failed++);character=new CharacterMainControl{Companion=true};context=new EnemySpawnContext{character=character,preset=preset};owner.Commit(context);
  Check(character.Team==Teams.middle,"companion must retain hostile-safety exemption");owner.SpawnPending.SetResult(new EnemySpawnCoreResult{success=false});await Probe.LastTask;
 }
 private static async Task Parade()
 {
  Reset();var owner=new ModBehaviour();int complete=0,spawned=-1,received=0;bool valid=true;
  owner.SpawnRandomEventParadeDucks(Vector3.zero,Vector3.forward,2,()=>valid,c=>received++,(r,s)=>{complete++;spawned=s;Check(r==2,"parade requested count");});await Probe.LastTask;
  Check(complete==1&&spawned==0,"missing parade preset must still complete once");
  complete=0;var pending=new TaskCompletionSource<CharacterMainControl>();var preset=new CharacterRandomPreset{Factory=()=>pending.Task};Resources.Values=new[]{new SpawnEgg{spawnCharacter=preset}};
  owner.SpawnRandomEventParadeDucks(Vector3.zero,Vector3.forward,2,()=>valid,c=>received++,(r,s)=>{complete++;spawned=s;});
  Check(owner.RandomEventSpawnEggBehaviorForRuntime!=null&&ReferenceEquals(owner.RandomEventEggSpawnPresetForRuntime,preset),"parade must preserve shared egg cache/cleanup exemption");
  valid=false;var late=new CharacterMainControl();pending.SetResult(late);await Probe.LastTask;
  Check(late==null&&received==0&&complete==1&&spawned==0,"late parade result must clean up and complete once");
  valid=true;complete=0;int factoryCalls=0;preset.Factory=()=>{factoryCalls++;return Task.FromResult(new CharacterMainControl());};
  owner.SpawnRandomEventParadeDucks(Vector3.zero,Vector3.forward,2,()=>valid,c=>{received++;Check(c.gameObject.activeSelf,"parade callback before activation");},(r,s)=>{complete++;spawned=s;});await Probe.LastTask;
  Check(factoryCalls==2&&received==2&&complete==1&&spawned==2,"parade count/completion changed");Ordered("release-sleep","active:RndEvt_ParadeDuck");
 }
 private static async Task Merchant()
 {
  Reset();var owner=new ModBehaviour();int failed=0,success=0;bool valid=true;
  owner.SpawnRandomEventMerchant(Vector3.zero,()=>valid,(c,s)=>success++,()=>failed++);await Probe.LastTask;Check(failed==1&&!owner.HasRandomEventMerchantPreset(),"missing merchant preset should fail once");
  var pending=new TaskCompletionSource<CharacterMainControl>();owner.Merchant=new CharacterRandomPreset{Factory=()=>pending.Task};
  owner.SpawnRandomEventMerchant(Vector3.zero,()=>valid,(c,s)=>success++,()=>failed++);valid=false;var late=new CharacterMainControl();pending.SetResult(late);await Probe.LastTask;
  Check(late==null&&success==0&&failed==1,"late merchant result must clean up without success/failure callback");
  valid=true;CharacterMainControl merchant=null;StockShop shop=null;owner.Merchant.Factory=()=>Task.FromResult(new CharacterMainControl());Probe.Events.Clear();
  owner.SpawnRandomEventMerchant(Vector3.zero,()=>valid,(c,s)=>{merchant=c;shop=s;success++;},()=>failed++);await Probe.LastTask;
  Check(success==1&&merchant.Team==Teams.player&&shop!=null,"merchant live completion changed");Ordered("factory","team:player","merchant-health","release-sleep","build-shop","move-scene");
  StockShopView.Instance=new StockShopView{Target=shop};Probe.Events.Clear();owner.DespawnRandomEventMerchant(merchant,shop);Ordered("close-shop","destroy:shop","destroy:RndEvt_Merchant");
  Check(merchant==null&&shop==null,"merchant cleanup leaked components");
  RandomEventsRuntimeModule.ShopSuccess=false;owner.SpawnRandomEventMerchant(Vector3.zero,()=>valid,(c,s)=>success++,()=>failed++);await Probe.LastTask;Check(failed==2&&success==1,"failed shop build must clean merchant and notify once");
 }
 private static void Environment()
 {
  Reset();var owner=new ModBehaviour();var runtime=owner.Runtime;var enemy=new CharacterMainControl{Team=Teams.wolf};var dead=new CharacterMainControl{Team=Teams.wolf};dead.Health.IsDead=true;var pet=new CharacterMainControl{Team=Teams.wolf,Companion=true};
  owner.RandomEventCachedCharactersForRuntime.AddRange(new[]{CharacterMainControl.Main,enemy,dead,pet,new CharacterMainControl()});var targets=new List<CharacterMainControl>{pet};runtime.CollectEventBuffTargets(targets);
  Check(targets.Count==1&&targets[0]==enemy&&Probe.Events[0]=="refresh-cache","buff target scan must refresh original cache and filter companions/dead/neutral");
  bool force;Duckov.Weathers.Weather weather;Duckov.Weathers.WeatherManager.Instance=null;
  Check(!runtime.TryApplyRandomEventForcedWeather(Duckov.Weathers.Weather.Rain,out force,out weather)&&!force&&weather==Duckov.Weathers.Weather.Sunny,"missing weather defaults changed");
  Duckov.Weathers.WeatherManager.Instance=new Duckov.Weathers.WeatherManager{ForceWeather=false,ForceWeatherValue=Duckov.Weathers.Weather.Sunny};
  Check(runtime.TryApplyRandomEventForcedWeather(Duckov.Weathers.Weather.Rain,out force,out weather)&&!force&&weather==Duckov.Weathers.Weather.Sunny,"weather original snapshot lost");runtime.RestoreRandomEventForcedWeather(force,weather);
  Check(!Duckov.Weathers.WeatherManager.Instance.ForceWeather&&Duckov.Weathers.WeatherManager.Instance.ForceWeatherValue==Duckov.Weathers.Weather.Sunny,"weather restoration changed");
  int landed=0;var crate=new GameObject("crate");var routine=owner.RandomEventAirdropDropRoutine(crate,Vector3.zero,10,1,()=>{landed++;Probe.Add("landed");});int frames=0;Probe.Events.Clear();
  while(routine.MoveNext()){frames++;Check(routine.Current==null,"airdrop wait changed");}Check(frames==4&&landed==1&&crate.transform.position.y==0,"airdrop duration/landing changed");Ordered("marker","landing","ai-sound","landed");
  Check(AIMainBrain.Last.fromTeam==Teams.player&&AIMainBrain.Last.fromCharacter==null,"airdrop AI sound changed");
  crate=new GameObject("cancelled");routine=owner.RandomEventAirdropDropRoutine(crate,Vector3.zero,10,1,()=>landed++);routine.MoveNext();UnityEngine.Object.Destroy(crate);Check(!routine.MoveNext()&&landed==1,"destroyed airdrop must stop before landing callback");
 }
 private static async Task Main(){await Intruder();await Parade();await Merchant();Environment();Console.WriteLine("RandomEventEffectsOwners: PASS ("+checks+" assertions)");}
}
