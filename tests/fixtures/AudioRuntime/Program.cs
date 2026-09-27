using System;
using System.IO;
using System.Reflection;
using BossRush;
using Duckov.ItemUsage;
using UnityEngine;

internal static class Program
{
    private static string folder;
    private static int assertions;
#if MISSING_AUDIO
    private static readonly bool HasAudio = false;
#else
    private static readonly bool HasAudio = true;
#endif

    private static void Check(bool condition, string message)
    {
        assertions++;
        if (!condition) throw new InvalidOperationException(message);
    }
    private static bool Same(Vector3 a, Vector3 b) { return a.x == b.x && a.y == b.y && a.z == b.z; }
    private static object Cached(string name)
    {
        return typeof(BossRushAudioRuntimeService).GetField(name, BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
    }
    private static string NewPath(string name)
    {
        string path = Path.Combine(folder, Guid.NewGuid().ToString("N"), name);
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        return path;
    }
    private static ModBehaviour Host(string modPath = null)
    {
        ModBehaviour host = new ModBehaviour { info = new ModInfo { RawPath = modPath } };
        host.BindProductionAudioForFixture();
        return host;
    }
    private static void Reset()
    {
        BossRushAudioRuntimeService.ResetBossRushAudioHooksStaticCaches();
        Probe.Clear();
    }
    private static CharacterMainControl Player(bool collider = true)
    {
        CharacterMainControl main = new GameObject().AddComponent<CharacterMainControl>();
        if (collider) main.gameObject.AddComponent<Collider>();
        main.transform.position = new Vector3(3, 7, -11);
        main.CurrentAimDirection = new Vector3(-2, 0, 0.5f);
        return main;
    }
    private static SpawnEgg Behavior(bool collider = true)
    {
        SpawnEgg behavior = new GameObject().AddComponent<SpawnEgg>();
        behavior.eggPrefab = new GameObject().AddComponent<Egg>();
        if (collider) behavior.eggPrefab.gameObject.AddComponent<Collider>();
        behavior.spawnCharacter = new CharacterRandomPreset();
        behavior.eggSpawnDelay = 4.75f;
        return behavior;
    }
    private static void Posts(int expected, string message)
    {
        Check(Probe.Posts.Count == (HasAudio ? expected : 0), message);
    }

    private static void BindingAndSpawn()
    {
        Reset();
        ModBehaviour host = Host();
        Check(Probe.PathReads == 0 && Probe.PlayerReads == 0 && Probe.Scans == 0 && !(bool)Cached("_postCustomSfxResolved"),
            "Binding must not sample queries, scan resources or resolve audio");
        string rootFile = NewPath("ngm.mp3");
        File.WriteAllText(rootFile, "fixture root audio");
        string basePath = Path.GetDirectoryName(rootFile);
        string assetFile = Path.Combine(basePath, "Assets", "ngm.mp3");
        Directory.CreateDirectory(Path.GetDirectoryName(assetFile));
        File.WriteAllText(assetFile, "fixture asset audio");
        host.info.RawPath = basePath;
        CharacterMainControl main = Player();
        host.RawPlayer = Player();
        Probe.Main = main;
        SpawnEgg first = Behavior();
        SpawnEgg second = Behavior();
        Probe.Resources = new[] { first, second };
        host.TrySpawnEggForPlayer();
        Posts(1, "Ngm must play once before egg creation");
        if (HasAudio) Check(Probe.Posts[0].Path == assetFile && ReferenceEquals(Probe.Posts[0].Target, main.gameObject) && !Probe.Posts[0].Loop,
            "Ngm must prefer Assets/ngm.mp3 and pass the current player without looping");
        Check(Probe.PathReads == 1 && Probe.PlayerReads == 0 && Probe.Scans == 1 && Probe.Instantiates == 1 && Probe.Collisions == 1,
            "Main player spawn must use the first scan result without reading fallback player");
        Egg egg = Probe.Eggs[0];
        Check(egg.InitCalls == 1 && ReferenceEquals(egg.From, main) && ReferenceEquals(egg.Preset, first.spawnCharacter)
            && egg.Delay == 4.75f && Same(egg.SpawnPosition, main.transform.position) && Same(egg.Velocity, main.CurrentAimDirection),
            "Egg must retain player position, unit aim velocity, preset and original spawn delay");
        Check(ReferenceEquals(host.RandomEventSpawnEggBehaviorForRuntime, first) && ReferenceEquals(host.ArenaEggSpawnPreset, first.spawnCharacter),
            "Spawned egg caches must be shared with random events and arena cleanup");
        Check(Probe.Trace.IndexOf("path") < Probe.Trace.IndexOf("resources") && Probe.Trace.IndexOf("collision") < Probe.Trace.IndexOf("init")
            && (!HasAudio || Probe.Trace.IndexOf("audio") < Probe.Trace.IndexOf("instantiate")),
            "Sound, resource scan, collision exclusion and egg initialization order must remain intact");
        File.Move(assetFile, assetFile + ".moved");
        host.TrySpawnEggForPlayer();
        Posts(2, "Repeated ngm playback must run on every egg request");
        if (HasAudio) Check(Probe.Posts[1].Path == rootFile, "Ngm must recheck Assets and fall back to live root audio");
        Check(Probe.Scans == 1 && Probe.PathReads == 2, "Spawn behavior stays cached while ngm paths remain live");
        host.info.RawPath = null;
        host.TrySpawnEggForPlayer();
        Posts(2, "A removed mod path must suppress only audio");
        Check(Probe.Eggs.Count == 3, "Missing ngm path must not suppress egg creation");
    }

