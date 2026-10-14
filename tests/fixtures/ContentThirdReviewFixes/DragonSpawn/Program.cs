using System;
using System.Threading.Tasks;
using BossRush;
using UnityEngine;
using UnityEngine.SceneManagement;
using Cysharp.Threading.Tasks;

internal static class Program
{
    private static int checks;
    private static void Check(bool value,string label) { checks++; if (!value) throw new Exception(label); }
    private sealed class Session
    {
        internal bool Active = true;
        internal readonly CharacterMainControl Actor;
        internal readonly DragonDescendantRuntimeModule Runtime;
        internal Session(bool active = false)
        {
            MonoBehaviour.Starts = MonoBehaviour.InactiveStarts = SpawnedEnemyActivationHelper.Releases = 0;
            UniTask.Deferred = null;
            BossRushCoop.IsConnected=false; BossRushCoop.IsHost=false; BossRushCoop.RunId="birth-room";
            SceneManager.Current = new Scene { handle = 11, buildIndex = 7 };
            ModBehaviour.Instance = new ModBehaviour { IsModeFActive = true };
            BossRushAudioManager.Instance = new BossRushAudioManager();
            var go = new GameObject();
            Actor = go.AddComponent<CharacterMainControl>();
            Actor.Health = new Health(); Actor.characterPreset = new CharacterRandomPreset();
            go.SetActive(active);
            Runtime = new DragonDescendantRuntimeModule {
                owner = ModBehaviour.Instance, Equipment = Task.CompletedTask,
                Preset = new CharacterRandomPreset { Factory = Task.FromResult(Actor) }
            };
        }
        internal Task<CharacterMainControl> Spawn(bool deferred = false,bool child = false,bool nonWave = false)
        { return Runtime.SpawnDragonDescendant(new Vector3(), child, true, deferred, nonWave, () => Active); }
        internal void Cancelled(Task<CharacterMainControl> task,string label)
        {
            Check(task.GetAwaiter().GetResult() == null,label + " returns no actor");
            Check(Actor == null && Actor.gameObject == null,label + " destroys GO and components");
            Check(Runtime.dragonDescendantInstance == null && Runtime.dragonDescendantAbilities == null,label + " releases runtime references");
            Check(MonoBehaviour.Starts == 0 && MonoBehaviour.InactiveStarts == 0,label + " never starts abilities");
            Check(Runtime.CleanupCalls == 1 && Runtime.Loot == 0 && Runtime.Recovery == 0,label + " real cancellation cleanup once before commit");
            Check(BossRushAudioManager.Instance.Plays == 0,label + " never starts BGM");
        }
    }
    private static void Main()
    {
        foreach (bool active in new[] { false, true })
        {
            var s = new Session(active);
            var spawned = s.Spawn().GetAwaiter().GetResult();
            Check(spawned != null && spawned == s.Actor,"factory active/inactive completes real spawn");
            Check(s.Actor.gameObject.activeInHierarchy && MonoBehaviour.Starts == 1 && MonoBehaviour.InactiveStarts == 0,"timer starts exactly once on active actor");
            Check(SpawnedEnemyActivationHelper.Releases == 1 && s.Actor.Health.BarRequests == 1,"distance sleep released and bar requested");
            Check(s.Runtime.dragonDescendantAbilities.PrefabPreparations == 1 && s.Runtime.dragonDescendantAbilities.ShootSubscriptions == 1,"real Initialize retains prewarm and shooting subscription");
            Check(s.Runtime.Waves == 1 && s.Runtime.Loot == 1 && s.Runtime.Recovery == 1 && s.Runtime.SetBonuses == 1 && BossRushAudioManager.Instance.Plays == 1,"successful spawn keeps full boss features");
        }
        {
            var s = new Session(); var task = s.Spawn(true);
            Check(!task.IsCompleted && !s.Actor.gameObject.activeSelf && s.Runtime.dragonDescendantAbilities == null && MonoBehaviour.Starts == 0,"deferred actor stays inactive with no installed controller");
            UniTask.Deferred.SetResult(true);
            Check(task.GetAwaiter().GetResult() == s.Actor && MonoBehaviour.Starts == 1,"deferred activation installs timer after validation");
        }
        foreach (string reason in new[] { "mode", "scene", "owner" })
        {
            var s = new Session(); var task = s.Spawn(true);
            if (reason == "mode") s.Active = false;
            if (reason == "scene") SceneManager.Current = new Scene { handle = 12, buildIndex = 7 };
            if (reason == "owner") ModBehaviour.Instance = new ModBehaviour();
            UniTask.Deferred.SetResult(true); s.Cancelled(task,"deferred " + reason);
            Check(SpawnedEnemyActivationHelper.Releases == 0,"stale deferred actor never reactivated");
        }
        {
            var s = new Session(); var factory = new TaskCompletionSource<CharacterMainControl>();
            s.Runtime.Preset.Factory = factory.Task; var task = s.Spawn();
            s.Active = false; factory.SetResult(s.Actor); s.Cancelled(task,"late factory after mode cleanup");
            Check(s.Runtime.Waves == 0,"late factory never joins wave tracking");
        }
        {
            var s = new Session(); var equipment = new TaskCompletionSource<bool>();
            s.Runtime.Equipment = equipment.Task; var task = s.Spawn();
            s.Active = false; equipment.SetResult(true); s.Cancelled(task,"late equipment after mode cleanup");
        }
        {
            var s = new Session(); Check(s.Spawn(child:true).Result == s.Actor,"child summon activated");
            Check(s.Runtime.Waves == 0 && s.Runtime.SetBonuses == 0 && s.Runtime.Loot == 1 && BossRushAudioManager.Instance.Plays == 0 && MonoBehaviour.Starts == 1,"child retains abilities and loot without wave or BGM");
        }
        {
            var s = new Session(); Check(s.Spawn(nonWave:true).Result == s.Actor,"random nonwave spawn activated");
            Check(s.Runtime.Waves == 0 && s.Runtime.Loot == 1 && MonoBehaviour.Starts == 1,"nonwave retains full combat and loot");
        }
        foreach (string edge in new[] { "Stop", "new_room", "new_role" })
        {
            var s=new Session(); BossRushCoop.IsConnected=true; BossRushCoop.IsHost=true;
            var pending=new TaskCompletionSource<CharacterMainControl>();s.Runtime.Preset.Factory=pending.Task;
            var task=s.Runtime.SpawnDragonDescendant(new Vector3()); // real public entrance with no caller predicate
            if(edge=="Stop") BossRushCoop.IsConnected=false;
            if(edge=="new_room") BossRushCoop.RunId="replacement-room";
            if(edge=="new_role") BossRushCoop.IsHost=false;
            pending.SetResult(s.Actor);s.Cancelled(task,"default-predicate factory " + edge);
            Check(s.Runtime.Waves==0,"stale room continuation never registers a wave");
        }
        {
            var s=new Session(true);BossRushCoop.IsConnected=true;BossRushCoop.IsHost=false;
            var controller=s.Actor.gameObject.AddComponent<DragonDescendantAbilityController>();controller.Initialize(s.Actor);
            Check(CoopAiPredictionHealthProtection.Replicas.Contains(s.Actor) && MonoBehaviour.Starts==0,"client binding permanently marks replica before combat timer");
            BossRushCoop.IsConnected=false;controller.Shoot(20);
            Check(controller.Rockets==0 && !controller.Clamp(),"Stop cannot turn old mirror into rocket or resurrection authority");
        }
        {
            var s=new Session(true);BossRushCoop.IsConnected=true;BossRushCoop.IsHost=true;
            var controller=s.Actor.gameObject.AddComponent<DragonDescendantAbilityController>();controller.Initialize(s.Actor);
            controller.Shoot(20);Check(controller.Rockets==2 && controller.Clamp(),"original host retains full rocket count and one-time resurrection eligibility");
            var timer=controller.Timer();Check(timer.MoveNext(),"original grenade timer schedules real interval");
            BossRushCoop.IsConnected=false;
            Check(!timer.MoveNext() && controller.Grenades==0,"timer suspended before Stop cancels actual grenade continuation");
            controller.Shoot(20);Check(controller.Rockets==2 && !controller.Clamp(),"old host body cannot resume offline combat");
        }
        Console.WriteLine("DragonSpawn: " + checks + " assertions PASS");
    }
}
