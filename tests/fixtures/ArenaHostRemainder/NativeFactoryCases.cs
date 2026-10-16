using System;
using System.Linq;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BossRush
{
    internal static class NativeFactoryCases
    {
        internal static readonly Exception FactoryError = new InvalidOperationException("original factory exception");
        private static int assertions;
        private static void Check(bool condition, string message)
        { assertions++; if (!condition) throw new Exception("NativeFactoryCleanup: " + message); }

        private sealed class Pending
        {
            internal readonly ModBehaviour Host = new ModBehaviour();
            internal readonly WavesArenaRuntimeModule Runtime;
            internal readonly CharacterMainControl Character;
            internal readonly TaskCompletionSource<bool> Gate = new TaskCompletionSource<bool>();
            internal readonly Task<CharacterMainControl> Task;
            internal Pending(bool fail = false)
            {
                UnityEngine.Object.All.Clear(); Probe.Events.Clear();
                SceneManager.Name = "arena"; SceneManager.Handle = 17;
                BossRushCoop.IsConnected = false; BossRushCoop.IsHost = false;
                BossRushCoop.LocalAssetId = "asset-a"; BossRushCoop.RunId = "";
                CharacterMainControl.Main = new CharacterMainControl("main");
                Runtime = new WavesArenaRuntimeModule(Host);
                WavesArenaRuntimeModule.PrepareSceneCharacterCacheForLoad();
                Character = new CharacterMainControl("native-pending");
                Character.characterPreset = new CharacterRandomPreset { nameKey = "daxing", DisplayName = "daxing", team = Teams.wolf };
                Task = Character.characterPreset.CreateCharacterAsync(Character, Gate.Task, fail);
            }
        }

        internal static void Run()
        {
            ValidateInstalledNativeBinding();
            // Execute the old failure schedule before applying any hook. A real CLR task
            // resumes after the production Clear method has destroyed its exact item tree.
            var old = new Pending();
            old.Runtime.ClearEnemiesForBossRush(); old.Gate.SetResult(true);
            Exception oldError = null; try { old.Task.GetAwaiter().GetResult(); } catch (Exception e) { oldError = e; }
            Console.WriteLine("NativeFactory old_schedule error=" + (oldError == null ? "none" : oldError.GetType().FullName)
                + " destroyed=" + old.Character.CharacterItem.Destroyed + " ready=" + old.Character.FactoryReady
                + " alive=" + (old.Character != null) + " cache=" + WavesArenaRuntimeModule.CharacterCache.Count);
            Check(oldError is NullReferenceException && old.Character.CharacterItem.Destroyed, "unprotected real continuation reproduces the original same-object failure");

            var harmony = new Harmony("BossRush.Tests.NativeArenaFactoryLifecycle");
            harmony.CreateClassProcessor(typeof(NativeArenaFactoryLifecyclePatch)).Patch();
            try
            {
                var p = new Pending();
                var lease = p.Character.GetComponent<NativeArenaFactoryLease>();
                Check(lease != null && lease.IsCreating && !p.Task.IsCompleted, "actual suspended MoveNext attaches the production lease without completing the task");
                p.Runtime.ClearEnemiesForBossRush(); p.Runtime.ClearEnemiesForBossRush();
                Check(p.Character != null && !p.Character.CharacterItem.Destroyed && !p.Character.FactoryReady, "two production clear requests preserve the exact unfinished character and tree");
                BossRushCoop.SceneGeneration += 1;
                p.Gate.SetResult(true);
                Check(ReferenceEquals(p.Task.GetAwaiter().GetResult(), p.Character) && p.Character.FactoryReady, "actual task result and factory continuation are preserved");
                Check(p.Character == null && p.Character.CharacterItem.Destroyed && Probe.Events.Count(e => e == "destroy") == 1, "real terminal state retries original clear once, even after a global generation update");
                lease.Complete(); Check(Probe.Events.Count(e => e == "destroy") == 1, "duplicate terminal notification cannot destroy again");

                p = new Pending(); p.Runtime.ForceKillAllEnemies(); p.Runtime.ForceKillAllEnemies();
                Check(!p.Character.Health.IsDead, "ForceKill does not mutate unfinished character health");
                p.Gate.SetResult(true); p.Task.GetAwaiter().GetResult();
                Check(p.Character != null && p.Character.FactoryReady && p.Character.Health.IsDead, "ForceKill retains its real death path after factory completion");

                p = new Pending(); p.Runtime.TryCleanNonBossRushDaXingXing();
                Check(p.Character != null, "foreign native boss cleanup also protects an unfinished factory");
                p.Gate.SetResult(true); p.Task.GetAwaiter().GetResult();
                Check(p.Character == null, "foreign boss cleanup is retried, not permanently exempted");

                p = new Pending(); p.Runtime.TryCleanNonBossRushDaXingXing(); p.Character.Companion = true;
                p.Gate.SetResult(true); p.Task.GetAwaiter().GetResult();
                Check(p.Character != null, "completion rechecks actual companion ownership before removal");
                p.Character.Companion = false; p.Runtime.TryCleanNonBossRushDaXingXing();
                Check(p.Character == null, "completed characters continue through the ordinary synchronous cleanup");

                p = new Pending(); p.Gate.SetResult(true); p.Task.GetAwaiter().GetResult();
                Check(p.Character != null && !p.Character.GetComponent<NativeArenaFactoryLease>().IsCreating, "factory without cleanup request remains alive and terminal");
                p.Runtime.ClearEnemiesForBossRush(); Check(p.Character == null, "already completed factory is cleared immediately");

                foreach (string change in new[] { "owner", "body", "asset", "scene", "character_scene", "stop", "room", "host", "offline_join" })
                {
                    p = new Pending();
                    if (change == "stop" || change == "room" || change == "host")
                    { BossRushCoop.IsConnected = true; BossRushCoop.IsHost = true; BossRushCoop.RunId = "room-a"; }
                    p.Runtime.ClearEnemiesForBossRush();
                    switch (change)
                    {
                        case "owner": p.Runtime.OnDestroy(); break;
                        case "body": CharacterMainControl.Main = new CharacterMainControl("replacement"); break;
                        case "asset": BossRushCoop.LocalAssetId = "asset-b"; break;
                        case "scene": SceneManager.Handle++; break;
                        case "character_scene": p.Character.gameObject.scene = new Scene { handle = 99, name = "moved" }; break;
                        case "stop": BossRushCoop.IsConnected = false; break;
                        case "room": BossRushCoop.RunId = "room-b"; break;
                        case "host": BossRushCoop.IsHost = false; break;
                        case "offline_join": BossRushCoop.IsConnected = true; BossRushCoop.IsHost = true; BossRushCoop.RunId = "new-room"; break;
                    }
                    p.Gate.SetResult(true); p.Task.GetAwaiter().GetResult();
                    Check(p.Character != null && p.Character.FactoryReady && !p.Character.CharacterItem.Destroyed, "stale cleanup is canceled for " + change);
                }

                p = new Pending(true); p.Runtime.ClearEnemiesForBossRush(); p.Gate.SetResult(true);
                Exception error = null; try { p.Task.GetAwaiter().GetResult(); } catch (Exception e) { error = e; }
                Check(ReferenceEquals(error, FactoryError) && p.Task.IsFaulted, "native-style internal async failure keeps the exact original exception and faulted task");
                Check(p.Character == null, "terminal failure releases a previously requested cleanup");

                p = new Pending(); p.Runtime.ClearEnemiesForBossRush(); lease = p.Character.GetComponent<NativeArenaFactoryLease>();
                UnityEngine.Object.Destroy(p.Character.gameObject);
                Check(!lease.IsCreating, "actual character destruction retires the pending factory owner");
                int destroyed = Probe.Events.Count(e => e == "destroy");
                p.Gate.SetResult(true); try { p.Task.GetAwaiter().GetResult(); } catch (NullReferenceException) { }
                Check(Probe.Events.Count(e => e == "destroy") == destroyed, "external Unity destruction retires receipts without retrying another character");
            }
            finally { harmony.UnpatchAll(harmony.Id); }
            Console.WriteLine("NativeFactoryCleanup: PASS " + assertions + " assertions; production methods and actual Harmony/CLR suspension; Unity/native factory body are explicit seams");
        }

        private static void ValidateInstalledNativeBinding()
        {
            string managed = Environment.GetEnvironmentVariable("BOSSRUSH_GAME_MANAGED");
            Check(!string.IsNullOrEmpty(managed) && File.Exists(Path.Combine(managed, "TeamSoda.Duckov.Core.dll")), "actual installed native Core DLL is required for binding validation");
            ResolveEventHandler resolve = delegate(object sender, ResolveEventArgs args)
            {
                string file = Path.Combine(managed, new AssemblyName(args.Name).Name + ".dll");
                return File.Exists(file) ? Assembly.ReflectionOnlyLoadFrom(file) : Assembly.ReflectionOnlyLoad(args.Name);
            };
            AppDomain.CurrentDomain.ReflectionOnlyAssemblyResolve += resolve;
            try
            {
                Assembly native = Assembly.ReflectionOnlyLoadFrom(Path.Combine(managed, "TeamSoda.Duckov.Core.dll"));
                Type[] machines = native.GetType("CharacterRandomPreset", true).GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)
                    .Where(t => t.Name.StartsWith("<CreateCharacterAsync>d__", StringComparison.Ordinal)).ToArray();
                Check(machines.Length == 1 && machines[0].IsValueType, "actual native async machine is unique and a value type");
                Type machine = machines[0];
                FieldInfo[] characters = machine.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .Where(f => f.FieldType.FullName == "CharacterMainControl" && f.Name.StartsWith("<character>", StringComparison.Ordinal)).ToArray();
                Check(characters.Length == 1, "production selector matches exactly one actual native character field");
                FieldInfo state = machine.GetField("<>1__state", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                MethodInfo move = machine.GetMethod("MoveNext", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                Check(state != null && state.FieldType.FullName == "System.Int32" && move != null && move.ReturnType.FullName == "System.Void", "production state/MoveNext binding matches installed native API");
                Console.WriteLine("NativeFactory actual_binding machine=" + machine.FullName + " character=" + characters[0].Name
                    + " move_token=" + move.MetadataToken.ToString("x8") + " IL_bytes=" + move.GetMethodBody().GetILAsByteArray().Length);
            }
            finally { AppDomain.CurrentDomain.ReflectionOnlyAssemblyResolve -= resolve; }
        }
    }
}