    private static void FallbackAndDestroyedObjects()
    {
        Reset();
        ModBehaviour host = Host();
        CharacterMainControl fallback = Player();
        host.RawPlayer = fallback;
        Probe.Resources = new[] { Behavior() };
        Probe.ThrowMain = true;
        host.TrySpawnEggForPlayer();
        Check(Probe.Eggs.Count == 1 && ReferenceEquals(Probe.Eggs[0].From, fallback) && Probe.PlayerReads == 2,
            "Failed Main lookup must retain both fallback reads and spawn from the fallback player");
        UnityEngine.Object.Destroy(fallback.gameObject);
        Check(fallback == null && fallback.GetComponent<Collider>() == null, "GameObject destruction must destroy attached components");
        host.TrySpawnEggForPlayer();
        Check(Probe.Eggs.Count == 1, "Destroyed fallback players must not spawn eggs");
        Probe.ThrowMain = false;
        Probe.Main = Player();
        SpawnEgg old = host.RandomEventSpawnEggBehaviorForRuntime;
        UnityEngine.Object.Destroy(old.gameObject);
        SpawnEgg replacement = Behavior();
        Probe.Resources = new[] { replacement };
        host.TrySpawnEggForPlayer();
        Check(Probe.Scans == 2 && ReferenceEquals(host.RandomEventSpawnEggBehaviorForRuntime, replacement)
            && ReferenceEquals(Probe.Eggs[1].Preset, replacement.spawnCharacter),
            "Destroyed cached SpawnEgg must rescan and replace the shared preset");
        UnityEngine.Object.Destroy(Probe.Main.gameObject);
        host.TrySpawnEggForPlayer();
        Check(Probe.Eggs.Count == 2, "Scene destruction must reject both destroyed Main and fallback players");
    }

    private static void SharedCachesAndCleanup()
    {
        Reset();
        ModBehaviour first = Host();
        ModBehaviour second = Host();
        SpawnEgg behavior = Behavior();
        first.RandomEventSpawnEggBehaviorForRuntime = behavior;
        first.RandomEventEggSpawnPresetForRuntime = behavior.spawnCharacter;
        Check(ReferenceEquals(second.RandomEventSpawnEggBehaviorForRuntime, behavior) && ReferenceEquals(second.ArenaEggSpawnPreset, behavior.spawnCharacter),
            "Random event cache writes must remain static across host instances");
        Probe.Main = Player();
        second.TrySpawnEggForPlayer();
        Check(Probe.Scans == 0 && Probe.Eggs.Count == 1, "Random event cache must feed player egg spawning without a second scan");
        string path = NewPath("cleanup.wav"); File.WriteAllText(path, "fixture");
        first.PlaySoundEffect(path);
        Check((bool)Cached("_postCustomSfxResolved"), "Existing sound file must resolve the audio method lazily");
        Probe.Trace.Clear();
        first.CleanupProductionAudioForFixture();
        Check(ReferenceEquals(Probe.BeforeReset, behavior) && ReferenceEquals(Probe.BeforeResetPreset, behavior.spawnCharacter)
            && Probe.CacheEmptyAfterReset && string.Join(",", Probe.Trace) == "magic-reset,npc-reset,dialogue-reset",
            "Actual audio reset must execute between the original MagicBlend and NPC cleanup slots");
        Check(Cached("_cachedPostCustomSfx") == null && !(bool)Cached("_postCustomSfxResolved")
            && ((System.Collections.IDictionary)Cached("_cachedSoundFileExists")).Count == 0,
            "Audio cleanup must clear file cache, delegate and resolution marker");
        first.CleanupProductionAudioForFixture();
        Check(Probe.CacheEmptyAfterReset, "Repeated original cleanup must remain safe");
    }

    private static void FileCacheReflectionAndTargets()
    {
        Reset();
        ModBehaviour host = Host();
        string path = NewPath("sound.wav");
        host.PlaySoundEffect(path);
        File.WriteAllText(path, "fixture audio");
        host.PlaySoundEffect(path);
        Posts(0, "Missing sound results must stay cached until cleanup");
        Check(!(bool)Cached("_postCustomSfxResolved"), "Missing files must not resolve the audio method");
        host.CleanupProductionAudioForFixture();
        CharacterMainControl main = Player(); Probe.Main = main;
        host.PlaySoundEffect(path);
        Posts(1, "Cleanup must allow a previously missing sound file to be discovered");
        Check((bool)Cached("_postCustomSfxResolved"), "First existing sound must set the lazy resolution marker");
#if COMPATIBLE_VOID
        Check(Cached("_cachedPostCustomSfx") != null, "Void-compatible audio must use the cached delegate");
#else
        Check(Cached("_cachedPostCustomSfx") == null, "Official non-void or missing audio must keep the delegate empty");
#endif
        if (HasAudio) Check(ReferenceEquals(Probe.Posts[0].Target, main.gameObject) && !Probe.Posts[0].Loop,
            "Active main player sound must preserve target and loop false");
        File.Move(path, path + ".moved");
        main.gameObject.activeSelf = false;
        host.PlaySoundEffect(path);
        Posts(2, "Existing sound results must stay cached after the file moves");
        if (HasAudio) Check(ReferenceEquals(Probe.Posts[1].Target, null), "Inactive main players must produce a null general sound target");
        UnityEngine.Object.Destroy(main.gameObject);
        host.PlaySoundEffect(path);
        Posts(3, "Destroyed main player must not suppress general sound");
        if (HasAudio) Check(ReferenceEquals(Probe.Posts[2].Target, null), "Destroyed main players must produce a null general sound target");
        host.PlaySoundEffect(path.ToUpperInvariant());
        Posts(4, "Sound existence cache must remain case insensitive");
        Probe.ThrowPost = true;
        host.PlaySoundEffect(path);
        Posts(5, "Throwing audio must be contained without duplicate fallback playback");
        host.PlaySoundEffect(null); host.PlaySoundEffect("");
        Posts(5, "Empty sound paths must never reach audio");
    }

    private static void FailureBoundaries()
    {
        Reset();
        string ngm = NewPath("ngm.mp3"); File.WriteAllText(ngm, "fixture");
        ModBehaviour host = Host(Path.GetDirectoryName(ngm));
        Probe.Main = Player(); Probe.Main.gameObject.activeSelf = false;
        SpawnEgg behavior = Behavior(); Probe.Resources = new[] { behavior };
        Probe.ThrowPost = true;
        host.TrySpawnEggForPlayer();
        Check(Probe.Eggs.Count == 1, "Audio exceptions must not block egg spawning");
        if (HasAudio) Check(ReferenceEquals(Probe.Posts[0].Target, Probe.Main.gameObject), "Ngm must retain its original inactive-player target behavior");
        Probe.ThrowPost = false; Probe.ThrowInstantiate = true;
        host.TrySpawnEggForPlayer();
        Check(Probe.Eggs.Count == 1 && ReferenceEquals(host.ArenaEggSpawnPreset, behavior.spawnCharacter),
            "Instantiation failure must preserve preset assignment and skip initialization");
        Probe.ThrowInstantiate = false; Probe.ThrowInit = true;
        host.TrySpawnEggForPlayer();
        Check(Probe.Eggs.Count == 2 && Probe.Eggs[1].InitCalls == 1, "Egg Init exceptions must be contained");
        Probe.ThrowInit = false;
        behavior.spawnCharacter = null;
        CharacterRandomPreset previous = host.ArenaEggSpawnPreset;
        host.TrySpawnEggForPlayer();
        Check(Probe.Eggs.Count == 3 && Probe.Eggs[2].Preset == null && ReferenceEquals(host.ArenaEggSpawnPreset, previous),
            "Null spawn presets must retain the previous cleanup exemption while still initializing the egg");
        behavior.eggPrefab = null;
        host.TrySpawnEggForPlayer();
        Check(Probe.Eggs.Count == 3, "Missing egg prefab must skip instantiation");
        host.RandomEventSpawnEggBehaviorForRuntime = null;
        Probe.ThrowResources = true;
        host.TrySpawnEggForPlayer();
        Check(Probe.Eggs.Count == 3, "Resource lookup failures must be contained");
        Probe.ThrowResources = false; Probe.Resources = new SpawnEgg[0];
        host.TrySpawnEggForPlayer();
        Check(Probe.Eggs.Count == 3, "Empty resource results must skip instantiation");
        Probe.Resources = new[] { Behavior(false) }; host.info.ThrowRead = true;
        host.TrySpawnEggForPlayer();
        Check(Probe.Eggs.Count == 4 && Probe.Eggs[3].InitCalls == 1, "Path read errors and missing colliders must not suppress egg initialization");
    }

    private static int Main(string[] args)
    {
        folder = Path.GetFullPath(args[0]); Directory.CreateDirectory(folder);
        BindingAndSpawn(); FallbackAndDestroyedObjects(); SharedCachesAndCleanup();
        FileCacheReflectionAndTargets(); FailureBoundaries();
        Console.WriteLine("AudioRuntime PASS: " + assertions + " assertions; audio present=" + HasAudio);
        return 0;
    }
}
